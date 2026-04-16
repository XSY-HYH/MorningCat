using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using MorningCat.Commands;
using MorningCat.Config;
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;

namespace MorningCat.Modules
{
    public class SystemModule
    {
        private OneBotClient _client;
        private CommandRegistry _commandRegistry;
        private Action _exitCallback;
        private Func<Task> _restartCallback;

        public void SetServices(OneBotClient client, CommandRegistry commandRegistry, Action exitCallback, Func<Task> restartCallback)
        {
            _client = client;
            _commandRegistry = commandRegistry;
            _exitCallback = exitCallback;
            _restartCallback = restartCallback;
        }

        public async Task Init()
        {
            Log.Info("系统模块初始化中...");

            if (_client == null || _commandRegistry == null || _exitCallback == null || _restartCallback == null)
            {
                Log.Error("依赖注入不完整，无法初始化系统模块");
                return;
            }

            RegisterStopCommand();
            RegisterRestartCommand();

            Log.Info("系统模块初始化完成");
            await Task.CompletedTask;
        }

        private void RegisterStopCommand()
        {
            var success = _commandRegistry.RegisterCommand(
                "stop",
                "停止机器人",
                "@机器人 stop 停止机器人运行",
                new List<CommandParameter>(),
                HandleStopCommand,
                "SystemModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info("stop命令注册成功");
            }
            else
            {
                Log.Error("stop命令注册失败");
            }
        }

        private void RegisterRestartCommand()
        {
            var success = _commandRegistry.RegisterCommand(
                "restart",
                "重启机器人",
                "@机器人 restart 重启机器人",
                new List<CommandParameter>(),
                HandleRestartCommand,
                "SystemModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info("restart命令注册成功");
            }
            else
            {
                Log.Error("restart命令注册失败");
            }
        }

        private async Task HandleStopCommand(CommandContext context)
        {
            var message = context.Message;

            await SendMessageAsync(message, "机器人正在停止...");

            Log.Info($"收到停止命令，来自用户: {message.UserId}");

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                _exitCallback();
            });
        }

        private async Task HandleRestartCommand(CommandContext context)
        {
            var message = context.Message;

            await SendMessageAsync(message, "机器人正在重启...");

            Log.Info($"收到重启命令，来自用户: {message.UserId}");

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await _restartCallback();
            });
        }

        private Task SendMessageAsync(MessageObject message, string text)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (message.MessageType == "private")
                    {
                        await _client.SendPrivateMsgAsync(message.UserId ?? 0, text);
                    }
                    else if (message.MessageType == "group")
                    {
                        await _client.SendGroupMsgAsync(message.GroupId ?? 0, text);
                    }
                }
                catch
                {
                }
            });

            return Task.CompletedTask;
        }

        public IEnumerable<string> GetDependencies()
        {
            return Array.Empty<string>();
        }

        public async Task Exit()
        {
            Log.Info("系统模块正在清理...");
            _commandRegistry?.UnregisterCommand("stop");
            _commandRegistry?.UnregisterCommand("restart");
            Log.Info("系统模块清理完成");
            await Task.CompletedTask;
        }
    }
}
