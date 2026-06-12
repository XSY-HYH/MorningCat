using MorningCat.PPC;

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

var debug = false;
var workDir = Directory.GetCurrentDirectory();
var commands = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--debug" || args[i] == "-d")
    {
        debug = true;
    }
    else if (args[i] == "--workdir" || args[i] == "-w")
    {
        if (i + 1 < args.Length)
        {
            workDir = args[++i];
        }
    }
    else if (!args[i].StartsWith("-"))
    {
        commands.Add(args[i]);
    }
}

if (commands.Count == 0)
{
    PrintHelp();
    return 0;
}

var engine = new BuildEngine(workDir, debug);
var validCommands = new HashSet<string> { "build", "clean", "test", "runcore", "init" };

foreach (var cmd in commands)
{
    if (!validCommands.Contains(cmd))
    {
        Console.WriteLine($"未知命令: {cmd}");
        Console.WriteLine();
        PrintHelp();
        return 1;
    }
}

var exitCode = 0;

foreach (var cmd in commands)
{
    if (exitCode != 0) break;

    exitCode = cmd switch
    {
        "build" => engine.Build(),
        "clean" => engine.Clean(),
        "test" => engine.Test(),
        "runcore" => engine.RunCore(),
        "init" => engine.Init(),
        _ => 1
    };
}

return exitCode;

static int PrintHelp()
{
    Console.WriteLine("MorningCat.PPC - Python 插件编译器");
    Console.WriteLine();
    Console.WriteLine("用法: MorningCat.PPC <命令> [命令...] [选项]");
    Console.WriteLine();
    Console.WriteLine("命令:");
    Console.WriteLine("  build     编译 Python 插件为 DLL");
    Console.WriteLine("  test      测试插件（模块加载/DI注入/命令执行）");
    Console.WriteLine("  runcore   运行构建产物的核心");
    Console.WriteLine("  clean     清理临时构建文件");
    Console.WriteLine("  init      创建默认项目结构");
    Console.WriteLine();
    Console.WriteLine("选项:");
    Console.WriteLine("  --debug, -d       显示详细调试信息");
    Console.WriteLine("  --workdir, -w     指定工作目录 (默认: 当前目录)");
    return 0;
}
