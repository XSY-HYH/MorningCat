using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Logging;
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;
using MorningCat.Config;

namespace MorningCat.Commands
{
    public enum ParameterType
    {
        String,
        Integer,
        Float,
        Boolean,
        At
    }

    public enum CommandPermission
    {
        Everyone,
        GroupAdmin,
        Owner,
        BotOwner
    }

    public enum CommandScope
    {
        All,
        PrivateOnly
    }

    public class CommandParameter
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsRequired { get; set; }
        public ParameterType Type { get; set; } = ParameterType.String;
        public string DefaultValue { get; set; }
        public List<CommandParameter> SubParameters { get; set; } = new List<CommandParameter>();
    }

    public class CommandInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string HelpText { get; set; }
        public List<CommandParameter> Parameters { get; set; } = new List<CommandParameter>();
        public Func<CommandContext, Task> Handler { get; set; }
        public string ModuleName { get; set; }
        public bool IsRegistered { get; set; }
        public CommandPermission Permission { get; set; } = CommandPermission.Everyone;
        public CommandScope Scope { get; set; } = CommandScope.All;
        public bool RequireAt { get; set; } = false;
        public bool RequireSlash { get; set; } = true;
    }

    /// <summary>
    /// 命令上下文
    /// </summary>
    public class CommandContext
    {
        public MessageObject Message { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public string RawCommand { get; set; }
        public OneBotClient Client { get; set; }
    }

    /// <summary>
    /// 参数验证结果
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public int ErrorPosition { get; set; }
        public string ErrorParameter { get; set; }
    }

    /// <summary>
    /// 命令注册处理器
    /// 管理所有命令的注册、查询和执行
    /// </summary>
    public class CommandRegistry
    {
        private readonly Dictionary<string, CommandInfo> _commands = new Dictionary<string, CommandInfo>();
        private readonly object _lock = new object();
        private OneBotClient _client;
        private ConfigManager _configManager;
        
        private static readonly HashSet<string> BuiltinModules = new HashSet<string>
        {
            "HelpModule",
            "PluginModule", 
            "SystemModule",
            "SetModule"
        };

        public CommandRegistry(OneBotClient client, ConfigManager configManager)
        {
            _client = client;
            _configManager = configManager;
        }

        public void SetClient(OneBotClient client)
        {
            _client = client;
        }

        public bool RegisterCommand(
            string commandName, 
            string description, 
            string helpText,
            List<CommandParameter> parameters,
            Func<CommandContext, Task> handler,
            string moduleName,
            CommandPermission permission = CommandPermission.Everyone,
            CommandScope scope = CommandScope.All,
            bool requireAt = false,
            bool requireSlash = true)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                Log.Debug("命令名称不能为空");
                return false;
            }

            commandName = commandName.TrimStart('/').ToLower();

            lock (_lock)
            {
                if (_commands.ContainsKey(commandName))
                {
                    Log.Debug($"命令 '{commandName}' 已存在，无法重复注册");
                    return false;
                }

                var commandInfo = new CommandInfo
                {
                    Name = commandName,
                    Description = description,
                    HelpText = helpText,
                    Parameters = parameters ?? new List<CommandParameter>(),
                    Handler = handler,
                    ModuleName = moduleName,
                    IsRegistered = true,
                    Permission = permission,
                    Scope = scope,
                    RequireAt = requireAt,
                    RequireSlash = requireSlash
                };

                _commands[commandName] = commandInfo;
                Log.Debug($"命令 '{commandName}' 注册成功 (模块: {moduleName}, RequireAt: {requireAt}, RequireSlash: {requireSlash})");
                return true;
            }
        }

        public bool UnregisterCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return false;

            commandName = commandName.TrimStart('/').ToLower();

            lock (_lock)
            {
                if (_commands.Remove(commandName))
                {
                    Log.Debug($"命令 '{commandName}' 已注销");
                    return true;
                }
                return false;
            }
        }

        public void UnregisterModuleCommands(string moduleName)
        {
            if (string.IsNullOrEmpty(moduleName))
                return;

            lock (_lock)
            {
                var commandsToRemove = _commands
                    .Where(kvp => kvp.Value.ModuleName == moduleName)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var commandName in commandsToRemove)
                {
                    _commands.Remove(commandName);
                    Log.Debug($"命令 '{commandName}' 已注销 (模块卸载: {moduleName})");
                }
            }
        }

        public CommandInfo GetCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return null;

            commandName = commandName.TrimStart('/').ToLower();

            lock (_lock)
            {
                return _commands.TryGetValue(commandName, out var command) ? command : null;
            }
        }

        public List<CommandInfo> GetAllCommands()
        {
            lock (_lock)
            {
                return _commands.Values.ToList();
            }
        }

        public bool HasCommand(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
                return false;

            commandName = commandName.TrimStart('/').ToLower();

            lock (_lock)
            {
                return _commands.ContainsKey(commandName);
            }
        }

        public async Task<bool> ExecuteCommandAsync(MessageObject message, string commandText)
        {
            if (string.IsNullOrEmpty(commandText))
                return false;

            string cleanedText = CleanMessageText(commandText);
            
            bool isAtBot = CheckIsAtBot(message);
            Log.Debug($"消息检查: isAtBot={isAtBot}, cleanedText={cleanedText}");

            var parts = cleanedText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string firstPart = parts[0];
            bool hasSlash = firstPart.StartsWith("/");
            string commandName = firstPart.TrimStart('/').ToLower();

            var command = GetCommand(commandName);
            if (command == null)
                return false;

            Log.Debug($"找到命令: {command.Name}, RequireAt={command.RequireAt}, RequireSlash={command.RequireSlash}");

            bool isPrivateMessage = message.MessageType == "private";
            Log.Debug($"消息类型检查: MessageType={message.MessageType}, isPrivateMessage={isPrivateMessage}");

            if (command.RequireAt && !isAtBot && !isPrivateMessage)
            {
                Log.Debug($"命令 '{command.Name}' 需要@机器人，但消息中没有@");
                return false;
            }

            if (command.RequireSlash && !hasSlash)
            {
                Log.Debug($"命令 '{command.Name}' 需要/前缀，但消息中没有/");
                return false;
            }

            if (!command.RequireSlash && !command.RequireAt)
            {
                if (!hasSlash)
                {
                    Log.Debug($"命令 '{command.Name}' RequireSlash=false 但 RequireAt 也为 false，必须有/前缀");
                    return false;
                }
            }

            if (!CheckPermission(message, command.Permission))
            {
                await SendPermissionDeniedMessageAsync(message);
                return true;
            }

            if (!CheckScope(message, command.Scope))
            {
                await SendScopeDeniedMessageAsync(message);
                return true;
            }

            try
            {
                var args = parts.Skip(1).ToArray();
                var validationResult = ValidateParameters(args, command.Parameters, cleanedText, message);
                
                if (!validationResult.IsValid)
                {
                    await SendErrorMessageAsync(message, validationResult, cleanedText);
                    return false;
                }

                var parameters = ParseParameters(args, command.Parameters, message);

                var context = new CommandContext
                {
                    Message = message,
                    Parameters = parameters,
                    RawCommand = cleanedText,
                    Client = _client
                };

                await Task.Run(async () => await command.Handler(context));
                return true;
            }
            catch (Exception ex)
            {
                bool isPluginCommand = !BuiltinModules.Contains(command.ModuleName);
                
                if (isPluginCommand)
                {
                    Log.Error($"插件命令 '{commandName}' (模块: {command.ModuleName}) 执行失败: {ex.Message}");
                    Log.Debug($"插件异常堆栈追踪:\n{ex.StackTrace}");
                }
                else
                {
                    Log.Error($"执行命令 '{commandName}' 失败: {ex.Message}");
                }
                return false;
            }
        }

        private bool CheckIsAtBot(MessageObject message)
        {
            if (message.MessageSegments != null && message.MessageSegments.Count > 0)
            {
                var selfId = message.SelfId;
                foreach (var segment in message.MessageSegments)
                {
                    if (segment.Type == "at" && segment.Data != null)
                    {
                        if (segment.Data.TryGetValue("qq", out var qqValue))
                        {
                            var qqStr = qqValue?.ToString();
                            if (qqStr == selfId.ToString() || qqStr == "all")
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(message.PlainText))
            {
                var selfId = message.SelfId.ToString();
                var atPattern = $"[CQ:at,qq={selfId}]";
                if (message.PlainText.Contains(atPattern) ||
                    message.PlainText.Contains("[CQ:at,qq=all]"))
                {
                    return true;
                }
            }

            return false;
        }

        private ValidationResult ValidateParameters(string[] args, List<CommandParameter> parameterDefs, string rawCommand, MessageObject message)
        {
            if (parameterDefs == null || parameterDefs.Count == 0)
                return new ValidationResult { IsValid = true };

            return ValidateParametersRecursive(args, parameterDefs, rawCommand, 0, message);
        }

        private ValidationResult ValidateParametersRecursive(string[] args, List<CommandParameter> parameterDefs, string rawCommand, int argStartIndex, MessageObject message)
        {
            int atParamCount = 0;
            for (int i = 0; i < parameterDefs.Count; i++)
            {
                var paramDef = parameterDefs[i];
                
                if (paramDef.Type == ParameterType.At)
                {
                    atParamCount++;
                    continue;
                }
                
                int argIndex = argStartIndex + i - atParamCount;
                bool hasArg = argIndex < args.Length;

                if (paramDef.IsRequired && !hasArg)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"缺少必需参数: {paramDef.Name}",
                        ErrorParameter = paramDef.Name,
                        ErrorPosition = CalculateErrorPosition(rawCommand, argIndex)
                    };
                }

                if (hasArg)
                {
                    var typeResult = ValidateParameterType(args[argIndex], paramDef);
                    if (!typeResult.IsValid)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = typeResult.ErrorMessage,
                            ErrorParameter = paramDef.Name,
                            ErrorPosition = CalculateErrorPosition(rawCommand, argIndex)
                        };
                    }

                    if (paramDef.SubParameters != null && paramDef.SubParameters.Count > 0)
                    {
                        var subResult = ValidateParametersRecursive(args, paramDef.SubParameters, rawCommand, argIndex + 1, message);
                        if (!subResult.IsValid)
                        {
                            return subResult;
                        }
                    }
                }
            }

            return new ValidationResult { IsValid = true };
        }

        private ValidationResult ValidateParameterType(string value, CommandParameter paramDef)
        {
            switch (paramDef.Type)
            {
                case ParameterType.Integer:
                    if (!int.TryParse(value, out _))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"参数 '{paramDef.Name}' 必须是整数，但收到: {value}"
                        };
                    }
                    break;

                case ParameterType.Float:
                    if (!float.TryParse(value, out _))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"参数 '{paramDef.Name}' 必须是数字，但收到: {value}"
                        };
                    }
                    break;

                case ParameterType.Boolean:
                    var validBooleans = new[] { "true", "false", "1", "0", "yes", "no", "是", "否" };
                    if (!validBooleans.Contains(value.ToLower()))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"参数 '{paramDef.Name}' 必须是布尔值(true/false)，但收到: {value}"
                        };
                    }
                    break;

                case ParameterType.At:
                    break;
            }

            return new ValidationResult { IsValid = true };
        }

        private int CalculateErrorPosition(string rawCommand, int paramIndex)
        {
            var parts = rawCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int position = 0;

            for (int i = 0; i <= paramIndex && i < parts.Length; i++)
            {
                position += parts[i].Length + 1;
            }

            return Math.Max(0, position - 1);
        }

        private Task SendErrorMessageAsync(MessageObject message, ValidationResult validationResult, string rawCommand)
        {
            string displayCommand = rawCommand;
            const int maxLength = 30;
            
            if (displayCommand.Length > maxLength)
            {
                displayCommand = displayCommand.Substring(0, maxLength) + "...";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"错误的命令: \"{displayCommand}\"");
            
            if (validationResult.ErrorPosition > 0)
            {
                int pointerPos = Math.Min(validationResult.ErrorPosition, displayCommand.Length);
                sb.Append("^".PadLeft(pointerPos + 1));
                sb.AppendLine($" ({validationResult.ErrorParameter})");
            }
            
            sb.AppendLine($"{validationResult.ErrorMessage}");

            string errorText = sb.ToString();

            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (message.MessageType == "private")
                        {
                            await _client.SendPrivateMsgAsync(message.UserId ?? 0, errorText);
                        }
                        else if (message.MessageType == "group")
                        {
                            await _client.SendGroupMsgAsync(message.GroupId ?? 0, errorText);
                        }
                    }
                    catch
                    {
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Debug($"发送错误消息失败: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        private Dictionary<string, string> ParseParameters(string[] args, List<CommandParameter> parameterDefs, MessageObject message)
        {
            var result = new Dictionary<string, string>();

            if (parameterDefs == null || parameterDefs.Count == 0)
                return result;

            var atUsers = ExtractAtUsers(message);
            int atIndex = 0;

            ParseParametersRecursive(args, parameterDefs, result, 0, atUsers, ref atIndex);
            return result;
        }

        private List<long> ExtractAtUsers(MessageObject message)
        {
            var atUsers = new List<long>();
            var selfId = message.SelfId;

            if (message.MessageSegments != null && message.MessageSegments.Count > 0)
            {
                foreach (var segment in message.MessageSegments)
                {
                    if (segment.Type == "at" && segment.Data != null)
                    {
                        if (segment.Data.TryGetValue("qq", out var qqValue))
                        {
                            var qqStr = qqValue?.ToString();
                            if (!string.IsNullOrEmpty(qqStr) && qqStr != "all")
                            {
                                if (long.TryParse(qqStr, out var qq) && qq != selfId)
                                {
                                    atUsers.Add(qq);
                                }
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(message.PlainText))
            {
                var atPattern = System.Text.RegularExpressions.Regex.Matches(message.PlainText, @"\[CQ:at,qq=(\d+)\]");
                foreach (System.Text.RegularExpressions.Match match in atPattern)
                {
                    if (long.TryParse(match.Groups[1].Value, out var qq) && qq != selfId && !atUsers.Contains(qq))
                    {
                        atUsers.Add(qq);
                    }
                }
            }

            return atUsers;
        }

        private int ParseParametersRecursive(string[] args, List<CommandParameter> parameterDefs, Dictionary<string, string> result, int argStartIndex, List<long> atUsers, ref int atIndex)
        {
            int consumedArgs = 0;

            for (int i = 0; i < parameterDefs.Count; i++)
            {
                var paramDef = parameterDefs[i];
                int argIndex = argStartIndex + i;

                if (paramDef.Type == ParameterType.At)
                {
                    if (atIndex < atUsers.Count)
                    {
                        result[paramDef.Name] = atUsers[atIndex].ToString();
                        atIndex++;
                    }
                    else if (paramDef.DefaultValue != null)
                    {
                        result[paramDef.Name] = paramDef.DefaultValue;
                    }
                    continue;
                }

                if (argIndex < args.Length)
                {
                    result[paramDef.Name] = args[argIndex];
                    consumedArgs++;

                    if (paramDef.SubParameters != null && paramDef.SubParameters.Count > 0)
                    {
                        var subConsumed = ParseParametersRecursive(args, paramDef.SubParameters, result, argIndex + 1, atUsers, ref atIndex);
                        consumedArgs += subConsumed;
                    }
                }
                else if (paramDef.DefaultValue != null)
                {
                    result[paramDef.Name] = paramDef.DefaultValue;
                }
            }

            return consumedArgs;
        }

        private string CleanMessageText(string text)
        {
            string cleaned = text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(cleaned) && 
                cleaned.StartsWith("\"") && cleaned.EndsWith("\""))
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2);
            }
            return cleaned;
        }

        public string GetCommandHelp(string commandName)
        {
            var command = GetCommand(commandName);
            if (command == null)
                return $"命令 '{commandName}' 不存在";

            var help = $"命令: /{command.Name}\n";
            help += $"描述: {command.Description}\n";
            
            if (!string.IsNullOrEmpty(command.HelpText))
            {
                help += $"\n{command.HelpText}\n";
            }

            if (command.Parameters.Count > 0)
            {
                help += "\n参数:\n";
                foreach (var param in command.Parameters)
                {
                    var required = param.IsRequired ? "必需" : "可选";
                    var typeStr = GetTypeString(param.Type);
                    var defaultVal = param.DefaultValue != null ? $" (默认: {param.DefaultValue})" : "";
                    help += $"  {param.Name} [{typeStr}] [{required}]{defaultVal} - {param.Description}\n";
                }
            }

            return help;
        }

        private string GetTypeString(ParameterType type)
        {
            return type switch
            {
                ParameterType.String => "字符串",
                ParameterType.Integer => "整数",
                ParameterType.Float => "数字",
                ParameterType.Boolean => "布尔值",
                _ => "未知"
            };
        }

        private bool CheckPermission(MessageObject message, CommandPermission permission)
        {
            if (permission == CommandPermission.Everyone)
                return true;

            var config = _configManager.GetConfig();
            var userId = message.UserId ?? 0;

            Log.Debug($"权限检查: permission={permission}, userId={userId}, ownerQQ={config.OwnerQQ}, isOwner={config.IsOwner(userId)}");

            if (permission == CommandPermission.BotOwner)
            {
                return config.IsOwner(userId);
            }

            if (message.MessageType != "group")
            {
                return config.IsOwner(userId);
            }

            var sender = message.Sender;
            if (sender == null)
                return config.IsOwner(userId);

            if (permission == CommandPermission.Owner)
            {
                return sender.Role == "owner" || config.IsOwner(userId);
            }

            if (permission == CommandPermission.GroupAdmin)
            {
                return sender.Role == "owner" || sender.Role == "admin" || config.IsOwner(userId);
            }

            return false;
        }

        public bool HasPermission(MessageObject message, CommandPermission permission)
        {
            return CheckPermission(message, permission);
        }

        public bool IsBotOwner(long userId)
        {
            return _configManager.GetConfig().IsOwner(userId);
        }

        public string GetMessageScope(MessageObject message)
        {
            return message.MessageType switch
            {
                "private" => "私聊",
                "group" => "群聊",
                _ => "未知"
            };
        }

        private Task SendPermissionDeniedMessageAsync(MessageObject message)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string errorText = "你无权使用此命令";
                    
                    if (message.MessageType == "private")
                    {
                        await _client.SendPrivateMsgAsync(message.UserId ?? 0, errorText);
                    }
                    else if (message.MessageType == "group")
                    {
                        await _client.SendGroupMsgAsync(message.GroupId ?? 0, errorText);
                    }
                }
                catch
                {
                }
            });
            
            return Task.CompletedTask;
        }

        private bool CheckScope(MessageObject message, CommandScope scope)
        {
            if (scope == CommandScope.All)
                return true;

            if (scope == CommandScope.PrivateOnly)
                return message.MessageType == "private";

            return true;
        }

        private Task SendScopeDeniedMessageAsync(MessageObject message)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string errorText = "此命令仅限私聊使用";
                    
                    if (message.MessageType == "private")
                    {
                        await _client.SendPrivateMsgAsync(message.UserId ?? 0, errorText);
                    }
                    else if (message.MessageType == "group")
                    {
                        await _client.SendGroupMsgAsync(message.GroupId ?? 0, errorText);
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