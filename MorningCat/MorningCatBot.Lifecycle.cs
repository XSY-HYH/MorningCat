using System.Threading.Tasks;
using Logging;
using MorningCat.I18n;
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
                Log.Info(I18nManager.S("lifecycle.connection_changed", e.Platform, e.State));
                
                if (e.State == PlatformConnectionState.Authenticated)
                {
                    _authCompletionSource?.TrySetResult(true);
                    _isAuthenticated = true;
                    _isReconnecting = false;
                    StopReconnectTimer();
                    Log.Info(I18nManager.S("lifecycle.authenticated"));

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
                Log.Error(I18nManager.S("lifecycle.connection_changed_error", ex.Message));
            }
        }

        private void OnPlatformDisconnected(object? sender, (PlatformId Platform, string Reason) e)
        {
            try
            {
                Log.Name("MDC");
                Log.Warning(I18nManager.S("lifecycle.disconnected", e.Platform, e.Reason));
                
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
                Log.Error(I18nManager.S("lifecycle.disconnected_error", ex.Message));
            }
        }

        private void StartReconnectTimer()
        {
            if (_isReconnecting || !_isRunning) return;
            
            var config = _configManager.GetConfig();
            var delay = config.ReconnectDelay > 0 ? config.ReconnectDelay : 5;
            
            Log.Info(I18nManager.S("lifecycle.reconnect_in", delay));
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
                Log.Info(I18nManager.S("lifecycle.reconnecting"));
                
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
                                Log.Info(I18nManager.S("lifecycle.reconnect_success"));
                                StopReconnectTimer();
                                return;
                            }
                        }
                    }
                }
                
                Log.Warning(I18nManager.S("lifecycle.reconnect_failed"));
            }
            catch (System.Exception ex)
            {
                Log.Error(I18nManager.S("lifecycle.reconnect_error", ex.Message));
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
                    Log.Info(I18nManager.S("lifecycle.bot_info_updated", result.Data.Nickname, result.Data.UserId));
                }
                else
                {
                    Log.Warning(I18nManager.S("lifecycle.bot_info_failed", result?.ErrorMessage));
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning(I18nManager.S("lifecycle.bot_info_error", ex.Message));
            }
        }

        private async Task RefreshContactListAsync()
        {
            try
            {
                if (_webUIManager == null) return;
                var groups = await _webUIManager.GetGroupListAsync();
                var friends = await _webUIManager.GetFriendListAsync();
                Log.Debug(I18nManager.S("lifecycle.contacts_refreshed", groups.Count, friends.Count));
            }
            catch (System.Exception ex)
            {
                Log.Warning(I18nManager.S("lifecycle.contacts_refresh_failed", ex.Message));
            }
        }
    }
}
