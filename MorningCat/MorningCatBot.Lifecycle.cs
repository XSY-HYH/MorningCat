using System.Threading.Tasks;
using Logging;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private void OnPlatformConnectionChanged(object? sender, (PlatformId Platform, PlatformConnectionState State) e)
        {
            try
            {
                Log.Name("MDC");
                Log.Info($"平台 {e.Platform} 连接状态变化: {e.State}");
                
                if (e.State == PlatformConnectionState.Authenticated)
                {
                    _authCompletionSource?.TrySetResult(true);
                    _isAuthenticated = true;
                    _isReconnecting = false;
                    StopReconnectTimer();
                    Log.Info("平台认证成功喵AWA");

                    _webUIManager?.SetConnectionStatus(true);

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        await UpdateBotInfoAsync();
                        await RefreshContactListAsync();
                    });
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"OnPlatformConnectionChanged异常: {ex.Message}");
            }
        }

        private void OnPlatformDisconnected(object? sender, (PlatformId Platform, string Reason) e)
        {
            try
            {
                Log.Name("MDC");
                Log.Warning($"平台 {e.Platform} 断开连接: {e.Reason}");
                
                if (e.Platform == PlatformId.OneBot)
                {
                    _authCompletionSource?.TrySetResult(false);
                    _isAuthenticated = false;
                    System.Threading.Interlocked.Increment(ref _wsDisconnectCount);
                    
                    _webUIManager?.ClearBotInfo();
                    _webUIManager?.SetConnectionStatus(false);
                    
                    StartReconnectTimer();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"OnPlatformDisconnected异常: {ex.Message}");
            }
        }

        private void StartReconnectTimer()
        {
            if (_isReconnecting || !_isRunning) return;
            
            var config = _configManager.GetConfig();
            var delay = config.ReconnectDelay > 0 ? config.ReconnectDelay : 5;
            
            Log.Info($"将在 {delay} 秒后尝试重连...");
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
                Log.Info("正在尝试重新连接...");
                
                var adapter = _mdc.GetAdapter<OneBotPlatformAdapter>(PlatformId.OneBot);
                if (adapter != null)
                {
                    var success = await adapter.ReconnectAsync();
                    
                    if (success)
                    {
                        _authCompletionSource = new TaskCompletionSource<bool>();
                        var authTask = _authCompletionSource.Task;
                        
                        if (await Task.WhenAny(authTask, Task.Delay(15000)) == authTask)
                        {
                            var authSuccess = await authTask;
                            if (authSuccess)
                            {
                                Log.Info("重连成功喵！");
                                StopReconnectTimer();
                                return;
                            }
                        }
                    }
                }
                
                Log.Warning("重连失败，将在下次定时器触发时重试...");
            }
            catch (System.Exception ex)
            {
                Log.Error($"重连异常: {ex.Message}");
            }
        }

        private async Task UpdateBotInfoAsync()
        {
            try
            {
                var adapter = _mdc.GetAdapter<OneBotPlatformAdapter>(PlatformId.OneBot);
                if (adapter == null) return;
                
                var result = await adapter.GetLoginInfoAsync();
                
                if (result != null && result.Success && result.Data != null)
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
                    Log.Warning($"获取机器人账号信息失败: {result?.ErrorMessage}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"获取机器人账号信息失败: {ex.Message}");
            }
        }

        private async Task RefreshContactListAsync()
        {
            try
            {
                var groups = await _webUIManager.GetGroupListAsync();
                var friends = await _webUIManager.GetFriendListAsync();
                Log.Debug($"联系人列表已刷新: {groups.Count} 个群, {friends.Count} 个好友");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"刷新联系人列表失败: {ex.Message}");
            }
        }
    }
}
