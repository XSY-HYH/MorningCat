using System;
using System.Threading.Tasks;
using Logging;
using MorningCat.Config;
using MorningCat.I18n;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private async Task ConnectPlatformsAsync()
        {
            try
            {
                Log.Name("MDC");
                Log.Info(_i18n.T("connection.connecting"));
                
                var results = await _mdc.ConnectAllAsync();
                
                foreach (var kv in results)
                {
                    if (kv.Value)
                    {
                        Log.Info(_i18n.T("connection.connected", kv.Key));
                    }
                    else
                    {
                        Log.Error(_i18n.T("connection.failed", kv.Key));
                    }
                }
                
                // 等待认证
                if (_mdc.IsPlatformEnabled(PlatformId.OneBot))
                {
                    _authCompletionSource = new TaskCompletionSource<bool>();
                    var authTask = _authCompletionSource.Task;
                    
                    if (await Task.WhenAny(authTask, Task.Delay(15000)) == authTask)
                    {
                        _isAuthenticated = await authTask;
                        if (_isAuthenticated)
                        {
                            Log.Info(_i18n.T("connection.auth_success"));
                        }
                        else
                        {
                            throw new Exception("OneBot login verification failed, please check token!");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("OneBot login verification timeout!");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(_i18n.T("connection.platform_failed", ex.Message));
                throw;
            }
        }
    }
}
