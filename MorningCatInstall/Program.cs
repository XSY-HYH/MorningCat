using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MorningCatInstall
{
    class Program
    {
        private static readonly HttpClient _httpClient;
        private const string BaseUrl = "https://110.42.98.47:59113";
        private const string UpdateLibName = "MorningCatUpdataLib.dll";
        private const string UpdateLibUrl = "MorningCat/MorningCatUpdataLib.dll";
        private const string CoreDirectory = "MorningCatCore";
        private const string MainExe = "MorningCat.exe";
        private static readonly object _consoleLock = new object();
        private static int _progressTop = -1;

        static Program()
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

        static async Task Main(string[] args)
        {
            var rootDir = AppDomain.CurrentDomain.BaseDirectory;
            var updateLibPath = Path.Combine(rootDir, UpdateLibName);

            Console.Title = "MorningCat Installer";
            Console.WriteLine("========================================");
            Console.WriteLine("      MorningCat Installer v1.0");
            Console.WriteLine("========================================");
            Console.WriteLine();

            try
            {
                if (!File.Exists(updateLibPath))
                {
                    Log("未找到更新库，开始下载...");
                    await DownloadUpdateLibAsync(updateLibPath);
                }
                else
                {
                    Log("检查更新库版本...");
                    await CheckAndUpdateLibAsync(updateLibPath);
                }

                Log("加载更新库...");
                await ExecuteUpdateAsync(updateLibPath);

                var coreDir = Path.Combine(rootDir, CoreDirectory);
                var mainExePath = Path.Combine(coreDir, MainExe);

                if (File.Exists(mainExePath))
                {
                    Log("启动 MorningCat...");
                    StartMainProgram(mainExePath);
                    Log("安装程序即将退出...");
                    await Task.Delay(1000);
                    Environment.Exit(0);
                }
                else
                {
                    Log($"错误: 未找到主程序 {mainExePath}");
                    Log("请检查更新是否成功完成");
                    Console.WriteLine("按任意键退出...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Log($"发生错误: {ex.Message}");
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
        }

        private static async Task DownloadUpdateLibAsync(string localPath)
        {
            var url = $"{BaseUrl}/api/download/{Uri.EscapeDataString(UpdateLibUrl)}";

            var tempPath = localPath + ".tmp";
            
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                
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
                        
                        if (totalBytes > 0)
                        {
                            var progress = (int)((double)downloadedBytes / totalBytes * 100);
                            if (progress != lastProgress && (progress % 5 == 0 || progress == 100))
                            {
                                DrawProgress(progress, 100, "下载更新库");
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
                Log("更新库下载完成");
                ClearProgressLine();
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

        private static async Task CheckAndUpdateLibAsync(string localPath)
        {
            try
            {
                var url = $"{BaseUrl}/api/files?path=MorningCat";
                
                var response = await _httpClient.GetStringAsync(url);
                var dirList = JsonSerializer.Deserialize<DirectoryListResponse>(response);

                if (dirList?.Files == null)
                {
                    Log("无法获取服务器文件列表，使用本地版本");
                    return;
                }

                var serverFile = dirList.Files.FirstOrDefault(f => f.Name == UpdateLibName);
                if (serverFile == null)
                {
                    Log("服务器上未找到更新库文件，使用本地版本");
                    return;
                }

                var localHash = await ComputeFileSha256Async(localPath);

                if (!string.Equals(localHash, serverFile.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Log("发现新版本，正在更新...");
                    await DownloadUpdateLibAsync(localPath);
                }
                else
                {
                    Log("更新库已是最新版本");
                }
            }
            catch (Exception ex)
            {
                Log($"检查更新失败: {ex.Message}，使用本地版本");
            }
        }

        private static async Task ExecuteUpdateAsync(string dllPath)
        {
            var assembly = Assembly.LoadFrom(dllPath);
            var updaterType = assembly.GetType("MorningCatUpdataLib.Updater");
            
            if (updaterType == null)
            {
                throw new Exception("无法找到 Updater 类型");
            }

            // 设置详细日志为 false，减少输出
            var setVerboseMethod = updaterType.GetMethod("SetVerboseLogging");
            if (setVerboseMethod != null)
            {
                setVerboseMethod.Invoke(null, new object[] { false });
            }

            var method = updaterType.GetMethod("MorningCatUpdate");
            if (method == null)
            {
                method = updaterType.GetMethod("MorningCatUpdata");
                if (method == null)
                {
                    throw new Exception("无法找到 MorningCatUpdate 方法");
                }
            }

            // 关键修复：不传递 logCallback，因为 Updater 已经直接输出到控制台
            // 只传递 progressCallback 用于进度显示
            Action<string>? logCallback = null;  // 设为 null，避免重复输出
            Action<int, int, string>? progressCallback = (current, total, status) =>
            {
                DrawProgress(current, total, status);
            };

            var task = (Task?)method.Invoke(null, new object?[] { logCallback, progressCallback });
            if (task != null)
            {
                await task;
            }
            
            ClearProgressLine();
            Console.WriteLine();
        }

        private static void StartMainProgram(string exePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log($"启动主程序失败: {ex.Message}");
            }
        }

        private static async Task<string> ComputeFileSha256Async(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void DrawProgress(int current, int total, string status)
        {
            lock (_consoleLock)
            {
                if (total <= 0) return;
                
                var percent = (int)((double)current / total * 100);
                var barLength = 50;
                var filledLength = (int)((double)percent / 100 * barLength);
                
                var bar = new string('=', filledLength);
                if (filledLength < barLength)
                {
                    bar += ">" + new string(' ', barLength - filledLength - 1);
                }
                
                if (_progressTop == -1)
                {
                    _progressTop = Console.CursorTop;
                }
                
                Console.SetCursorPosition(0, _progressTop);
                Console.Write($"\r[{bar,-50}] {percent,3}% ({current}/{total})");
                
                var shortStatus = status.Length > 60 ? status.Substring(0, 57) + "..." : status;
                Console.SetCursorPosition(0, _progressTop + 1);
                Console.Write($"   {shortStatus,-60}");
            }
        }

        private static void ClearProgressLine()
        {
            if (_progressTop != -1)
            {
                Console.SetCursorPosition(0, _progressTop);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, _progressTop + 1);
                Console.Write(new string(' ', Console.WindowWidth));
                _progressTop = -1;
            }
        }

        private static void Log(string message)
        {
            lock (_consoleLock)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                
                if (_progressTop != -1)
                {
                    ClearProgressLine();
                }
                
                Console.WriteLine($"[{timestamp}] {message}");
            }
        }
    }

    // 数据类保持不变
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