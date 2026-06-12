using System;
using System.Threading.Tasks;
using Logging;
using MorningCat.Config;
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
                Log.Info("正在连接到消息平台...");
                
                var results = await _mdc.ConnectAllAsync();
                
                foreach (var kv in results)
                {
                    if (kv.Value)
                    {
                        Log.Info($"平台 {kv.Key} 连接成功");
                    }
                    else
                    {
                        Log.Error($"平台 {kv.Key} 连接失败");
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
                            Log.Info("OneBot认证成功喵AWA");
                        }
                        else
                        {
                            throw new Exception("OneBot登录验证失败，请检查token是否正确QAQ！");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("OneBot登录验证超时QAQ！");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"连接消息平台失败QAQ！: {ex.Message}");
                throw;
            }
        }
    }
}
