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
        private HashSet<string> _signatureFailedModules = new HashSet<string>();
        private DateTime _startTime = DateTime.Now;
        private int _wsDisconnectCount = 0;
        
        public event EventHandler<UnhandledMessageEventArgs>? OnUnhandledMessage;
        
        /// <summary>获取MDC实例</summary>
        public MessageDistributionCore MDC => _mdc;
        
        public PluginMetadata GetPluginMetadata(string moduleName)
        {
            if (_pluginMetadata.TryGetValue(moduleName, out var metadata))
            {
                return metadata;
            }
            return null;
        }
        
        public bool IsNewConfig => _configManager.IsNewConfig;
        
        public MorningCatBot(Action exitCallback = null, bool testMode = false)
        {
            _exitCallback = exitCallback;
            _isTestMode = testMode;
            
            // 初始化MDC
            _mdc = new MessageDistributionCore();
            _oneBotAdapter = new OneBotPlatformAdapter(new ConfigManager());
            _mdc.RegisterAdapter(_oneBotAdapter);
            _mdc.EnablePlatform(PlatformId.OneBot);
            
            _moduleManager = new ModuleManager();
            _configManager = new ConfigManager();
            _pluginConfigManager = new PluginConfigManager();
            _commandRegistry = new CommandRegistry(_mdc, _configManager);
            _pluginCommandAPI = new PluginCommandAPI(_commandRegistry);
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
            Log.Info("猫猫起床中...");
            await _signatureVerifier.FetchPublicKeyAsync();
            await InitializeModuleManagerAsync();
            await ConnectPlatformsAsync();
            _isRunning = true;
            Log.Info("猫猫起床了喵AWA");
        }
        
        public async Task StopAsync()
        {
            if (!_isRunning) return;
            Log.Info("猫猫伸懒腰...");
            StopReconnectTimer();
            await StopWebUIAsync();
            StopGui();
            
            try
            {
                await _moduleManager.UnloadAllModulesAsync();
                Log.Debug("所有模块已卸载");
            }
            catch (Exception ex)
            {
                Log.Warning($"卸载模块时出错: {ex.Message}");
            }
            
            _mdc.OnMessageReceived -= OnPlatformMessageReceived;
            _mdc.OnPlatformConnectionChanged -= OnPlatformConnectionChanged;
            _mdc.OnPlatformDisconnected -= OnPlatformDisconnected;
            
            await _mdc.CloseAllAsync();
            _authCompletionSource?.TrySetCanceled();
            _isRunning = false;
            Log.Info("猫猫睡觉去了！");
        }
        
        public async Task RestartAsync()
        {
            Log.Info("猫猫正在重启...");
            
            await StopAsync();
            
            _mdc = new MessageDistributionCore();
            _oneBotAdapter = new OneBotPlatformAdapter(_configManager);
            _mdc.RegisterAdapter(_oneBotAdapter);
            _mdc.EnablePlatform(PlatformId.OneBot);
            
            _moduleManager = new ModuleManager();
            _pluginConfigManager = new PluginConfigManager();
            _commandRegistry = new CommandRegistry(_mdc, _configManager);
            _pluginCommandAPI = new PluginCommandAPI(_commandRegistry);
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
            
            await StartAsync();
            await StartWebUIAsync();
            StartGui();
            
            Log.Info("猫猫重启完成喵AWA");
        }
        
        public void RequestExit()
        {
            _exitCallback?.Invoke();
        }
    }
}