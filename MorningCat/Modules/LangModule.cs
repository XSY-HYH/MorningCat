using System;
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
    public class LangModule
    {
        private MessageDistributionCore _mdc;
        private CommandRegistry _commandRegistry;
        private ConfigManager _configManager;

        public void SetServices(MessageDistributionCore mdc, CommandRegistry commandRegistry, ConfigManager configManager)
        {
            _mdc = mdc;
            _commandRegistry = commandRegistry;
            _configManager = configManager;
        }

        public void UpdateMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public async Task Init()
        {
            if (_mdc == null || _commandRegistry == null || _configManager == null)
            {
                Log.Error(I18nManager.S("lang_module.di_incomplete"));
                return;
            }

            RegisterLangCommand();
            await Task.CompletedTask;
        }

        private void RegisterLangCommand()
        {
            var success = _commandRegistry.RegisterCommand(
                "lang",
                I18nManager.S("lang_module.command_desc"),
                I18nManager.S("lang_module.command_help"),
                new List<CommandParameter>
                {
                    new() { Name = "language", Description = I18nManager.S("lang_module.param_language"), IsRequired = false }
                },
                HandleLangCommand,
                "LangModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true,
                requireSlash: true
            );

            if (success)
            {
                Log.Info(I18nManager.S("lang_module.registered"));
            }
            else
            {
                Log.Error(I18nManager.S("lang_module.register_failed"));
            }
        }

        private async Task HandleLangCommand(CommandContext context)
        {
            var message = context.Message;

            try
            {
                // 从 RawCommand 中提取参数
                var parts = context.RawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length <= 1)
                {
                    var currentLang = I18nManager.Instance.CurrentLang;
                    var available = string.Join(", ", I18nManager.Instance.AvailableLanguages);
                    await SendMessageAsync(message, I18nManager.S("lang_module.current", currentLang, available));
                    return;
                }

                var targetLang = parts[1].Trim().ToLower();

                if (I18nManager.Instance.SwitchLanguage(targetLang))
                {
                    _configManager.UpdateConfig(c => c.Lang = targetLang);
                    await SendMessageAsync(message, I18nManager.S("lang_module.switched", targetLang));
                    Log.Info(I18nManager.S("lang_module.switched_log", targetLang));
                }
                else
                {
                    var available = string.Join(", ", I18nManager.Instance.AvailableLanguages);
                    await SendMessageAsync(message, I18nManager.S("lang_module.switch_failed", targetLang, available));
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("lang_module.handle_failed", ex.Message));
            }
        }

        private async Task SendMessageAsync(PlatformMessage originalMessage, string responseText)
        {
            try
            {
                await _mdc.SendAsync(originalMessage, builder => builder
                    .Reply(originalMessage.MessageId)
                    .Text(responseText));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("message.send_failed", ex.Message));
            }
        }

        public async Task Exit()
        {
            _commandRegistry?.UnregisterCommand("lang");
            await Task.CompletedTask;
        }
    }
}
