using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using Microsoft.Data.Sqlite;
using MorningCat.Config;
using System.Data.SqlClient;

namespace MorningCat.PluginAPI
{
    public interface IPluginDatabase
    {
        int ExecuteNonQuery(string sql, params DbParameter[] parameters);
        Task<int> ExecuteNonQueryAsync(string sql, params DbParameter[] parameters);
        object ExecuteScalar(string sql, params DbParameter[] parameters);
        Task<object> ExecuteScalarAsync(string sql, params DbParameter[] parameters);
        List<Dictionary<string, object>> Query(string sql, params DbParameter[] parameters);
        Task<List<Dictionary<string, object>>> QueryAsync(string sql, params DbParameter[] parameters);
        DbParameter CreateParameter(string name, object value);
        DataTable QueryTable(string sql, params DbParameter[] parameters);
        Task<DataTable> QueryTableAsync(string sql, params DbParameter[] parameters);
        string DatabasePath { get; }
        List<string> GetTableNames();
        List<ColumnInfo> GetColumns(string tableName);
    }

    public class ColumnInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool NotNull { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string DefaultValue { get; set; } = "";
    }

    public class DatabaseEntry
    {
        public string Key { get; set; } = "";
        public string Id { get; set; } = "";
        public string PluginClassName { get; set; } = "";
        public string DatabasePath { get; set; } = "";
        public string DatabaseType { get; set; } = "";
        public long FileSize { get; set; }
        public List<string> Tables { get; set; } = new();
    }

    public class PluginDatabaseAPI
    {
        private readonly DatabaseConfig _config;
        private readonly string _baseDirectory;
        private readonly Dictionary<string, IPluginDatabase> _databases = new();
        private readonly object _lock = new();

        public PluginDatabaseAPI(DatabaseConfig config)
        {
            _config = config;
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            _baseDirectory = string.IsNullOrEmpty(location)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(location);
        }

        public IPluginDatabase GetDatabase(string id, string pluginClassName)
        {
            var key = $"{id}-{pluginClassName}";
            lock (_lock)
            {
                if (_databases.TryGetValue(key, out var db))
                    return db;

                IPluginDatabase newDb;
                if (string.Equals(_config.Type, "sql", StringComparison.OrdinalIgnoreCase))
                {
                    newDb = new SqlDatabase(_config.ConnectionString, id, pluginClassName);
                }
                else
                {
                    newDb = new SqliteDatabase(_baseDirectory, id, pluginClassName);
                }

                _databases[key] = newDb;
                return newDb;
            }
        }

        public List<DatabaseEntry> GetAllDatabases()
        {
            var result = new List<DatabaseEntry>();
            lock (_lock)
            {
                foreach (var kvp in _databases)
                {
                    var parts = kvp.Key.Split('-', 2);
                    var entry = new DatabaseEntry
                    {
                        Key = kvp.Key,
                        Id = parts.Length > 0 ? parts[0] : "",
                        PluginClassName = parts.Length > 1 ? parts[1] : "",
                        DatabasePath = kvp.Value.DatabasePath,
                        DatabaseType = _config.Type,
                        Tables = kvp.Value.GetTableNames()
                    };

                    if (File.Exists(kvp.Value.DatabasePath))
                    {
                        entry.FileSize = new FileInfo(kvp.Value.DatabasePath).Length;
                    }

                    result.Add(entry);
                }
            }
            return result;
        }

        public IPluginDatabase? GetDatabaseByKey(string key)
        {
            lock (_lock)
            {
                return _databases.TryGetValue(key, out var db) ? db : null;
            }
        }

        public List<DatabaseEntry> ScanDatabaseFiles()
        {
            var result = GetAllDatabases();

            if (string.Equals(_config.Type, "sql", StringComparison.OrdinalIgnoreCase))
                return result;

            var dbDir = Path.Combine(_baseDirectory, "Database");
            if (!Directory.Exists(dbDir))
                return result;

            var existingKeys = new HashSet<string>(_databases.Keys);

            foreach (var file in Directory.GetFiles(dbDir, "*.db"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (existingKeys.Contains(fileName))
                    continue;

                var dashIdx = fileName.IndexOf('-');
                var entry = new DatabaseEntry
                {
                    Key = fileName,
                    Id = dashIdx >= 0 ? fileName[..dashIdx] : fileName,
                    PluginClassName = dashIdx >= 0 ? fileName[(dashIdx + 1)..] : "",
                    DatabasePath = file,
                    DatabaseType = "sqlite",
                    FileSize = new FileInfo(file).Length,
                    Tables = new List<string>()
                };

                try
                {
                    using var conn = new SqliteConnection($"Data Source={file}");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        entry.Tables.Add(reader.GetString(0));
                    }
                }
                catch { }

                result.Add(entry);
            }

            return result;
        }
    }

    internal class SqliteDatabase : IPluginDatabase
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public string DatabasePath => _dbPath;

        public SqliteDatabase(string baseDirectory, string id, string pluginClassName)
        {
            var dbDir = Path.Combine(baseDirectory, "Database");
            if (!Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            _dbPath = Path.Combine(dbDir, $"{id}-{pluginClassName}.db");
            _connectionString = $"Data Source={_dbPath}";
            Log.Info($"[数据库] SQLite数据库已创建: {_dbPath}");
        }

        private SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);

        public DbParameter CreateParameter(string name, object value)
        {
            return new SqliteParameter(name, value ?? DBNull.Value);
        }

        public List<string> GetTableNames()
        {
            var tables = new List<string>();
            if (!File.Exists(_dbPath)) return tables;
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            catch { }
            return tables;
        }

        public List<ColumnInfo> GetColumns(string tableName)
        {
            var columns = new List<ColumnInfo>();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = reader.GetString(1),
                        Type = reader.GetString(2),
                        NotNull = reader.GetInt32(3) == 1,
                        DefaultValue = reader.IsDBNull(4) ? "" : reader.GetValue(4)?.ToString() ?? "",
                        IsPrimaryKey = reader.GetInt32(5) == 1
                    });
                }
            }
            catch { }
            return columns;
        }

        public int ExecuteNonQuery(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return cmd.ExecuteNonQuery();
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return await cmd.ExecuteNonQueryAsync();
        }

        public object ExecuteScalar(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return cmd.ExecuteScalar();
        }

        public async Task<object> ExecuteScalarAsync(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return await cmd.ExecuteScalarAsync();
        }

        public List<Dictionary<string, object>> Query(string sql, params DbParameter[] parameters)
        {
            var results = new List<Dictionary<string, object>>();
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row[reader.GetName(i)] = val == DBNull.Value ? null : val;
                }
                results.Add(row);
            }
            return results;
        }

        public async Task<List<Dictionary<string, object>>> QueryAsync(string sql, params DbParameter[] parameters)
        {
            var results = new List<Dictionary<string, object>>();
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row[reader.GetName(i)] = val == DBNull.Value ? null : val;
                }
                results.Add(row);
            }
            return results;
        }

        public DataTable QueryTable(string sql, params DbParameter[] parameters)
        {
            var dt = new DataTable();
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
            return dt;
        }

        public async Task<DataTable> QueryTableAsync(string sql, params DbParameter[] parameters)
        {
            var dt = new DataTable();
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = await cmd.ExecuteReaderAsync();
            dt.Load(reader);
            return dt;
        }
    }

    internal class SqlDatabase : IPluginDatabase
    {
        private readonly string _connectionString;
        private readonly string _id;
        private readonly string _pluginClassName;

        public string DatabasePath => $"sql://{_id}-{_pluginClassName}";

        public SqlDatabase(string connectionString, string id, string pluginClassName)
        {
            _connectionString = connectionString;
            _id = id;
            _pluginClassName = pluginClassName;
            Log.Info($"[数据库] SQL数据库已连接: {_id}-{_pluginClassName}");
        }

        private SqlConnection CreateConnection() => new SqlConnection(_connectionString);

        public DbParameter CreateParameter(string name, object value)
        {
            return new SqlParameter(name, value ?? DBNull.Value);
        }

        public List<string> GetTableNames()
        {
            var tables = new List<string>();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            catch { }
            return tables;
        }

        public List<ColumnInfo> GetColumns(string tableName)
        {
            var columns = new List<ColumnInfo>();
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='{tableName}' ORDER BY ORDINAL_POSITION";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = reader.GetString(0),
                        Type = reader.GetString(1),
                        NotNull = reader.GetString(2) == "NO",
                        DefaultValue = reader.IsDBNull(3) ? "" : reader.GetValue(3)?.ToString() ?? ""
                    });
                }
            }
            catch { }
            return columns;
        }

        public int ExecuteNonQuery(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return cmd.ExecuteNonQuery();
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return await cmd.ExecuteNonQueryAsync();
        }

        public object ExecuteScalar(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return cmd.ExecuteScalar();
        }

        public async Task<object> ExecuteScalarAsync(string sql, params DbParameter[] parameters)
        {
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            return await cmd.ExecuteScalarAsync();
        }

        public List<Dictionary<string, object>> Query(string sql, params DbParameter[] parameters)
        {
            var results = new List<Dictionary<string, object>>();
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row[reader.GetName(i)] = val == DBNull.Value ? null : val;
                }
                results.Add(row);
            }
            return results;
        }

        public async Task<List<Dictionary<string, object>>> QueryAsync(string sql, params DbParameter[] parameters)
        {
            var results = new List<Dictionary<string, object>>();
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row[reader.GetName(i)] = val == DBNull.Value ? null : val;
                }
                results.Add(row);
            }
            return results;
        }

        public DataTable QueryTable(string sql, params DbParameter[] parameters)
        {
            var dt = new DataTable();
            using var conn = CreateConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
            return dt;
        }

        public async Task<DataTable> QueryTableAsync(string sql, params DbParameter[] parameters)
        {
            var dt = new DataTable();
            using var conn = CreateConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            using var reader = await cmd.ExecuteReaderAsync();
            dt.Load(reader);
            return dt;
        }
    }
}
