using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.I18n;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Modules
{
    public class HelpModule
    {
        private MessageDistributionCore _mdc;
        private PluginConfigManager _configManager;
        private CommandRegistry _commandRegistry;
        private const int CommandsPerPage = 8;

        public void SetServices(MessageDistributionCore mdc, PluginConfigManager configManager, CommandRegistry commandRegistry)
        {
            _mdc = mdc;
            _configManager = configManager;
            _commandRegistry = commandRegistry;
        }

        public void UpdateMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public async Task Init()
        {
            Log.Debug(I18nManager.S("help_module.initializing"));

            if (_mdc == null || _configManager == null || _commandRegistry == null)
            {
                Log.Debug(I18nManager.S("help_module.di_incomplete"));
                return;
            }

            RegisterHelpCommand();

            Log.Info(I18nManager.S("help_module.initialized"));
            await Task.CompletedTask;
        }

        private void RegisterHelpCommand()
        {
            var parameters = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "command",
                    Description = I18nManager.S("help_module.param_command"),
                    IsRequired = false,
                    DefaultValue = null
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "help",
                I18nManager.S("help_module.desc"),
                I18nManager.S("help_module.help_text"),
                parameters,
                HandleHelpCommand,
                "HelpModule",
                CommandPermission.Everyone,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info(I18nManager.S("help_module.registered"));
            }
            else
            {
                Log.Error(I18nManager.S("help_module.register_failed"));
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
                Log.Error(I18nManager.S("help_module.handle_failed", ex.Message));
            }
        }

        private async Task ShowCommandListAsync(PlatformMessage message, int page)
        {
            var allCommands = _commandRegistry.GetAllCommands();
            var userId = long.TryParse(message.SenderId, out var uid) ? uid : 0;
            var isBotOwner = _commandRegistry.IsBotOwner(userId);
            var isGroup = message.MessageType == UnifiedMessageType.Group;

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
                await SendMessageAsync(message, I18nManager.S("help_module.no_commands"));
                return;
            }

            int totalPages = (int)Math.Ceiling((double)filteredCommands.Count / CommandsPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var pageCommands = filteredCommands
                .Skip((page - 1) * CommandsPerPage)
                .Take(CommandsPerPage)
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(I18nManager.S("help_module.command_list", page, totalPages));
            sb.AppendLine();

            foreach (var cmd in pageCommands)
            {
                var permStr = GetPermissionString(cmd.Permission);
                var scopeStr = cmd.Scope == CommandScope.PrivateOnly ? I18nManager.S("help_module.scope_private") : "";
                sb.AppendLine($"/{cmd.Name} - {cmd.Description}{permStr}{scopeStr}");
            }

            sb.AppendLine();
            sb.AppendLine(I18nManager.S("help_module.more_pages"));
            sb.AppendLine(I18nManager.S("help_module.detail_help"));

            await SendMessageAsync(message, sb.ToString());
        }

        private string GetPermissionString(CommandPermission permission)
        {
            return permission switch
            {
                CommandPermission.Everyone => "",
                CommandPermission.GroupAdmin => I18nManager.S("help_module.perm_group_admin"),
                CommandPermission.Owner => I18nManager.S("help_module.perm_owner"),
                CommandPermission.BotOwner => I18nManager.S("help_module.perm_bot_owner"),
                _ => ""
            };
        }

        private Task SendMessageAsync(PlatformMessage message, string text)
        {
            return MessageHelper.SendMessageAsync(_mdc, message, text);
        }

        public IEnumerable<string> GetDependencies()
        {
            return Array.Empty<string>();
        }

        public async Task Exit()
        {
            Log.Info(I18nManager.S("help_module.cleaning"));
            _commandRegistry?.UnregisterCommand("help");
            Log.Info(I18nManager.S("help_module.cleaned"));
            await Task.CompletedTask;
        }
    }
}
