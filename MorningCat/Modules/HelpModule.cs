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
    public class HelpModule
    {
        private OneBotClient _client;
        private PluginConfigManager _configManager;
        private CommandRegistry _commandRegistry;
        private const int CommandsPerPage = 8;

        public void SetServices(OneBotClient client, PluginConfigManager configManager, CommandRegistry commandRegistry)
        {
            _client = client;
            _configManager = configManager;
            _commandRegistry = commandRegistry;
        }

        public async Task Init()
        {
            Log.Info("帮助模块初始化中...");

            if (_client == null || _configManager == null || _commandRegistry == null)
            {
                Log.Error("依赖注入不完整，无法初始化帮助模块");
                return;
            }

            RegisterHelpCommand();

            Log.Info("帮助模块初始化完成");
            await Task.CompletedTask;
        }

        private void RegisterHelpCommand()
        {
            var parameters = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "command",
                    Description = "要查询的命令名称或页码",
                    IsRequired = false,
                    DefaultValue = null
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "help",
                "显示命令帮助信息",
                "@机器人 help 查看可用命令\n@机器人 help <页码> 查看指定页\n@机器人 help <命令名> 查看详细帮助",
                parameters,
                HandleHelpCommand,
                "HelpModule",
                CommandPermission.Everyone,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info("help命令注册成功");
            }
            else
            {
                Log.Error("help命令注册失败");
            }
        }

        private async Task HandleHelpCommand(CommandContext context)
        {
            try
            {
                var parameters = context.Parameters;
                var message = context.Message;

                if (parameters.TryGetValue("command", out var arg) && !string.IsNullOrEmpty(arg))
                {
                    if (int.TryParse(arg, out int page))
                    {
                        await ShowCommandListAsync(message, page);
                    }
                    else
                    {
                        var helpText = _commandRegistry.GetCommandHelp(arg);
                        await SendMessageAsync(message, helpText);
                    }
                }
                else
                {
                    await ShowCommandListAsync(message, 1);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"处理help命令失败: {ex.Message}");
            }
        }

        private async Task ShowCommandListAsync(MessageObject message, int page)
        {
            var allCommands = _commandRegistry.GetAllCommands();
            var userId = message.UserId ?? 0;
            var isBotOwner = _commandRegistry.IsBotOwner(userId);
            var isGroup = message.MessageType == "group";

            List<CommandInfo> filteredCommands;

            if (isGroup)
            {
                filteredCommands = allCommands
                    .Where(cmd => cmd.Permission == CommandPermission.Everyone)
                    .Where(cmd => cmd.Scope == CommandScope.All)
                    .ToList();
            }
            else
            {
                if (isBotOwner)
                {
                    filteredCommands = allCommands.ToList();
                }
                else
                {
                    filteredCommands = allCommands
                        .Where(cmd => cmd.Permission == CommandPermission.Everyone)
                        .ToList();
                }
            }

            if (filteredCommands.Count == 0)
            {
                await SendMessageAsync(message, "没有可用的命令");
                return;
            }

            int totalPages = (int)Math.Ceiling((double)filteredCommands.Count / CommandsPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var pageCommands = filteredCommands
                .Skip((page - 1) * CommandsPerPage)
                .Take(CommandsPerPage)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"可用命令列表 (第 {page}/{totalPages} 页):");
            sb.AppendLine();

            foreach (var cmd in pageCommands)
            {
                var permStr = GetPermissionString(cmd.Permission);
                var scopeStr = cmd.Scope == CommandScope.PrivateOnly ? " [私聊]" : "";
                sb.AppendLine($"/{cmd.Name} - {cmd.Description}{permStr}{scopeStr}");
            }

            sb.AppendLine();
            sb.AppendLine("使用 /help <页码> 查看更多");
            sb.AppendLine("使用 /help <命令名> 查看详细帮助");

            await SendMessageAsync(message, sb.ToString());
        }

        private string GetPermissionString(CommandPermission permission)
        {
            return permission switch
            {
                CommandPermission.Everyone => "",
                CommandPermission.GroupAdmin => " [群管理]",
                CommandPermission.Owner => " [群主]",
                CommandPermission.BotOwner => " [持有者]",
                _ => ""
            };
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
            Log.Info("帮助模块正在清理...");
            _commandRegistry?.UnregisterCommand("help");
            Log.Info("帮助模块清理完成");
            await Task.CompletedTask;
        }
    }
}
