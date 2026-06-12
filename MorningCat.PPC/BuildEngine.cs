using System.Reflection;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Spectre.Console;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MorningCat.PPC;

public class BuildEngine
{
    private readonly bool _debug;
    private readonly string _workDir;
    private readonly string _tempDir;
    private PluginConfig? _pluginConfig;
    private BuildConfig? _buildConfig;

    public BuildEngine(string workDir, bool debug = false)
    {
        _workDir = Path.GetFullPath(workDir);
        _tempDir = Path.Combine(_workDir, "temp");
        _debug = debug;
    }

    public int Build()
    {
        AnsiConsole.MarkupLine("[bold cyan]MorningCat.Python 插件编译器[/]");
        AnsiConsole.WriteLine();

        if (!LoadPluginConfig()) return 1;
        if (!LoadBuildConfig()) return 1;

        return AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
            .Start(ctx =>
            {
                var step1 = ctx.AddTask("准备构建环境");
                if (!PrepareEnvironment(step1)) return 1;

                var step2 = ctx.AddTask("下载 Runtime");
                if (!DownloadRuntime(step2)) return 1;

                var step3 = ctx.AddTask("下载 Python 依赖");
                if (!DownloadDependencies(step3)) return 1;

                var step4 = ctx.AddTask("复制资源文件");
                if (!CopyResources(step4)) return 1;

                var step5 = ctx.AddTask("编译项目");
                if (!ExecuteBuild(step5)) return 1;

                var step6 = ctx.AddTask("提取构建产物");
                if (!ExtractArtifact(step6)) return 1;

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]构建完成:[/] {Path.Combine(_tempDir, "Artifact")}");
                return 0;
            });
    }

    public int Init()
    {
        var pluginYmlPath = Path.Combine(_workDir, "Plugin.yml");
        if (!File.Exists(pluginYmlPath))
        {
            var defaultYml = @"name: 我的插件
description: MorningCat Python 插件
author: 作者
website:
tags:
  - python
entry: src/main.py
resources: []
dependencies: []
libraryDependencies: []
";
            File.WriteAllText(pluginYmlPath, defaultYml);
            AnsiConsole.MarkupLine("[green]已创建 Plugin.yml[/]");
        }

        var buildYmlPath = Path.Combine(_workDir, "build.yml");
        if (!File.Exists(buildYmlPath))
        {
            var defaultBuildYml = @"output:
runtime:
  version: 10.0.300
dependencies: []
linkerArgs: []
";
            File.WriteAllText(buildYmlPath, defaultBuildYml);
            AnsiConsole.MarkupLine("[green]已创建 build.yml[/]");
        }

        var srcDir = Path.Combine(_workDir, "src");
        var mainPyPath = Path.Combine(srcDir, "main.py");
        if (!File.Exists(mainPyPath))
        {
            Directory.CreateDirectory(srcDir);
            var defaultMainPy = @"from MorningCat.PythonBridge import PluginBase

class MyPlugin(PluginBase):
    def on_init(self):
        self.log(""插件已初始化"")

    def on_message(self, event):
        pass

plugin = MyPlugin()
";
            File.WriteAllText(mainPyPath, defaultMainPy);
            AnsiConsole.MarkupLine("[green]已创建 src/main.py[/]");
        }

        AnsiConsole.MarkupLine("[green]项目初始化完成[/]");
        return 0;
    }

    public int Clean()
    {
        AnsiConsole.MarkupLine("[yellow]清理临时文件...[/]");

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
                AnsiConsole.MarkupLine($"[green]已删除:[/] {_tempDir}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]清理失败:[/] {ex.Message}");
            }
        }

        return 0;
    }

    public int Test()
    {
        if (!LoadPluginConfig()) return 1;

        var artifactDir = Path.Combine(_tempDir, "Artifact");
        if (!Directory.Exists(artifactDir))
        {
            AnsiConsole.MarkupLine("[red]错误: 未找到构建产物，请先执行 build[/]");
            return 1;
        }

        var dependencyDir = Path.Combine(_tempDir, "Dependency");
        var testDir = Path.Combine(_tempDir, "test");
        var logsDir = Path.Combine(_tempDir, "logs");

        var outputName = _buildConfig?.GetOutputName(_pluginConfig!.Name) ?? _pluginConfig!.Name;
        var host = new PluginTestHost(artifactDir, dependencyDir, testDir, logsDir, _debug);
        return host.RunAllTests(outputName);
    }

    public int RunCore()
    {
        if (!LoadPluginConfig()) return 1;

        var artifactDir = Path.Combine(_tempDir, "Artifact");
        if (!Directory.Exists(artifactDir))
        {
            AnsiConsole.MarkupLine("[red]错误: 未找到构建产物，请先执行 build[/]");
            return 1;
        }

        var dll = Directory.GetFiles(artifactDir, "*.dll").FirstOrDefault();
        if (dll == null)
        {
            AnsiConsole.MarkupLine("[red]错误: 未找到 DLL 产物[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"运行: {Path.GetFileName(dll)}");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"exec \"{dll}\"",
            WorkingDirectory = artifactDir,
            UseShellExecute = false
        };

        try
        {
            var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]错误: 无法启动进程[/]");
                return 1;
            }
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]运行失败:[/] {ex.Message}");
            return 1;
        }
    }

    private bool LoadPluginConfig()
    {
        var configPath = Path.Combine(_workDir, "Plugin.yml");
        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine("[red]错误: 未找到 Plugin.yml[/]");
            return false;
        }

        try
        {
            var yaml = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            _pluginConfig = deserializer.Deserialize<PluginConfig>(yaml);

            if (string.IsNullOrEmpty(_pluginConfig?.Name))
            {
                AnsiConsole.MarkupLine("[red]错误: Plugin.yml 缺少 name 字段[/]");
                return false;
            }

            if (!_pluginConfig.HasPythonTag)
            {
                AnsiConsole.MarkupLine("[red]错误: Plugin.yml 标签必须包含 python[/]");
                return false;
            }

            if (_debug)
            {
                AnsiConsole.MarkupLine($"[grey][调试] 名称:[/] {_pluginConfig.Name}");
                AnsiConsole.MarkupLine($"[grey][调试] 描述:[/] {_pluginConfig.Description}");
                AnsiConsole.MarkupLine($"[grey][调试] 作者:[/] {_pluginConfig.Author}");
                AnsiConsole.MarkupLine($"[grey][调试] 入口:[/] {_pluginConfig.Entry}");
                AnsiConsole.MarkupLine($"[grey][调试] 标签:[/] [{string.Join(", ", _pluginConfig.Tags)}]");
            }

            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]解析失败:[/] {ex.Message}");
            return false;
        }
    }

    private bool LoadBuildConfig()
    {
        var configPath = Path.Combine(_workDir, "build.yml");
        if (!File.Exists(configPath))
        {
            _buildConfig = new BuildConfig();
            if (_debug) AnsiConsole.MarkupLine("[grey][调试] 未找到 build.yml，使用默认配置[/]");
            return true;
        }

        try
        {
            var yaml = File.ReadAllText(configPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            _buildConfig = deserializer.Deserialize<BuildConfig>(yaml) ?? new BuildConfig();

            if (_debug)
            {
                AnsiConsole.MarkupLine($"[grey][调试] 输出:[/] {_buildConfig.GetOutputName(_pluginConfig!.Name)}");
                AnsiConsole.MarkupLine($"[grey][调试] Runtime 版本:[/] {_buildConfig.Runtime.Version}");
                AnsiConsole.MarkupLine($"[grey][调试] Runtime URL:[/] {_buildConfig.Runtime.Url}");
                AnsiConsole.MarkupLine($"[grey][调试] 依赖:[/] {_buildConfig.Dependencies.Count} 个");
                AnsiConsole.MarkupLine($"[grey][调试] 链接参数:[/] [{string.Join(", ", _buildConfig.LinkerArgs)}]");
            }

            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]build.yml 解析失败:[/] {ex.Message}");
            return false;
        }
    }

    private bool PrepareEnvironment(ProgressTask task)
    {
        var steps = new[]
        {
            ("Template", "MorningCat.PPC.Resources.Template.zip"),
            ("Dependency", "MorningCat.PPC.Resources.Dependency.zip"),
            ("Runtime", "MorningCat.PPC.Resources.Runtime.zip"),
        };

        var assembly = Assembly.GetExecutingAssembly();

        for (var i = 0; i < steps.Length; i++)
        {
            var (dirName, resourceName) = steps[i];
            var targetDir = Path.Combine(_tempDir, dirName);
            if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Length > 0)
            {
                if (_debug) AnsiConsole.MarkupLine($"[grey]  跳过: {dirName}[/]");
                task.Value = task.MaxValue * (i + 1) / steps.Length;
                continue;
            }

            Directory.CreateDirectory(targetDir);

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                AnsiConsole.MarkupLine($"[yellow]警告: 嵌入式资源 {resourceName} 不存在[/]");
                if (_debug)
                    AnsiConsole.MarkupLine($"[grey][调试] 可用资源: [{string.Join(", ", assembly.GetManifestResourceNames())}][/]");
                task.Value = task.MaxValue * (i + 1) / steps.Length;
                continue;
            }

            try
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                var totalEntries = archive.Entries.Count;
                var processed = 0;

                foreach (var entry in archive.Entries)
                {
                    var destPath = Path.Combine(targetDir, entry.FullName);
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        Directory.CreateDirectory(destPath);
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(destPath);
                        if (dir != null) Directory.CreateDirectory(dir);
                        using var entryStream = entry.Open();
                        using var fileStream = File.Create(destPath);
                        entryStream.CopyTo(fileStream);
                    }

                    processed++;
                    var stepBase = task.MaxValue * i / steps.Length;
                    var stepRange = task.MaxValue / steps.Length;
                    task.Value = stepBase + stepRange * processed / totalEntries;
                }
                if (_debug) AnsiConsole.MarkupLine($"[grey]  解压: {dirName} ({archive.Entries.Count} 个文件)[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]解压失败 {dirName}:[/] {ex.Message}");
                return false;
            }
        }

        task.Value = task.MaxValue;
        return true;
    }

    private bool DownloadRuntime(ProgressTask task)
    {
        var runtimeUrl = _buildConfig?.Runtime.Url;
        var runtimeDir = Path.Combine(_tempDir, "Runtime");
        var platformSubDir = GetPlatformSubDir();
        var platformRuntimeDir = Path.Combine(runtimeDir, platformSubDir);

        if (Directory.Exists(platformRuntimeDir) && Directory.GetFiles(platformRuntimeDir, "*", SearchOption.AllDirectories).Length > 0)
        {
            if (_debug) AnsiConsole.MarkupLine($"[grey]  Runtime 已存在: {platformSubDir}[/]");
            task.Value = task.MaxValue;
            return true;
        }

        if (string.IsNullOrWhiteSpace(runtimeUrl))
        {
            AnsiConsole.MarkupLine("[yellow]警告: 未配置 Runtime 下载地址，且本地无 Runtime[/]");
            AnsiConsole.MarkupLine("[yellow]请在 build.yml 中设置 runtime.url[/]");
            task.Value = task.MaxValue;
            return true;
        }

        try
        {
            if (_debug) AnsiConsole.MarkupLine($"[grey]  下载 Runtime: {runtimeUrl}[/]");

            using var httpClient = new HttpClient();
            var tempFile = Path.Combine(_tempDir, "runtime_download.tmp");

            task.IsIndeterminate = true;
            var data = httpClient.GetByteArrayAsync(runtimeUrl).Result;
            File.WriteAllBytes(tempFile, data);
            task.IsIndeterminate = false;

            Directory.CreateDirectory(platformRuntimeDir);

            if (runtimeUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(tempFile, platformRuntimeDir, overwriteFiles: true);
            }
            else if (runtimeUrl.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || runtimeUrl.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                var tar = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{tempFile}\" -C \"{platformRuntimeDir}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                tar?.WaitForExit();
            }

            File.Delete(tempFile);
            if (_debug) AnsiConsole.MarkupLine($"[grey]  Runtime 已下载到: {platformSubDir}[/]");
        }
        catch (Exception ex)
        {
            task.IsIndeterminate = false;
            AnsiConsole.MarkupLine($"[red]Runtime 下载失败:[/] {ex.Message}");
            return false;
        }

        task.Value = task.MaxValue;
        return true;
    }

    private bool DownloadDependencies(ProgressTask task)
    {
        var deps = _buildConfig?.Dependencies;
        if (deps == null || deps.Count == 0)
        {
            task.Value = task.MaxValue;
            return true;
        }

        var pyLibDir = Path.Combine(_tempDir, "Template", "PyLibs");
        Directory.CreateDirectory(pyLibDir);

        for (var i = 0; i < deps.Count; i++)
        {
            var dep = deps[i];

            if (string.IsNullOrWhiteSpace(dep.Url))
            {
                AnsiConsole.MarkupLine($"[yellow]警告: 依赖 {dep.Name} 没有指定下载链接，跳过[/]");
                task.Value = task.MaxValue * (i + 1) / deps.Count;
                continue;
            }

            try
            {
                var fileName = dep.Url.Split('/').Last();
                var destPath = Path.Combine(pyLibDir, fileName);

                if (File.Exists(destPath))
                {
                    if (_debug) AnsiConsole.MarkupLine($"[grey]  跳过已下载: {dep.Name}[/]");
                    task.Value = task.MaxValue * (i + 1) / deps.Count;
                    continue;
                }

                if (_debug) AnsiConsole.MarkupLine($"[grey]  下载: {dep.Name} from {dep.Url}[/]");

                using var httpClient = new HttpClient();
                var data = httpClient.GetByteArrayAsync(dep.Url).Result;
                File.WriteAllBytes(destPath, data);

                if (destPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(destPath, pyLibDir);
                    File.Delete(destPath);
                }
                else if (destPath.EndsWith(".whl", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(destPath, pyLibDir);
                    File.Delete(destPath);
                }
                else if (destPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || destPath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
                {
                    var extractDir = Path.Combine(pyLibDir, Path.GetFileNameWithoutExtension(fileName));
                    Directory.CreateDirectory(extractDir);
                    var tar = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "tar",
                        Arguments = $"-xzf \"{destPath}\" -C \"{extractDir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    tar?.WaitForExit();
                    File.Delete(destPath);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]下载失败 {dep.Name}:[/] {ex.Message}");
                return false;
            }

            task.Value = task.MaxValue * (i + 1) / deps.Count;
        }

        task.Value = task.MaxValue;
        return true;
    }

    private bool CopyResources(ProgressTask task)
    {
        var resourceDir = Path.Combine(_tempDir, "Template", "Resource");
        Directory.CreateDirectory(resourceDir);

        var entryPath = Path.Combine(_workDir, _pluginConfig!.Entry);
        if (!File.Exists(entryPath))
        {
            AnsiConsole.MarkupLine($"[red]错误: 入口文件不存在[/] {entryPath}");
            return false;
        }

        var allResources = new List<string> { _pluginConfig.Entry };
        allResources.AddRange(_pluginConfig.Resources);

        for (var i = 0; i < allResources.Count; i++)
        {
            var res = allResources[i];
            var srcPath = Path.Combine(_workDir, res);
            if (File.Exists(srcPath) || Directory.Exists(srcPath))
                CopyFileOrDir(_workDir, res, resourceDir);

            task.Value = task.MaxValue * (i + 1) / allResources.Count;
        }

        var metaContent = $@"name={_pluginConfig.Name}
description={_pluginConfig.Description}
author={_pluginConfig.Author}
website={_pluginConfig.Website}
tags={string.Join(",", _pluginConfig.Tags)}
dependencies={string.Join(",", _pluginConfig.Dependencies)}
libraryDependencies={string.Join(",", _pluginConfig.LibraryDependencies)}";
        File.WriteAllText(Path.Combine(resourceDir, "plugin.meta"), metaContent);

        File.WriteAllText(Path.Combine(resourceDir, "entry.path"), _pluginConfig.Entry);

        var templateDir = Path.Combine(_tempDir, "Template");
        var bridgeSrc = Path.Combine(templateDir, "PythonBridge.py");
        if (File.Exists(bridgeSrc))
        {
            File.Copy(bridgeSrc, Path.Combine(resourceDir, "MorningCat.PythonBridge.py"), true);
        }

        return true;
    }

    private bool ExecuteBuild(ProgressTask task)
    {
        task.IsIndeterminate = true;

        var runtimeDir = Path.Combine(_tempDir, "Runtime");
        var templateDir = Path.Combine(_tempDir, "Template");
        var dependencyDir = Path.Combine(_tempDir, "Dependency");
        var logsDir = Path.Combine(_tempDir, "logs");

        var outputName = _buildConfig?.GetOutputName(_pluginConfig!.Name) ?? _pluginConfig!.Name;
        var linkerArgs = _buildConfig?.LinkerArgs;

        var host = new DotNetBuildHost(runtimeDir, templateDir, dependencyDir, logsDir, _debug);
        var result = host.Build(outputName, linkerArgs, out var logFile);

        task.IsIndeterminate = false;
        task.Value = task.MaxValue;

        if (!result)
        {
            AnsiConsole.MarkupLine("[red]编译失败[/]");
            foreach (var err in host.Errors)
                AnsiConsole.MarkupLine($"  [red]{err.EscapeMarkup()}[/]");
            if (logFile != null)
                AnsiConsole.MarkupLine($"  日志: {logFile}");
            return false;
        }

        if (host.Warnings.Count > 0)
            AnsiConsole.MarkupLine($"  [yellow]{host.Warnings.Count} 个警告[/]");

        if (logFile != null && _debug)
            AnsiConsole.MarkupLine($"  [grey]日志: {logFile}[/]");

        return true;
    }

    private bool ExtractArtifact(ProgressTask task)
    {
        var artifactDir = Path.Combine(_tempDir, "Artifact");
        Directory.CreateDirectory(artifactDir);

        var templateDir = Path.Combine(_tempDir, "Template");
        var binDir = Path.Combine(templateDir, "bin", "Release");

        if (!Directory.Exists(binDir))
        {
            AnsiConsole.MarkupLine("[red]错误: 未找到编译产物目录[/]");
            return false;
        }

        var dlls = Directory.GetFiles(binDir, "*.dll");
        for (var i = 0; i < dlls.Length; i++)
        {
            File.Copy(dlls[i], Path.Combine(artifactDir, Path.GetFileName(dlls[i])), true);
            task.Value = task.MaxValue * (i + 1) / dlls.Length;
        }

        var pyLibsDir = Path.Combine(templateDir, "PyLibs");
        if (Directory.Exists(pyLibsDir))
        {
            var pyLibDest = Path.Combine(artifactDir, "PyLibs");
            CopyDirectory(pyLibsDir, pyLibDest);
        }

        AnsiConsole.MarkupLine($"提取了 {dlls.Length} 个文件到 Artifact/");
        return true;
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

    private void CopyFileOrDir(string baseDir, string relativePath, string targetDir)
    {
        var srcPath = Path.Combine(baseDir, relativePath);
        var destPath = Path.Combine(targetDir, relativePath);

        if (File.Exists(srcPath))
        {
            var dir = Path.GetDirectoryName(destPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.Copy(srcPath, destPath, true);
        }
        else if (Directory.Exists(srcPath))
        {
            CopyDirectory(srcPath, destPath);
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }
}
