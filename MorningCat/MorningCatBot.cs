using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Config;
using MorningCat.Commands;
using MorningCat.PluginAPI;
using MorningCat.Events;
using MorningCat.Security;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;
using MorningCat.PluginErrorDatabase;
using MorningCat.I18n;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private MessageDistributionCore _mdc;
        private OneBotPlatformAdapter _oneBotAdapter;
        private ModuleManager _moduleManager;
        private ConfigManager _configManager;
        private PluginConfigManager _pluginConfigManager;
        private CommandRegistry _commandRegistry;
        private PluginCommandAPI _pluginCommandAPI;
        private PluginDatabaseAPI _pluginDatabaseAPI;
        private PluginSignatureVerifier _signatureVerifier;
        private bool _isRunning = false;
        private bool _isAuthenticated = false;
        private TaskCompletionSource<bool> _authCompletionSource = new TaskCompletionSource<bool>();
        private SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
        private Action _exitCallback;
        private Dictionary<string, PluginMetadata> _pluginMetadata = new Dictionary<string, PluginMetadata>();
        private Timer _reconnectTimer;
        private bool _isReconnecting = false;
        private bool _isTestMode = false;
        private bool _isDebugMode = false;
        private PluginErrorMatcher? _errorMatcher;
        private HashSet<string> _signatureFailedModules = new HashSet<string>();
        private DateTime _startTime = DateTime.Now;
        private int _wsDisconnectCount = 0;
        private I18nManager _i18n = new();
        private string? _overrideLang;

        public event EventHandler<UnhandledMessageEventArgs>? OnUnhandledMessage;
        
        /// <summary>获取MDC实例</summary>
        public MessageDistributionCore MDC => _mdc;
        
        /// <summary>获取I18n实例</summary>
        public I18nManager I18n => _i18n;
        
        /// <summary>插件异常匹配器（仅测试模式+调试模式可用）</summary>
        public PluginErrorMatcher? ErrorMatcher => _errorMatcher;
        
        public PluginMetadata GetPluginMetadata(string moduleName)
        {
            if (_pluginMetadata.TryGetValue(moduleName, out var metadata))
            {
                return metadata;
            }
            return null;
        }
        
        public bool IsNewConfig => _configManager.IsNewConfig;
        
        public MorningCatBot(Action exitCallback = null, bool testMode = false, bool debugMode = false, string? overrideLang = null)
        {
            _exitCallback = exitCallback;
            _isTestMode = testMode;
            _isDebugMode = debugMode;
            _overrideLang = overrideLang;

            // 最先初始化国际化组件（默认 en），确保后续所有组件可使用翻译
            _i18n.InitializeDefault();

            // 初始化MDC
            _mdc = new MessageDistributionCore();
            _oneBotAdapter = new OneBotPlatformAdapter(new ConfigManager());
            _mdc.RegisterAdapter(_oneBotAdapter);
            _mdc.EnablePlatform(PlatformId.OneBot);

            _moduleManager = new ModuleManager();
            _configManager = new ConfigManager();
            _pluginConfigManager = new PluginConfigManager();
            _commandRegistry = new CommandRegistry(_mdc, _configManager);
            _commandRegistry.SetBot(this);
            _pluginCommandAPI = new PluginCommandAPI(_commandRegistry);
            _pluginCommandAPI.SetUnloadCallback(async (moduleName) =>
            {
                Log.Name("MorningCatBotCore");
                Log.Warning(I18nManager.S("command.unloading_violating_plugin", moduleName));
                await _moduleManager.UnloadModuleAsync(moduleName);
            });
            _pluginDatabaseAPI = new PluginDatabaseAPI(_configManager.GetConfig().Database);
            _signatureVerifier = new PluginSignatureVerifier(_configManager, testMode);
            
            // MDC消息事件
            _mdc.OnMessageReceived += OnPlatformMessageReceived;
            _mdc.OnPlatformConnectionChanged += OnPlatformConnectionChanged;
            _mdc.OnPlatformDisconnected += OnPlatformDisconnected;
        }
        
        public async Task StartAsync()
        {
            if (_isRunning) return;
            Log.Name("MorningCatBotCore");
            Log.Info(_i18n.T("bot.starting"));

            // 初始化国际化组件（完整初始化：创建 lang 目录、解压、切换语言）
            var baseDir = AppContext.BaseDirectory;
            var lang = _overrideLang ?? _configManager.GetConfig().Lang;
            if (!_i18n.Initialize(lang, baseDir))
            {
                Log.Error(_i18n.T("i18n.lang_load_failed", _i18n.InitError));
                throw new Exception(_i18n.InitError);
            }
            Log.Info(_i18n.T("i18n.initialized", _i18n.CurrentLang));
            
            // 初始化插件异常匹配器（仅测试模式+调试模式）
            _errorMatcher = new PluginErrorMatcher(_isTestMode, _isDebugMode);
            await _errorMatcher.InitializeAsync();
            if (_errorMatcher.IsEnabled)
            {
                Log.Debug(I18nManager.S("module.error_matcher_enabled"));
            }
            await _signatureVerifier.FetchPublicKeyAsync();
            await InitializeModuleManagerAsync();
            await ConnectPlatformsAsync();
            _isRunning = true;
            Log.Info(_i18n.T("bot.started"));
        }
        
        public async Task StopAsync()
        {
            if (!_isRunning) return;
            Log.Info(_i18n.T("bot.stopping"));
            StopReconnectTimer();
            await StopWebUIAsync();
            StopGui();
            
            try
            {
                await _moduleManager.UnloadAllModulesAsync();
                Log.Debug(_i18n.T("bot.modules_unloaded"));
            }
            catch (Exception ex)
            {
                Log.Warning(_i18n.T("bot.module_unload_error", ex.Message));
            }
            
            _mdc.OnMessageReceived -= OnPlatformMessageReceived;
            _mdc.OnPlatformConnectionChanged -= OnPlatformConnectionChanged;
            _mdc.OnPlatformDisconnected -= OnPlatformDisconnected;
            
            await _mdc.CloseAllAsync();
            _authCompletionSource?.TrySetCanceled();
            _isRunning = false;
            Log.Info(_i18n.T("bot.stopped"));
        }
        
        public async Task RestartAsync()
        {
            Log.Info(_i18n.T("bot.restarting"));
            
            await StopAsync();
            
            _mdc = new MessageDistributionCore();
            _oneBotAdapter = new OneBotPlatformAdapter(_configManager);
            _mdc.RegisterAdapter(_oneBotAdapter);
            _mdc.EnablePlatform(PlatformId.OneBot);
            
            _moduleManager = new ModuleManager();
            _pluginConfigManager = new PluginConfigManager();
            _commandRegistry = new CommandRegistry(_mdc, _configManager);
            _commandRegistry.SetBot(this);
            _pluginCommandAPI = new PluginCommandAPI(_commandRegistry);
            _pluginCommandAPI.SetUnloadCallback(async (moduleName) =>
            {
                Log.Name("MorningCatBotCore");
                Log.Warning(I18nManager.S("command.unloading_violating_plugin", moduleName));
                await _moduleManager.UnloadModuleAsync(moduleName);
            });
            _pluginDatabaseAPI = new PluginDatabaseAPI(_configManager.GetConfig().Database);
            _signatureVerifier = new PluginSignatureVerifier(_configManager, _isTestMode);
            _authCompletionSource = new TaskCompletionSource<bool>();
            _webUIManager = null;
            _guiManager = null;
            _pluginMetadata.Clear();
            _assemblyNameToModuleName.Clear();
            _moduleEventsSubscribed = false;
            
            _mdc.OnMessageReceived += OnPlatformMessageReceived;
            _mdc.OnPlatformConnectionChanged += OnPlatformConnectionChanged;
            _mdc.OnPlatformDisconnected += OnPlatformDisconnected;
            
            try
            {
                await StartAsync();
            }
            catch (Exception ex)
            {
                Log.Error(_i18n.T("bot.start_error", ex.Message));
            }
            await StartWebUIAsync();
            StartGui();
            
            Log.Info(_i18n.T("bot.restarted"));
        }
        
        public void RequestExit()
        {
            _exitCallback?.Invoke();
        }
        
        /// <summary>
        /// 尝试匹配插件异常到已知错误库
        /// </summary>
        internal async void TryMatchPluginError(Exception ex, string source)
        {
            if (_errorMatcher?.IsEnabled != true) return;
            try
            {
                var match = await _errorMatcher.MatchAsync(ex);
                if (match.Found)
                {
                    Log.Debug(PluginErrorMatcher.FormatDebugLog(match, source, ex));
                }
            }
            catch { }
        }
    }
}