using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;

namespace MorningCat.Modules
{
    public class SetModule
    {
        private OneBotClient _client;
        private CommandRegistry _commandRegistry;
        private ConfigManager _configManager;
        private PluginConfigManager _pluginConfigManager;
        private ModuleManager _moduleManager;

        public void SetServices(OneBotClient client, CommandRegistry commandRegistry, ConfigManager configManager, PluginConfigManager pluginConfigManager, ModuleManager moduleManager)
        {
            _client = client;
            _commandRegistry = commandRegistry;
            _configManager = configManager;
            _pluginConfigManager = pluginConfigManager;
            _moduleManager = moduleManager;
        }

        public async Task Init()
        {
            Log.Info("设置模块初始化中...");

            if (_client == null || _commandRegistry == null || _configManager == null || _pluginConfigManager == null || _moduleManager == null)
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

        private async Task HandleSystemSetAsync(MessageObject message, Dictionary<string, string> parameters)
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
                if (message.MessageType == "group")
                {
                    await SendMessageAsync(message, "系统禁止在群聊显示系统配置");
                    return;
                }
                await ListSystemConfigAsync(message);
                return;
            }

            var hasTo = parameters.TryGetValue("to", out var toKeyword) && toKeyword?.ToLower() == "to";
            var hasValue = parameters.TryGetValue("value", out var value);

            if (hasTo && hasValue && !string.IsNullOrEmpty(value))
            {
                await SetSystemValueAsync(message, keyPath, value);
            }
            else
            {
                if (message.MessageType == "group")
                {
                    await SendMessageAsync(message, "系统禁止在群聊显示系统配置");
                    return;
                }
                await GetSystemValueAsync(message, keyPath);
            }
        }

        private async Task ListSystemConfigAsync(MessageObject message)
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
                sb.AppendLine($"admin_qqs: {(config.AdminQQs != null && config.AdminQQs.Count > 0 ? string.Join(", ", config.AdminQQs) : "(无)")}");

                await SendMessageAsync(message, sb.ToString());
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"获取系统配置失败:\n{ex.Message}");
            }
        }

        private async Task GetSystemValueAsync(MessageObject message, string keyPath)
        {
            try
            {
                var config = _configManager.GetConfig();
                var value = GetNestedValue(config, keyPath);

                if (value != null)
                {
                    await SendMessageAsync(message, $"系统配置 {keyPath}:\n{value}");
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

        private async Task SetSystemValueAsync(MessageObject message, string keyPath, string value)
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

        private async Task HandlePluginSetAsync(MessageObject message, Dictionary<string, string> parameters)
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
                if (message.MessageType == "group")
                {
                    await SendMessageAsync(message, "系统禁止在群聊显示插件配置");
                    return;
                }
                await ListPluginConfigAsync(message, pluginName);
                return;
            }

            var hasTo = parameters.TryGetValue("to", out var toKeyword) && toKeyword?.ToLower() == "to";
            var hasValue = parameters.TryGetValue("value", out var value);

            if (hasTo && hasValue && !string.IsNullOrEmpty(value))
            {
                await SetPluginValueAsync(message, pluginName, keyPath, value);
            }
            else
            {
                if (message.MessageType == "group")
                {
                    await SendMessageAsync(message, "系统禁止在群聊显示插件配置");
                    return;
                }
                await GetPluginValueAsync(message, pluginName, keyPath);
            }
        }

        private async Task ListPluginConfigAsync(MessageObject message, string pluginName)
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

        private async Task GetPluginValueAsync(MessageObject message, string pluginName, string keyPath)
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

        private async Task SetPluginValueAsync(MessageObject message, string pluginName, string keyPath, string value)
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
                var property = type.GetProperty(key);

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
                var property = type.GetProperty(keys[i]);

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
            var finalProperty = finalType.GetProperty(finalKey);

            if (finalProperty == null)
            {
                throw new Exception($"属性 {finalKey} 不存在");
            }

            if (!finalProperty.CanWrite)
            {
                throw new Exception($"属性 {finalKey} 是只读的");
            }

            var convertedValue = ConvertValue(value, finalProperty.PropertyType);
            finalProperty.SetValue(current, convertedValue);
        }

        private object ConvertValue(string value, Type targetType)
        {
            if (targetType == typeof(string))
            {
                return value;
            }
            else if (targetType == typeof(int))
            {
                return int.Parse(value);
            }
            else if (targetType == typeof(long))
            {
                return long.Parse(value);
            }
            else if (targetType == typeof(bool))
            {
                return bool.Parse(value);
            }
            else if (targetType == typeof(double))
            {
                return double.Parse(value);
            }
            else if (targetType == typeof(float))
            {
                return float.Parse(value);
            }
            else
            {
                return Convert.ChangeType(value, targetType);
            }
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
    }
}
