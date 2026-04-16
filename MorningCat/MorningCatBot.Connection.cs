using System;
using System.Threading.Tasks;
using Logging;
using MorningCat.Config;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private async Task ConnectToNapCatAsync()
        {
            try
            {
                Log.Info("正在连接到 OneBot 服务...");
                
                var config = _configManager.GetConfig();
                
                _isAuthenticated = false;
                _authCompletionSource = new TaskCompletionSource<bool>();
                
                bool connectSuccess = _client.ConnectSync(config.NapCatServerUrl, config.NapCatToken, 10);
                
                if (connectSuccess)
                {
                    var authTask = _authCompletionSource.Task;
                    if (await Task.WhenAny(authTask, Task.Delay(15000)) == authTask)
                    {
                        _isAuthenticated = await authTask;
                        if (_isAuthenticated)
                        {
                            Log.Info("已成功连接到 OneBot 服务并完成登录验证喵AWA");
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
                else
                {
                    throw new Exception("连接OneBot服务失败QAQ！");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"连接 OneBot 服务失败QAQ！: {ex.Message}");
                throw;
            }
        }
    }
}
