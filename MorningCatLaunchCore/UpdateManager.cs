using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MorningCatLaunchCore
{
    public class UpdateProgress
    {
        public string Phase { get; set; } = "";
        public int CurrentFile { get; set; }
        public int TotalFiles { get; set; }
        public string CurrentFileName { get; set; } = "";
        public double Percent { get; set; }
    }

    public class DirectoryListingResponse
    {
        [JsonPropertyName("currentPath")]
        public string CurrentPath { get; set; } = "";

        [JsonPropertyName("directoryHash")]
        public string DirectoryHash { get; set; } = "";

        [JsonPropertyName("directories")]
        public List<DirectoryEntry> Directories { get; set; } = new();

        [JsonPropertyName("files")]
        public List<RemoteFileEntry> Files { get; set; } = new();
    }

    public class DirectoryEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("lastModified")]
        public string LastModified { get; set; } = "";

        [JsonPropertyName("canView")]
        public bool CanView { get; set; }

        [JsonPropertyName("canDownload")]
        public bool CanDownload { get; set; }
    }

    public class RemoteFileEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("lastModified")]
        public string LastModified { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("previewable")]
        public bool Previewable { get; set; }

        [JsonPropertyName("canView")]
        public bool CanView { get; set; }

        [JsonPropertyName("canDownload")]
        public bool CanDownload { get; set; }

        [JsonPropertyName("canPreview")]
        public bool CanPreview { get; set; }
    }

    public class UpdateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string RepoDllPath { get; set; } = "";
        public int FilesUpdated { get; set; }
        public int FilesChecked { get; set; }
    }

    public static class LaunchCore
    {
        private const int ExitCodeRestart = 100;

        private static readonly string _primaryServer = "https://110.42.98.47:59113";
        private static readonly string _wsUrl = "wss://110.42.98.47:59113/ws?path=MorningCat/Core";
        private static readonly HttpClient _httpClient;
        private static readonly string _launchCorePath;
        private static volatile bool _restartRequested;
        private static int _wsHeartbeatIntervalHours = 12;
        private static bool _enableWsMonitor = false;
        private static DownloadSourceType _forceSource = DownloadSourceType.Auto;
        private static DownloadSourceProvider? _sourceProvider;

        static LaunchCore()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            _launchCorePath = Path.Combine(AppContext.BaseDirectory, "MorningCatLaunchCore.dll");
        }

        /// <summary>
        /// 创建带完整浏览器标准请求头的 HttpRequestMessage
        /// </summary>
        internal static HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            var request = new HttpRequestMessage(method, url);
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows NT 10.0"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "X11; Linux"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Macintosh; Intel Mac OS X 10_15_7" : "Unknown";
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                Architecture.X86 => "i686",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
            };
            var version = Assembly.GetExecutingAssembly().GetName().Version;

            request.Headers.Add("User-Agent", $"Mozilla/5.0 ({os}; {arch}) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36 MorningCatLaunchCore/{version}");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            request.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            request.Headers.Add("Cache-Control", "no-cache");
            request.Headers.Add("Sec-Fetch-Dest", "document");
            request.Headers.Add("Sec-Fetch-Mode", "navigate");
            request.Headers.Add("Sec-Fetch-Site", "none");
            request.Headers.Add("Sec-Fetch-User", "?1");
            request.Headers.Add("Upgrade-Insecure-Requests", "1");
            return request;
        }

        public static int Run(string[] args)
        {
            Console.WriteLine("[MLC] LaunchCore starting...");

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--enable-ws-monitor")
                {
                    _enableWsMonitor = true;
                    Console.WriteLine("[MLC] WS monitor enabled");
                }
                else if (args[i] == "--ws-heartbeat-hours" && i + 1 < args.Length && int.TryParse(args[i + 1], out var hours) && hours > 0)
                {
                    _wsHeartbeatIntervalHours = hours;
                    Console.WriteLine($"[MLC] WS heartbeat interval set to {hours} hour(s)");
                }
                else if (args[i] == "--source" && i + 1 < args.Length)
                {
                    var sourceArg = args[i + 1].ToLower();
                    _forceSource = sourceArg switch
                    {
                        "github" => DownloadSourceType.GitHub,
                        "official" => DownloadSourceType.Official,
                        _ => DownloadSourceType.Auto
                    };
                    Console.WriteLine($"[MLC] 下载源强制设置为: {_forceSource}");
                }
            }

            // 初始化下载源提供者并测试延迟
            _sourceProvider = new DownloadSourceProvider(_httpClient);
            var selectedSource = _sourceProvider.TestAndSelectBestAsync(_forceSource).GetAwaiter().GetResult();

            bool updateServerReachable = false;

            var selfUpdateResult = SelfUpdateCheck(selectedSource);
            if (!selfUpdateResult.shouldContinue)
            {
                Console.WriteLine("[MLC] Self-update applied, returning restart code...");
                return ExitCodeRestart;
            }
            updateServerReachable = selfUpdateResult.serverReachable;

            var coreUpdateResult = CheckAndUpdateCore(args, selectedSource).GetAwaiter().GetResult();
            if (!updateServerReachable && coreUpdateResult == null)
            {
                Console.WriteLine("[MLC] Update server unreachable, skipping WS monitor");
            }
            else if (coreUpdateResult != null)
            {
                updateServerReachable = true;
            }

            string? repoDllPath = coreUpdateResult?.RepoDllPath;
            if (string.IsNullOrEmpty(repoDllPath))
            {
                var fallbackDll = FindLocalRepoDll(AppContext.BaseDirectory);
                if (fallbackDll == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[MLC] MorningCat.dll not found, cannot start");
                    Console.ResetColor();
                    Thread.Sleep(5000);
                    return 1;
                }
                repoDllPath = fallbackDll;
            }

            Console.WriteLine($"[MLC] Loading MorningCat from: {repoDllPath}");

            CancellationTokenSource? wsCts = null;
            if (_enableWsMonitor && updateServerReachable)
            {
                wsCts = new CancellationTokenSource();
                var wsThread = new Thread(() => WatchForUpdates(wsCts.Token))
                {
                    IsBackground = true,
                    Name = "WS-Watcher"
                };
                wsThread.Start();
            }

            bool needRestart = LoadAndRunCore(repoDllPath, args);

            wsCts?.Cancel();

            if (needRestart || _restartRequested)
            {
                Console.WriteLine("[MLC] Restart requested, returning restart code...");
                return ExitCodeRestart;
            }

            return 0;
        }

        public static void RequestRestart()
        {
            Console.WriteLine("[MLC] Restart requested via internal API");
            Volatile.Write(ref _restartRequested, true);
        }

        private static (bool shouldContinue, bool serverReachable) SelfUpdateCheck(DownloadSource source)
        {
            try
            {
                Console.WriteLine("[MLC] Checking LaunchCore self-update...");

                if (source.Type == DownloadSourceType.GitHub)
                {
                    return SelfUpdateCheckGitHub(source);
                }

                return SelfUpdateCheckOfficial(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] Self-update check failed: {ex.Message}, continuing with local version");
                return (true, false);
            }
        }

        private static (bool shouldContinue, bool serverReachable) SelfUpdateCheckGitHub(DownloadSource source)
        {
            try
            {
                var version = _sourceProvider!.GetLatestGitHubVersionAsync().GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(version))
                {
                    Console.WriteLine("[MLC] GitHub: 无法获取最新版本，跳过自更新");
                    return (true, false);
                }

                var downloadUrl = _sourceProvider.GetGitHubMlcDownloadUrl(version);
                Console.WriteLine($"[MLC] GitHub: MLC 下载链接: {downloadUrl}");

                // 下载 zip 到临时目录
                var tempDir = Path.Combine(Path.GetTempPath(), $"mlc_update_{Guid.NewGuid():N}");
                var zipPath = Path.Combine(tempDir, "mlc.zip");

                try
                {
                    Directory.CreateDirectory(tempDir);
                    DownloadZipAsync(downloadUrl, zipPath).GetAwaiter().GetResult();
                    ExtractZip(zipPath, AppContext.BaseDirectory);

                    Console.WriteLine("[MLC] GitHub: 自更新完成，需要重启");
                    return (false, true);
                }
                finally
                {
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] GitHub 自更新失败: {ex.Message}，回退到本地版本");
                return (true, false);
            }
        }

        private static (bool shouldContinue, bool serverReachable) SelfUpdateCheckOfficial(DownloadSource source)
        {
            try
            {
                var listing = FetchDirectoryListingAsync("MorningCat/LaunchCore", CancellationToken.None).GetAwaiter().GetResult();
                if (listing == null)
                {
                    Console.WriteLine("[MLC] Failed to fetch LaunchCore file list, skipping self-update");
                    return (true, false);
                }

                var remoteFiles = FetchAllRemoteFilesAsync("MorningCat/LaunchCore", listing, null, CancellationToken.None).GetAwaiter().GetResult();

                var diffFiles = new List<RemoteFileWithRelativePath>();
                foreach (var file in remoteFiles)
                {
                    var localPath = Path.Combine(AppContext.BaseDirectory, file.RelativePath);
                    if (!File.Exists(localPath))
                    {
                        diffFiles.Add(file);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(file.File.Sha256))
                    {
                        var localHash = ComputeFileHashAsync(localPath).GetAwaiter().GetResult();
                        if (!string.Equals(localHash, file.File.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            diffFiles.Add(file);
                        }
                    }
                }

                if (diffFiles.Count == 0)
                {
                    Console.WriteLine("[MLC] Self-update: LaunchCore is up to date");
                    return (true, true);
                }

                Console.WriteLine($"[MLC] Self-update: {diffFiles.Count} file(s) need update");

                foreach (var file in diffFiles)
                {
                    var localPath = Path.Combine(AppContext.BaseDirectory, file.RelativePath);
                    var dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var downloadUrl = $"{_primaryServer}/api/download/{file.RemotePath}";
                    Console.WriteLine($"[MLC] Self-update downloading: {file.RelativePath}");
                    DownloadFileAsync(downloadUrl, localPath).GetAwaiter().GetResult();
                }

                Console.WriteLine("[MLC] Self-update: all files updated, restart required");
                return (false, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] Official self-update failed: {ex.Message}, continuing with local version");
                return (true, false);
            }
        }

        private static async Task<UpdateResult?> CheckAndUpdateCore(string[] args, DownloadSource source)
        {
            try
            {
                if (source.Type == DownloadSourceType.GitHub)
                {
                    return await CheckAndUpdateCoreGitHub(source);
                }

                return await CheckAndUpdateCoreOfficial(source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] Core update check failed: {ex.Message}");
                return null;
            }
        }

        private static async Task<UpdateResult?> CheckAndUpdateCoreGitHub(DownloadSource source)
        {
            try
            {
                var version = await _sourceProvider!.GetLatestGitHubVersionAsync();
                if (string.IsNullOrEmpty(version))
                {
                    Console.WriteLine("[MLC] GitHub: 无法获取最新核心版本");
                    return null;
                }

                var downloadUrl = _sourceProvider.GetGitHubCoreDownloadUrl(version);
                Console.WriteLine($"[MLC] GitHub: Core 下载链接: {downloadUrl}");

                var repoCorePath = Path.Combine(AppContext.BaseDirectory, "MorningCatCore");

                // 简单版本检查：如果本地已有该版本则跳过
                var versionFile = Path.Combine(repoCorePath, ".version");
                if (File.Exists(versionFile) && (await File.ReadAllTextAsync(versionFile)).Trim() == version)
                {
                    Console.WriteLine($"[MLC] GitHub: 核心已是最新版本 {version}");
                    return new UpdateResult
                    {
                        Success = true,
                        Message = "Core is up to date",
                        RepoDllPath = FindRepoDll(repoCorePath),
                        FilesUpdated = 0,
                        FilesChecked = 0
                    };
                }

                Console.WriteLine($"[MLC] GitHub: 下载核心 {version}...");

                var tempDir = Path.Combine(Path.GetTempPath(), $"core_update_{Guid.NewGuid():N}");
                var zipPath = Path.Combine(tempDir, "core.zip");

                try
                {
                    Directory.CreateDirectory(tempDir);
                    await DownloadZipAsync(downloadUrl, zipPath);

                    if (!Directory.Exists(repoCorePath))
                        Directory.CreateDirectory(repoCorePath);

                    ExtractZip(zipPath, repoCorePath);

                    // 写入版本标记
                    await File.WriteAllTextAsync(versionFile, version);

                    return new UpdateResult
                    {
                        Success = true,
                        Message = $"Core updated to {version} via GitHub",
                        RepoDllPath = FindRepoDll(repoCorePath),
                        FilesUpdated = -1,
                        FilesChecked = -1
                    };
                }
                finally
                {
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] GitHub 核心更新失败: {ex.Message}");
                return null;
            }
        }

        private static async Task<UpdateResult?> CheckAndUpdateCoreOfficial(DownloadSource source)
        {
            try
            {
                Console.WriteLine("[MLC] Checking for core updates...");

                var listing = await FetchDirectoryListingAsync("MorningCat/Core", CancellationToken.None);
                if (listing == null)
                {
                    Console.WriteLine("[MLC] Failed to fetch version list from server");
                    return null;
                }

                var latestVersion = listing.Directories
                    .Select(d => d.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderByDescending(n => n, new VersionComparer())
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(latestVersion))
                {
                    Console.WriteLine("[MLC] No valid version found");
                    return null;
                }

                Console.WriteLine($"[MLC] Latest version: {latestVersion}");

                var versionListing = await FetchDirectoryListingAsync($"MorningCat/Core/{latestVersion}", CancellationToken.None);
                if (versionListing == null)
                {
                    Console.WriteLine($"[MLC] Failed to fetch file list for version {latestVersion}");
                    return null;
                }

                var remoteFiles = await FetchAllRemoteFilesAsync($"MorningCat/Core/{latestVersion}", versionListing, null, CancellationToken.None);
                var versionPath = $"MorningCat/Core/{latestVersion}";

                var repoCorePath = Path.Combine(AppContext.BaseDirectory, "MorningCatCore");

                if (!Directory.Exists(repoCorePath))
                {
                    Console.WriteLine("[MLC] MorningCatCore folder not found, downloading all files...");
                    Directory.CreateDirectory(repoCorePath);

                    var downloadResult = await DownloadAllFilesWithRelativePathAsync(
                        repoCorePath, versionPath, remoteFiles, null, CancellationToken.None);

                    if (!downloadResult)
                    {
                        return new UpdateResult
                        {
                            Success = false,
                            Message = "Failed to download core files"
                        };
                    }

                    return new UpdateResult
                    {
                        Success = true,
                        Message = "All core files downloaded",
                        RepoDllPath = FindRepoDll(repoCorePath),
                        FilesUpdated = remoteFiles.Count,
                        FilesChecked = remoteFiles.Count
                    };
                }

                var diffFiles = await ComputeDiffWithRelativePathAsync(
                    repoCorePath, remoteFiles, null, CancellationToken.None);

                CleanupLocalFiles(repoCorePath, remoteFiles);

                if (diffFiles.Count == 0)
                {
                    Console.WriteLine("[MLC] All files are up to date");
                    return new UpdateResult
                    {
                        Success = true,
                        Message = "All files are up to date",
                        RepoDllPath = FindRepoDll(repoCorePath),
                        FilesUpdated = 0,
                        FilesChecked = remoteFiles.Count
                    };
                }

                Console.WriteLine($"[MLC] Found {diffFiles.Count} file(s) to update");

                return await ApplyUpdatesWithRelativePathAsync(
                    repoCorePath, versionPath, diffFiles, remoteFiles, null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] Core update check failed: {ex.Message}");
                return null;
            }
        }

        private static bool LoadAndRunCore(string repoDllPath, string[] args)
        {
            var repoDir = Path.GetDirectoryName(repoDllPath)!;
            var loadContext = new System.Runtime.Loader.AssemblyLoadContext("MorningCatContext", true);
            var dllBytesCache = new ConcurrentDictionary<string, byte[]>();

            SetNativeResolver(repoDir);

            loadContext.Resolving += (context, assemblyName) =>
            {
                try { return System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName); } catch { }

                var dllPath = Path.Combine(repoDir, $"{assemblyName.Name}.dll");
                if (!File.Exists(dllPath)) return null;

                var bytes = dllBytesCache.GetOrAdd(dllPath, p =>
                {
                    using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var buf = new byte[fs.Length];
                    fs.ReadExactly(buf, 0, buf.Length);
                    return buf;
                });

                using var stream = new MemoryStream(bytes);
                return context.LoadFromStream(stream);
            };

            var mainDllBytes = File.ReadAllBytes(repoDllPath);
            using (var mainStream = new MemoryStream(mainDllBytes))
            {
                loadContext.LoadFromStream(mainStream);
            }

            foreach (var dll in Directory.GetFiles(repoDir, "*.dll"))
            {
                if (dll.Equals(repoDllPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(dll))); continue; } catch { }

                try
                {
                    var bytes = dllBytesCache.GetOrAdd(dll, p =>
                    {
                        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var buf = new byte[fs.Length];
                        fs.ReadExactly(buf, 0, buf.Length);
                        return buf;
                    });
                    using var stream = new MemoryStream(bytes);
                    loadContext.LoadFromStream(stream);
                }
                catch { }
            }

            var programType = loadContext.Assemblies
                .FirstOrDefault(a => a.GetName().Name == "MorningCat")
                ?.GetType("MorningCat.Program");

            if (programType == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[MLC] Could not find MorningCat.Program type");
                Console.ResetColor();
                Thread.Sleep(5000);
                return false;
            }

            foreach (var asm in loadContext.Assemblies)
            {
                try
                {
                    NativeLibrary.SetDllImportResolver(asm, (libraryName, assembly, searchPath) =>
                    {
                        var nativeSearchPaths = new[]
                        {
                            Path.Combine(repoDir, "runtimes", GetRuntimeIdentifier(), "native"),
                            Path.Combine(repoDir, "runtimes", GetRuntimeIdentifierNoArch(), "native"),
                            repoDir
                        };

                        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? new[] { ".dll" }
                            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                                ? new[] { ".dylib" }
                                : new[] { ".so", ".so.0" };

                        foreach (var searchDir in nativeSearchPaths)
                        {
                            if (!Directory.Exists(searchDir)) continue;
                            foreach (var ext in extensions)
                            {
                                var candidate = Path.Combine(searchDir, libraryName + ext);
                                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                                    return handle;

                                var candidateLib = Path.Combine(searchDir, "lib" + libraryName + ext);
                                if (File.Exists(candidateLib) && NativeLibrary.TryLoad(candidateLib, out var handle2))
                                    return handle2;
                            }
                        }
                        return IntPtr.Zero;
                    });
                }
                catch (InvalidOperationException) { }
            }

            bool needRestart = false;

            var mainMethodWithCallback = programType.GetMethod("MainWithRestartCallback",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

            if (mainMethodWithCallback != null)
            {
                var paramCount = mainMethodWithCallback.GetParameters().Length;

                Action restartCallback = () =>
                {
                    Console.WriteLine("[MLC] Restart callback triggered by MorningCat");
                    Volatile.Write(ref _restartRequested, true);
                    needRestart = true;
                };

                if (paramCount == 4)
                {
                    Action shutdownCallback = () =>
                    {
                        Volatile.Write(ref _restartRequested, true);
                        needRestart = true;
                    };
                    Action updateCallback = () =>
                    {
                        Volatile.Write(ref _restartRequested, true);
                        needRestart = true;
                    };
                    mainMethodWithCallback.Invoke(null, new object[] { args, restartCallback, shutdownCallback, updateCallback });
                }
                else if (paramCount == 2)
                {
                    mainMethodWithCallback.Invoke(null, new object[] { args, restartCallback });
                }
                else
                {
                    var mainMethod = programType.GetMethod("Main", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    mainMethod?.Invoke(null, new object[] { args });
                }
            }
            else
            {
                var mainMethod = programType.GetMethod("Main", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                mainMethod?.Invoke(null, new object[] { args });
            }

            dllBytesCache.Clear();
            try { loadContext.Unload(); } catch { }
            ForceGc();

            return needRestart;
        }

        private static void SetNativeResolver(string repoDir)
        {
            NativeLibrary.SetDllImportResolver(System.Reflection.Assembly.GetExecutingAssembly(), (libraryName, assembly, searchPath) =>
            {
                var nativeSearchPaths = new[]
                {
                    Path.Combine(repoDir, "runtimes", GetRuntimeIdentifier(), "native"),
                    Path.Combine(repoDir, "runtimes", GetRuntimeIdentifierNoArch(), "native"),
                    repoDir
                };

                var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? new[] { ".dll" }
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? new[] { ".dylib" }
                        : new[] { ".so", ".so.0" };

                foreach (var searchDir in nativeSearchPaths)
                {
                    if (!Directory.Exists(searchDir)) continue;
                    foreach (var ext in extensions)
                    {
                        var candidate = Path.Combine(searchDir, libraryName + ext);
                        if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                            return handle;

                        var candidateLib = Path.Combine(searchDir, "lib" + libraryName + ext);
                        if (File.Exists(candidateLib) && NativeLibrary.TryLoad(candidateLib, out var handle2))
                            return handle2;
                    }
                }
                return IntPtr.Zero;
            });
        }

        private static void WatchForUpdates(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_restartRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

                    Console.WriteLine($"[MLC] WS watcher connecting to {_wsUrl}...");
                    ws.ConnectAsync(new Uri(_wsUrl), ct).Wait(ct);

                    if (ws.State != WebSocketState.Open)
                    {
                        Console.WriteLine("[MLC] WS watcher failed to connect, retrying in 30s...");
                        Thread.Sleep(30000);
                        continue;
                    }

                    Console.WriteLine("[MLC] WS watcher connected, monitoring for remote changes...");

                    var buffer = new byte[4096];
                    var lastHeartbeat = DateTime.UtcNow;

                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested && !_restartRequested)
                    {
                        try
                        {
                            if ((DateTime.UtcNow - lastHeartbeat).TotalHours >= _wsHeartbeatIntervalHours)
                            {
                                var pingBytes = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");
                                ws.SendAsync(new ArraySegment<byte>(pingBytes), WebSocketMessageType.Text, true, ct).GetAwaiter().GetResult();
                                lastHeartbeat = DateTime.UtcNow;
                                Console.WriteLine("[MLC] WS heartbeat sent");
                            }

                            var receiveTask = ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                            receiveTask.Wait(TimeSpan.FromMinutes(1), ct);

                            if (!receiveTask.IsCompleted)
                                continue;

                            var result = receiveTask.GetAwaiter().GetResult();

                            if (result.MessageType == WebSocketMessageType.Close)
                                break;

                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                Console.WriteLine($"[MLC] WS message received: {message}");

                                try
                                {
                                    var doc = JsonDocument.Parse(message);
                                    if (doc.RootElement.TryGetProperty("type", out var typeElement))
                                    {
                                        var type = typeElement.GetString();
                                        if (type == "update" || type == "file_changed")
                                        {
                                            Console.WriteLine("[MLC] Remote update detected, requesting restart...");
                                            Volatile.Write(ref _restartRequested, true);
                                            break;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (OperationCanceledException) { break; }
                        catch (WebSocketException) { break; }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MLC] WS watcher error: {ex.Message}, retrying in 30s...");
                }

                if (!ct.IsCancellationRequested && !_restartRequested)
                    Thread.Sleep(30000);
            }
        }

        #region UpdateManager helpers (from old UpdateManager.cs)

        private static async Task<DirectoryListingResponse?> FetchDirectoryListingAsync(string path, CancellationToken ct)
        {
            try
            {
                var url = $"{_primaryServer}/api/files?path={Uri.EscapeDataString(path)}";
                var request = CreateRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    return JsonSerializer.Deserialize<DirectoryListingResponse>(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MLC] FetchDirectoryListing error: {ex.Message}");
            }
            return null;
        }

        private class RemoteFileWithRelativePath
        {
            public RemoteFileEntry File { get; set; } = null!;
            public string RelativePath { get; set; } = "";
            public string RemotePath { get; set; } = "";
        }

        private static string GetRuntimeIdentifier()
        {
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                     RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                     RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => "unknown"
            };
            return $"{os}-{arch}";
        }

        private static string GetRuntimeIdentifierNoArch()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                   RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                   RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";
        }

        private static async Task<List<RemoteFileWithRelativePath>> FetchAllRemoteFilesAsync(
            string basePath, DirectoryListingResponse listing,
            Action<UpdateProgress>? progressCallback, CancellationToken ct)
        {
            var allFiles = new List<RemoteFileWithRelativePath>();

            foreach (var file in listing.Files)
            {
                allFiles.Add(new RemoteFileWithRelativePath
                {
                    File = file,
                    RelativePath = file.Name,
                    RemotePath = $"{basePath}/{file.Name}"
                });
            }

            foreach (var dir in listing.Directories)
            {
                if (string.IsNullOrEmpty(dir.Name)) continue;

                if (dir.Name.Equals("runtimes", StringComparison.OrdinalIgnoreCase))
                {
                    var runtimeListing = await FetchDirectoryListingAsync($"{basePath}/runtimes", ct);
                    if (runtimeListing == null) continue;

                    var rid = GetRuntimeIdentifier();
                    var ridNoArch = GetRuntimeIdentifierNoArch();
                    var matchedDirs = runtimeListing.Directories
                        .Where(d => !string.IsNullOrEmpty(d.Name) &&
                                    (d.Name.Equals(rid, StringComparison.OrdinalIgnoreCase) ||
                                     d.Name.Equals(ridNoArch, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    foreach (var matchedDir in matchedDirs)
                    {
                        var subPath = $"{basePath}/runtimes/{matchedDir.Name}";
                        var subListing = await FetchDirectoryListingAsync(subPath, ct);
                        if (subListing == null) continue;

                        var subFiles = await FetchAllRemoteFilesAsync(subPath, subListing, progressCallback, ct);
                        foreach (var sf in subFiles)
                        {
                            sf.RelativePath = $"runtimes/{matchedDir.Name}/{sf.RelativePath}";
                            allFiles.Add(sf);
                        }
                    }
                }
                else
                {
                    var subPath = $"{basePath}/{dir.Name}";
                    var subListing = await FetchDirectoryListingAsync(subPath, ct);
                    if (subListing == null) continue;

                    var subFiles = await FetchAllRemoteFilesAsync(subPath, subListing, progressCallback, ct);
                    foreach (var sf in subFiles)
                    {
                        sf.RelativePath = $"{dir.Name}/{sf.RelativePath}";
                        allFiles.Add(sf);
                    }
                }
            }

            return allFiles;
        }

        private static void CleanupLocalFiles(string repoCorePath, List<RemoteFileWithRelativePath> remoteFiles)
        {
            if (!Directory.Exists(repoCorePath)) return;

            var remoteRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in remoteFiles)
                remoteRelativePaths.Add(f.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            var localFiles = Directory.GetFiles(repoCorePath, "*", SearchOption.AllDirectories);
            int deletedCount = 0;

            foreach (var localFile in localFiles)
            {
                var relativePath = Path.GetRelativePath(repoCorePath, localFile);
                if (!remoteRelativePaths.Contains(relativePath))
                {
                    try { File.Delete(localFile); deletedCount++; }
                    catch { }
                }
            }

            var localDirs = Directory.GetDirectories(repoCorePath, "*", SearchOption.AllDirectories)
                .OrderByDescending(d => d.Length);
            foreach (var dir in localDirs)
            {
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                catch { }
            }

            if (deletedCount > 0)
                Console.WriteLine($"[MLC] Cleaned up {deletedCount} orphaned file(s)");
        }

        private static async Task<List<RemoteFileWithRelativePath>> ComputeDiffWithRelativePathAsync(
            string repoCorePath, List<RemoteFileWithRelativePath> remoteFiles,
            Action<UpdateProgress>? progressCallback, CancellationToken ct)
        {
            var diffFiles = new List<RemoteFileWithRelativePath>();
            var maxConcurrency = Environment.ProcessorCount;
            var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task<RemoteFileWithRelativePath?>>();

            for (int i = 0; i < remoteFiles.Count; i++)
            {
                var file = remoteFiles[i];
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var localPath = Path.Combine(repoCorePath, file.RelativePath);
                        if (!File.Exists(localPath)) return file;
                        if (string.IsNullOrEmpty(file.File.Sha256)) return null;

                        var localHash = await ComputeFileHashAsync(localPath);
                        if (!string.Equals(localHash, file.File.Sha256, StringComparison.OrdinalIgnoreCase))
                            return file;

                        return null;
                    }
                    finally { semaphore.Release(); }
                }, ct));
            }

            var results = await Task.WhenAll(tasks);
            foreach (var r in results)
            {
                if (r != null) diffFiles.Add(r);
            }

            return diffFiles;
        }

        private static async Task<UpdateResult> ApplyUpdatesWithRelativePathAsync(
            string repoCorePath, string versionPath,
            List<RemoteFileWithRelativePath> diffFiles,
            List<RemoteFileWithRelativePath> allRemoteFiles,
            Action<UpdateProgress>? progressCallback, CancellationToken ct)
        {
            int updatedCount = 0;

            if (diffFiles.Count > 3)
            {
                Console.WriteLine($"[MLC] {diffFiles.Count} files to update, using batch download...");
                var batchResult = await DownloadAllFilesWithRelativePathAsync(
                    repoCorePath, versionPath, diffFiles, progressCallback, ct);
                if (!batchResult)
                    return new UpdateResult { Success = false, Message = "Batch download failed" };
                updatedCount = diffFiles.Count;
            }
            else
            {
                for (int i = 0; i < diffFiles.Count; i++)
                {
                    var file = diffFiles[i];
                    var localPath = Path.Combine(repoCorePath, file.RelativePath);

                    var dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var downloadUrl = $"{_primaryServer}/api/download/{file.RemotePath}";
                    Console.WriteLine($"[MLC] Downloading: {file.RelativePath}");

                    await DownloadFileAsync(downloadUrl, localPath);
                    updatedCount++;
                }
            }

            return new UpdateResult
            {
                Success = true,
                Message = $"Updated {updatedCount} file(s)",
                RepoDllPath = FindRepoDll(repoCorePath),
                FilesUpdated = updatedCount,
                FilesChecked = allRemoteFiles.Count
            };
        }

        private static async Task<bool> DownloadAllFilesWithRelativePathAsync(
            string repoCorePath, string versionPath,
            List<RemoteFileWithRelativePath> remoteFiles,
            Action<UpdateProgress>? progressCallback, CancellationToken ct)
        {
            int downloaded = 0;
            foreach (var file in remoteFiles)
            {
                var localPath = Path.Combine(repoCorePath, file.RelativePath);
                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var downloadUrl = $"{_primaryServer}/api/download/{file.RemotePath}";
                try
                {
                    await DownloadFileAsync(downloadUrl, localPath);
                    downloaded++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MLC] Failed to download {file.RelativePath}: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        private static async Task DownloadFileAsync(string url, string targetPath)
        {
            var tempPath = targetPath + ".tmp";
            try
            {
                var request = CreateRequest(HttpMethod.Get, url);
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }

                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                File.Move(tempPath, targetPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        private static async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string? FindRepoDll(string repoCorePath)
        {
            if (!Directory.Exists(repoCorePath)) return null;
            return Directory.GetFiles(repoCorePath, "MorningCat.dll", SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string? FindLocalRepoDll(string rootDir)
        {
            var repoCorePath = Path.Combine(rootDir, "MorningCatCore");
            return FindRepoDll(repoCorePath);
        }

        private static void ForceGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static async Task DownloadZipAsync(string url, string targetPath)
        {
            var request = CreateRequest(HttpMethod.Get, url);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }
        }

        private static void ExtractZip(string zipPath, string targetDir)
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
        }

        #endregion
    }

    public class VersionComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var xParts = x.Split('.', '-');
            var yParts = y.Split('.', '-');

            for (int i = 0; i < Math.Max(xParts.Length, yParts.Length); i++)
            {
                var xPart = i < xParts.Length ? xParts[i] : "0";
                var yPart = i < yParts.Length ? yParts[i] : "0";

                if (int.TryParse(xPart, out var xNum) && int.TryParse(yPart, out var yNum))
                {
                    if (xNum != yNum) return xNum.CompareTo(yNum);
                }
                else
                {
                    var cmp = string.Compare(xPart, yPart, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }

            return 0;
        }
    }
}
