using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.I18n;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Modules
{
    public class SetModule
    {
        private MessageDistributionCore _mdc;
        private CommandRegistry _commandRegistry;
        private ConfigManager _configManager;
        private PluginConfigManager _pluginConfigManager;
        private ModuleManager _moduleManager;

        public void SetServices(MessageDistributionCore mdc, CommandRegistry commandRegistry, ConfigManager configManager, PluginConfigManager pluginConfigManager, ModuleManager moduleManager)
        {
            _mdc = mdc;
            _commandRegistry = commandRegistry;
            _configManager = configManager;
            _pluginConfigManager = pluginConfigManager;
            _moduleManager = moduleManager;
        }

        public void UpdateMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public async Task Init()
        {
            Log.Info(I18nManager.S("set_module.initializing"));

            if (_mdc == null || _commandRegistry == null || _configManager == null || _pluginConfigManager == null || _moduleManager == null)
            {
                Log.Error(I18nManager.S("set_module.di_incomplete"));
                return;
            }

            RegisterSetCommand();

            Log.Info(I18nManager.S("set_module.initialized"));
            await Task.CompletedTask;
        }

        private void RegisterSetCommand()
        {
            var subParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "type",
                    Description = I18nManager.S("set_module.param_type"),
                    IsRequired = true,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "target",
                    Description = I18nManager.S("set_module.param_target"),
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "key",
                    Description = I18nManager.S("set_module.param_key"),
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "to",
                    Description = I18nManager.S("set_module.param_to"),
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "value",
                    Description = I18nManager.S("set_module.param_value"),
                    IsRequired = false,
                    Type = ParameterType.String
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "set",
                I18nManager.S("set_module.desc"),
                I18nManager.S("set_module.help_text"),
                subParams,
                HandleSetCommand,
                "SetModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info(I18nManager.S("set_module.registered"));
            }
            else
            {
                Log.Error(I18nManager.S("set_module.register_failed"));
            }
        }

        private async Task HandleSetCommand(CommandContext context)
        {
            var parameters = context.Parameters;
            var message = context.Message;

            if (!parameters.TryGetValue("type", out var type) || string.IsNullOrEmpty(type))
            {
                await SendMessageAsync(message, I18nManager.S("set_module.specify_type"));
                return;
            }

            type = type.ToLower();

            switch (type)
            {
                case "system":
                    await HandleSystemSetAsync(message, parameters);
                    break;
                case "plugin":
                    await HandlePluginSetAsync(message, parameters);
                    break;
                default:
                    await SendMessageAsync(message, I18nManager.S("set_module.unknown_type", type));
                    break;
            }
        }

        private async Task HandleSystemSetAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            var keyPath = parameters.TryGetValue("target", out var target) ? target : null;
            if (string.IsNullOrEmpty(keyPath))
            {
                keyPath = parameters.TryGetValue("key", out var key) ? key : null;
            }

            if (string.IsNullOrEmpty(keyPath))
            {
                await SendMessageAsync(message, I18nManager.S("set_module.specify_key"));
                return;
            }

            if (keyPath.ToLower() == "list")
            {
                if (message.MessageType == UnifiedMessageType.Group)
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.group_forbidden"));
                    return;
                }
                await ListSystemConfigAsync(message);
                return;
            }

            var hasTo = parameters.TryGetValue("to", out var toKeyword) && toKeyword?.ToLower() == "to";
            var hasValue = parameters.TryGetValue("value", out var value);

            if (!hasTo && parameters.TryGetValue("key", out var keyValue) && keyValue?.ToLower() == "to")
            {
                hasTo = true;
                hasValue = true;
                value = toKeyword;
            }

            if (hasTo && hasValue && !string.IsNullOrEmpty(value))
            {
                await SetSystemValueAsync(message, keyPath, value);
            }
            else
            {
                if (message.MessageType == UnifiedMessageType.Group)
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.group_forbidden"));
                    return;
                }
                await GetSystemValueAsync(message, keyPath);
            }
        }

        private async Task ListSystemConfigAsync(PlatformMessage message)
        {
            try
            {
                var config = _configManager.GetConfig();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(I18nManager.S("set_module.system_config_list"));
                sb.AppendLine();
                sb.AppendLine($"onebot_server_url: {config.OneBotServerUrl}");
                sb.AppendLine($"onebot_token: {(string.IsNullOrEmpty(config.OneBotToken) ? I18nManager.S("set_module.config_not_set") : "******")}");
                sb.AppendLine($"modules_directory: {config.ModulesDirectory}");
                sb.AppendLine($"auto_load_modules: {config.AutoLoadModules}");
                sb.AppendLine($"owner_qq: {config.OwnerQQ}");
                sb.AppendLine($"blocked_users: {(config.BlockedUsers != null && config.BlockedUsers.Count > 0 ? string.Join(", ", config.BlockedUsers) : I18nManager.S("set_module.config_none"))}");
                sb.AppendLine($"blocked_groups: {(config.BlockedGroups != null && config.BlockedGroups.Count > 0 ? string.Join(", ", config.BlockedGroups) : I18nManager.S("set_module.config_none"))}");
                sb.AppendLine($"plugin_store_url: {(string.IsNullOrEmpty(config.PluginStoreUrl) ? I18nManager.S("set_module.config_default") : config.PluginStoreUrl)}");
                sb.AppendLine($"webui.enabled: {config.WebUI.Enabled}");
                sb.AppendLine($"webui.listen_address: {config.WebUI.ListenAddress}");
                sb.AppendLine($"webui.port: {config.WebUI.Port}");
                sb.AppendLine($"webui.username: {config.WebUI.Username}");

                await SendMessageAsync(message, sb.ToString());
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.get_system_config_failed", ex.Message));
            }
        }

        private async Task GetSystemValueAsync(PlatformMessage message, string keyPath)
        {
            try
            {
                var config = _configManager.GetConfig();
                var value = GetNestedValue(config, keyPath);

                if (value != null)
                {
                    string displayValue;
                    if (value is System.Collections.IList list && list.Count > 0)
                    {
                        displayValue = string.Join(", ", list.Cast<object>());
                    }
                    else if (value is System.Collections.IList emptyList)
                    {
                        displayValue = I18nManager.S("set_module.config_none");
                    }
                    else
                    {
                        displayValue = value.ToString();
                    }
                    await SendMessageAsync(message, I18nManager.S("set_module.system_config_value", keyPath, displayValue));
                }
                else
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.key_not_exist", keyPath));
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.get_system_config_failed", ex.Message));
            }
        }

        private async Task SetSystemValueAsync(PlatformMessage message, string keyPath, string value)
        {
            try
            {
                var config = _configManager.GetConfig();
                SetNestedValue(config, keyPath, value);
                _configManager.SaveConfig();

                await SendMessageAsync(message, I18nManager.S("set_module.system_config_set", keyPath, value));
                Log.Info(I18nManager.S("config.updated", keyPath, value));
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.set_system_failed", ex.Message));
            }
        }

        private async Task HandlePluginSetAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("target", out var pluginName) || string.IsNullOrEmpty(pluginName))
            {
                await SendMessageAsync(message, I18nManager.S("set_module.specify_plugin"));
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var moduleExists = allModules.Any(m => m.ModuleName == pluginName);

            if (!moduleExists)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.plugin_not_exist", pluginName));
                return;
            }

            if (!parameters.TryGetValue("key", out var keyPath) || string.IsNullOrEmpty(keyPath))
            {
                await SendMessageAsync(message, I18nManager.S("set_module.specify_plugin_key"));
                return;
            }

            if (keyPath.ToLower() == "list")
            {
                if (message.MessageType == UnifiedMessageType.Group)
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.plugin_group_forbidden"));
                    return;
                }
                await ListPluginConfigAsync(message, pluginName);
                return;
            }

            var hasTo = parameters.TryGetValue("to", out var toKeyword) && toKeyword?.ToLower() == "to";
            var hasValue = parameters.TryGetValue("value", out var value);

            if (!hasTo && parameters.TryGetValue("key", out var keyValue) && keyValue?.ToLower() == "to")
            {
                hasTo = true;
                hasValue = true;
                value = toKeyword;
            }

            if (hasTo && hasValue && !string.IsNullOrEmpty(value))
            {
                await SetPluginValueAsync(message, pluginName, keyPath, value);
            }
            else
            {
                if (message.MessageType == UnifiedMessageType.Group)
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.plugin_group_forbidden"));
                    return;
                }
                await GetPluginValueAsync(message, pluginName, keyPath);
            }
        }

        private async Task ListPluginConfigAsync(PlatformMessage message, string pluginName)
        {
            try
            {
                var config = await _pluginConfigManager.GetConfigAsync<Dictionary<string, object>>(pluginName, "config", new Dictionary<string, object>());

                if (config == null || config.Count == 0)
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.plugin_no_config", pluginName));
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(I18nManager.S("set_module.plugin_config_list", pluginName));
                sb.AppendLine();

                foreach (var kvp in config)
                {
                    var valueStr = kvp.Value?.ToString() ?? I18nManager.S("set_module.plugin_config_empty");
                    sb.AppendLine($"{kvp.Key}: {valueStr}");
                }

                await SendMessageAsync(message, sb.ToString());
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.get_plugin_config_failed", ex.Message));
            }
        }

        private async Task GetPluginValueAsync(PlatformMessage message, string pluginName, string keyPath)
        {
            try
            {
                var value = await _pluginConfigManager.GetValueAsync<string>(pluginName, "config", keyPath);

                if (value != null)
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.plugin_config_value", pluginName, keyPath, value));
                }
                else
                {
                    await SendMessageAsync(message, I18nManager.S("set_module.plugin_key_not_exist", pluginName, keyPath));
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.get_plugin_config_failed", ex.Message));
            }
        }

        private async Task SetPluginValueAsync(PlatformMessage message, string pluginName, string keyPath, string value)
        {
            try
            {
                await _pluginConfigManager.SetValueAsync(pluginName, "config", keyPath, value);

                await SendMessageAsync(message, I18nManager.S("set_module.plugin_config_set", pluginName, keyPath, value));
                Log.Info(I18nManager.S("config.plugin_updated", pluginName, keyPath, value));
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("set_module.set_plugin_failed", ex.Message));
            }
        }

        private object GetNestedValue(object obj, string keyPath)
        {
            var keys = keyPath.Split('.');
            object current = obj;

            foreach (var key in keys)
            {
                if (current == null) return null;

                var type = current.GetType();
                var property = type.GetProperty(SnakeToPascalCase(key)) ?? type.GetProperty(key);

                if (property != null)
                {
                    current = property.GetValue(current);
                }
                else
                {
                    return null;
                }
            }

            return current;
        }

        private void SetNestedValue(object obj, string keyPath, string value)
        {
            var keys = keyPath.Split('.');
            object current = obj;

            for (int i = 0; i < keys.Length - 1; i++)
            {
                var type = current.GetType();
                var property = type.GetProperty(SnakeToPascalCase(keys[i])) ?? type.GetProperty(keys[i]);

                if (property == null)
                {
                    throw new Exception(I18nManager.S("set_module.property_not_exist", keys[i]));
                }

                current = property.GetValue(current);
                if (current == null)
                {
                    throw new Exception(I18nManager.S("set_module.property_null", keys[i]));
                }
            }

            var finalKey = keys[keys.Length - 1];
            var finalType = current.GetType();
            var finalProperty = finalType.GetProperty(SnakeToPascalCase(finalKey)) ?? finalType.GetProperty(finalKey);

            if (finalProperty == null)
            {
                throw new Exception(I18nManager.S("set_module.property_not_exist", finalKey));
            }

            if (!finalProperty.CanWrite)
            {
                throw new Exception(I18nManager.S("set_module.property_readonly", finalKey));
            }

            var propertyType = finalProperty.PropertyType;

            if (propertyType == typeof(List<long>))
            {
                var list = (List<long>)finalProperty.GetValue(current);
                if (list == null)
                {
                    list = new List<long>();
                    finalProperty.SetValue(current, list);
                }

                if (long.TryParse(value, out var longValue))
                {
                    if (!list.Contains(longValue))
                    {
                        list.Add(longValue);
                    }
                }
                else
                {
                    throw new Exception(I18nManager.S("set_module.convert_failed", value, "long"));
                }
            }
            else if (propertyType == typeof(List<string>))
            {
                var list = (List<string>)finalProperty.GetValue(current);
                if (list == null)
                {
                    list = new List<string>();
                    finalProperty.SetValue(current, list);
                }

                if (!list.Contains(value))
                {
                    list.Add(value);
                }
            }
            else
            {
                var (success, convertedValue, error) = ConvertValue(value, propertyType);
                if (!success)
                {
                    throw new Exception(error);
                }
                finalProperty.SetValue(current, convertedValue);
            }
        }

        private string SnakeToPascalCase(string snakeCase)
        {
            if (string.IsNullOrEmpty(snakeCase)) return snakeCase;
            if (!snakeCase.Contains('_')) return snakeCase;

            var parts = snakeCase.Split('_');
            var result = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length > 0)
                {
                    result.Append(char.ToUpperInvariant(part[0]));
                    if (part.Length > 1)
                        result.Append(part.Substring(1));
                }
            }
            return result.ToString();
        }

        private (bool Success, object Value, string Error) ConvertValue(string value, Type targetType)
        {
            try
            {
                if (targetType == typeof(string))
                {
                    return (true, value, null);
                }
                else if (targetType == typeof(int))
                {
                    return (true, int.Parse(value), null);
                }
                else if (targetType == typeof(long))
                {
                    return (true, long.Parse(value), null);
                }
                else if (targetType == typeof(bool))
                {
                    return (true, bool.Parse(value), null);
                }
                else if (targetType == typeof(double))
                {
                    return (true, double.Parse(value), null);
                }
                else if (targetType == typeof(float))
                {
                    return (true, float.Parse(value), null);
                }
                else
                {
                    return (true, Convert.ChangeType(value, targetType), null);
                }
            }
            catch (FormatException)
            {
                var typeName = targetType.Name;
                return (false, null, I18nManager.S("set_module.convert_failed", value, typeName));
            }
            catch (OverflowException)
            {
                var typeName = targetType.Name;
                return (false, null, I18nManager.S("set_module.convert_failed", value, typeName));
            }
            catch (Exception ex)
            {
                return (false, null, I18nManager.S("set_module.convert_failed", value, ex.Message));
            }
        }

        private Task SendMessageAsync(PlatformMessage message, string text)
        {
            return MessageHelper.SendMessageAsync(_mdc, message, text);
        }
    }
}
