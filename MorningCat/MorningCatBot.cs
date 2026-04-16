using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Logging;
using OneBotLib;
using OneBotLib.Events;
using ModuleManagerLib;
using MorningCat.Config;
using MorningCat.Commands;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private OneBotClient _client;
        private ModuleManager _moduleManager;
        private ConfigManager _configManager;
        private PluginConfigManager _pluginConfigManager;
        private CommandRegistry _commandRegistry;
        private bool _isRunning = false;
        private bool _isAuthenticated = false;
        private TaskCompletionSource<bool> _authCompletionSource = new TaskCompletionSource<bool>();
        private SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1);
        private Action _exitCallback;
        private Dictionary<string, PluginMetadata> _pluginMetadata = new Dictionary<string, PluginMetadata>();
        private Timer _reconnectTimer;
        private bool _isReconnecting = false;
        
        public PluginMetadata GetPluginMetadata(string moduleName)
        {
            if (_pluginMetadata.TryGetValue(moduleName, out var metadata))
            {
                return metadata;
            }
            return null;
        }
        
        public bool IsNewConfig => _configManager.IsNewConfig;
        
        public MorningCatBot(Action exitCallback = null)
        {
            _exitCallback = exitCallback;
            _client = new OneBotClient();
            _moduleManager = new ModuleManager();
            _configManager = new ConfigManager();
            _pluginConfigManager = new PluginConfigManager();
            _commandRegistry = new CommandRegistry(_client, _configManager);
            
            _client.OnMessage += OnMessageReceived;
            _client.OnLifecycle += OnLifecycleEventReceived;
            _client.OnHeartbeat += OnHeartbeatReceived;
            _client.OnConnectionStateChanged += OnConnectionStateChanged;
        }
        
        public async Task StartAsync()
        {
            if (_isRunning) return;
            Log.Info("猫猫起床中...");
            await InitializeModuleManagerAsync();
            await ConnectToNapCatAsync();
            _isRunning = true;
            Log.Info("猫猫起床了喵AWA");
        }
        
        public async Task StopAsync()
        {
            if (!_isRunning) return;
            Log.Info("猫猫伸懒腰...");
            StopReconnectTimer();
            await StopWebUIAsync();
            _client.OnConnectionStateChanged -= OnConnectionStateChanged;
            _client.OnHeartbeat -= OnHeartbeatReceived;
            _client.OnLifecycle -= OnLifecycleEventReceived;
            _client.OnMessage -= OnMessageReceived;
            if (_client != null)
            {
                await _client.CloseAsync();
            }
            _authCompletionSource?.TrySetCanceled();
            _isRunning = false;
            Log.Info("猫猫睡觉去了！");
        }
        
        public async Task RestartAsync()
        {
            Log.Info("猫猫正在重启...");
            
            await StopAsync();
            
            _client = new OneBotClient();
            _moduleManager = new ModuleManager();
            _configManager = new ConfigManager();
            _pluginConfigManager = new PluginConfigManager();
            _commandRegistry = new CommandRegistry(_client, _configManager);
            _authCompletionSource = new TaskCompletionSource<bool>();
            
            _client.OnMessage += OnMessageReceived;
            _client.OnLifecycle += OnLifecycleEventReceived;
            _client.OnHeartbeat += OnHeartbeatReceived;
            _client.OnConnectionStateChanged += OnConnectionStateChanged;
            
            await StartAsync();
            
            Log.Info("猫猫重启完成喵AWA");
        }
        
        public void RequestExit()
        {
            _exitCallback?.Invoke();
        }
    }
}
