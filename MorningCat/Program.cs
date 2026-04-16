using System;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using MorningCat;

namespace MorningCat
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using var singletonLock = new SingletonLock();
            
            if (!singletonLock.TryAcquire())
            {
                Log.Name("MorningCat");
                Log.Error("已有 MorningCat 正在运行！");
                await Task.Delay(5000);
                return;
            }

            try
            {
                VirtualTerminal.Enable();

                bool isDebugMode = args.Contains("--debug") || args.Contains("-d");
                
                Log.Name("MorningCat");
                if (isDebugMode)
                {
                    Log.SetConsoleLevel(LogLevel.Debug);
                    Log.SetFileLevel(LogLevel.Debug);
                    Log.Debug("调试模式已启用");
                }
                else
                {
                    Log.SetConsoleLevel(LogLevel.Info);
                    Log.SetFileLevel(LogLevel.Debug);
                }
                ConsoleANSI.ConsoleAnsiArtist.PrintAnsiText("MorningCat", "100, 200, 255");
                Console.WriteLine();
                Log.Debug("MorningCat启动中...");
                var exitEvent = new TaskCompletionSource<bool>();
                var bot = new MorningCatBot(() =>
                {
                    exitEvent.TrySetResult(true);
                });
                if (bot.IsNewConfig)
                {
                    Log.Info("请修改配置文件后重新启动！");
                    await Task.Delay(5000);
                    return;
                }
                await bot.StartAsync();
                await bot.StartWebUIAsync();
                Log.Info("MorningCat 已启动AWA");
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    exitEvent.TrySetResult(true);
                };
                await exitEvent.Task;
                Log.Info("正在关闭 MorningCat...");
                await bot.StopAsync();
                Log.Info("MorningCat 已安全关闭");
            }
            catch (Exception ex)
            {
                Log.Critical($"启动失败: {ex.Message}");
                Log.Debug($"详细错误: {ex}");
            }
        }
    }
}
