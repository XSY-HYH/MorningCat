using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MorningCat.PPC;

public class DotNetBuildHost
{
    private readonly string _runtimeDir;
    private readonly string _templateDir;
    private readonly string _dependencyDir;
    private readonly string _logDir;
    private readonly bool _debug;

    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    private StreamWriter? _logWriter;

    public DotNetBuildHost(string runtimeDir, string templateDir, string dependencyDir, string logDir, bool debug)
    {
        _runtimeDir = runtimeDir;
        _templateDir = templateDir;
        _dependencyDir = dependencyDir;
        _logDir = logDir;
        _debug = debug;
    }

    public bool Build(string outputName, List<string>? extraLinkerArgs, out string? logFile)
    {
        logFile = null;

        Directory.CreateDirectory(_logDir);
        logFile = Path.Combine(_logDir, $"build_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        try
        {
            _logWriter = new StreamWriter(logFile, false, System.Text.Encoding.UTF8) { AutoFlush = true };
            WriteLog($"MorningCat.PPC DotNet Build Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteLog($"RuntimeDir: {_runtimeDir}");
            WriteLog($"TemplateDir: {_templateDir}");
            WriteLog($"DependencyDir: {_dependencyDir}");
            WriteLog($"OutputName: {outputName}");
            WriteLog($"OS: {RuntimeInformation.OSDescription}");
            WriteLog($"Architecture: {RuntimeInformation.ProcessArchitecture}");
            WriteLog(new string('=', 60));

            var platformDir = FindPlatformRuntimeDir();
            if (platformDir == null)
            {
                Errors.Add($"未找到当前平台的 Runtime 目录");
                WriteLog($"[FATAL] 未找到当前平台的 Runtime 目录");
                WriteLog($"[DEBUG] 需要: {GetPlatformSubDir()}");
                WriteLog($"[DEBUG] 可用目录: {(Directory.Exists(_runtimeDir) ? string.Join(", ", Directory.GetDirectories(_runtimeDir).Select(Path.GetFileName)) : "无")}");
                return false;
            }

            WriteLog($"平台 Runtime: {platformDir}");

            var dotnetExe = FindDotNetExe(platformDir);
            if (dotnetExe == null)
            {
                Errors.Add($"未找到 dotnet 可执行文件: {platformDir}");
                WriteLog($"[FATAL] 未找到 dotnet 可执行文件");
                return false;
            }

            WriteLog($"dotnet: {dotnetExe}");

            var projectFile = Directory.GetFiles(_templateDir, "*.csproj").FirstOrDefault();
            if (projectFile == null)
            {
                Errors.Add("未找到 .csproj 项目文件");
                WriteLog("[FATAL] 未找到 .csproj 项目文件");
                return false;
            }

            WriteLog($"项目文件: {projectFile}");

            var args = BuildArguments(projectFile, outputName, extraLinkerArgs);
            WriteLog($"命令: {dotnetExe} {args}");

            var psi = new ProcessStartInfo
            {
                FileName = dotnetExe,
                Arguments = args,
                WorkingDirectory = _templateDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            SetEnvironmentVariables(psi, platformDir);

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                _logWriter?.WriteLine($"[OUT] {e.Data}");
                if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) && !e.Data.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    Errors.Add(e.Data);
                else if (e.Data.Contains("warning", StringComparison.OrdinalIgnoreCase))
                    Warnings.Add(e.Data);
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                _logWriter?.WriteLine($"[ERR] {e.Data}");
                Errors.Add(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            WriteLog($"退出码: {process.ExitCode}");

            if (process.ExitCode != 0)
            {
                Errors.Add($"构建失败，退出码: {process.ExitCode}");
                return false;
            }

            WriteLog("构建成功");
            return true;
        }
        catch (Exception ex)
        {
            Errors.Add($"构建异常: {ex.Message}");
            WriteLog($"[EXCEPTION] {ex.Message}");
            WriteLog($"[STACK] {ex.StackTrace}");
            return false;
        }
        finally
        {
            _logWriter?.Close();
            _logWriter = null;
        }
    }

    private string? FindPlatformRuntimeDir()
    {
        var subDir = GetPlatformSubDir();
        var dir = Path.Combine(_runtimeDir, subDir);
        if (Directory.Exists(dir)) return dir;

        if (Directory.Exists(_runtimeDir) && Directory.GetFiles(_runtimeDir, "dotnet*", SearchOption.TopDirectoryOnly).Length > 0)
            return _runtimeDir;

        if (Directory.Exists(_runtimeDir))
        {
            foreach (var d in Directory.GetDirectories(_runtimeDir))
            {
                if (Directory.GetFiles(d, "dotnet*", SearchOption.TopDirectoryOnly).Length > 0)
                    return d;
            }
        }

        return null;
    }

    private static string GetPlatformSubDir()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "unknown";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "64",
            Architecture.X86 => "86",
            Architecture.Arm64 => "Arm64",
            Architecture.Arm => "Arm",
            _ => "64"
        };

        return Path.Combine(os, arch);
    }

    private static string? FindDotNetExe(string platformDir)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        var path = Path.Combine(platformDir, exeName);
        if (File.Exists(path)) return path;

        foreach (var file in Directory.GetFiles(platformDir, "dotnet*"))
        {
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private static string BuildArguments(string projectFile, string outputName, List<string>? extraLinkerArgs)
    {
        var outputDir = Path.Combine(Path.GetDirectoryName(projectFile)!, "bin", "Release");
        var args = $"build \"{projectFile}\" -c Release -o \"{outputDir}\"";

        if (extraLinkerArgs != null && extraLinkerArgs.Count > 0)
        {
            foreach (var arg in extraLinkerArgs.Distinct(StringComparer.OrdinalIgnoreCase))
                args += $" {arg}";
        }

        return args;
    }

    private static void SetEnvironmentVariables(ProcessStartInfo psi, string platformDir)
    {
        var dotnetRoot = Path.GetDirectoryName(platformDir) ?? platformDir;
        psi.Environment["DOTNET_ROOT"] = platformDir;
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["NUGET_PACKAGES"] = Path.Combine(Path.GetTempPath(), "MorningCat_PPC_nuget");

        var existingPath = psi.Environment.TryGetValue("PATH", out var p) ? p : "";
        psi.Environment["PATH"] = $"{platformDir}{Path.PathSeparator}{existingPath}";
    }

    private void WriteLog(string message)
    {
        _logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }
}
