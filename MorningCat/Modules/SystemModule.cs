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
    public class SystemModule
    {
        private MessageDistributionCore _mdc;
        private CommandRegistry _commandRegistry;
        private Action _exitCallback;
        private Func<Task> _restartCallback;

        public void SetServices(MessageDistributionCore mdc, CommandRegistry commandRegistry, Action exitCallback, Func<Task> restartCallback)
        {
            _mdc = mdc;
            _commandRegistry = commandRegistry;
            _exitCallback = exitCallback;
            _restartCallback = restartCallback;
        }

        public void UpdateMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public async Task Init()
        {
            Log.Info(I18nManager.S("system_module.initializing"));

            if (_mdc == null || _commandRegistry == null || _exitCallback == null || _restartCallback == null)
            {
                Log.Error(I18nManager.S("system_module.di_incomplete"));
                return;
            }

            RegisterStopCommand();
            RegisterRestartCommand();

            Log.Info(I18nManager.S("system_module.initialized"));
            await Task.CompletedTask;
        }

        private void RegisterStopCommand()
        {
            var success = _commandRegistry.RegisterCommand(
                "stop",
                I18nManager.S("system_module.desc_stop"),
                I18nManager.S("system_module.help_stop"),
                new List<CommandParameter>(),
                HandleStopCommand,
                "SystemModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info(I18nManager.S("system_module.stop_registered"));
            }
            else
            {
                Log.Error(I18nManager.S("system_module.stop_register_failed"));
            }
        }

        private void RegisterRestartCommand()
        {
            var success = _commandRegistry.RegisterCommand(
                "restart",
                I18nManager.S("system_module.desc_restart"),
                I18nManager.S("system_module.help_restart"),
                new List<CommandParameter>(),
                HandleRestartCommand,
                "SystemModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info(I18nManager.S("system_module.restart_registered"));
            }
            else
            {
                Log.Error(I18nManager.S("system_module.restart_register_failed"));
            }
        }

        private async Task HandleStopCommand(CommandContext context)
        {
            var message = context.Message;

            await SendMessageAsync(message, I18nManager.S("system_module.stopping"));

            Log.Info(I18nManager.S("system_module.stop_received", message.SenderId));

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                _exitCallback();
            });
        }

        private async Task HandleRestartCommand(CommandContext context)
        {
            var message = context.Message;

            await SendMessageAsync(message, I18nManager.S("system_module.restarting"));

            Log.Info(I18nManager.S("system_module.restart_received", message.SenderId));

            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                await _restartCallback();
            });
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
            Log.Info(I18nManager.S("system_module.cleaning"));
            _commandRegistry?.UnregisterCommand("stop");
            _commandRegistry?.UnregisterCommand("restart");
            Log.Info(I18nManager.S("system_module.cleaned"));
            await Task.CompletedTask;
        }
    }
}
