using Microsoft.Data.Sqlite;
using System.Reflection;

namespace MorningCat.PluginErrorDatabase;

/// <summary>
/// 已知错误匹配结果
/// </summary>
public class ErrorMatchResult
{
    public bool Found { get; set; }
    public string ExceptionType { get; set; } = "";
    public string Description { get; set; } = "";
    public string Solution { get; set; } = "";
    public string Category { get; set; } = "";
    public double Confidence { get; set; }
}

/// <summary>
/// 插件异常匹配器 - 在插件抛出异常后匹配内嵌数据库中的已知崩溃
/// 仅在测试模式 + Debug 模式同时开启时工作
/// </summary>
public class PluginErrorMatcher : IDisposable
{
    private SqliteConnection? _connection;
    private bool _enabled;
    private bool _initialized;

    /// <summary>
    /// 初始化异常匹配器
    /// </summary>
    /// <param name="testMode">是否启用测试模式</param>
    /// <param name="debugMode">是否启用调试模式</param>
    public PluginErrorMatcher(bool testMode, bool debugMode)
    {
        _enabled = testMode && debugMode;
    }

    /// <summary>
    /// 是否已启用
    /// </summary>
    public bool IsEnabled => _enabled;

    /// <summary>
    /// 异步初始化（从内嵌资源提取数据库）
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!_enabled || _initialized) return;

        try
        {
            var dbBytes = ExtractEmbeddedDatabase();
            if (dbBytes == null || dbBytes.Length == 0)
            {
                _enabled = false;
                return;
            }

            // 写入临时文件
            var tempPath = Path.Combine(Path.GetTempPath(), $"mct_error_db_{Guid.NewGuid():N}.sqlite");
            await File.WriteAllBytesAsync(tempPath, dbBytes);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = tempPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            await _connection.OpenAsync();
            _initialized = true;
        }
        catch
        {
            _enabled = false;
        }
    }

    /// <summary>
    /// 匹配异常，返回已知错误信息
    /// </summary>
    public async Task<ErrorMatchResult> MatchAsync(Exception exception)
    {
        if (!_enabled || !_initialized || _connection == null)
            return new ErrorMatchResult { Found = false };

        try
        {
            var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
            var message = exception.Message ?? "";

            // 先精确匹配异常类型 + 消息模式
            var result = await QueryAsync(exceptionType, message);
            if (result.Found) return result;

            // 再仅按异常类型匹配
            result = await QueryByTypeAsync(exceptionType);
            if (result.Found) return result;

            // 检查 InnerException
            if (exception.InnerException != null)
            {
                var innerResult = await MatchAsync(exception.InnerException);
                if (innerResult.Found)
                {
                    innerResult.Confidence *= 0.8; // 内部异常匹配置信度略低
                    return innerResult;
                }
            }

            return new ErrorMatchResult { Found = false };
        }
        catch
        {
            return new ErrorMatchResult { Found = false };
        }
    }

    /// <summary>
    /// 格式化匹配结果为 debug 日志字符串
    /// </summary>
    public static string FormatDebugLog(ErrorMatchResult match, string pluginName, Exception exception)
    {
        if (!match.Found) return "";

        return $"[PluginErrorDB]插件 {pluginName} 异常匹配到已知问题:\n" +
               $"  类型: {match.ExceptionType}\n" +
               $"  描述: {match.Description}\n" +
               $"  建议: {match.Solution}\n" +
               $"  分类: {match.Category}\n" +
               $"  置信度: {match.Confidence:P0}";
    }

    private async Task<ErrorMatchResult> QueryAsync(string exceptionType, string message)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT exception_type, description, solution, category
            FROM known_errors
            WHERE exception_type = @type AND message_pattern IS NOT NULL
            ORDER BY CASE WHEN @msg LIKE '%' || message_pattern || '%' THEN 1 ELSE 2 END";
        cmd.Parameters.AddWithValue("@type", exceptionType);
        cmd.Parameters.AddWithValue("@msg", message);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ErrorMatchResult
            {
                Found = true,
                ExceptionType = reader.GetString(0),
                Description = reader.GetString(1),
                Solution = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Confidence = 0.9
            };
        }

        return new ErrorMatchResult { Found = false };
    }

    private async Task<ErrorMatchResult> QueryByTypeAsync(string exceptionType)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT exception_type, description, solution, category
            FROM known_errors
            WHERE exception_type = @type
            LIMIT 1";
        cmd.Parameters.AddWithValue("@type", exceptionType);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ErrorMatchResult
            {
                Found = true,
                ExceptionType = reader.GetString(0),
                Description = reader.GetString(1),
                Solution = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Confidence = 0.5
            };
        }

        return new ErrorMatchResult { Found = false };
    }

    private static byte[]? ExtractEmbeddedDatabase()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("error_db.sqlite", StringComparison.OrdinalIgnoreCase));

        if (resourceName == null) return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            var dbPath = _connection.DataSource;
            _connection.Close();
            _connection.Dispose();
            _connection = null;

            // 清理临时数据库文件
            if (!string.IsNullOrEmpty(dbPath) && File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }

        _initialized = false;
    }
}
