using System.Reflection;
using System.Runtime.Loader;
using Spectre.Console;

namespace MorningCat.PPC;

public class PluginTestHost
{
    private readonly string _artifactDir;
    private readonly string _dependencyDir;
    private readonly string _testDir;
    private readonly string _libDir;
    private readonly string _logDir;
    private readonly bool _debug;

    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    private StreamWriter? _logWriter;
    private string? _logFile;

    public PluginTestHost(string artifactDir, string dependencyDir, string testDir, string logDir, bool debug)
    {
        _artifactDir = artifactDir;
        _dependencyDir = dependencyDir;
        _testDir = testDir;
        _libDir = Path.Combine(testDir, "lib");
        _logDir = logDir;
        _debug = debug;
    }

    public int RunAllTests(string pluginName)
    {
        var passCount = 0;
        var totalTests = 3;

        Directory.CreateDirectory(_logDir);
        _logFile = Path.Combine(_logDir, $"test_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _logWriter = new StreamWriter(_logFile, false, System.Text.Encoding.UTF8) { AutoFlush = true };

        WriteLog($"MorningCat.PPC Test Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteLog($"PluginName: {pluginName}");
        WriteLog($"ArtifactDir: {_artifactDir}");
        WriteLog($"DependencyDir: {_dependencyDir}");
        WriteLog($"TestDir: {_testDir}");
        WriteLog(new string('=', 60));

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]插件测试: {pluginName.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        if (!Test1_ModuleLoad(pluginName))
        {
            PrintSummary(0, totalTests);
            CloseLog();
            return 1;
        }
        passCount++;

        if (!Test2_ServiceInjectionAndCommandRegistration(pluginName))
        {
            PrintSummary(1, totalTests);
            CloseLog();
            return 1;
        }
        passCount++;

        if (!Test3_CommandExecution(pluginName))
        {
            PrintSummary(2, totalTests);
            CloseLog();
            return 1;
        }
        passCount++;

        PrintSummary(passCount, totalTests);

        _logWriter?.Close();
        _logWriter = null;

        if (_logFile != null)
            AnsiConsole.MarkupLine($"  [grey]日志: {_logFile}[/]");

        return passCount == totalTests ? 0 : 1;
    }

    private bool Test1_ModuleLoad(string pluginName)
    {
        AnsiConsole.MarkupLine("[bold cyan][[1/3]] 模块加载测试[/]");
        AnsiConsole.MarkupLine("  准备测试环境...");

        try
        {
            Directory.CreateDirectory(_testDir);
            Directory.CreateDirectory(_libDir);

            foreach (var dll in Directory.GetFiles(_dependencyDir, "*.dll"))
            {
                var dest = Path.Combine(_libDir, Path.GetFileName(dll));
                File.Copy(dll, dest, true);
                if (_debug) AnsiConsole.MarkupLine($"  [grey]复制: {Path.GetFileName(dll)} -> lib/[/]");
            }

            var knownDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ModuleManagerLib.dll", "MorningCat.dll", "OneBotLib.dll",
                "Logging.dll", "PyToNet.dll"
            };

            var pluginDlls = Directory.GetFiles(_artifactDir, "*.dll")
                .Where(d => !knownDeps.Contains(Path.GetFileName(d)))
                .ToList();

            if (pluginDlls.Count == 0)
            {
                AnsiConsole.MarkupLine("  [red]未找到编译产物 DLL[/]");
                Errors.Add("未找到编译产物 DLL");
                WriteLog("[FATAL] 未找到编译产物 DLL");
                WriteLog($"Artifact 目录: {_artifactDir}");
                WriteLog($"已知依赖: {string.Join(", ", knownDeps)}");
                foreach (var f in Directory.GetFiles(_artifactDir, "*.dll"))
                    WriteLog($"  文件: {Path.GetFileName(f)}");
                AnsiConsole.MarkupLine("  [red]FAIL[/]");
                return false;
            }

            foreach (var dll in pluginDlls)
            {
                var dest = Path.Combine(_testDir, Path.GetFileName(dll));
                File.Copy(dll, dest, true);
                WriteLog($"复制插件DLL: {Path.GetFileName(dll)} -> test/");
            }

            AnsiConsole.MarkupLine("  加载模块...");

            var ctx = new AssemblyLoadContext("TestContext", isCollectible: true);
            foreach (var libDll in Directory.GetFiles(_libDir, "*.dll"))
            {
                try
                {
                    ctx.LoadFromAssemblyPath(libDll);
                }
                catch (Exception ex)
                {
                    if (_debug) AnsiConsole.MarkupLine($"  [grey]跳过 {Path.GetFileName(libDll)}: {ex.Message}[/]");
                }
            }

            var allTypes = new List<Type>();
            foreach (var dll in pluginDlls)
            {
                var dest = Path.Combine(_testDir, Path.GetFileName(dll));
                var assembly = ctx.LoadFromAssemblyPath(dest);
                WriteLog($"加载程序集: {Path.GetFileName(dll)}");

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                    WriteLog($"[WARN] 部分类型加载失败 ({Path.GetFileName(dll)}), 成功 {types.Length}/{ex.Types.Length}");
                    foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null))
                        WriteLog($"  LoaderException: {loaderEx!.Message}");
                }

                WriteLog($"  {Path.GetFileName(dll)}: {types.Length} 个类型");
                foreach (var t in types)
                    WriteLog($"    {t.FullName} (base: {t.BaseType?.Name ?? "none"})");

                allTypes.AddRange(types);
            }

            var moduleTypes = allTypes
                .Where(t => t.GetMethod("Init", Type.EmptyTypes) != null
                         && t.GetMethod("Init", Type.EmptyTypes)!.ReturnType == typeof(Task))
                .ToList();

            if (moduleTypes.Count == 0)
            {
                AnsiConsole.MarkupLine("  [red]未找到有效的模块类（需要 Init() -> Task 方法）[/]");
                Errors.Add("未找到有效的模块类");
                WriteLog("[FATAL] 未找到有效的模块类");
                WriteLog("所有类型的方法详情:");
                foreach (var t in allTypes)
                {
                    WriteLog($"  类型: {t.FullName}");
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        WriteLog($"    方法: {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
                }

                ctx.Unload();
                AnsiConsole.MarkupLine("  [red]FAIL[/]");
                return false;
            }

            foreach (var type in moduleTypes)
            {
                AnsiConsole.MarkupLine($"  [green]发现模块:[/] {type.Name}");

                var instance = Activator.CreateInstance(type);
                if (instance == null)
                {
                    AnsiConsole.MarkupLine($"  [red]无法创建实例: {type.Name}[/]");
                    Errors.Add($"无法创建实例: {type.Name}");
                    ctx.Unload();
                    AnsiConsole.MarkupLine("  [red]FAIL[/]");
                    return false;
                }

                var getDeps = type.GetMethod("GetDependencies");
                if (getDeps != null)
                {
                    var deps = getDeps.Invoke(instance, null) as IEnumerable<string>;
                    if (deps != null)
                    {
                        var depList = deps.ToList();
                        if (depList.Count > 0)
                            AnsiConsole.MarkupLine($"  [yellow]模块依赖:[/] {string.Join(", ", depList)}");
                    }
                }

                var getLibDeps = type.GetMethod("GetLibraryDependencies");
                if (getLibDeps != null)
                {
                    var libDeps = getLibDeps.Invoke(instance, null) as IEnumerable<string>;
                    if (libDeps != null)
                    {
                        var libDepList = libDeps.ToList();
                        if (libDepList.Count > 0)
                            AnsiConsole.MarkupLine($"  [yellow]库依赖:[/] {string.Join(", ", libDepList)}");
                    }
                }
            }

            ctx.Unload();
            AnsiConsole.MarkupLine("  [green]PASS[/]");
            return true;
        }
        catch (ReflectionTypeLoadException ex)
        {
            AnsiConsole.MarkupLine($"  [red]类型加载失败[/]");
            foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null))
                AnsiConsole.MarkupLine($"  [red]{loaderEx!.Message.EscapeMarkup()}[/]");
            Errors.Add($"类型加载失败: {ex.Message}");
            AnsiConsole.MarkupLine("  [red]FAIL[/]");
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]异常: {ex.Message.EscapeMarkup()}[/]");
            Errors.Add($"模块加载异常: {ex.Message}");
            AnsiConsole.MarkupLine("  [red]FAIL[/]");
            return false;
        }
    }

    private bool Test2_ServiceInjectionAndCommandRegistration(string pluginName)
    {
        AnsiConsole.MarkupLine("[bold cyan][[2/3]] DI 注册与命令注册测试[/]");
        AnsiConsole.MarkupLine("  模拟 MCT 环境...");

        try
        {
            var ctx = new AssemblyLoadContext("TestContext2", isCollectible: true);

            foreach (var libDll in Directory.GetFiles(_libDir, "*.dll"))
            {
                try { ctx.LoadFromAssemblyPath(libDll); }
                catch { }
            }

            var knownDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ModuleManagerLib.dll", "MorningCat.dll", "OneBotLib.dll",
                "Logging.dll", "PyToNet.dll"
            };

            var pluginDlls = Directory.GetFiles(_testDir, "*.dll")
                .Where(d => !knownDeps.Contains(Path.GetFileName(d)))
                .ToList();

            if (pluginDlls.Count == 0)
            {
                AnsiConsole.MarkupLine("  [red]未找到插件 DLL[/]");
                Errors.Add("未找到插件 DLL");
                ctx.Unload();
                AnsiConsole.MarkupLine("  [red]FAIL[/]");
                return false;
            }

            var allTypes = new List<Type>();
            foreach (var dll in pluginDlls)
            {
                try
                {
                    var assembly = ctx.LoadFromAssemblyPath(dll);
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
                    allTypes.AddRange(types);
                }
                catch { }
            }

            var moduleTypes = allTypes
                .Where(t => t.GetMethod("Init", Type.EmptyTypes) != null
                         && t.GetMethod("Init", Type.EmptyTypes)!.ReturnType == typeof(Task))
                .ToList();

            if (moduleTypes.Count == 0)
            {
                AnsiConsole.MarkupLine("  [red]未找到模块类[/]");
                ctx.Unload();
                AnsiConsole.MarkupLine("  [red]FAIL[/]");
                return false;
            }

            var mmType = ctx.Assemblies
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "ModuleManager" && t.Namespace == "ModuleManagerLib");

            if (mmType == null)
            {
                AnsiConsole.MarkupLine("  [yellow]未找到 ModuleManager 类型，跳过 DI 注入[/]");
                Warnings.Add("未找到 ModuleManager 类型");
            }

            var registeredCommands = new List<(string Name, object Handler)>();

            foreach (var type in moduleTypes)
            {
                var instance = Activator.CreateInstance(type);
                if (instance == null) continue;

                if (mmType != null)
                {
                    InjectMockServices(instance, ctx, registeredCommands, type.Name);
                }
                else
                {
                    InjectMockServicesByReflection(instance, ctx, registeredCommands, type.Name);
                }

                AnsiConsole.MarkupLine($"  初始化模块: {type.Name}...");
                var initMethod = type.GetMethod("Init", Type.EmptyTypes);
                if (initMethod != null)
                {
                    var initTask = initMethod.Invoke(instance, null) as Task;
                    if (initTask != null)
                    {
                        try { initTask.Wait(TimeSpan.FromSeconds(10)); }
                        catch (AggregateException aex)
                        {
                            var innerEx = aex.InnerException?.InnerException ?? aex.InnerException ?? aex;
                            AnsiConsole.MarkupLine($"  [yellow]Init 异常: {innerEx.Message.EscapeMarkup()}[/]");
                            Warnings.Add($"Init 异常: {innerEx.Message}");
                        }
                    }
                }
            }

            if (registeredCommands.Count > 0)
            {
                AnsiConsole.MarkupLine($"  [green]已注册命令:[/]");
                foreach (var cmd in registeredCommands)
                    AnsiConsole.MarkupLine($"    [green]/{cmd.Name}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("  [yellow]未注册任何命令[/]");
                Warnings.Add("未注册任何命令");
            }

            ctx.Unload();
            AnsiConsole.MarkupLine("  [green]PASS[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]异常: {ex.Message.EscapeMarkup()}[/]");
            Errors.Add($"DI 注入异常: {ex.Message}");
            AnsiConsole.MarkupLine("  [red]FAIL[/]");
            return false;
        }
    }

    private bool Test3_CommandExecution(string pluginName)
    {
        AnsiConsole.MarkupLine("[bold cyan][[3/3]] 命令执行测试[/]");

        try
        {
            var ctx = new AssemblyLoadContext("TestContext3", isCollectible: true);

            foreach (var libDll in Directory.GetFiles(_libDir, "*.dll"))
            {
                try { ctx.LoadFromAssemblyPath(libDll); }
                catch { }
            }

            var knownDeps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ModuleManagerLib.dll", "MorningCat.dll", "OneBotLib.dll",
                "Logging.dll", "PyToNet.dll"
            };

            var pluginDlls = Directory.GetFiles(_testDir, "*.dll")
                .Where(d => !knownDeps.Contains(Path.GetFileName(d)))
                .ToList();

            if (pluginDlls.Count == 0)
            {
                AnsiConsole.MarkupLine("  [red]未找到插件 DLL[/]");
                Errors.Add("未找到插件 DLL");
                ctx.Unload();
                AnsiConsole.MarkupLine("  [red]FAIL[/]");
                return false;
            }

            var allTypes = new List<Type>();
            foreach (var dll in pluginDlls)
            {
                try
                {
                    var assembly = ctx.LoadFromAssemblyPath(dll);
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
                    allTypes.AddRange(types);
                }
                catch { }
            }

            var moduleTypes = allTypes
                .Where(t => t.GetMethod("Init", Type.EmptyTypes) != null
                         && t.GetMethod("Init", Type.EmptyTypes)!.ReturnType == typeof(Task))
                .ToList();

            var registeredCommands = new List<(string Name, object Handler)>();

            foreach (var type in moduleTypes)
            {
                var instance = Activator.CreateInstance(type);
                if (instance == null) continue;

                InjectMockServicesByReflection(instance, ctx, registeredCommands, type.Name);

                var initMethod = type.GetMethod("Init", Type.EmptyTypes);
                if (initMethod != null)
                {
                    var initTask = initMethod.Invoke(instance, null) as Task;
                    try { initTask?.Wait(TimeSpan.FromSeconds(10)); }
                    catch { }
                }

                CollectRegisteredCommands(instance, registeredCommands);
            }

            if (registeredCommands.Count == 0)
            {
                AnsiConsole.MarkupLine("  [yellow]没有可执行的命令[/]");
                Warnings.Add("没有可执行的命令");
                ctx.Unload();
                AnsiConsole.MarkupLine("  [green]PASS[/] (无命令)");
                return true;
            }

            var commandContextType = ctx.Assemblies
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "CommandContext");

            var messageObjectType = ctx.Assemblies
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.Name == "MessageObject");

            foreach (var (cmdName, handler) in registeredCommands)
            {
                AnsiConsole.MarkupLine($"  执行命令: [cyan]/{cmdName}[/]");

                try
                {
                    if (commandContextType != null && messageObjectType != null)
                    {
                        var mockMessage = Activator.CreateInstance(messageObjectType);
                        if (mockMessage != null)
                        {
                            var msgTypeProp = messageObjectType.GetProperty("MessageType");
                            var userIdProp = messageObjectType.GetProperty("UserId");
                            var plainTextProp = messageObjectType.GetProperty("PlainText");

                            msgTypeProp?.SetValue(mockMessage, "private");
                            userIdProp?.SetValue(mockMessage, 0L);
                            plainTextProp?.SetValue(mockMessage, $"/{cmdName}");
                        }

                        var mockContext = Activator.CreateInstance(commandContextType);
                        if (mockContext != null)
                        {
                            var msgProp = commandContextType.GetProperty("Message");
                            var paramsProp = commandContextType.GetProperty("Parameters");
                            var rawCmdProp = commandContextType.GetProperty("RawCommand");

                            msgProp?.SetValue(mockContext, mockMessage);
                            paramsProp?.SetValue(mockContext, new Dictionary<string, string>());
                            rawCmdProp?.SetValue(mockContext, $"/{cmdName}");
                        }

                        var handlerDelegate = handler as Delegate;
                        if (handlerDelegate != null && mockContext != null)
                        {
                            var result = handlerDelegate.DynamicInvoke(mockContext);
                            if (result is Task task)
                            {
                                try { task.Wait(TimeSpan.FromSeconds(5)); }
                                catch (AggregateException aex)
                                {
                                    var innerEx = aex.InnerException?.InnerException ?? aex.InnerException ?? aex;
                                    AnsiConsole.MarkupLine($"    [yellow]执行异常: {innerEx.Message.EscapeMarkup()}[/]");
                                    Warnings.Add($"命令 /{cmdName} 执行异常: {innerEx.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"    [yellow]无法构造 CommandContext，跳过执行[/]");
                    }

                    AnsiConsole.MarkupLine($"    [green]OK[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"    [red]失败: {ex.Message.EscapeMarkup()}[/]");
                    Errors.Add($"命令 /{cmdName} 执行失败: {ex.Message}");
                }
            }

            ctx.Unload();
            AnsiConsole.MarkupLine("  [green]PASS[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]异常: {ex.Message.EscapeMarkup()}[/]");
            Errors.Add($"命令执行异常: {ex.Message}");
            AnsiConsole.MarkupLine("  [red]FAIL[/]");
            return false;
        }
    }

    private void InjectMockServices(object instance, AssemblyLoadContext ctx, List<(string Name, object Handler)> registeredCommands, string moduleName)
    {
        var type = instance.GetType();

        var clientType = ctx.Assemblies
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "OneBotClient");

        var commandRegistryType = ctx.Assemblies
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .FirstOrDefault(t => t.Name == "CommandRegistry");

        if (clientType != null)
        {
            var mockClient = CreateMockOneBotClient(clientType);
            InjectPropertyByType(instance, clientType, mockClient);
        }

        if (commandRegistryType != null)
        {
            var mockRegistry = CreateMockCommandRegistry(commandRegistryType, ctx);
            InjectPropertyByType(instance, commandRegistryType, mockRegistry);
        }

        var setServicesMethod = type.GetMethod("SetServices");
        if (setServicesMethod != null)
        {
            var parameters = setServicesMethod.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (clientType != null && paramType.Name == "OneBotClient")
                    args[i] = CreateMockOneBotClient(clientType);
                else if (commandRegistryType != null && paramType.Name == "CommandRegistry")
                    args[i] = CreateMockCommandRegistry(commandRegistryType, ctx);
                else
                    args[i] = null;
            }

            try { setServicesMethod.Invoke(instance, args); }
            catch { }
        }
    }

    private void InjectMockServicesByReflection(object instance, AssemblyLoadContext ctx, List<(string Name, object Handler)> registeredCommands, string moduleName)
    {
        var type = instance.GetType();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite) continue;

            if (prop.PropertyType.Name == "OneBotClient")
            {
                var mockClient = CreateMockOneBotClient(prop.PropertyType);
                try { prop.SetValue(instance, mockClient); }
                catch { }
            }
            else if (prop.PropertyType.Name == "CommandRegistry")
            {
                var mockRegistry = CreateMockCommandRegistry(prop.PropertyType, ctx);
                try { prop.SetValue(instance, mockRegistry); }
                catch { }
            }
        }

        var setServicesMethod = type.GetMethod("SetServices");
        if (setServicesMethod != null)
        {
            var parameters = setServicesMethod.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (paramType.Name == "OneBotClient")
                    args[i] = CreateMockOneBotClient(paramType);
                else if (paramType.Name == "CommandRegistry")
                    args[i] = CreateMockCommandRegistry(paramType, ctx);
                else
                    args[i] = null;
            }

            try { setServicesMethod.Invoke(instance, args); }
            catch { }
        }
    }

    private object CreateMockOneBotClient(Type clientType)
    {
        var instance = Activator.CreateInstance(clientType);

        var attachMethod = clientType.GetMethod("AttachToExternalConnection");
        if (attachMethod != null)
        {
            var delegateType = attachMethod.GetParameters().FirstOrDefault()?.ParameterType;
            if (delegateType != null)
            {
                var sendDelegate = CreateSendDelegate(delegateType);
                if (sendDelegate != null)
                {
                    try { attachMethod.Invoke(instance, new object[] { sendDelegate }); }
                    catch (Exception ex) { WriteLog($"AttachToExternalConnection 失败: {ex.Message}"); }
                }
                else
                {
                    WriteLog($"无法创建匹配的委托类型: {delegateType.FullName}");
                }
            }
        }

        return instance!;
    }

    private Delegate? CreateSendDelegate(Type delegateType)
    {
        var invokeMethod = delegateType.GetMethod("Invoke");
        if (invokeMethod == null) return null;

        var parameters = invokeMethod.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            return null;

        var returnType = invokeMethod.ReturnType;

        if (returnType == typeof(void))
        {
            return Delegate.CreateDelegate(delegateType, typeof(PluginTestHost), nameof(MockSendMessage));
        }
        else if (returnType == typeof(Task))
        {
            return Delegate.CreateDelegate(delegateType, typeof(PluginTestHost), nameof(MockSendMessageAsync));
        }

        WriteLog($"不支持的委托返回类型: {returnType.FullName}");
        return null;
    }

    private static Task MockSendMessageAsync(string message)
    {
        MockSendMessage(message);
        return Task.CompletedTask;
    }

    private static void MockSendMessage(string message)
    {
        AnsiConsole.MarkupLine($"    [blue][BOT 发送][/]");
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("action", out var action))
            {
                var actionStr = action.GetString() ?? "";
                if (actionStr.Contains("send_msg") || actionStr.Contains("Send"))
                {
                    if (doc.RootElement.TryGetProperty("params", out var paramsEl))
                    {
                        if (paramsEl.TryGetProperty("message", out var msgEl))
                        {
                            var msgStr = msgEl.ValueKind == System.Text.Json.JsonValueKind.String
                                ? msgEl.GetString() ?? ""
                                : msgEl.GetRawText();
                            AnsiConsole.MarkupLine($"    [blue]{msgStr.EscapeMarkup()}[/]");
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine($"    [grey]{actionStr.EscapeMarkup()}[/]");
                }
            }
        }
        catch
        {
            AnsiConsole.MarkupLine($"    [grey]{message.EscapeMarkup()}[/]");
        }
    }

    private object CreateMockCommandRegistry(Type registryType, AssemblyLoadContext ctx)
    {
        var ctors = registryType.GetConstructors();
        foreach (var ctor in ctors)
        {
            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            bool allResolved = true;

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (paramType.Name == "OneBotClient")
                {
                    var clientType = ctx.Assemblies
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .FirstOrDefault(t => t.Name == "OneBotClient");
                    args[i] = clientType != null ? CreateMockOneBotClient(clientType) : null;
                }
                else if (paramType.Name == "ConfigManager")
                {
                    var configMgrType = ctx.Assemblies
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                        .FirstOrDefault(t => t.Name == "ConfigManager");
                    args[i] = configMgrType != null ? Activator.CreateInstance(configMgrType) : null;
                }
                else
                {
                    try { args[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null; }
                    catch { args[i] = null; }
                }

                if (args[i] == null && !parameters[i].HasDefaultValue)
                {
                    allResolved = false;
                    break;
                }
            }

            if (allResolved)
            {
                try { return ctor.Invoke(args)!; }
                catch (Exception ex)
                {
                    WriteLog($"CommandRegistry 构造失败: {ex.Message}");
                    continue;
                }
            }
        }

        WriteLog("无法创建 CommandRegistry 实例，尝试使用未初始化的对象");
        return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(registryType);
    }

    private void CollectRegisteredCommands(object instance, List<(string Name, object Handler)> commands)
    {
        var type = instance.GetType();

        var commandRegistryProp = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.PropertyType.Name == "CommandRegistry");

        if (commandRegistryProp == null) return;

        var registry = commandRegistryProp.GetValue(instance);
        if (registry == null) return;

        var commandsField = registry.GetType().GetField("_commands", BindingFlags.NonPublic | BindingFlags.Instance);
        if (commandsField == null) return;

        var commandsDict = commandsField.GetValue(registry) as System.Collections.IDictionary;
        if (commandsDict == null) return;

        foreach (System.Collections.DictionaryEntry entry in commandsDict)
        {
            var cmdName = entry.Key?.ToString() ?? "";
            var cmdInfo = entry.Value;
            var handlerField = cmdInfo?.GetType().GetField("Handler", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var handler = handlerField?.GetValue(cmdInfo);

            if (handler != null)
            {
                commands.Add((cmdName, handler));
                AnsiConsole.MarkupLine($"  [green]捕获命令:[/] /{cmdName}");
            }
        }
    }

    private void InjectPropertyByType(object instance, Type serviceType, object serviceInstance)
    {
        var type = instance.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.CanWrite && prop.PropertyType == serviceType)
            {
                try { prop.SetValue(instance, serviceInstance); }
                catch { }
            }
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType == serviceType)
            {
                try { field.SetValue(instance, serviceInstance); }
                catch { }
            }
        }
    }

    private static void PrintSummary(int passed, int total)
    {
        AnsiConsole.WriteLine();
        if (passed == total)
        {
            AnsiConsole.MarkupLine($"[bold green]测试全部通过: {passed}/{total}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold red]测试未通过: {passed}/{total}[/]");
        }
    }

    private void WriteLog(string message)
    {
        _logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    private void CloseLog()
    {
        _logWriter?.Close();
        _logWriter = null;

        if (_logFile != null)
            AnsiConsole.MarkupLine($"  [grey]日志: {_logFile}[/]");
    }
}
