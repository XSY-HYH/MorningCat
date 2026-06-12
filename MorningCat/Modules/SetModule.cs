using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
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
            Log.Info("设置模块初始化中...");

            if (_mdc == null || _commandRegistry == null || _configManager == null || _pluginConfigManager == null || _moduleManager == null)
            {
                Log.Error("依赖注入不完整，无法初始化设置模块");
                return;
            }

            RegisterSetCommand();

            Log.Info("设置模块初始化完成");
            await Task.CompletedTask;
        }

        private void RegisterSetCommand()
        {
            var subParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "type",
                    Description = "类型: system/plugin",
                    IsRequired = true,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "target",
                    Description = "目标（plugin时为插件类名）",
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "key",
                    Description = "键路径",
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "to",
                    Description = "to关键字",
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "value",
                    Description = "值",
                    IsRequired = false,
                    Type = ParameterType.String
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "set",
                "设置管理",
                "@机器人 set system list 查看所有系统配置（仅私聊）\n@机器人 set system <键路径> 查看系统配置（仅私聊）\n@机器人 set system <键路径> to <值> 设置系统配置\n@机器人 set plugin <插件名> list 查看插件所有配置（仅私聊）\n@机器人 set plugin <插件名> <键路径> 查看插件配置（仅私聊）\n@机器人 set plugin <插件名> <键路径> to <值> 设置插件配置",
                subParams,
                HandleSetCommand,
                "SetModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info("set命令注册成功");
            }
            else
            {
                Log.Error("set命令注册失败");
            }
        }

        private async Task HandleSetCommand(CommandContext context)
        {
            var parameters = context.Parameters;
            var message = context.Message;

            if (!parameters.TryGetValue("type", out var type) || string.IsNullOrEmpty(type))
            {
                await SendMessageAsync(message, "请指定类型\n用法: /set system/plugin ...");
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
                    await SendMessageAsync(message, $"未知类型: {type}\n可用类型: system, plugin");
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
                await SendMessageAsync(message, "请指定键路径\n用法: /set system <键路径> [to <值>]\n用法: /set system list");
                return;
            }

            if (keyPath.ToLower() == "list")
            {
                if (message.MessageType == UnifiedMessageType.Group)
                {
                    await SendMessageAsync(message, "系统禁止在群聊显示系统配置");
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
                    await SendMessageAsync(message, "系统禁止在群聊显示系统配置");
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
                sb.AppendLine("系统配置列表:");
                sb.AppendLine();
                sb.AppendLine($"nap_cat_server_url: {config.NapCatServerUrl}");
                sb.AppendLine($"nap_cat_token: {(string.IsNullOrEmpty(config.NapCatToken) ? "(未设置)" : "******")}");
                sb.AppendLine($"modules_directory: {config.ModulesDirectory}");
                sb.AppendLine($"auto_load_modules: {config.AutoLoadModules}");
                sb.AppendLine($"owner_qq: {config.OwnerQQ}");
                sb.AppendLine($"blocked_users: {(config.BlockedUsers != null && config.BlockedUsers.Count > 0 ? string.Join(", ", config.BlockedUsers) : "(无)")}");
                sb.AppendLine($"blocked_groups: {(config.BlockedGroups != null && config.BlockedGroups.Count > 0 ? string.Join(", ", config.BlockedGroups) : "(无)")}");
                sb.AppendLine($"plugin_store_url: {(string.IsNullOrEmpty(config.PluginStoreUrl) ? "(默认)" : config.PluginStoreUrl)}");
                sb.AppendLine($"webui.enabled: {config.WebUI.Enabled}");
                sb.AppendLine($"webui.listen_address: {config.WebUI.ListenAddress}");
                sb.AppendLine($"webui.port: {config.WebUI.Port}");
                sb.AppendLine($"webui.username: {config.WebUI.Username}");

                await SendMessageAsync(message, sb.ToString());
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"获取系统配置失败:\n{ex.Message}");
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
                        displayValue = "(空列表)";
                    }
                    else
                    {
                        displayValue = value.ToString();
                    }
                    await SendMessageAsync(message, $"系统配置 {keyPath}:\n{displayValue}");
                }
                else
                {
                    await SendMessageAsync(message, $"键路径 {keyPath} 不存在或值为空");
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"获取系统配置失败:\n{ex.Message}");
            }
        }

        private async Task SetSystemValueAsync(PlatformMessage message, string keyPath, string value)
        {
            try
            {
                var config = _configManager.GetConfig();
                SetNestedValue(config, keyPath, value);
                _configManager.SaveConfig();

                await SendMessageAsync(message, $"已设置系统配置 {keyPath} = {value}");
                Log.Info($"系统配置已更新: {keyPath} = {value}");
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"设置系统配置失败:\n{ex.Message}");
            }
        }

        private async Task HandlePluginSetAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("target", out var pluginName) || string.IsNullOrEmpty(pluginName))
            {
                await SendMessageAsync(message, "请指定插件名\n用法: /set plugin <插件名> list\n用法: /set plugin <插件名> <键路径> [to <值>]");
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var moduleExists = allModules.Any(m => m.ModuleName == pluginName);

            if (!moduleExists)
            {
                await SendMessageAsync(message, $"插件 {pluginName} 不存在");
                return;
            }

            if (!parameters.TryGetValue("key", out var keyPath) || string.IsNullOrEmpty(keyPath))
            {
                await SendMessageAsync(message, "请指定键路径或使用 list\n用法: /set plugin <插件名> list\n用法: /set plugin <插件名> <键路径> [to <值>]");
                return;
            }

            if (keyPath.ToLower() == "list")
            {
                if (message.MessageType == UnifiedMessageType.Group)
                {
                    await SendMessageAsync(message, "系统禁止在群聊显示插件配置");
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
                    await SendMessageAsync(message, "系统禁止在群聊显示插件配置");
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
                    await SendMessageAsync(message, $"插件 {pluginName} 没有任何配置");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"插件 {pluginName} 配置列表:");
                sb.AppendLine();

                foreach (var kvp in config)
                {
                    var valueStr = kvp.Value?.ToString() ?? "(空)";
                    sb.AppendLine($"{kvp.Key}: {valueStr}");
                }

                await SendMessageAsync(message, sb.ToString());
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"获取插件配置失败:\n{ex.Message}");
            }
        }

        private async Task GetPluginValueAsync(PlatformMessage message, string pluginName, string keyPath)
        {
            try
            {
                var value = await _pluginConfigManager.GetValueAsync<string>(pluginName, "config", keyPath);

                if (value != null)
                {
                    await SendMessageAsync(message, $"插件 {pluginName} 配置 {keyPath}:\n{value}");
                }
                else
                {
                    await SendMessageAsync(message, $"插件 {pluginName} 的键路径 {keyPath} 不存在或值为空");
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"获取插件配置失败:\n{ex.Message}");
            }
        }

        private async Task SetPluginValueAsync(PlatformMessage message, string pluginName, string keyPath, string value)
        {
            try
            {
                await _pluginConfigManager.SetValueAsync(pluginName, "config", keyPath, value);

                await SendMessageAsync(message, $"已设置插件 {pluginName} 配置 {keyPath} = {value}");
                Log.Info($"插件配置已更新: {pluginName}.{keyPath} = {value}");
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"设置插件配置失败:\n{ex.Message}");
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
                    throw new Exception($"属性 {keys[i]} 不存在");
                }

                current = property.GetValue(current);
                if (current == null)
                {
                    throw new Exception($"属性 {keys[i]} 的值为空");
                }
            }

            var finalKey = keys[keys.Length - 1];
            var finalType = current.GetType();
            var finalProperty = finalType.GetProperty(SnakeToPascalCase(finalKey)) ?? finalType.GetProperty(finalKey);

            if (finalProperty == null)
            {
                throw new Exception($"属性 {finalKey} 不存在");
            }

            if (!finalProperty.CanWrite)
            {
                throw new Exception($"属性 {finalKey} 是只读的");
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
                    throw new Exception($"无法将 \"{value}\" 转换为 long 类型");
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
                return (false, null, $"无法将 \"{value}\" 转换为 {typeName} 类型");
            }
            catch (OverflowException)
            {
                var typeName = targetType.Name;
                return (false, null, $"值 \"{value}\" 超出了 {typeName} 类型的范围");
            }
            catch (Exception ex)
            {
                return (false, null, $"类型转换失败: {ex.Message}");
            }
        }

        private Task SendMessageAsync(PlatformMessage message, string text)
        {
            return MessageHelper.SendMessageAsync(_mdc, message, text);
        }
    }
}
