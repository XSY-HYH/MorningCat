using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Config;
using MorningCat.WebUI;

namespace MorningCat.WebUI
{
    public class WebUIManager : ISystemInfoProvider, IBotInfoProvider, IPluginInfoProvider, ILogProvider, IConfigProvider
    {
        private WebUIServer? _server;
        private readonly ModuleManager _moduleManager;
        private readonly Dictionary<string, PluginMetadata> _pluginMetadata;
        private readonly Dictionary<string, string> _assemblyNameToModuleName;
        private readonly string _logDirectory;
        private DateTime _startTime;
        private string _version = "1.0.0";
        private WebUIConfig _config;
        private readonly ConfigManager _configManager;
        private readonly PluginConfigManager _pluginConfigManager;
        private BotInfo? _botInfo;
        private bool _isNapCatConnected = true;
        private readonly List<Action<LogEntry>> _logSubscribers = new List<Action<LogEntry>>();
        private readonly List<Action<string>> _rawLogSubscribers = new List<Action<string>>();
        private readonly object _logLock = new object();
        private readonly List<string> _recentLogs = new List<string>();
        private const int MaxRecentLogs = 100;
        private Func<Task>? _restartCallback;
        private Action? _shutdownCallback;

        public int Port => _server?.Port ?? 0;
        public bool IsRunning => _server?.IsRunning ?? false;

        public WebUIManager(
            ModuleManager moduleManager, 
            Dictionary<string, PluginMetadata> pluginMetadata, 
            WebUIConfig config, 
            ConfigManager configManager,
            Dictionary<string, string> assemblyNameToModuleName)
        {
            _moduleManager = moduleManager;
            _pluginMetadata = pluginMetadata;
            _config = config;
            _configManager = configManager;
            _assemblyNameToModuleName = assemblyNameToModuleName;
            _pluginConfigManager = new PluginConfigManager();
            
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _logDirectory = Path.Combine(exeDir ?? ".", "logs");
            
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                _version = $"{version.Major}.{version.Minor}.{version.Build}";
            }

            Log.OnLogOutput += OnLogOutput;
        }

        private void OnLogOutput(string logMessage)
        {
            lock (_logLock)
            {
                var logWithNewline = logMessage + "\n";
                _recentLogs.Add(logWithNewline);
                if (_recentLogs.Count > MaxRecentLogs)
                {
                    _recentLogs.RemoveAt(0);
                }

                foreach (var subscriber in _rawLogSubscribers.ToList())
                {
                    try
                    {
                        subscriber(logWithNewline);
                    }
                    catch
                    {
                    }
                }

                var parsed = ParseRawLogLine(logMessage);
                if (parsed != null)
                {
                    foreach (var subscriber in _logSubscribers.ToList())
                    {
                        try
                        {
                            subscriber(parsed);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private LogEntry? ParseRawLogLine(string rawLine)
        {
            try
            {
                var plainText = Regex.Replace(rawLine, "\u001b\\[[0-9;]*m", "");
                var parts = plainText.Split(new[] { " - " }, 4, StringSplitOptions.None);
                if (parts.Length >= 4)
                {
                    var timeStr = parts[0];
                    var level = parts[1];
                    var source = parts[2].Trim('[', ']');
                    var message = parts[3];

                    if (DateTime.TryParse(timeStr, out var time))
                    {
                        return new LogEntry
                        {
                            Time = time,
                            Level = level,
                            Source = source,
                            Message = message
                        };
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        public void SetStartTime(DateTime startTime)
        {
            _startTime = startTime;
        }

        public void SetBotInfo(long userId, string nickname, string qid, int level, bool isOnline)
        {
            _botInfo = new BotInfo
            {
                UserId = userId,
                Nickname = nickname,
                Qid = qid,
                Level = level,
                IsOnline = isOnline
            };
        }

        public void ClearBotInfo()
        {
            _botInfo = null;
        }

        public BotInfo? GetBotInfo()
        {
            if (_botInfo != null)
            {
                _botInfo.IsNapCatConnected = _isNapCatConnected;
            }
            return _botInfo;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _isNapCatConnected = isConnected;
            Log.Debug($"[WebUI] NapCat 连接状态更新: {(isConnected ? "已连接" : "已断开")}");
        }

        public async Task StartAsync()
        {
            if (_server != null && _server.IsRunning)
            {
                Log.Warning("WebUI 已在运行中");
                return;
            }

            _server = new WebUIServer(_config.Username, _config.Password);
            _server.SetSystemInfoProvider(this);
            _server.SetBotInfoProvider(this);
            _server.SetPluginInfoProvider(this);
            _server.SetLogProvider(this);
            _server.SetConfigProvider(this);
            _server.OnCredentialsChanged = OnCredentialsChanged;

            try
            {
                await _server.StartAsync(_config.Port);
                Log.Info($"WebUI 已启动: http://127.0.0.1:{_config.Port}");
                
                var accountService = _server.GetAccountService();
                var (username, password) = accountService.GetDefaultCredentials();
                Log.Info($"WebUI 默认账户: {username} / {password}");
            }
            catch (Exception ex)
            {
                Log.Error($"WebUI 启动失败: {ex.Message}");
            }
        }

        private void OnCredentialsChanged(string username, string password)
        {
            try
            {
                _configManager.UpdateConfig(config =>
                {
                    config.WebUI.Username = username;
                    config.WebUI.Password = password;
                });
                _config.Username = username;
                _config.Password = password;
                Log.Info("WebUI 凭据已更新并保存到配置文件");
            }
            catch (Exception ex)
            {
                Log.Error($"保存 WebUI 凭据失败: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            if (_server != null)
            {
                await _server.StopAsync();
                Log.Info("WebUI 已停止");
            }
        }

        public string GenerateLoginToken()
        {
            if (_server == null)
                return string.Empty;
            
            var accountService = _server.GetAccountService();
            return accountService.GenerateToken();
        }

        public string GetLoginUrl()
        {
            if (_server == null || !_server.IsRunning)
                return string.Empty;
            
            var token = GenerateLoginToken();
            return $"http://127.0.0.1:{_server.Port}/login?token={token}";
        }

        public SystemInfo GetSystemInfo()
        {
            var process = Process.GetCurrentProcess();
            var memoryUsedMB = process.WorkingSet64 / (1024 * 1024);
            var memoryTotalMB = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            
            var cpuUsage = GetCpuUsage();
            
            var allModules = _moduleManager.GetAllModules();
            var runningCount = allModules.Count(m => m.Status == ModuleStatus.Running);

            return new SystemInfo
            {
                Version = _version,
                MemoryUsedMB = memoryUsedMB,
                MemoryTotalMB = memoryTotalMB,
                CpuUsage = cpuUsage,
                CpuModel = GetCpuModel(),
                CpuSpeed = GetCpuSpeed(),
                Arch = RuntimeInformation.ProcessArchitecture.ToString(),
                PluginCount = allModules.Count,
                RunningPluginCount = runningCount,
                StartTime = _startTime,
                Uptime = DateTime.Now - _startTime
            };
        }

        public void RequestRestart()
        {
            Log.Info("[WebUI] 请求重启应用程序...");
            if (_restartCallback != null)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    await _restartCallback();
                });
            }
            else
            {
                Log.Warning("[WebUI] 未设置重启回调，使用默认实现");
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        Process.Start(exePath);
                    }
                    Environment.Exit(0);
                });
            }
        }

        public void RequestShutdown()
        {
            Log.Info("[WebUI] 请求关闭应用程序...");
            if (_shutdownCallback != null)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    _shutdownCallback();
                });
            }
            else
            {
                Log.Warning("[WebUI] 未设置关闭回调，使用默认实现");
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    Environment.Exit(0);
                });
            }
        }

        public void SetRestartCallback(Func<Task> restartCallback)
        {
            _restartCallback = restartCallback;
        }

        public void SetShutdownCallback(Action shutdownCallback)
        {
            _shutdownCallback = shutdownCallback;
        }

        private static string GetCpuModel()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    return key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var cpuInfo = File.ReadAllText("/proc/cpuinfo");
                    var modelLine = cpuInfo.Split('\n').FirstOrDefault(l => l.StartsWith("model name"));
                    return modelLine?.Split(':').LastOrDefault()?.Trim() ?? "Unknown";
                }
            }
            catch { }
            return "Unknown";
        }

        private static string GetCpuSpeed()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    var mhz = key?.GetValue("~MHz");
                    if (mhz != null)
                    {
                        var ghz = Convert.ToInt32(mhz) / 1000.0;
                        return ghz.ToString("F2");
                    }
                }
            }
            catch { }
            return "0";
        }

        private static double _lastCpuUsage = 0;
        private static DateTime _lastCpuCheck = DateTime.MinValue;
        private static readonly object _cpuLock = new object();

        private double GetCpuUsage()
        {
            lock (_cpuLock)
            {
                var process = Process.GetCurrentProcess();
                var currentTime = DateTime.UtcNow;
                
                if (_lastCpuCheck == DateTime.MinValue)
                {
                    _lastCpuUsage = process.TotalProcessorTime.TotalMilliseconds;
                    _lastCpuCheck = currentTime;
                    return 0;
                }

                var timeDiff = (currentTime - _lastCpuCheck).TotalMilliseconds;
                if (timeDiff < 1000)
                {
                    return _lastCpuUsage;
                }

                var cpuTimeDiff = process.TotalProcessorTime.TotalMilliseconds - _lastCpuUsage;
                var cpuUsage = (cpuTimeDiff / timeDiff) * 100 / Environment.ProcessorCount;
                
                _lastCpuUsage = cpuUsage;
                _lastCpuCheck = currentTime;
                
                return Math.Min(cpuUsage, 100);
            }
        }

        public List<PluginInfo> GetPlugins()
        {
            var result = new List<PluginInfo>();
            var allModules = _moduleManager.GetAllModules();

            Log.Debug($"[GetPlugins] 获取插件列表，共 {allModules.Count} 个模块");
            
            foreach (var module in allModules)
            {
                var info = new PluginInfo
                {
                    ModuleName = module.ModuleName,
                    Status = module.Status.ToString(),
                    IsBuiltin = IsBuiltinModule(module.ModuleName),
                    AssemblyPath = module.AssemblyPath
                };

                if (_pluginMetadata.TryGetValue(module.ModuleName, out var metadata))
                {
                    info.DisplayName = metadata.DisplayName;
                    info.Author = metadata.Author;
                    info.Description = metadata.Description;
                    info.IconBase64 = metadata.IconBase64;
                    Log.Debug($"[GetPlugins] 模块 {module.ModuleName}: DisplayName={metadata.DisplayName}, Author={metadata.Author}, HasIcon={!string.IsNullOrEmpty(metadata.IconBase64)}");
                }
                else
                {
                    Log.Debug($"[GetPlugins] 模块 {module.ModuleName}: 无元数据");
                }

                result.Add(info);
            }

            var modulesDir = ConfigManager.GetCorrectedPath("Modules");
            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    Log.Debug($"[GetPlugins] 发现 {disabledFiles.Length} 个禁用的插件");
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        
                        var info = new PluginInfo
                        {
                            ModuleName = fileName,
                            Status = "Disabled",
                            IsBuiltin = false,
                            AssemblyPath = file
                        };
                        
                        if (_assemblyNameToModuleName.TryGetValue(fileName, out var moduleName))
                        {
                            info.ModuleName = moduleName;
                            if (_pluginMetadata.TryGetValue(moduleName, out var metadata))
                            {
                                info.DisplayName = metadata.DisplayName;
                                info.Author = metadata.Author;
                                info.Description = metadata.Description;
                                info.IconBase64 = metadata.IconBase64;
                            }
                        }
                        else if (_pluginMetadata.TryGetValue(fileName, out var metadata))
                        {
                            info.DisplayName = metadata.DisplayName;
                            info.Author = metadata.Author;
                            info.Description = metadata.Description;
                            info.IconBase64 = metadata.IconBase64;
                        }
                        
                        result.Add(info);
                        Log.Debug($"[GetPlugins] 禁用插件: {fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[GetPlugins] 获取禁用插件失败: {ex.Message}");
            }

            Log.Debug($"[GetPlugins] 返回 {result.Count} 个插件");
            return result;
        }

        public PluginInfo? GetPlugin(string moduleName)
        {
            var plugins = GetPlugins();
            return plugins.FirstOrDefault(p => p.ModuleName == moduleName);
        }

        public bool DisablePlugin(string moduleName)
        {
            try
            {
                Log.Debug($"[DisablePlugin] 开始禁用插件: {moduleName}");
                
                var allModules = _moduleManager.GetAllModules();
                var module = allModules.FirstOrDefault(m => m.ModuleName == moduleName);
                
                if (module == null)
                {
                    Log.Warning($"[DisablePlugin] 未找到模块: {moduleName}");
                    return false;
                }

                var assemblyPath = module.AssemblyPath;
                if (string.IsNullOrEmpty(assemblyPath))
                {
                    Log.Warning($"[DisablePlugin] 插件路径无效: {moduleName}");
                    return false;
                }

                if (assemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug($"[DisablePlugin] 插件已被禁用: {moduleName}");
                    return true;
                }

                if (module.Status == ModuleStatus.Running)
                {
                    Log.Debug($"[DisablePlugin] 插件正在运行，尝试卸载...");
                    var success = _moduleManager.UnloadModuleAsync(moduleName).GetAwaiter().GetResult();
                    Log.Debug($"[DisablePlugin] 卸载结果: {success}");
                }

                var disabledPath = $"{assemblyPath}.disabled";
                Log.Debug($"[DisablePlugin] 检查文件: {assemblyPath}");
                
                if (File.Exists(assemblyPath))
                {
                    Log.Debug($"[DisablePlugin] 文件存在，尝试重命名为: {disabledPath}");
                    try
                    {
                        File.Move(assemblyPath, disabledPath);
                        Log.Info($"[DisablePlugin] 插件 {moduleName} 已禁用（重启后生效）");
                        return true;
                    }
                    catch (IOException ex)
                    {
                        Log.Debug($"[DisablePlugin] 文件被占用: {ex.Message}，创建标记文件");
                        File.WriteAllText(disabledPath, "");
                        Log.Info($"[DisablePlugin] 插件 {moduleName} 已标记为禁用（文件被占用，重启后生效）");
                        return true;
                    }
                }
                else
                {
                    Log.Warning($"[DisablePlugin] 文件不存在: {assemblyPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DisablePlugin] 禁用插件失败: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public bool EnablePlugin(string moduleName)
        {
            try
            {
                Log.Debug($"[EnablePlugin] 开始启用插件: {moduleName}");
                
                var modulesDir = ConfigManager.GetCorrectedPath("Modules");
                
                string? disabledPath = null;
                string? assemblyName = null;
                
                if (_assemblyNameToModuleName.TryGetValue(moduleName, out var mappedModuleName))
                {
                    assemblyName = moduleName;
                    disabledPath = Path.Combine(modulesDir, $"{assemblyName}.dll.disabled");
                    Log.Debug($"[EnablePlugin] 通过程序集名查找: {disabledPath}");
                }
                else
                {
                    var reverseMapping = _assemblyNameToModuleName.FirstOrDefault(x => x.Value == moduleName);
                    if (!string.IsNullOrEmpty(reverseMapping.Key))
                    {
                        assemblyName = reverseMapping.Key;
                        disabledPath = Path.Combine(modulesDir, $"{assemblyName}.dll.disabled");
                        Log.Debug($"[EnablePlugin] 通过模块名反查程序集名: {assemblyName} -> {disabledPath}");
                    }
                }
                
                if (string.IsNullOrEmpty(disabledPath) || !File.Exists(disabledPath))
                {
                    disabledPath = Path.Combine(modulesDir, $"{moduleName}.dll.disabled");
                    assemblyName = moduleName;
                    Log.Debug($"[EnablePlugin] 直接使用模块名查找: {disabledPath}");
                }
                
                Log.Debug($"[EnablePlugin] 检查禁用文件: {disabledPath}");
                
                if (!File.Exists(disabledPath))
                {
                    Log.Warning($"[EnablePlugin] 禁用文件不存在: {disabledPath}");
                    return false;
                }

                var dllPath = disabledPath.Substring(0, disabledPath.Length - ".disabled".Length);
                Log.Debug($"[EnablePlugin] 目标路径: {dllPath}");
                
                File.Move(disabledPath, dllPath);
                Log.Info($"[EnablePlugin] 插件 {moduleName} 已启用（重启后生效）");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[EnablePlugin] 启用插件失败: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public bool UnloadPlugin(string moduleName)
        {
            try
            {
                Log.Debug($"[UnloadPlugin] 开始卸载插件: {moduleName}");
                
                if (IsBuiltinModule(moduleName))
                {
                    Log.Warning($"[UnloadPlugin] 无法卸载内置模块: {moduleName}");
                    return false;
                }

                var dependents = _moduleManager.GetModulesDependentOn(moduleName);
                if (dependents != null && dependents.Count > 0)
                {
                    Log.Warning($"[UnloadPlugin] 无法卸载 {moduleName}: 以下插件依赖此模块: {string.Join(", ", dependents)}");
                    return false;
                }

                Log.Debug($"[UnloadPlugin] 调用 UnloadModuleAsync...");
                var success = _moduleManager.UnloadModuleAsync(moduleName).GetAwaiter().GetResult();
                Log.Debug($"[UnloadPlugin] UnloadModuleAsync 返回: {success}");
                
                if (success)
                {
                    _pluginMetadata.Remove(moduleName);
                    Log.Info($"[UnloadPlugin] 插件 {moduleName} 已卸载");
                    return true;
                }
                Log.Warning($"[UnloadPlugin] UnloadModuleAsync 返回 false");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[UnloadPlugin] 卸载插件失败: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public PluginDetail? GetPluginDetail(string moduleName)
        {
            Log.Debug($"[GetPluginDetail] 获取插件详情: {moduleName}");
            
            var allModules = _moduleManager.GetAllModules();
            var module = allModules.FirstOrDefault(m => m.ModuleName == moduleName);
            
            if (module != null)
            {
                Log.Debug($"[GetPluginDetail] 找到模块: {module.ModuleName}, 状态: {module.Status}");
                
                var detail = new PluginDetail
                {
                    ModuleName = module.ModuleName,
                    Status = module.Status.ToString(),
                    IsBuiltin = IsBuiltinModule(module.ModuleName),
                    ModuleType = module.ModuleType?.FullName,
                    AssemblyPath = module.AssemblyPath,
                    HasInstance = module.ModuleInstance != null
                };

                if (_pluginMetadata.TryGetValue(module.ModuleName, out var metadata))
                {
                    detail.DisplayName = metadata.DisplayName;
                    detail.Author = metadata.Author;
                    detail.Description = metadata.Description;
                    detail.Website = metadata.Website;
                    detail.IconBase64 = metadata.IconBase64;
                    Log.Debug($"[GetPluginDetail] 元数据: DisplayName={metadata.DisplayName}, Author={metadata.Author}, HasIcon={!string.IsNullOrEmpty(metadata.IconBase64)}");
                }

                var deps = _moduleManager.GetModuleDependencies(module.ModuleName);
                if (deps != null)
                {
                    detail.Dependencies = deps;
                    Log.Debug($"[GetPluginDetail] 依赖: {string.Join(", ", deps)}");
                }

                var dependents = _moduleManager.GetModulesDependentOn(module.ModuleName);
                if (dependents != null)
                {
                    detail.Dependents = dependents;
                    Log.Debug($"[GetPluginDetail] 被依赖: {string.Join(", ", dependents)}");
                }

                return detail;
            }

            var modulesDir = ConfigManager.GetCorrectedPath("Modules");
            var disabledPath = Path.Combine(modulesDir, $"{moduleName}.dll.disabled");
            if (File.Exists(disabledPath))
            {
                Log.Debug($"[GetPluginDetail] 找到禁用插件: {disabledPath}");
                return new PluginDetail
                {
                    ModuleName = moduleName,
                    Status = "Disabled",
                    IsBuiltin = false,
                    AssemblyPath = disabledPath,
                    HasInstance = false
                };
            }

            Log.Debug($"[GetPluginDetail] 未找到插件: {moduleName}");
            return null;
        }

        public List<PluginConfigInfo> GetPluginConfigs(string moduleName)
        {
            var configs = new List<PluginConfigInfo>();
            try
            {
                var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var configDir = Path.Combine(exeDir ?? ".", "Config");
                
                if (!Directory.Exists(configDir))
                    return configs;
                
                var pattern = $"{moduleName}-*.yml";
                var files = Directory.GetFiles(configDir, pattern);
                
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var configName = fileName.Substring(moduleName.Length + 1);
                    var fileInfo = new FileInfo(file);
                    
                    configs.Add(new PluginConfigInfo
                    {
                        ConfigName = configName,
                        FilePath = file,
                        LastModified = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"获取插件配置列表失败: {ex.Message}");
            }
            
            return configs;
        }

        public Dictionary<string, object>? GetPluginConfig(string moduleName, string configName)
        {
            try
            {
                var config = _pluginConfigManager.GetConfigAsync<Dictionary<string, object>>(moduleName, configName).Result;
                return config;
            }
            catch (Exception ex)
            {
                Log.Error($"获取插件配置失败: {ex.Message}");
                return null;
            }
        }

        public bool SavePluginConfig(string moduleName, string configName, Dictionary<string, object> config)
        {
            try
            {
                _pluginConfigManager.SetConfigAsync(moduleName, configName, config).Wait();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"保存插件配置失败: {ex.Message}");
                return false;
            }
        }

        public WebUIConfigData GetConfig()
        {
            var config = _configManager.GetConfig();
            return new WebUIConfigData
            {
                NapCatServerUrl = config.NapCatServerUrl,
                NapCatToken = config.NapCatToken,
                ModulesDirectory = config.ModulesDirectory,
                AutoLoadModules = config.AutoLoadModules,
                OwnerQQ = config.OwnerQQ,
                AdminQQs = config.AdminQQs,
                WebUI = new WebUISettings
                {
                    Enabled = config.WebUI.Enabled,
                    Port = config.WebUI.Port,
                    Username = config.WebUI.Username,
                    Password = config.WebUI.Password
                }
            };
        }

        public void UpdateConfig(Action<WebUIConfigData> updateAction)
        {
            var webUIConfig = GetConfig();
            updateAction(webUIConfig);
            
            _configManager.UpdateConfig(config =>
            {
                config.NapCatServerUrl = webUIConfig.NapCatServerUrl;
                config.NapCatToken = webUIConfig.NapCatToken;
                config.ModulesDirectory = webUIConfig.ModulesDirectory;
                config.AutoLoadModules = webUIConfig.AutoLoadModules;
                config.OwnerQQ = webUIConfig.OwnerQQ;
                config.AdminQQs = webUIConfig.AdminQQs;
                config.WebUI.Enabled = webUIConfig.WebUI.Enabled;
                config.WebUI.Port = webUIConfig.WebUI.Port;
                config.WebUI.Username = webUIConfig.WebUI.Username;
                if (!string.IsNullOrEmpty(webUIConfig.WebUI.Password))
                {
                    config.WebUI.Password = webUIConfig.WebUI.Password;
                }
            });
        }

        public List<LogEntry> GetLogs(int count = 100, string? level = null)
        {
            var result = new List<LogEntry>();

            try
            {
                if (!Directory.Exists(_logDirectory))
                    return result;

                var logFiles = Directory.GetFiles(_logDirectory, "*.log")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Take(5)
                    .ToList();

                var allLines = new List<(DateTime Time, string Level, string Source, string Message)>();

                foreach (var file in logFiles)
                {
                    try
                    {
                        var lines = File.ReadAllLines(file);
                        foreach (var line in lines)
                        {
                            var parsed = ParseRawLogLine(line);
                            if (parsed != null)
                            {
                                if (level == null || parsed.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                                {
                                    allLines.Add((parsed.Time, parsed.Level, parsed.Source, parsed.Message));
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                var recentLines = allLines.OrderByDescending(l => l.Time).Take(count);
                
                foreach (var line in recentLines)
                {
                    result.Add(new LogEntry
                    {
                        Time = line.Time,
                        Level = line.Level,
                        Source = line.Source,
                        Message = line.Message
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"读取日志失败: {ex.Message}");
            }

            return result;
        }

        public void ClearLogs()
        {
            try
            {
                if (Directory.Exists(_logDirectory))
                {
                    var files = Directory.GetFiles(_logDirectory, "*.log");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
            }
        }

        public void SubscribeToLogs(Action<LogEntry> callback)
        {
            lock (_logLock)
            {
                _logSubscribers.Add(callback);
            }
        }

        public void UnsubscribeFromLogs(Action<LogEntry> callback)
        {
            lock (_logLock)
            {
                _logSubscribers.Remove(callback);
            }
        }

        public void SubscribeToRawLogs(Action<string> callback)
        {
            lock (_logLock)
            {
                _rawLogSubscribers.Add(callback);
            }
        }

        public void UnsubscribeFromRawLogs(Action<string> callback)
        {
            lock (_logLock)
            {
                _rawLogSubscribers.Remove(callback);
            }
        }

        public List<string> GetRecentRawLogs(int count = 50)
        {
            lock (_logLock)
            {
                return _recentLogs.TakeLast(count).ToList();
            }
        }

        private static bool IsBuiltinModule(string moduleName)
        {
            var builtinModules = new[] { "HelpModule", "PluginModule", "SystemModule", "SetModule" };
            return builtinModules.Contains(moduleName);
        }
    }
}
