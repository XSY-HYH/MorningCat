using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Logging;

namespace MorningCat
{
    public class Program
    {
        private static readonly string[] _assemblySearchPaths = Array.Empty<string>();
        private static Action? _updateCallback;
        private static TaskCompletionSource<bool>? _exitEvent;

        public static Action? UpdateCallback => _updateCallback;

        static Program()
        {
            var baseDir = AppContext.BaseDirectory;
            var modulesDir = Path.Combine(baseDir, "Modules");
            var modulesLibDir = Path.Combine(modulesDir, "Library");
            var coreDir = Path.Combine(baseDir, "MorningCatCore");

            _assemblySearchPaths = new[]
            {
                Path.GetFullPath(modulesLibDir),
                Path.GetFullPath(modulesDir),
                baseDir,
                Path.GetFullPath(coreDir)
            };
        }

        public static async Task<int> Main(string[] args)
        {
            _exitEvent = new TaskCompletionSource<bool>();

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    Log.Name("全局异常");
                    Log.Error($"未处理异常: {ex.Message}\n{ex.StackTrace}");
                }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Log.Name("全局异常");
                Log.Error($"未观察的任务异常: {e.Exception?.InnerException?.Message ?? e.Exception?.Message}");
                e.SetObserved();
            };

            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

                VirtualTerminal.Enable();

                bool isDebugMode = args.Contains("--debug") || args.Contains("-d");
                bool isTestMode = args.Contains("--testmode") || args.Contains("-t");

                Log.Name("MorningCat主类");
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

                if (isTestMode)
                {
                    Log.Warning("测试模式已启用 - 插件签名验证已禁用");
                }

                ConsoleANSI.ConsoleAnsiArtist.PrintAnsiText("MorningCat", "100, 200, 255");
                Console.WriteLine();
                Log.Debug("MorningCat启动中...");

                var bot = new MorningCatBot(() =>
                {
                    _exitEvent.TrySetResult(true);
                }, isTestMode);

                if (bot.IsNewConfig)
                {
                    Log.Info("请修改配置文件后重新启动！");
                    await Task.Delay(5000);
                    return 1;
                }

                await bot.StartAsync();
                await bot.StartWebUIAsync();
                bot.StartGui();
                Log.Info("MorningCat启动成功喵！");

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    _exitEvent.TrySetResult(true);
                };

                await _exitEvent.Task;
                Log.Debug("正在关闭MorningCat...");
                await bot.StopAsync();
                Log.Info("MorningCat已安全关闭");
                return 0;
            }
            catch (Exception ex)
            {
                Log.Critical($"启动失败QAQ: {ex.Message}");
                Log.Debug($"详细错误: {ex}");
                return 1;
            }
        }

        public static void MainWithRestartCallback(
            string[] args,
            Action restartCallback,
            Action shutdownCallback,
            Action updateCallback)
        {
            _updateCallback = () =>
            {
                updateCallback?.Invoke();
                _exitEvent?.TrySetResult(true);
            };
            Main(args).GetAwaiter().GetResult();
        }

        private static Assembly? ResolveAssembly(object? sender, ResolveEventArgs args)
        {
            var assemblyName = new AssemblyName(args.Name);
            var dllName = assemblyName.Name + ".dll";

            foreach (var searchPath in _assemblySearchPaths)
            {
                var dllPath = Path.Combine(searchPath, dllName);
                if (File.Exists(dllPath))
                {
                    try
                    {
                        return Assembly.LoadFrom(dllPath);
                    }
                    catch
                    {
                    }
                }
            }

            var defaultAlcAssembly = AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(a => a.GetName().FullName == args.Name);
            if (defaultAlcAssembly != null)
                return defaultAlcAssembly;

            return null;
        }
    }
}