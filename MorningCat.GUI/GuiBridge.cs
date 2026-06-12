using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Logging;

namespace MorningCat.GUI
{
    public class GuiManager : IDisposable
    {
        private Process? _electronProcess;
        private bool _disposed = false;
        private bool _isRunning = false;
        private int _webuiPort = 8080;

        public bool IsRunning => _isRunning;

        public Action? OnRestartRequested { get; set; }
        public Action? OnShutdownRequested { get; set; }

        public bool Initialize()
        {
            return true;
        }

        public void SetWebuiPort(int port)
        {
            _webuiPort = port;
        }

        public void Show()
        {
            if (_isRunning) return;

            try
            {
                var (electronExe, appDir) = FindElectronApp();
                if (electronExe == null)
                {
                    Log.Warning("[GUI] 未找到Electron可执行文件，GUI无法启动");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = electronExe,
                    Arguments = $"\"{appDir}\" --webui-port={_webuiPort}",
                    WorkingDirectory = appDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _electronProcess = new Process { StartInfo = startInfo };
                _electronProcess.EnableRaisingEvents = true;

                _electronProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.Debug($"[Electron] {e.Data}");
                };

                _electronProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log.Debug($"[Electron-Err] {e.Data}");
                };

                _electronProcess.Exited += (s, e) =>
                {
                    _isRunning = false;
                    Log.Info("[GUI] Electron进程已退出");
                };

                _electronProcess.Start();
                _electronProcess.BeginOutputReadLine();
                _electronProcess.BeginErrorReadLine();

                _isRunning = true;
                Log.Info($"[GUI] Electron GUI已启动 (PID: {_electronProcess.Id}, WebUI端口: {_webuiPort})");
            }
            catch (Exception ex)
            {
                Log.Warning($"[GUI] Electron启动失败: {ex.Message}");
                _isRunning = false;
            }
        }

        public void Hide()
        {
        }

        public void SetRestartCallback(Action callback)
        {
            OnRestartRequested = callback;
        }

        public void SetShutdownCallback(Action callback)
        {
            OnShutdownRequested = callback;
        }

        public void UpdateBotStatus(string status)
        {
        }

        public void UpdateSystemInfo(string cpuModel, double cpuUsage, long memTotal, long memUsed)
        {
        }

        public void UpdatePluginList(string jsonPlugins)
        {
        }

        public void Shutdown()
        {
            if (_electronProcess != null && !_electronProcess.HasExited)
            {
                try
                {
                    _electronProcess.Kill(true);
                    Log.Info("[GUI] Electron进程已终止");
                }
                catch (Exception ex)
                {
                    Log.Debug($"[GUI] 终止Electron进程异常: {ex.Message}");
                }
            }
            _isRunning = false;
            _electronProcess = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Shutdown();
            _disposed = true;
        }

        private (string? electronExe, string? appDir) FindElectronApp()
        {
            string baseDir = AppContext.BaseDirectory;

            string[] appDirCandidates = new[]
            {
                Path.Combine(baseDir, "GUI", "electron"),
                Path.Combine(baseDir, "electron"),
                Path.Combine(baseDir, "..", "MorningCat.GUI", "electron"),
            };

            string? resolvedAppDir = null;
            foreach (var dir in appDirCandidates)
            {
                string full = Path.GetFullPath(dir);
                if (File.Exists(Path.Combine(full, "main.js")) && File.Exists(Path.Combine(full, "package.json")))
                {
                    resolvedAppDir = full;
                    break;
                }
            }

            if (resolvedAppDir == null)
            {
                Log.Debug("[GUI] 未找到Electron app目录(main.js + package.json)");
                return (null, null);
            }

            string platform = GetPlatformName();
            string executableName = GetExecutableName();

            string[] electronExeCandidates = new[]
            {
                Path.Combine(resolvedAppDir, "node_modules", "electron", "dist", executableName),
                Path.Combine(baseDir, "electron", platform, executableName),
                Path.Combine(baseDir, "electron", executableName),
            };

            foreach (var exe in electronExeCandidates)
            {
                string full = Path.GetFullPath(exe);
                if (File.Exists(full))
                {
                    Log.Debug($"[GUI] 找到Electron: {full}, App目录: {resolvedAppDir}");
                    return (full, resolvedAppDir);
                }
            }

            Log.Debug("[GUI] 未找到Electron可执行文件");
            return (null, resolvedAppDir);
        }

        private string GetPlatformName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "mac";
            else
                return "unknown";
        }

        private string GetExecutableName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "electron.exe";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "electron";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "electron.app";
            else
                return "electron";
        }
    }
}