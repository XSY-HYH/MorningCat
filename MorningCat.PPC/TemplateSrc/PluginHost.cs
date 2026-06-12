using System.Reflection;
using System.Text;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.PluginAPI;
using OneBotLib;
using PyToNet;
using PyToNet.Bidirectional;

namespace MorningCat.PythonPlugin;

public class PluginHost : ModuleBase
{
    private BidirectionalEngine? _engine;
    internal string PluginName { get; set; } = "";
    internal string PluginDescription { get; set; } = "";
    internal string PluginAuthor { get; set; } = "";
    internal string PluginWebsite { get; set; } = "";
    internal string[] PluginTags { get; set; } = Array.Empty<string>();
    internal string[] PluginDependencies { get; set; } = Array.Empty<string>();
    internal string[] LibraryDependencies { get; set; } = Array.Empty<string>();

    public OneBotClient? Client { get; set; }
    public CommandRegistry? CommandRegistry { get; set; }
    public PluginCommandAPI? PluginCommandAPI { get; set; }

    public override IEnumerable<string> GetDependencies() => PluginDependencies;
    public override IEnumerable<string> GetLibraryDependencies() => LibraryDependencies;

    public override async Task Init()
    {
        LoadPluginMetadata();

        var pythonPath = FindPythonPath();
        if (pythonPath == null)
        {
            Log.Error("[PythonPlugin] 未找到 Python 解释器，请安装 Python 3.8+");
            return;
        }

        var entryCode = LoadResourceCode();
        if (entryCode == null)
        {
            Log.Error("[PythonPlugin] 未找到入口 Python 文件");
            return;
        }

        try
        {
            _engine = new BidirectionalEngine(pythonPath);

            var bridge = new PythonBridge(this);
            _engine.RegisterObject("morningcat", bridge);

            var bridgeCode = LoadBridgeCode();
            await _engine.ExecutePythonAsync(bridgeCode);
            await _engine.ExecutePythonAsync(entryCode);

            Log.Info($"[PythonPlugin] {PluginName} 已加载");
        }
        catch (Exception ex)
        {
            Log.Error($"[PythonPlugin] 初始化失败: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public override async Task Exit()
    {
        try
        {
            if (_engine != null)
            {
                await _engine.ExecutePythonAsync("__on_exit__()");
                _engine.Dispose();
                _engine = null;
            }
        }
        catch { }

        Log.Info($"[PythonPlugin] {PluginName} 已卸载");
        await Task.CompletedTask;
    }

    private void LoadPluginMetadata()
    {
        var meta = ReadResourceText("plugin.meta");
        if (meta == null) return;

        foreach (var line in meta.Split('\n'))
        {
            var sep = line.IndexOf('=');
            if (sep < 0) continue;
            var key = line[..sep].Trim();
            var val = line[(sep + 1)..].Trim();

            switch (key)
            {
                case "name": PluginName = val; break;
                case "description": PluginDescription = val; break;
                case "author": PluginAuthor = val; break;
                case "website": PluginWebsite = val; break;
                case "tags": PluginTags = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "dependencies": PluginDependencies = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "libraryDependencies": LibraryDependencies = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
            }
        }
    }

    private string? FindPythonPath()
    {
        var candidates = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Programs", "Python", "Python311"));
            candidates.Add(Path.Combine(localAppData, "Programs", "Python", "Python312"));
            candidates.Add(Path.Combine(localAppData, "Programs", "Python", "Python313"));
            candidates.Add(@"C:\Python311");
            candidates.Add(@"C:\Python312");
            candidates.Add(@"C:\Python313");
        }

        foreach (var dir in candidates)
        {
            var exe = OperatingSystem.IsWindows() ? "python.exe" : "python3";
            if (File.Exists(Path.Combine(dir, exe)))
                return dir;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = OperatingSystem.IsWindows() ? "python" : "python3",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    var path = proc.StandardOutput.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return Path.GetDirectoryName(path);
                }
            }
        }
        catch { }

        return null;
    }

    private string? LoadResourceCode()
    {
        var entry = ReadResourceText("entry.path");
        if (entry == null) return null;

        var resourceName = entry.Replace('/', '.').Replace('\\', '.');
        return ReadResourceText(resourceName);
    }

    private string? LoadBridgeCode()
    {
        return ReadResourceText("MorningCat.PythonBridge");
    }

    private string? ReadResourceText(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullNames = assembly.GetManifestResourceNames();

        var match = fullNames.FirstOrDefault(n =>
            n.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase) ||
            n.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

        if (match == null) return null;

        using var stream = assembly.GetManifestResourceStream(match);
        if (stream == null) return null;

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

public class PythonBridge
{
    private readonly PluginHost _host;

    public PythonBridge(PluginHost host)
    {
        _host = host;
    }

    public void LogInfo(string message) => Log.Info($"[Python] {message}");
    public void LogError(string message) => Log.Error($"[Python] {message}");
    public void LogDebug(string message) => Log.Debug($"[Python] {message}");

    public async Task SendMessage(long userId, string message)
    {
        if (_host.Client != null)
            await _host.Client.SendPrivateMsgAsync(userId, message);
    }

    public async Task SendGroupMessage(long groupId, string message)
    {
        if (_host.Client != null)
            await _host.Client.SendGroupMsgAsync(groupId, message);
    }

    public string GetPluginName() => _host.PluginName;
    public string GetPluginDescription() => _host.PluginDescription;
    public string GetPluginAuthor() => _host.PluginAuthor;
}
