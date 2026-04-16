using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MorningCatUpdataLib
{
    public class Updater
    {
        private static readonly HttpClient _httpClient;
        private const string BaseUrl = "https://110.42.98.47:59113";
        private const string TargetDirectory = "MorningCatCore";
        private static readonly object _consoleLock = new object();
        private static bool _verboseLogging = false;

        static Updater()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        /// <summary>
        /// 获取当前运行时的平台标识
        /// 格式: {os}-{arch}
        /// 例如: win-x64, linux-x64, osx-arm64, linux-arm64
        /// </summary>
        private static string GetCurrentRuntimeIdentifier()
        {
            string os;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                os = "win";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                os = "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                os = "osx";
            else
                os = "unknown";

            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "unknown"
            };

            return $"{os}-{arch}";
        }

        /// <summary>
        /// 获取当前平台对应的发布版本名称
        /// 例如: v1.0.0-release-linux64, v1.0.0-release-win64
        /// </summary>
        private static string GetPlatformVersionPattern()
        {
            var runtimeId = GetCurrentRuntimeIdentifier();
            
            // 映射运行时标识到版本后缀
            return runtimeId switch
            {
                "win-x64" => "win64",
                "win-x86" => "win32",
                "win-arm64" => "win-arm64",
                "linux-x64" => "linux64",
                "linux-arm64" => "linux-arm64",
                "linux-arm" => "linux-arm",
                "osx-x64" => "osx64",
                "osx-arm64" => "osx-arm64",
                _ => throw new NotSupportedException($"不支持的平台: {runtimeId}")
            };
        }

        /// <summary>
        /// 获取当前平台的显示名称（用于日志）
        /// </summary>
        private static string GetPlatformDisplayName()
        {
            var runtimeId = GetCurrentRuntimeIdentifier();
            return runtimeId switch
            {
                "win-x64" => "Windows 64位",
                "win-x86" => "Windows 32位",
                "win-arm64" => "Windows ARM64",
                "linux-x64" => "Linux 64位",
                "linux-arm64" => "Linux ARM64",
                "linux-arm" => "Linux ARM32",
                "osx-x64" => "macOS Intel",
                "osx-arm64" => "macOS Apple Silicon",
                _ => runtimeId
            };
        }

        public static async Task<bool> MorningCatUpdate(Action<string>? logCallback = null, 
            Action<int, int, string>? progressCallback = null)
        {
            Log(logCallback, "开始更新检查...", false);
            
            // 显示当前平台信息
            var platform = GetPlatformDisplayName();
            var platformSuffix = GetPlatformVersionPattern();
            Log(logCallback, $"当前平台: {platform} ({platformSuffix})", false);

            try
            {
                var rootDir = AppDomain.CurrentDomain.BaseDirectory;
                var targetDir = Path.Combine(rootDir, TargetDirectory);

                Log(logCallback, $"目标目录: {targetDir}", false);

                // 1. 获取 Core 目录下的版本列表
                var coreContent = await GetDirectoryContentAsync("MorningCat/Core");
                if (coreContent?.Directories == null || coreContent.Directories.Count == 0)
                {
                    Log(logCallback, "错误: 无法获取版本列表", false);
                    return false;
                }

                // 2. 根据当前平台查找对应的发布版本
                var platformSuffixLower = platformSuffix.ToLower();
                
                // 筛选出包含当前平台后缀的版本
                var matchedVersions = coreContent.Directories
                    .Where(d => d.Name.Contains("release", StringComparison.OrdinalIgnoreCase))
                    .Where(d => d.Name.ToLower().Contains(platformSuffixLower))
                    .OrderByDescending(d => d.Name)
                    .ToList();

                if (matchedVersions.Count == 0)
                {
                    // 如果没有找到精确匹配，尝试查找所有 release 版本并显示可用版本
                    var allReleases = coreContent.Directories
                        .Where(d => d.Name.Contains("release", StringComparison.OrdinalIgnoreCase))
                        .Select(d => d.Name)
                        .ToList();
                    
                    Log(logCallback, $"错误: 没有找到适用于 {platform} 的发布版本", false);
                    Log(logCallback, $"可用版本: {string.Join(", ", allReleases)}", false);
                    Log(logCallback, $"需要的后缀: {platformSuffix}", false);
                    return false;
                }

                var latestRelease = matchedVersions.First();
                Log(logCallback, $"找到匹配的发布版本: {latestRelease.Name} (适用于 {platform})", false);

                // 3. 获取 release 版本目录下的文件
                var versionContent = await GetDirectoryContentAsync(latestRelease.Url.TrimEnd('/'));
                if (versionContent?.Files == null || versionContent.Files.Count == 0)
                {
                    Log(logCallback, $"错误: 版本 {latestRelease.Name} 中没有找到文件", false);
                    return false;
                }

                var serverFiles = versionContent.Files;
                Log(logCallback, $"服务器上有 {serverFiles.Count} 个文件", false);

                // 4. 创建目标目录
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log(logCallback, $"创建目录: {targetDir}", false);
                }

                // 5. 获取本地已有文件及其哈希
                var localFiles = new Dictionary<string, string>();
                var existingFiles = Directory.GetFiles(targetDir);
                Log(logCallback, $"正在扫描本地文件...", false);
                
                for (int i = 0; i < existingFiles.Length; i++)
                {
                    var file = existingFiles[i];
                    var fileName = Path.GetFileName(file);
                    try
                    {
                        var hash = await ComputeFileSha256Async(file);
                        localFiles[fileName] = hash;
                        
                        if (i % 50 == 0 || i == existingFiles.Length - 1)
                        {
                            progressCallback?.Invoke(i + 1, existingFiles.Length, $"扫描本地文件: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(logCallback, $"读取本地文件哈希失败 {fileName}: {ex.Message}", true);
                    }
                }

                Log(logCallback, $"本地已有 {localFiles.Count} 个文件", false);

                // 6. 确定需要下载的文件
                var filesToDownload = new List<FileInfo>();
                var filesToSkip = 0;

                Log(logCallback, "正在对比文件差异...", false);
                
                foreach (var serverFile in serverFiles)
                {
                    if (localFiles.TryGetValue(serverFile.Name, out var localHash))
                    {
                        if (string.Equals(localHash, serverFile.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            filesToSkip++;
                            continue;
                        }
                        else
                        {
                            if (_verboseLogging)
                                Log(logCallback, $"文件需要更新: {serverFile.Name} (哈希不匹配)", true);
                        }
                    }
                    else
                    {
                        if (_verboseLogging)
                            Log(logCallback, $"文件需要下载: {serverFile.Name} (本地不存在)", true);
                    }
                    filesToDownload.Add(serverFile);
                }

                var totalToDownload = filesToDownload.Count;
                Log(logCallback, $"需要下载: {totalToDownload} 个文件, 跳过: {filesToSkip} 个文件", false);

                if (totalToDownload == 0)
                {
                    Log(logCallback, "所有文件都已是最新，无需下载", false);
                    progressCallback?.Invoke(1, 1, "已完成");
                    return true;
                }

                // 7. 下载缺失或更新的文件
                int downloaded = 0;
                int failed = 0;
                var failedFiles = new List<string>();

                for (int i = 0; i < filesToDownload.Count; i++)
                {
                    var file = filesToDownload[i];
                    var localPath = Path.Combine(targetDir, file.Name);
                    var currentIndex = i + 1;

                    var fileUrl = $"{BaseUrl}/api/download/{Uri.EscapeDataString(file.Url)}";
                    
                    if (_verboseLogging)
                        Log(logCallback, $"下载: {file.Name} ({FormatSize(file.Size)})", true);

                    try
                    {
                        await DownloadFileAsync(fileUrl, localPath, (progress) =>
                        {
                            progressCallback?.Invoke(currentIndex, totalToDownload, 
                                $"下载: {file.Name} - {progress}%");
                        });
                        
                        // 验证下载后的文件哈希
                        if (!string.IsNullOrEmpty(file.Sha256))
                        {
                            var downloadedHash = await ComputeFileSha256Async(localPath);
                            if (!string.Equals(downloadedHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new Exception($"哈希验证失败 (期望: {file.Sha256}, 实际: {downloadedHash})");
                            }
                        }
                        
                        downloaded++;
                        
                        if (_verboseLogging)
                            Log(logCallback, $"完成: {file.Name}", true);
                        else
                            progressCallback?.Invoke(currentIndex, totalToDownload, $"完成: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failedFiles.Add(file.Name);
                        Log(logCallback, $"下载失败: {file.Name} - {ex.Message}", true);
                        progressCallback?.Invoke(currentIndex, totalToDownload, $"失败: {file.Name}");
                        if (File.Exists(localPath))
                        {
                            try { File.Delete(localPath); } catch { }
                        }
                    }
                }

                var result = $"更新完成! 平台: {platform}, 下载: {downloaded}, 跳过: {filesToSkip}, 失败: {failed}";
                Log(logCallback, result, false);
                progressCallback?.Invoke(totalToDownload, totalToDownload, result);
                
                if (failed > 0)
                {
                    Log(logCallback, $"警告: {failed} 个文件下载失败: {string.Join(", ", failedFiles)}", false);
                    Log(logCallback, "请检查网络后重试", false);
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                var errorMsg = $"更新失败: {ex.Message}";
                Log(logCallback, errorMsg, false);
                progressCallback?.Invoke(0, 0, errorMsg);
                return false;
            }
        }

        // 保留原有的公共方法以保持兼容性
        [Obsolete("请使用 MorningCatUpdate 方法，此方法名有拼写错误")]
        public static Task MorningCatUpdata(Action<string>? logCallback = null, 
            Action<int, int, string>? progressCallback = null)
        {
            return MorningCatUpdate(logCallback, progressCallback);
        }

        /// <summary>
        /// 获取指定平台的版本信息（不执行下载）
        /// </summary>
        public static async Task<List<string>> GetAvailableVersionsForCurrentPlatform()
        {
            var versions = new List<string>();
            var platformSuffix = GetPlatformVersionPattern();
            
            try
            {
                var coreContent = await GetDirectoryContentAsync("MorningCat/Core");
                if (coreContent?.Directories != null)
                {
                    versions = coreContent.Directories
                        .Where(d => d.Name.Contains("release", StringComparison.OrdinalIgnoreCase))
                        .Where(d => d.Name.ToLower().Contains(platformSuffix.ToLower()))
                        .Select(d => d.Name)
                        .OrderByDescending(v => v)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取版本列表失败: {ex.Message}");
            }
            
            return versions;
        }

        private static async Task<DirectoryListResponse?> GetDirectoryContentAsync(string path)
        {
            var url = $"{BaseUrl}/api/files?path={Uri.EscapeDataString(path)}";
            var response = await _httpClient.GetStringAsync(url);
            return JsonSerializer.Deserialize<DirectoryListResponse>(response);
        }

        private static async Task DownloadFileAsync(string url, string localPath, Action<int>? progressCallback = null)
        {
            var tempPath = localPath + ".tmp";
            
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
                
                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;
                var buffer = new byte[8192];
                var lastProgress = -1;
                
                await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;
                        
                        if (totalBytes > 0 && progressCallback != null)
                        {
                            var progress = (int)((double)downloadedBytes / totalBytes * 100);
                            if (progress != lastProgress && (progress % 5 == 0 || progress == 100))
                            {
                                progressCallback(progress);
                                lastProgress = progress;
                            }
                        }
                    }
                }

                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }

                File.Move(tempPath, localPath);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        private static async Task<string> ComputeFileSha256Async(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        private static void Log(Action<string>? callback, string message, bool isVerbose)
        {
            if (isVerbose && !_verboseLogging)
                return;
                
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}";
            
            lock (_consoleLock)
            {
                Console.WriteLine(logMessage);
            }
            
            callback?.Invoke(message);
        }

        // 辅助方法：设置详细日志
        public static void SetVerboseLogging(bool enabled)
        {
            _verboseLogging = enabled;
        }
    }

    // 其他类保持不变...
    public class DirectoryListResponse
    {
        [JsonPropertyName("directories")]
        public List<DirectoryInfo>? Directories { get; set; }

        [JsonPropertyName("files")]
        public List<FileInfo>? Files { get; set; }

        [JsonPropertyName("currentPath")]
        public string? CurrentPath { get; set; }

        [JsonPropertyName("directoryHash")]
        public string? DirectoryHash { get; set; }
    }

    public class DirectoryInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("lastModified")]
        public string? LastModified { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("canView")]
        public bool CanView { get; set; }

        [JsonPropertyName("canDownload")]
        public bool CanDownload { get; set; }
    }

    public class FileInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("lastModified")]
        public string? LastModified { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; set; }

        [JsonPropertyName("previewable")]
        public bool Previewable { get; set; }

        [JsonPropertyName("canView")]
        public bool CanView { get; set; }

        [JsonPropertyName("canDownload")]
        public bool CanDownload { get; set; }

        [JsonPropertyName("canPreview")]
        public bool CanPreview { get; set; }
    }
}