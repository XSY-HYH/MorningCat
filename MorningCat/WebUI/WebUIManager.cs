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
using MorningCat.I18n;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;
using MorningCat.Modules;
using MorningCat.PluginAPI;
using MorningCat.Security;
using MorningCat.WebUI;

namespace MorningCat.WebUI
{
    public class WebUIManager : ISystemInfoProvider, IBotInfoProvider, IPluginInfoProvider, ILogProvider, IConfigProvider, IMessageProvider, IDatabaseInfoProvider, IMessageSendProvider, II18nProvider
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
        private bool _isOneBotConnected = true;
        private readonly List<Action<LogEntry>> _logSubscribers = new List<Action<LogEntry>>();
        private readonly List<Action<string>> _rawLogSubscribers = new List<Action<string>>();
        private readonly object _logLock = new object();
        private readonly List<string> _recentLogs = new List<string>();
        private const int MaxRecentLogs = 100;
        private Func<Task>? _restartCallback;
        private Action? _shutdownCallback;
        private readonly PluginSignatureVerifier? _signatureVerifier;
        private readonly HashSet<string> _signatureFailedModules;
        private MessageRelayModule? _messageRelayModule;
        private PluginDatabaseAPI? _pluginDatabaseAPI;
        private MessageDistributionCore? _mdc;
        private readonly List<Action<MessageEntry>> _messageSubscribers = new List<Action<MessageEntry>>();
        private readonly object _messageLock = new object();
        private I18n.I18nManager? _i18nManager;

        public int Port => _server?.Port ?? 0;
        public bool IsRunning => _server?.IsRunning ?? false;

        public WebUIManager(
            ModuleManager moduleManager, 
            Dictionary<string, PluginMetadata> pluginMetadata, 
            WebUIConfig config, 
            ConfigManager configManager,
            Dictionary<string, string> assemblyNameToModuleName,
            PluginConfigManager pluginConfigManager,
            PluginSignatureVerifier? signatureVerifier = null,
            HashSet<string>? signatureFailedModules = null)
        {
            _moduleManager = moduleManager;
            _pluginMetadata = pluginMetadata;
            _config = config;
            _configManager = configManager;
            _assemblyNameToModuleName = assemblyNameToModuleName;
            _pluginConfigManager = pluginConfigManager;
            _signatureVerifier = signatureVerifier;
            _signatureFailedModules = signatureFailedModules ?? new HashSet<string>();
            
            var location = Assembly.GetExecutingAssembly().Location;
            var exeDir = string.IsNullOrEmpty(location)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(location);
            _logDirectory = Path.Combine(exeDir, "logs");
            
            var mctAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MorningCat");
            var version = mctAssembly?.GetName().Version;
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

        public void SetMessageRelayModule(MessageRelayModule messageRelayModule)
        {
            _messageRelayModule = messageRelayModule;
            _messageRelayModule.OnFormattedMessage += OnFormattedMessage;
        }

        public void SetPluginDatabaseAPI(PluginDatabaseAPI pluginDatabaseAPI)
        {
            _pluginDatabaseAPI = pluginDatabaseAPI;
        }

        public void SetMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public void SetI18nManager(I18n.I18nManager i18nManager)
        {
            _i18nManager = i18nManager;
        }

        /// <summary>获取OneBot适配器（用于OneBot特有API）</summary>
        private OneBotPlatformAdapter? GetOneBotAdapter()
        {
            return _mdc?.GetAdapter<OneBotPlatformAdapter>(PlatformId.OneBot);
        }

        private void OnFormattedMessage(FormattedMessage msg)
        {
            var entry = new MessageEntry
            {
                GroupName = msg.GroupName,
                SenderName = msg.SenderName,
                Content = msg.Content,
                MessageType = msg.MessageType,
                UserId = long.TryParse(msg.UserId, out var uid) ? uid : 0,
                GroupId = msg.GroupId != null && long.TryParse(msg.GroupId, out var gid) ? gid : (long?)null,
                Time = msg.Time,
                HasUnsupportedContent = msg.HasUnsupportedContent
            };

            lock (_messageLock)
            {
                foreach (var subscriber in _messageSubscribers.ToList())
                {
                    try
                    {
                        subscriber(entry);
                    }
                    catch { }
                }
            }
        }

        public List<MessageEntry> GetRecentMessages(int count = 50)
        {
            if (_messageRelayModule == null)
                return new List<MessageEntry>();

            var messages = _messageRelayModule.GetRecentMessages(count);
            return messages.Select(m => new MessageEntry
            {
                GroupName = m.GroupName,
                SenderName = m.SenderName,
                Content = m.Content,
                MessageType = m.MessageType,
                UserId = long.TryParse(m.UserId, out var uid) ? uid : 0,
                GroupId = m.GroupId != null && long.TryParse(m.GroupId, out var gid) ? gid : (long?)null,
                Time = m.Time,
                HasUnsupportedContent = m.HasUnsupportedContent
            }).ToList();
        }

        public void SubscribeToMessages(Action<MessageEntry> callback)
        {
            lock (_messageLock) { _messageSubscribers.Add(callback); }
        }

        public void UnsubscribeFromMessages(Action<MessageEntry> callback)
        {
            lock (_messageLock) { _messageSubscribers.Remove(callback); }
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
                _botInfo.IsOneBotConnected = _isOneBotConnected;
            }
            return _botInfo;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _isOneBotConnected = isConnected;
            Log.Debug(I18nManager.S("webui.connection_status", isConnected ? "connected" : "disconnected"));
        }

        public async Task StartAsync()
        {
            if (_server != null && _server.IsRunning)
            {
                Log.Warning(I18nManager.S("webui.already_running"));
                return;
            }

            _server = new WebUIServer(_config.Username, _config.Password);
            _server.SetSystemInfoProvider(this);
            _server.SetBotInfoProvider(this);
            _server.SetPluginInfoProvider(this);
            _server.SetLogProvider(this);
            _server.SetConfigProvider(this);
            _server.SetMessageProvider(this);
            _server.SetDatabaseInfoProvider(this);
            _server.SetMessageSendProvider(this);
            _server.SetI18nProvider(this);
            _server.SetUpdateCallback(() => { Program.UpdateCallback?.Invoke(); });
            _server.OnCredentialsChanged = OnCredentialsChanged;
            var pluginStoreUrl = _configManager.GetConfig().PluginStoreUrl;
            Log.Debug(I18nManager.S("webui.plugin_store_url_read", pluginStoreUrl));
            _server.UpdatePluginMarketUrl(pluginStoreUrl);

            try
            {
                await _server.StartAsync(_config.Port, _config.ListenAddress);
                Log.Info(I18nManager.S("webui.started", _config.ListenAddress, _config.Port));
                
                var accountService = _server.GetAccountService();
                var (username, password) = accountService.GetDefaultCredentials();
                Log.Info(I18nManager.S("webui.default_credentials", username, password));    
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.start_failed", ex.Message));
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
                Log.Info(I18nManager.S("webui.credentials_updated"));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.credentials_save_failed", ex.Message));
            }
        }

        public async Task StopAsync()
        {
            if (_server != null)
            {
                await _server.StopAsync();
                Log.Info(I18nManager.S("webui.stopped"));
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
            Log.Info(I18nManager.S("webui.restart_requested"));
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
                Log.Warning(I18nManager.S("webui.restart_no_callback"));
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
            Log.Info(I18nManager.S("webui.shutdown_requested"));
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
                Log.Warning(I18nManager.S("webui.shutdown_no_callback"));
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

            Log.Debug(I18nManager.S("webui.plugin.get_plugins_count", allModules.Count));
            
            foreach (var module in allModules)
            {
                var info = new PluginInfo
                {
                    ModuleName = module.ModuleName,
                    Status = module.Status.ToString(),
                    IsBuiltin = IsBuiltinModule(module.ModuleName),
                    AssemblyPath = module.AssemblyPath,
                    SignatureStatus = GetSignatureStatus(module.ModuleName, module.AssemblyPath)
                };

                if (_pluginMetadata.TryGetValue(module.ModuleName, out var metadata))
                {
                    info.DisplayName = metadata.DisplayName;
                    info.Author = metadata.Author;
                    info.Description = metadata.Description;
                    info.IconBase64 = metadata.IconBase64;
                    info.Tags = metadata.Tags;
                    Log.Debug(I18nManager.S("webui.plugin.module_metadata", module.ModuleName, metadata.DisplayName, metadata.Author, !string.IsNullOrEmpty(metadata.IconBase64)));
                }
                else
                {
                    Log.Debug(I18nManager.S("webui.plugin.module_no_metadata", module.ModuleName));
                }

                result.Add(info);
            }

            var modulesDir = ConfigManager.GetCorrectedPath("Modules");
            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    Log.Debug(I18nManager.S("webui.plugin.disabled_count", disabledFiles.Length));
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        
                        var info = new PluginInfo
                        {
                            ModuleName = fileName,
                            Status = "Disabled",
                            IsBuiltin = false,
                            AssemblyPath = file,
                            SignatureStatus = GetSignatureStatus(fileName, file)
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
                                info.Tags = metadata.Tags;
                            }
                        }
                        else if (_pluginMetadata.TryGetValue(fileName, out var metadata))
                        {
                            info.DisplayName = metadata.DisplayName;
                            info.Author = metadata.Author;
                            info.Description = metadata.Description;
                            info.IconBase64 = metadata.IconBase64;
                            info.Tags = metadata.Tags;
                        }
                        
                        result.Add(info);
                        Log.Debug(I18nManager.S("webui.plugin.disabled_plugin", fileName));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.get_disabled_failed", ex.Message));
            }

            Log.Debug(I18nManager.S("webui.plugin.return_count", result.Count));
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
                Log.Debug(I18nManager.S("webui.plugin.disable_start", moduleName));
                
                var allModules = _moduleManager.GetAllModules();
                var module = allModules.FirstOrDefault(m => m.ModuleName == moduleName);
                
                if (module == null)
                {
                    Log.Warning(I18nManager.S("webui.plugin.disable_not_found", moduleName));
                    return false;
                }

                var assemblyPath = module.AssemblyPath;
                if (string.IsNullOrEmpty(assemblyPath))
                {
                    Log.Warning(I18nManager.S("webui.plugin.disable_invalid_path", moduleName));
                    return false;
                }

                if (assemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug(I18nManager.S("webui.plugin.disable_already", moduleName));
                    return true;
                }

                if (module.Status == ModuleStatus.Running)
                {
                    Log.Debug(I18nManager.S("webui.plugin.disable_unloading"));
                    var success = _moduleManager.UnloadModuleAsync(moduleName).GetAwaiter().GetResult();
                    Log.Debug(I18nManager.S("webui.plugin.disable_unload_result", success));
                }

                var disabledPath = $"{assemblyPath}.disabled";
                Log.Debug(I18nManager.S("webui.plugin.disable_check_file", assemblyPath));
                
                if (File.Exists(assemblyPath))
                {
                    Log.Debug(I18nManager.S("webui.plugin.disable_rename_to", disabledPath));
                    try
                    {
                        File.Move(assemblyPath, disabledPath);
                        Log.Info(I18nManager.S("webui.plugin.disable_success", moduleName));
                        return true;
                    }
                    catch (IOException ex)
                    {
                        Log.Debug(I18nManager.S("webui.plugin.disable_file_locked", ex.Message));
                        File.WriteAllText(disabledPath, "");
                        Log.Info(I18nManager.S("webui.plugin.disable_marked", moduleName));
                        return true;
                    }
                }
                else
                {
                    Log.Warning(I18nManager.S("webui.plugin.disable_file_not_exist", assemblyPath));
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.disable_failed", ex.Message));
                return false;
            }
        }

        public bool EnablePlugin(string moduleName)
        {
            try
            {
                Log.Debug(I18nManager.S("webui.plugin.enable_start", moduleName));
                
                var modulesDir = ConfigManager.GetCorrectedPath("Modules");
                
                string? disabledPath = null;
                string? assemblyName = null;
                
                if (_assemblyNameToModuleName.TryGetValue(moduleName, out var mappedModuleName))
                {
                    assemblyName = moduleName;
                    disabledPath = Path.Combine(modulesDir, $"{assemblyName}.dll.disabled");
                    Log.Debug(I18nManager.S("webui.plugin.enable_find_by_assembly", disabledPath));
                }
                else
                {
                    var reverseMapping = _assemblyNameToModuleName.FirstOrDefault(x => x.Value == moduleName);
                    if (!string.IsNullOrEmpty(reverseMapping.Key))
                    {
                        assemblyName = reverseMapping.Key;
                        disabledPath = Path.Combine(modulesDir, $"{assemblyName}.dll.disabled");
                        Log.Debug(I18nManager.S("webui.plugin.enable_reverse_mapping", assemblyName, disabledPath));
                    }
                }
                
                if (string.IsNullOrEmpty(disabledPath) || !File.Exists(disabledPath))
                {
                    disabledPath = Path.Combine(modulesDir, $"{moduleName}.dll.disabled");
                    assemblyName = moduleName;
                    Log.Debug(I18nManager.S("webui.plugin.enable_find_by_name", disabledPath));
                }
                
                Log.Debug(I18nManager.S("webui.plugin.enable_check_disabled", disabledPath));
                
                if (!File.Exists(disabledPath))
                {
                    Log.Warning(I18nManager.S("webui.plugin.enable_disabled_not_found", disabledPath));
                    return false;
                }

                var dllPath = disabledPath.Substring(0, disabledPath.Length - ".disabled".Length);
                Log.Debug(I18nManager.S("webui.plugin.enable_target_path", dllPath));
                
                File.Move(disabledPath, dllPath);
                Log.Info(I18nManager.S("webui.plugin.enable_success", moduleName));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.enable_failed", ex.Message));
                return false;
            }
        }

        public bool UnloadPlugin(string moduleName)
        {
            try
            {
                Log.Debug(I18nManager.S("webui.plugin.unload_start", moduleName));
                
                if (IsBuiltinModule(moduleName))
                {
                    Log.Warning(I18nManager.S("webui.plugin.unload_builtin", moduleName));
                    return false;
                }

                var dependents = _moduleManager.GetModulesDependentOn(moduleName);
                if (dependents != null && dependents.Count > 0)
                {
                    Log.Warning(I18nManager.S("webui.plugin.unload_dependent", moduleName, string.Join(", ", dependents)));
                    return false;
                }

                Log.Debug(I18nManager.S("webui.plugin.unload_calling"));
                var success = _moduleManager.UnloadModuleAsync(moduleName).GetAwaiter().GetResult();
                Log.Debug(I18nManager.S("webui.plugin.unload_result", success));
                
                if (success)
                {
                    _pluginMetadata.Remove(moduleName);
                    Log.Info(I18nManager.S("webui.plugin.unload_success", moduleName));
                    return true;
                }
                    Log.Warning(I18nManager.S("webui.plugin.unload_returned_false"));
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.unload_failed", ex.Message));
                return false;
            }
        }

        public PluginDetail? GetPluginDetail(string moduleName)
        {
            Log.Debug(I18nManager.S("webui.plugin.detail_start", moduleName));
            
            var allModules = _moduleManager.GetAllModules();
            var module = allModules.FirstOrDefault(m => m.ModuleName == moduleName);
            
            if (module != null)
            {
                Log.Debug(I18nManager.S("webui.plugin.detail_found", module.ModuleName, module.Status));
                
                var detail = new PluginDetail
                {
                    ModuleName = module.ModuleName,
                    Status = module.Status.ToString(),
                    IsBuiltin = IsBuiltinModule(module.ModuleName),
                    ModuleType = module.ModuleType?.FullName,
                    AssemblyPath = module.AssemblyPath,
                    HasInstance = module.ModuleInstance != null,
                    SignatureStatus = GetSignatureStatus(module.ModuleName, module.AssemblyPath)
                };

                if (_pluginMetadata.TryGetValue(module.ModuleName, out var metadata))
                {
                    detail.DisplayName = metadata.DisplayName;
                    detail.Author = metadata.Author;
                    detail.Description = metadata.Description;
                    detail.Website = metadata.Website;
                    detail.IconBase64 = metadata.IconBase64;
                    detail.Tags = metadata.Tags;
                    Log.Debug(I18nManager.S("webui.plugin.detail_metadata", metadata.DisplayName, metadata.Author, !string.IsNullOrEmpty(metadata.IconBase64)));
                }

                var deps = _moduleManager.GetModuleDependencies(module.ModuleName);
                if (deps != null)
                {
                    detail.Dependencies = deps;
                    Log.Debug(I18nManager.S("webui.plugin.detail_deps", string.Join(", ", deps)));
                }

                var dependents = _moduleManager.GetModulesDependentOn(module.ModuleName);
                if (dependents != null)
                {
                    detail.Dependents = dependents;
                    Log.Debug(I18nManager.S("webui.plugin.detail_dependents", string.Join(", ", dependents)));
                }

                return detail;
            }

            var modulesDir = ConfigManager.GetCorrectedPath("Modules");
            var disabledPath = Path.Combine(modulesDir, $"{moduleName}.dll.disabled");
            if (File.Exists(disabledPath))
            {
                Log.Debug(I18nManager.S("webui.plugin.detail_found_disabled", disabledPath));
                return new PluginDetail
                {
                    ModuleName = moduleName,
                    Status = "Disabled",
                    IsBuiltin = false,
                    AssemblyPath = disabledPath,
                    HasInstance = false,
                    SignatureStatus = GetSignatureStatus(moduleName, disabledPath)
                };
            }

            Log.Debug(I18nManager.S("webui.plugin.detail_not_found", moduleName));
            return null;
        }

        public List<PluginConfigInfo> GetPluginConfigs(string moduleName)
        {
            Log.Debug(I18nManager.S("webui.plugin.configs_request", moduleName));
            var configs = new List<PluginConfigInfo>();
            try
            {
                var resolvedName = _pluginConfigManager.ResolvePluginName(moduleName);
                Log.Debug(I18nManager.S("webui.plugin.configs_resolved", moduleName, resolvedName));
                
                var registered = _pluginConfigManager.GetRegisteredConfigs(moduleName);
                Log.Debug(I18nManager.S("webui.plugin.configs_registered_count", registered.Count));
                
                foreach (var reg in registered)
                {
                    Log.Debug(I18nManager.S("webui.plugin.configs_item", reg.ConfigName, reg.FilePath, File.Exists(reg.FilePath)));
                    configs.Add(new PluginConfigInfo
                    {
                        ConfigName = reg.ConfigName,
                        FilePath = reg.FilePath,
                        LastModified = reg.LastModified,
                        FileSize = reg.FileSize
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.get_configs_failed", ex.Message));
            }
            
            return configs;
        }

        public Dictionary<string, object>? GetPluginConfig(string moduleName, string configName)
        {
            try
            {
                return _pluginConfigManager.GetPluginConfigAsJson(moduleName, configName);
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.get_config_failed", ex.Message));
                return null;
            }
        }

        public bool SavePluginConfig(string moduleName, string configName, Dictionary<string, object> config)
        {
            try
            {
                var pluginName = _pluginConfigManager.ResolvePluginName(moduleName);
                _pluginConfigManager.SetConfigAsync(pluginName, configName, config).Wait();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.save_config_failed", ex.Message));
                return false;
            }
        }

        public WebUIConfigData GetConfig()
        {
            var config = _configManager.GetConfig();
            return new WebUIConfigData
            {
                OneBotServerUrl = config.OneBotServerUrl,
                OneBotToken = config.OneBotToken,
                ModulesDirectory = config.ModulesDirectory,
                AutoLoadModules = config.AutoLoadModules,
                EnableMctStatus = config.EnableMctStatus,
                OwnerQQ = config.OwnerQQ,
                BlockedUsers = config.BlockedUsers,
                BlockedGroups = config.BlockedGroups,
                Lang = config.Lang,
                Database = new DatabaseConfigData
                {
                    Type = config.Database.Type,
                    ConnectionString = config.Database.ConnectionString
                },
                PluginStoreUrl = config.PluginStoreUrl,
                WebUI = new WebUISettings
                {
                    Enabled = config.WebUI.Enabled,
                    ListenAddress = config.WebUI.ListenAddress,
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
                config.OneBotServerUrl = webUIConfig.OneBotServerUrl;
                config.OneBotToken = webUIConfig.OneBotToken;
                config.ModulesDirectory = webUIConfig.ModulesDirectory;
                config.AutoLoadModules = webUIConfig.AutoLoadModules;
                config.EnableMctStatus = webUIConfig.EnableMctStatus;
                config.OwnerQQ = webUIConfig.OwnerQQ;
                config.BlockedUsers = webUIConfig.BlockedUsers;
                config.BlockedGroups = webUIConfig.BlockedGroups;
                config.Lang = webUIConfig.Lang;
                config.Database.Type = webUIConfig.Database.Type;
                config.Database.ConnectionString = webUIConfig.Database.ConnectionString;
                config.PluginStoreUrl = webUIConfig.PluginStoreUrl;
                Log.Debug(I18nManager.S("webui.plugin_store_url_updated", webUIConfig.PluginStoreUrl));
                _server?.UpdatePluginMarketUrl(webUIConfig.PluginStoreUrl);
                config.WebUI.Enabled = webUIConfig.WebUI.Enabled;
                config.WebUI.ListenAddress = webUIConfig.WebUI.ListenAddress;
                config.WebUI.Port = webUIConfig.WebUI.Port;
                config.WebUI.Username = webUIConfig.WebUI.Username;
                if (!string.IsNullOrEmpty(webUIConfig.WebUI.Password))
                {
                    config.WebUI.Password = webUIConfig.WebUI.Password;
                }
            });
        }

        public byte[] ExportConfig()
        {
            var baseDir = Path.GetDirectoryName(ConfigManager.GetCorrectedPath("config.yml")) ?? AppContext.BaseDirectory;
            using var ms = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                // 导出 config.yml
                var configPath = ConfigManager.GetCorrectedPath("config.yml");
                if (File.Exists(configPath))
                {
                    var entry = zip.CreateEntry("config.yml", System.IO.Compression.CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(configPath);
                    fileStream.CopyTo(entryStream);
                }

                // 导出 lang 目录
                var langDir = Path.Combine(baseDir, "lang");
                if (Directory.Exists(langDir))
                {
                    foreach (var file in Directory.GetFiles(langDir, "*.yml"))
                    {
                        var entry = zip.CreateEntry($"lang/{Path.GetFileName(file)}", System.IO.Compression.CompressionLevel.Optimal);
                        using var entryStream = entry.Open();
                        using var fileStream = File.OpenRead(file);
                        fileStream.CopyTo(entryStream);
                    }
                }

                // 导出 Modules 目录中的插件配置
                var modulesDir = ConfigManager.GetCorrectedPath(
                    _configManager.GetConfig().ModulesDirectory);
                if (Directory.Exists(modulesDir))
                {
                    foreach (var dir in Directory.GetDirectories(modulesDir))
                    {
                        foreach (var configFile in Directory.GetFiles(dir, "*.yml"))
                        {
                            var relativePath = $"Modules/{Path.GetFileName(dir)}/{Path.GetFileName(configFile)}";
                            var entry = zip.CreateEntry(relativePath, System.IO.Compression.CompressionLevel.Optimal);
                            using var entryStream = entry.Open();
                            using var fileStream = File.OpenRead(configFile);
                            fileStream.CopyTo(entryStream);
                        }
                    }
                }
            }
            return ms.ToArray();
        }

        public string ImportConfig(byte[] zipData)
        {
            var baseDir = Path.GetDirectoryName(ConfigManager.GetCorrectedPath("config.yml")) ?? AppContext.BaseDirectory;
            var importedFiles = new List<string>();

            using var ms = new MemoryStream(zipData);
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            
            // 先验证 zip 内容
            string? configEntry = null;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName == "config.yml")
                {
                    configEntry = entry.FullName;
                }
            }

            if (configEntry == null)
            {
                throw new InvalidOperationException("Invalid backup: config.yml not found in archive");
            }

            // 备份当前配置
            var currentConfigPath = ConfigManager.GetCorrectedPath("config.yml");
            if (File.Exists(currentConfigPath))
            {
                var backupPath = currentConfigPath + $".backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(currentConfigPath, backupPath);
            }

            foreach (var entry in zip.Entries)
            {
                var targetPath = Path.Combine(baseDir, entry.FullName);

                // 安全检查：防止路径遍历
                var fullPath = Path.GetFullPath(targetPath);
                if (!fullPath.StartsWith(Path.GetFullPath(baseDir)))
                {
                    continue;
                }

                var entryDir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                using var entryStream = entry.Open();
                using var fileStream = File.Create(fullPath);
                entryStream.CopyTo(fileStream);

                importedFiles.Add(entry.FullName);
            }

            return string.Join(", ", importedFiles);
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
                Log.Error(I18nManager.S("webui.log_read_failed", ex.Message));
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

        public bool DeletePlugin(string moduleName)
        {
            try
            {
                Log.Debug(I18nManager.S("webui.plugin.delete_start", moduleName));
                
                if (IsBuiltinModule(moduleName))
                {
                    Log.Warning(I18nManager.S("webui.plugin.delete_builtin", moduleName));
                    return false;
                }

                var allModules = _moduleManager.GetAllModules();
                var module = allModules.FirstOrDefault(m => m.ModuleName == moduleName);
                
                if (module == null)
                {
                    Log.Warning(I18nManager.S("webui.plugin.delete_not_found", moduleName));
                    return false;
                }

                var assemblyPath = module.AssemblyPath;
                if (string.IsNullOrEmpty(assemblyPath))
                {
                    Log.Warning(I18nManager.S("webui.plugin.delete_invalid_path", moduleName));
                    return false;
                }

                var dependents = _moduleManager.GetModulesDependentOn(moduleName);
                if (dependents != null && dependents.Count > 0)
                {
                    Log.Warning(I18nManager.S("webui.plugin.delete_dependent", moduleName, string.Join(", ", dependents)));
                    return false;
                }

                if (module.Status == ModuleStatus.Running)
                {
                    Log.Debug(I18nManager.S("webui.plugin.delete_unloading"));
                    var unloadSuccess = _moduleManager.UnloadModuleAsync(moduleName).GetAwaiter().GetResult();
                    Log.Debug(I18nManager.S("webui.plugin.delete_unload_result", unloadSuccess));
                }

                _pluginMetadata.Remove(moduleName);

                if (assemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    assemblyPath = assemblyPath.Substring(0, assemblyPath.Length - ".disabled".Length);
                }

                if (!File.Exists(assemblyPath))
                {
                    Log.Warning(I18nManager.S("webui.plugin.delete_file_not_exist", assemblyPath));
                    return false;
                }

                var pluginDir = Path.GetDirectoryName(assemblyPath);
                var dllFileName = Path.GetFileName(assemblyPath);
                var pluginNameWithoutExt = Path.GetFileNameWithoutExtension(dllFileName);

                File.Delete(assemblyPath);

                var disabledPath = assemblyPath + ".disabled";
                if (File.Exists(disabledPath))
                {
                    File.Delete(disabledPath);
                }

                if (!string.IsNullOrEmpty(pluginDir) && Directory.Exists(pluginDir))
                {
                    var configPattern = $"{pluginNameWithoutExt}-*.yml";
                    foreach (var configFile in Directory.GetFiles(pluginDir, configPattern))
                    {
                        try { File.Delete(configFile); }
                        catch { }
                    }

                    var subDir = Path.Combine(pluginDir, pluginNameWithoutExt);
                    if (Directory.Exists(subDir))
                    {
                        try { Directory.Delete(subDir, true); }
                        catch { }
                    }
                }

                Log.Info(I18nManager.S("webui.plugin.delete_success", moduleName, assemblyPath));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.delete_failed", ex.Message));
                return false;
            }
        }

        private string? GetSignatureStatus(string moduleName, string? assemblyPath)
        {
            if (_signatureVerifier == null)
                return null;

            if (_signatureVerifier.IsTestMode)
                return null;

            if (_signatureFailedModules.Contains(moduleName))
                return "Failed";

            if (!string.IsNullOrEmpty(assemblyPath) && !assemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                return "Verified";

            return null;
        }

        public List<DatabaseEntryInfo> GetDatabases()
        {
            if (_pluginDatabaseAPI == null)
                return new List<DatabaseEntryInfo>();

            var entries = _pluginDatabaseAPI.ScanDatabaseFiles();
            return entries.Select(e => new DatabaseEntryInfo
            {
                Key = e.Key,
                Id = e.Id,
                PluginClassName = e.PluginClassName,
                DatabasePath = e.DatabasePath,
                DatabaseType = e.DatabaseType,
                FileSize = e.FileSize,
                Tables = e.Tables
            }).ToList();
        }

        public DatabaseDetailInfo? GetDatabaseDetail(string key)
        {
            var db = _pluginDatabaseAPI?.GetDatabaseByKey(key);
            if (db == null)
            {
                var entries = _pluginDatabaseAPI?.ScanDatabaseFiles();
                var entry = entries?.FirstOrDefault(e => e.Key == key);
                if (entry == null) return null;

                return new DatabaseDetailInfo
                {
                    Key = entry.Key,
                    Id = entry.Id,
                    PluginClassName = entry.PluginClassName,
                    DatabasePath = entry.DatabasePath,
                    DatabaseType = entry.DatabaseType,
                    FileSize = entry.FileSize,
                    Tables = entry.Tables,
                    TableColumns = new Dictionary<string, List<ColumnInfo>>()
                };
            }

            var tables = db.GetTableNames();
            var tableColumns = new Dictionary<string, List<ColumnInfo>>();
            foreach (var table in tables)
            {
                var cols = db.GetColumns(table);
                tableColumns[table] = cols.Select(c => new ColumnInfo
                {
                    Name = c.Name,
                    Type = c.Type,
                    NotNull = c.NotNull,
                    IsPrimaryKey = c.IsPrimaryKey,
                    DefaultValue = c.DefaultValue
                }).ToList();
            }

            var dbEntry = _pluginDatabaseAPI!.ScanDatabaseFiles().FirstOrDefault(e => e.Key == key);
            return new DatabaseDetailInfo
            {
                Key = key,
                Id = dbEntry?.Id ?? "",
                PluginClassName = dbEntry?.PluginClassName ?? "",
                DatabasePath = db.DatabasePath,
                DatabaseType = dbEntry?.DatabaseType ?? "",
                FileSize = dbEntry?.FileSize ?? 0,
                Tables = tables,
                TableColumns = tableColumns
            };
        }

        public List<Dictionary<string, object>> QueryDatabase(string key, string sql)
        {
            var db = _pluginDatabaseAPI?.GetDatabaseByKey(key);
            if (db == null)
                throw new Exception("数据库不存在");

            return db.Query(sql);
        }

        public int ExecuteDatabaseNonQuery(string key, string sql)
        {
            var db = _pluginDatabaseAPI?.GetDatabaseByKey(key);
            if (db == null)
                throw new Exception("数据库不存在");

            return db.ExecuteNonQuery(sql);
        }

        public async Task<bool> SendPrivateMessageAsync(long userId, string message)
        {
            var adapter = GetOneBotAdapter();
            if (adapter == null)
            {
                return false;
            }

            try
            {
                var result = await adapter.SendPrivateMessageAsync(userId.ToString(), message);
                return result.Success;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.send_private_failed", userId, ex.Message));
                return false;
            }
        }

        public async Task<bool> SendGroupMessageAsync(long groupId, string message)
        {
            var adapter = GetOneBotAdapter();
            if (adapter == null)
            {
                return false;
            }

            try
            {
                var result = await adapter.SendGroupMessageAsync(groupId.ToString(), message);
                return result.Success;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.send_group_failed", groupId, ex.Message));
                return false;
            }
        }

        public async Task<List<GroupInfo>> GetGroupListAsync()
        {
            var adapter = GetOneBotAdapter();
            if (adapter == null)
                return new List<GroupInfo>();

            try
            {
                var result = await adapter.Client.GetGroupListAsync(noCache: false);
                if (result.Success && result.Data != null)
                {
                    return result.Data.Select(g => new GroupInfo
                    {
                        GroupId = g.GroupId,
                        GroupName = g.GroupName
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.get_groups_failed", ex.Message));
            }
            return new List<GroupInfo>();
        }

        public async Task<List<FriendInfo>> GetFriendListAsync()
        {
            var adapter = GetOneBotAdapter();
            if (adapter == null)
                return new List<FriendInfo>();

            try
            {
                var result = await adapter.Client.GetFriendListAsync(noCache: false);
                if (result.Success && result.Data != null)
                {
                    return result.Data.Select(f => new FriendInfo
                    {
                        UserId = f.UserId,
                        Nickname = f.Nickname,
                        Remark = f.Remark
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("webui.plugin.get_friends_failed", ex.Message));
            }
            return new List<FriendInfo>();
        }

        // === II18nProvider ===
        public string CurrentLang => _i18nManager?.CurrentLang ?? "zh";

        public Dictionary<string, string> GetTranslations()
        {
            return _i18nManager?.GetAllTranslations() ?? new Dictionary<string, string>();
        }

        public Dictionary<string, string>? GetTranslationsForLang(string lang)
        {
            return _i18nManager?.GetTranslationsForLang(lang);
        }

        public List<string> GetAvailableLanguages()
        {
            return _i18nManager?.AvailableLanguages ?? new List<string>();
        }
    }
}
