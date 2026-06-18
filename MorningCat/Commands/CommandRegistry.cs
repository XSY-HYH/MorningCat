using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Logging;
using MorningCat.I18n;
using MorningCat.Config;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;
using MorningCat.PluginErrorDatabase;
using OneBotLib.Models;

namespace MorningCat.Commands
{
    public enum ParameterType
    {
        String,
        Integer,
        Float,
        Boolean,
        At,
        Reply
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
        PrivateOnly,
        GroupOnly
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
        public PlatformMessage Message { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public string RawCommand { get; set; }
        public MessageDistributionCore MDC { get; set; }

        /// <summary>
        /// 获取OneBot原始消息对象（仅OneBot平台可用）
        /// </summary>
        public MessageObject? GetOneBotMessage() => OneBotPlatformAdapter.ConvertToMessageObject(Message);
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
        private MessageDistributionCore _mdc;
        private ConfigManager _configManager;
        private MorningCatBot? _bot;
        
        private static readonly HashSet<string> BuiltinModules = new HashSet<string>
        {
            "HelpModule",
            "PluginModule", 
            "SystemModule",
            "SetModule"
        };

        public CommandRegistry(MessageDistributionCore mdc, ConfigManager configManager)
        {
            _mdc = mdc;
            _configManager = configManager;
        }

        public void SetMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public void SetBot(MorningCatBot bot)
        {
            _bot = bot;
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
                Log.Debug(I18nManager.S("command.name_empty"));
                return false;
            }

            commandName = commandName.TrimStart('/').ToLower();

            lock (_lock)
            {
                if (_commands.ContainsKey(commandName))
                {
                    Log.Debug(I18nManager.S("command.already_exists", commandName));
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

                if (commandInfo.Parameters.Count > 0)
                {
                    ValidateParameterNames(commandInfo.Parameters);
                }

                _commands[commandName] = commandInfo;
                Log.Debug(I18nManager.S("command.registered", commandName, moduleName, requireAt, requireSlash));
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
                    Log.Debug(I18nManager.S("command.unregistered", commandName));
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
                    Log.Debug(I18nManager.S("command.unregistered_by_module", commandName, moduleName));
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

        public async Task<bool> ExecuteCommandAsync(PlatformMessage message, string commandText)
        {
            if (string.IsNullOrEmpty(commandText))
                return false;

            string cleanedText = MessageHelper.CleanMessageText(commandText);
            
            bool isAtBot = message.IsAtBot;

            var parts = cleanedText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string firstPart = parts[0];
            bool hasSlash = firstPart.StartsWith("/");
            string commandName = firstPart.TrimStart('/').ToLower();

            var command = GetCommand(commandName);
            if (command == null)
            {
                return false;
            }

            bool isPrivateMessage = message.MessageType == UnifiedMessageType.Private;

            if (command.RequireAt && !isAtBot && !isPrivateMessage)
            {
                return false;
            }

            if (command.RequireSlash && !hasSlash)
            {
                Log.Debug(I18nManager.S("command.require_slash", commandName, command.RequireSlash, hasSlash));
                return false;
            }

            if (!CheckPermission(message, command.Permission))
            {
                await SendPermissionDeniedMessageAsync(message);
                return true;
            }

            if (!CheckScope(message, command.Scope))
            {
                await SendScopeDeniedMessageAsync(message, command.Scope);
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
                    MDC = _mdc
                };

                await Task.Run(async () => await command.Handler(context));
                return true;
            }
            catch (Exception ex)
            {
                bool isPluginCommand = !BuiltinModules.Contains(command.ModuleName);
                
                if (isPluginCommand)
                {
                    Log.Error(I18nManager.S("command.plugin_execute_failed", commandName, command.ModuleName, ex.Message));
                    Log.Debug(I18nManager.S("command.plugin_stack_trace", ex.StackTrace));
                    
                    // 尝试匹配已知错误
                    if (_bot?.ErrorMatcher?.IsEnabled == true)
                    {
                        try
                        {
                            var match = _bot.ErrorMatcher.MatchAsync(ex).GetAwaiter().GetResult();
                            if (match.Found)
                            {
                                Log.Debug(PluginErrorMatcher.FormatDebugLog(match, command.ModuleName, ex));
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    Log.Error(I18nManager.S("command.execute_failed", commandName, ex.Message));
                }
                return false;
            }
        }

        private ValidationResult ValidateParameters(string[] args, List<CommandParameter> parameterDefs, string rawCommand, PlatformMessage message)
        {
            if (parameterDefs == null || parameterDefs.Count == 0)
                return new ValidationResult { IsValid = true };

            return ValidateParametersRecursive(args, parameterDefs, rawCommand, 0, message);
        }

        private ValidationResult ValidateParametersRecursive(string[] args, List<CommandParameter> parameterDefs, string rawCommand, int argStartIndex, PlatformMessage message)
        {
            int atParamCount = 0;
            int replyParamCount = 0;
            for (int i = 0; i < parameterDefs.Count; i++)
            {
                var paramDef = parameterDefs[i];
                
                if (paramDef.Type == ParameterType.At)
                {
                    atParamCount++;
                    continue;
                }
                
                if (paramDef.Type == ParameterType.Reply)
                {
                    replyParamCount++;
                    continue;
                }
                
                int argIndex = argStartIndex + i - atParamCount - replyParamCount;
                bool hasArg = argIndex < args.Length;

                if (paramDef.IsRequired && !hasArg)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = I18nManager.S("command.missing_required_param", paramDef.Name),
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
                            ErrorMessage = I18nManager.S("command.param_must_be_integer", paramDef.Name, value)
                        };
                    }
                    break;

                case ParameterType.Float:
                    if (!float.TryParse(value, out _))
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = I18nManager.S("command.param_must_be_number", paramDef.Name, value)
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
                            ErrorMessage = I18nManager.S("command.param_must_be_boolean", paramDef.Name, value)
                        };
                    }
                    break;

                case ParameterType.At:
                case ParameterType.Reply:
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

        private Task SendErrorMessageAsync(PlatformMessage message, ValidationResult validationResult, string rawCommand)
        {
            string displayCommand = rawCommand;
            const int maxLength = 30;
            
            if (displayCommand.Length > maxLength)
            {
                displayCommand = displayCommand.Substring(0, maxLength) + "...";
            }

            var sb = new StringBuilder();
            sb.AppendLine(I18nManager.S("command.error_command", displayCommand));
            
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
                        await _mdc.SendMessageAsync(message, errorText);
                    }
                    catch
                    {
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Debug(I18nManager.S("command.send_error_failed", ex.Message));
            }
            
            return Task.CompletedTask;
        }

        private Dictionary<string, string> ParseParameters(string[] args, List<CommandParameter> parameterDefs, PlatformMessage message)
        {
            var result = new Dictionary<string, string>();

            if (parameterDefs == null || parameterDefs.Count == 0)
                return result;

            var atUsers = ExtractAtUsers(message);
            int atIndex = 0;
            
            var replyMessageId = ExtractReplyMessageId(message);

            ParseParametersRecursive(args, parameterDefs, result, 0, atUsers, ref atIndex, replyMessageId);
            return result;
        }

        private long? ExtractReplyMessageId(PlatformMessage message)
        {
            foreach (var segment in message.Segments)
            {
                if (segment.Type == "reply" && segment.Data.TryGetValue("message_id", out var idValue))
                {
                    var idStr = idValue?.ToString();
                    if (!string.IsNullOrEmpty(idStr) && long.TryParse(idStr, out var msgId))
                    {
                        return msgId;
                    }
                }
            }

            if (!string.IsNullOrEmpty(message.PlainText))
            {
                var replyMatch = System.Text.RegularExpressions.Regex.Match(message.PlainText, @"\[CQ:reply,id=(-?\d+)\]");
                if (replyMatch.Success && long.TryParse(replyMatch.Groups[1].Value, out var msgId))
                {
                    return msgId;
                }
            }

            return null;
        }

        private List<long> ExtractAtUsers(PlatformMessage message)
        {
            var atUsers = new List<long>();
            var selfId = message.SelfId;

            foreach (var segment in message.Segments)
            {
                if (segment.Type == "at" && segment.Data.TryGetValue("user_id", out var userIdValue))
                {
                    var userIdStr = userIdValue?.ToString();
                    if (!string.IsNullOrEmpty(userIdStr) && userIdStr != "all")
                    {
                        if (long.TryParse(userIdStr, out var qq) && qq.ToString() != selfId)
                        {
                            atUsers.Add(qq);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(message.PlainText))
            {
                var atPattern = System.Text.RegularExpressions.Regex.Matches(message.PlainText, @"\[CQ:at,qq=(\d+)\]");
                foreach (System.Text.RegularExpressions.Match match in atPattern)
                {
                    if (long.TryParse(match.Groups[1].Value, out var qq) && qq.ToString() != selfId && !atUsers.Contains(qq))
                    {
                        atUsers.Add(qq);
                    }
                }
            }

            return atUsers;
        }

        private int ParseParametersRecursive(string[] args, List<CommandParameter> parameterDefs, Dictionary<string, string> result, int argStartIndex, List<long> atUsers, ref int atIndex, long? replyMessageId)
        {
            int currentArgIndex = argStartIndex;
            int paramCount = parameterDefs.Count;
            
            for (int i = 0; i < paramCount; i++)
            {
                var paramDef = parameterDefs[i];
                bool isLastParam = (i == paramCount - 1);
                int remainingArgs = args.Length - currentArgIndex;

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
                
                if (paramDef.Type == ParameterType.Reply)
                {
                    if (replyMessageId.HasValue)
                    {
                        result[paramDef.Name] = replyMessageId.Value.ToString();
                    }
                    else if (paramDef.DefaultValue != null)
                    {
                        result[paramDef.Name] = paramDef.DefaultValue;
                    }
                    continue;
                }

                if (currentArgIndex < args.Length)
                {
                    if (isLastParam && paramDef.SubParameters != null && paramDef.SubParameters.Count > 0)
                    {
                        result[paramDef.Name] = args[currentArgIndex];
                        currentArgIndex++;
                        
                        var subConsumed = ParseParametersRecursive(args, paramDef.SubParameters, result, currentArgIndex, atUsers, ref atIndex, replyMessageId);
                        currentArgIndex += subConsumed;
                    }
                    else if (isLastParam && remainingArgs > 0)
                    {
                        result[paramDef.Name] = args[currentArgIndex];
                        currentArgIndex++;
                    }
                    else if (!isLastParam)
                    {
                        result[paramDef.Name] = args[currentArgIndex];
                        currentArgIndex++;
                    }
                    else
                    {
                        if (paramDef.DefaultValue != null)
                        {
                            result[paramDef.Name] = paramDef.DefaultValue;
                        }
                    }
                }
                else if (paramDef.DefaultValue != null)
                {
                    result[paramDef.Name] = paramDef.DefaultValue;
                }
                else if (paramDef.IsRequired)
                {
                    Log.Warning(I18nManager.S("command.param_missing", paramDef.Name));
                }
            }

            return currentArgIndex - argStartIndex;
        }

        private void ValidateParameterNames(List<CommandParameter> parameters, HashSet<string> seen = null)
        {
            if (seen == null) seen = new HashSet<string>();
            
            foreach (var param in parameters)
            {
                if (seen.Contains(param.Name))
                {
                    Log.Warning(I18nManager.S("command.param_conflict", param.Name));
                }
                else
                {
                    seen.Add(param.Name);
                }
                
                if (param.SubParameters != null && param.SubParameters.Count > 0)
                {
                    ValidateParameterNames(param.SubParameters, seen);
                }
            }
        }

        public string GetCommandHelp(string commandName)
        {
            var command = GetCommand(commandName);
            if (command == null)
                return I18nManager.S("command.not_found", commandName);

            var help = I18nManager.S("command.help_title", command.Name) + "\n";
            help += I18nManager.S("command.help_desc", command.Description) + "\n";
            
            if (!string.IsNullOrEmpty(command.HelpText))
            {
                help += $"\n{command.HelpText}\n";
            }

            if (command.Parameters.Count > 0)
            {
                help += "\n" + I18nManager.S("command.help_params") + "\n";
                foreach (var param in command.Parameters)
                {
                    var required = param.IsRequired ? I18nManager.S("command.param_required") : I18nManager.S("command.param_optional");
                    var typeStr = GetTypeString(param.Type);
                    var defaultVal = param.DefaultValue != null ? I18nManager.S("command.param_default", param.DefaultValue) : "";
                    help += $"  {param.Name} [{typeStr}] [{required}]{defaultVal} - {param.Description}\n";
                }
            }

            return help;
        }

        private string GetTypeString(ParameterType type)
        {
            return type switch
            {
                ParameterType.String => I18nManager.S("command.type_string"),
                ParameterType.Integer => I18nManager.S("command.type_integer"),
                ParameterType.Float => I18nManager.S("command.type_float"),
                ParameterType.Boolean => I18nManager.S("command.type_boolean"),
                _ => I18nManager.S("command.type_unknown")
            };
        }

        private bool CheckPermission(PlatformMessage message, CommandPermission permission)
        {
            if (permission == CommandPermission.Everyone)
                return true;

            var config = _configManager.GetConfig();
            var userId = long.TryParse(message.SenderId, out var uid) ? uid : 0;

            Log.Debug(I18nManager.S("command.permission_check", permission, userId, config.OwnerQQ, config.IsOwner(userId)));

            if (permission == CommandPermission.BotOwner)
            {
                return config.IsOwner(userId);
            }

            if (message.MessageType != UnifiedMessageType.Group)
            {
                return config.IsOwner(userId);
            }

            // 群内权限：从PlatformMessage获取角色信息
            var senderRole = message.SenderRole;
            if (senderRole == null)
                return config.IsOwner(userId);

            if (permission == CommandPermission.Owner)
            {
                return senderRole == "owner" || config.IsOwner(userId);
            }

            if (permission == CommandPermission.GroupAdmin)
            {
                return senderRole == "owner" || senderRole == "admin" || config.IsOwner(userId);
            }

            return false;
        }

        public bool HasPermission(PlatformMessage message, CommandPermission permission)
        {
            return CheckPermission(message, permission);
        }

        public bool IsBotOwner(long userId)
        {
            return _configManager.GetConfig().IsOwner(userId);
        }

        public string GetMessageScope(PlatformMessage message)
        {
            return message.MessageType switch
            {
                UnifiedMessageType.Private => I18nManager.S("command.scope_private"),
                UnifiedMessageType.Group => I18nManager.S("command.scope_group"),
                UnifiedMessageType.Channel => I18nManager.S("command.scope_channel"),
                _ => I18nManager.S("command.scope_unknown")
            };
        }

        private Task SendPermissionDeniedMessageAsync(PlatformMessage message)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string errorText = I18nManager.S("command.permission_denied");
                    await _mdc.SendMessageAsync(message, errorText);
                }
                catch
                {
                }
            });
            
            return Task.CompletedTask;
        }

        private bool CheckScope(PlatformMessage message, CommandScope scope)
        {
            if (scope == CommandScope.All)
                return true;

            if (scope == CommandScope.PrivateOnly)
                return message.MessageType == UnifiedMessageType.Private;

            if (scope == CommandScope.GroupOnly)
                return message.MessageType == UnifiedMessageType.Group;

            return true;
        }

        private Task SendScopeDeniedMessageAsync(PlatformMessage message, CommandScope scope)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string errorText = scope == CommandScope.PrivateOnly 
                        ? I18nManager.S("command.scope_private_only") 
                        : I18nManager.S("command.scope_group_only");
                    
                    await _mdc.SendMessageAsync(message, errorText);
                }
                catch
                {
                }
            });
            
            return Task.CompletedTask;
        }

        public async Task<PluginCommandResult> ExecuteCommandAsPlugin(
            CommandPermission permissionLevel,
            string commandName,
            string[] args,
            PlatformMessage originalMessage)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.name_empty")
                };
            }

            commandName = commandName.TrimStart('/').ToLower();

            var command = GetCommand(commandName);
            if (command == null)
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.not_found", commandName)
                };
            }

            if ((int)permissionLevel < (int)command.Permission)
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.permission_insufficient", command.Permission, permissionLevel)
                };
            }

            if (!CheckScope(originalMessage, command.Scope))
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.scope_mismatch", command.Scope == CommandScope.PrivateOnly ? I18nManager.S("command.scope_private") : I18nManager.S("command.scope_group"))
                };
            }

            try
            {
                var parameters = ParseParameters(args, command.Parameters, originalMessage);

                var context = new CommandContext
                {
                    Message = originalMessage,
                    Parameters = parameters,
                    RawCommand = $"/{commandName} {string.Join(" ", args)}".Trim(),
                    MDC = _mdc
                };

                await command.Handler(context);

                return new PluginCommandResult
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.execute_failed", ex.Message)
                };
            }
        }

        public async Task<PluginCommandResult> ExecuteCommandAsPlugin(
            CommandPermission permissionLevel,
            string commandLine,
            PlatformMessage originalMessage)
        {
            if (string.IsNullOrEmpty(commandLine))
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.command_line_empty")
                };
            }

            var parts = commandLine.TrimStart('/').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return new PluginCommandResult
                {
                    Success = false,
                    ErrorMessage = I18nManager.S("command.invalid_command_line")
                };
            }

            var commandName = parts[0];
            var args = parts.Skip(1).ToArray();

            return await ExecuteCommandAsPlugin(permissionLevel, commandName, args, originalMessage);
        }
    }

    public class PluginCommandResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }
}