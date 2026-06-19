using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Logging;
using MorningCat.I18n;

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
                    Log.Error(I18nManager.S("log.unhandled_exception", ex.Message, ex.StackTrace));
                }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Log.Error(I18nManager.S("log.unobserved_task_exception", e.Exception?.InnerException?.Message ?? e.Exception?.Message));
                e.SetObserved();
            };

            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

                VirtualTerminal.Enable();

                bool isDebugMode = args.Contains("--debug") || args.Contains("-d");
                bool isTestMode = args.Contains("--testmode") || args.Contains("-t");

                // 解析 --lang 参数
                string? overrideLang = null;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--lang" && i + 1 < args.Length)
                    {
                        overrideLang = args[i + 1];
                    }
                }

                // 先初始化国际化默认值（en），避免日志打印翻译键
                I18nManager.Instance.InitializeDefault();

                Log.Name("MorningCat");
                if (isDebugMode)
                {
                    Log.SetConsoleLevel(LogLevel.Debug);
                    Log.SetFileLevel(LogLevel.Debug);
                    Log.Debug(I18nManager.S("log.debug_mode_enabled"));
                }
                else
                {
                    Log.SetConsoleLevel(LogLevel.Info);
                    Log.SetFileLevel(LogLevel.Debug);
                }

                if (isTestMode)
                {
                    Log.Warning(I18nManager.S("log.test_mode_enabled"));
                }

                ConsoleANSI.ConsoleAnsiArtist.PrintAnsiText("MorningCat", "100, 200, 255");
                Console.WriteLine();
                Log.Debug(I18nManager.S("log.starting"));

                var bot = new MorningCatBot(() =>
                {
                    _exitEvent.TrySetResult(true);
                }, isTestMode, isDebugMode, overrideLang);

                if (bot.IsNewConfig)
                {
                    Log.Info(I18nManager.S("log.please_modify_config"));
                    await Task.Delay(5000);
                    return 1;
                }

                await bot.StartAsync();
                await bot.StartWebUIAsync();
                bot.StartGui();
                Log.Info(I18nManager.S("log.started"));

                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    _exitEvent.TrySetResult(true);
                };

                await _exitEvent.Task;
                Log.Debug(I18nManager.S("log.shutting_down"));
                await bot.StopAsync();
                Log.Info(I18nManager.S("log.safely_shutdown"));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Critical(I18nManager.S("log.startup_failed", ex.Message));
                Log.Debug(I18nManager.S("log.startup_error_detail", ex));
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