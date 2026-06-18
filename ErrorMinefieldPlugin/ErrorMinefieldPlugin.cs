using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Logging;
using MorningCat;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace ErrorMinefieldPlugin
{
    /// <summary>
    /// 雷区测试插件 - 按顺序踩各种插件开发雷区，触发 PluginErrorDatabase 匹配
    /// 仅在测试模式下使用，用于验证异常匹配系统
    /// 
    /// 命令:
    ///   /minefield          - 显示所有雷区测试项
    ///   /minefield <n>      - 执行第n个雷区测试
    ///   /minefield all      - 按顺序执行所有雷区测试
    /// </summary>
    public class ErrorMinefieldPlugin
    {
        private CommandRegistry _commandRegistry = null!;
        private MessageDistributionCore _mdc = null!;
        private MorningCatBot _bot = null!;
        private PluginConfigManager _configManager = null!;

        private readonly List<MinefieldEntry> _minefields = new();

        public CommandRegistry CommandRegistry { set => _commandRegistry = value; }
        public MessageDistributionCore MDC { set => _mdc = value; }
        public MorningCatBot MorningCatBot { set => _bot = value; }
        public PluginConfigManager ConfigManager { set => _configManager = value; }

        public async Task Init()
        {
            RegisterMinefields();
            RegisterCommand();
            Log.Name("ErrorMinefield");
            Log.Info("雷区测试插件已加载 - 输入 /minefield 查看测试项");
            await Task.CompletedTask;
        }

        public Task Exit()
        {
            // 故意不注销命令 - 雷区: Exit不注销
            Log.Debug("Exit() 被调用（但故意不注销命令）");
            return Task.CompletedTask;
        }

        private void RegisterMinefields()
        {
            _minefields.AddRange(new[]
            {
                new MinefieldEntry(1, "NullReference - 访问未初始化的DI属性", MineTest_NullRef),
                new MinefieldEntry(2, "NotImplemented - 命令注册但Handler抛NotImplementedException", MineTest_NotImplemented),
                new MinefieldEntry(3, "CollectionModified - 遍历时修改集合", MineTest_CollectionModified),
                new MinefieldEntry(4, "InvalidCast - 枚举隐式转换", MineTest_InvalidCast),
                new MinefieldEntry(5, "JsonTypeMismatch - JSON long/string类型不匹配", MineTest_JsonMismatch),
                new MinefieldEntry(6, "ObjectDisposed - 访问已释放对象", MineTest_ObjectDisposed),
                new MinefieldEntry(7, "FileNotFound - 加载不存在的程序集", MineTest_FileNotFound),
                new MinefieldEntry(8, "DivideByZero - 命令逻辑除零错误", MineTest_DivideByZero),
                new MinefieldEntry(9, "IndexOutOfRange - 数组越界", MineTest_IndexOutOfRange),
                new MinefieldEntry(10, "AsyncNoAwait - 异步方法未await导致未观察异常", MineTest_AsyncNoAwait),
                new MinefieldEntry(11, "CrossAlcAccess - 尝试跨ALC类型转换", MineTest_CrossAlcAccess),
                new MinefieldEntry(12, "CommandScopeInvalid - 使用不存在的命令作用域", MineTest_CommandScopeInvalid),
            });
        }

        private void RegisterCommand()
        {
            _commandRegistry.RegisterCommand(
                "minefield",
                "雷区测试插件",
                "用法: /minefield [n|all]",
                new List<CommandParameter>
                {
                    new CommandParameter
                    {
                        Name = "目标",
                        Description = "测试编号或all",
                        IsRequired = false,
                    }
                },
                HandleMinefieldCommand,
                "ErrorMinefieldPlugin",
                CommandPermission.BotOwner,
                CommandScope.All
            );
        }

        private async Task HandleMinefieldCommand(CommandContext context)
        {
            var target = context.Parameters.TryGetValue("目标", out var t) ? t : null;

            if (string.IsNullOrEmpty(target))
            {
                var lines = new List<string> { "=== 雷区测试项 ===" };
                foreach (var m in _minefields)
                    lines.Add($"  {m.Id}. {m.Description}");
                lines.Add("\n用法: /minefield <n> 或 /minefield all");
                await _mdc.SendAsync(context.Message, builder => builder.Text(string.Join("\n", lines)));
                return;
            }

            if (target == "all")
            {
                foreach (var m in _minefields)
                {
                    try
                    {
                        await _mdc.SendAsync(context.Message, builder => builder.Text($"[雷区 {m.Id}] {m.Description}"));
                        await m.Action();
                        await Task.Delay(500);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[雷区 {m.Id}] 触发异常: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                await _mdc.SendAsync(context.Message, builder => builder.Text("所有雷区测试已执行完毕，请查看日志"));
                return;
            }

            if (int.TryParse(target, out var id))
            {
                var entry = _minefields.FirstOrDefault(m => m.Id == id);
                if (entry == null)
                {
                    await _mdc.SendAsync(context.Message, builder => builder.Text($"未找到雷区 #{id}"));
                    return;
                }

                try
                {
                    await entry.Action();
                    await _mdc.SendAsync(context.Message, builder => builder.Text($"[雷区 {id}] 已触发，请查看日志"));
                }
                catch (Exception ex)
                {
                    Log.Error($"[雷区 {id}] 触发异常: {ex.GetType().Name}: {ex.Message}");
                    await _mdc.SendAsync(context.Message, builder => builder.Text($"[雷区 {id}] 异常: {ex.GetType().Name}: {ex.Message}"));
                }
                return;
            }

            await _mdc.SendAsync(context.Message, builder => builder.Text("无效参数，请输入编号或all"));
        }

        // ==================== 雷区实现 ====================

        /// <summary>雷区1: 访问未初始化的DI属性</summary>
        private Task MineTest_NullRef()
        {
            // 故意访问一个未注入的属性
            string? value = null;
            // 触发 NullReferenceException
            _ = value!.Length;
            return Task.CompletedTask;
        }

        /// <summary>雷区2: 命令Handler抛NotImplementedException</summary>
        private Task MineTest_NotImplemented()
        {
            throw new NotImplementedException("这个命令还没实现呢");
        }

        /// <summary>雷区3: 遍历时修改集合</summary>
        private Task MineTest_CollectionModified()
        {
            var list = new List<int> { 1, 2, 3, 4, 5 };
            foreach (var item in list)
            {
                if (item == 3)
                    list.Remove(item); // 集合已修改异常
            }
            return Task.CompletedTask;
        }

        /// <summary>雷区4: 枚举隐式转换</summary>
        private Task MineTest_InvalidCast()
        {
            // 模拟 PlatformId string -> enum 隐式转换
            string platformStr = "OneBot";
            // 故意尝试隐式转换（编译不通过，用另一种方式触发）
            object boxed = platformStr;
            var _ = (PlatformId)boxed; // InvalidCastException
            return Task.CompletedTask;
        }

        /// <summary>雷区5: JSON long/string类型不匹配</summary>
        private Task MineTest_JsonMismatch()
        {
            var json = """{"group_id": 123456}""";
            // 尝试反序列化为 string 类型的 group_id
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var groupId = doc.RootElement.GetProperty("group_id").GetString(); // 抛异常: 无法将 Number 转为 String
            Log.Debug($"groupId: {groupId}");
            return Task.CompletedTask;
        }

        /// <summary>雷区6: 访问已释放对象</summary>
        private Task MineTest_ObjectDisposed()
        {
            var cts = new CancellationTokenSource();
            cts.Dispose();
            // 访问已释放的对象
            cts.Cancel(); // ObjectDisposedException
            return Task.CompletedTask;
        }

        /// <summary>雷区7: 加载不存在的程序集</summary>
        private Task MineTest_FileNotFound()
        {
            Assembly.Load("ThisAssemblyDoesNotExist12345");
            return Task.CompletedTask;
        }

        /// <summary>雷区8: 除零错误</summary>
        private Task MineTest_DivideByZero()
        {
            int zero = 0;
            _ = 42 / zero; // DivideByZeroException
            return Task.CompletedTask;
        }

        /// <summary>雷区9: 数组越界</summary>
        private Task MineTest_IndexOutOfRange()
        {
            var arr = new int[3];
            _ = arr[10]; // IndexOutOfRangeException
            return Task.CompletedTask;
        }

        /// <summary>雷区10: 异步方法未await</summary>
        private Task MineTest_AsyncNoAwait()
        {
            // 启动一个不await的Task，里面抛异常
            _ = Task.Run(() => throw new InvalidOperationException("异步未观察异常"));
            return Task.CompletedTask;
        }

        /// <summary>雷区11: 尝试跨ALC类型转换</summary>
        private Task MineTest_CrossAlcAccess()
        {
            // 在插件ALC中加载的类型无法转换为主程序集中的类型
            var type = Type.GetType("MorningCat.MorningCatBot, MorningCat");
            if (type != null)
            {
                // 尝试创建实例并转换 - 这在跨ALC时会失败
                var instance = Activator.CreateInstance(type);
                // 这不会真正触发跨ALC异常（因为是同一个进程），但模拟了场景
                Log.Debug($"跨ALC类型: {instance?.GetType().FullName}");
            }
            // 改为直接触发 InvalidCastException 模拟
            throw new InvalidCastException($"无法将类型 PetPetPlugin 转换为 MorningCat.MorningCatBot（跨 AssemblyLoadContext）");
        }

        /// <summary>雷区12: 使用不存在的命令作用域</summary>
        private Task MineTest_CommandScopeInvalid()
        {
            // 模拟使用 CommandScope.Any（不存在）
            throw new ArgumentException("CommandScope 不包含 'Any' 的定义");
        }

        private record MinefieldEntry(int Id, string Description, Func<Task> Action);
    }
}
