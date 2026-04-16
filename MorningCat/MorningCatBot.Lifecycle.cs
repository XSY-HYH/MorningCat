using System.Threading.Tasks;
using Logging;
using OneBotLib;
using OneBotLib.Events;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private void OnLifecycleEventReceived(object? sender, LifecycleEventArgs e)
        {
            try
            {
                Log.Debug($"生命周期事件: {e.SubType}");
                
                if (e.SubType == "connect" || e.SubType == "enable")
                {
                    _authCompletionSource?.TrySetResult(true);
                    _isAuthenticated = true;
                    _isReconnecting = false;
                    StopReconnectTimer();
                    Log.Info("OneBot登录验证成功喵AWA");

                    _webUIManager?.SetConnectionStatus(true);

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        await UpdateBotInfoAsync();
                    });
                }
                else if (e.SubType == "disconnect" || e.SubType == "disable")
                {
                    _authCompletionSource?.TrySetResult(false);
                    _isAuthenticated = false;
                    Log.Warning($"OneBot连接状态变化 {e.SubType}！");
                    
                    _webUIManager?.ClearBotInfo();
                    _webUIManager?.SetConnectionStatus(false);
                    
                    StartReconnectTimer();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"OnLifecycleEventReceived 异常QAQ！: {ex.Message}");
            }
        }

        private void OnHeartbeatReceived(object? sender, HeartbeatEventArgs e)
        {
            try
            {
                Log.Debug($"[心跳] 收到心跳，间隔: {e.Interval}ms");
            }
            catch (System.Exception ex)
            {
                Log.Debug($"OnHeartbeatReceived 异常: {ex.Message}");
            }
        }

        private void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            try
            {
                Log.Info($"[连接状态] {e.OldState} -> {e.NewState}");
                
                if (e.NewState == ConnectionState.Disconnected && _isAuthenticated)
                {
                    Log.Warning("[连接状态] 检测到 WebSocket 断开！");
                    _isAuthenticated = false;
                    _webUIManager?.ClearBotInfo();
                    _webUIManager?.SetConnectionStatus(false);
                    
                    StartReconnectTimer();
                }
            }
            catch (System.Exception ex)
            {
                Log.Debug($"OnConnectionStateChanged 异常: {ex.Message}");
            }
        }

        private void StartReconnectTimer()
        {
            if (_isReconnecting || !_isRunning) return;
            
            var config = _configManager.GetConfig();
            var delay = config.ReconnectDelay > 0 ? config.ReconnectDelay : 5;
            
            Log.Info($"[重连] 将在 {delay} 秒后尝试重连...");
            _isReconnecting = true;
            
            _reconnectTimer?.Dispose();
            _reconnectTimer = new Timer(async _ => await TryReconnectAsync(), null, delay * 1000, delay * 1000);
        }

        private void StopReconnectTimer()
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            _isReconnecting = false;
        }

        private async Task TryReconnectAsync()
        {
            if (!_isRunning || _isAuthenticated)
            {
                StopReconnectTimer();
                return;
            }
            
            try
            {
                Log.Info("[重连] 正在尝试重新连接...");
                
                _client.OnConnectionStateChanged -= OnConnectionStateChanged;
                _client.OnHeartbeat -= OnHeartbeatReceived;
                _client.OnLifecycle -= OnLifecycleEventReceived;
                _client.OnMessage -= OnMessageReceived;
                
                await _client.CloseAsync();
                
                _client = new OneBotClient();
                _client.OnMessage += OnMessageReceived;
                _client.OnLifecycle += OnLifecycleEventReceived;
                _client.OnHeartbeat += OnHeartbeatReceived;
                _client.OnConnectionStateChanged += OnConnectionStateChanged;
                
                _commandRegistry.SetClient(_client);
                
                var config = _configManager.GetConfig();
                bool connectSuccess = _client.ConnectSync(config.NapCatServerUrl, config.NapCatToken, 10);
                
                if (connectSuccess)
                {
                    _authCompletionSource = new TaskCompletionSource<bool>();
                    var authTask = _authCompletionSource.Task;
                    
                    if (await Task.WhenAny(authTask, Task.Delay(15000)) == authTask)
                    {
                        var success = await authTask;
                        if (success)
                        {
                            Log.Info("[重连] 重连成功喵AWA");
                            StopReconnectTimer();
                            return;
                        }
                    }
                }
                
                Log.Warning("[重连] 重连失败，将在下次定时器触发时重试...");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[重连] 重连异常: {ex.Message}");
            }
        }

        private async Task UpdateBotInfoAsync()
        {
            try
            {
                var result = await _client.GetLoginInfoAsync();
                
                if (result.Success && result.Data != null)
                {
                    _webUIManager?.SetBotInfo(
                        result.Data.UserId,
                        result.Data.Nickname,
                        "",
                        0,
                        true
                    );
                    Log.Info($"机器人账号信息已更新: {result.Data.Nickname} ({result.Data.UserId})");
                }
                else
                {
                    Log.Warning($"获取机器人账号信息失败: {result.ErrorMessage}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"获取机器人账号信息失败: {ex.Message}");
            }
        }
    }
}
