using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MorningCat.Commands;
using MorningCat.I18n;
using Logging;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.PluginAPI
{
    public class CommandInfoEntry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ModuleName { get; set; } = "";
        public CommandPermission Permission { get; set; }
        public CommandScope Scope { get; set; }
        public bool RequireAt { get; set; }
        public bool RequireSlash { get; set; }
        public List<CommandParamEntry> Parameters { get; set; } = new List<CommandParamEntry>();
        public List<ParameterBranchEntry> AlternativeGroups { get; set; } = new List<ParameterBranchEntry>();
    }

    public class CommandParamEntry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsRequired { get; set; }
        public ParameterType Type { get; set; }
        public List<ParameterType> AllowedTypes { get; set; } = new List<ParameterType>();
        public string DefaultValue { get; set; } = "";
        public List<CommandParamEntry> SubParameters { get; set; } = new List<CommandParamEntry>();
    }

    /// <summary>
    /// 插件可用的同级分支定义
    /// </summary>
    public class ParameterBranchEntry
    {
        public string Name { get; set; } = "";
        public List<CommandParamEntry> Parameters { get; set; } = new List<CommandParamEntry>();
    }

    public class PluginCommandAPI
    {
        private readonly CommandRegistry _commandRegistry;
        private Func<string, Task> _onPluginUnload;

        public PluginCommandAPI(CommandRegistry commandRegistry)
        {
            _commandRegistry = commandRegistry;
        }

        /// <summary>
        /// 设置插件卸载回调，当命令注册违规时自动卸载插件
        /// </summary>
        public void SetUnloadCallback(Func<string, Task> onPluginUnload)
        {
            _onPluginUnload = onPluginUnload;
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
            var result = _commandRegistry.RegisterCommand(
                commandName, description, helpText, parameters, handler,
                moduleName, permission, scope, requireAt, requireSlash);

            // 注册失败且命令名包含空格，自动卸载插件
            if (!result && !string.IsNullOrEmpty(commandName) && commandName.TrimStart('/').Contains(' '))
            {
                Log.Error(I18nManager.S("command.name_contains_space_unload", moduleName));
                _onPluginUnload?.Invoke(moduleName).GetAwaiter().GetResult();
            }

            return result;
        }

        public bool UnregisterCommand(string commandName)
        {
            return _commandRegistry.UnregisterCommand(commandName);
        }

        public void UnregisterModuleCommands(string moduleName)
        {
            _commandRegistry.UnregisterModuleCommands(moduleName);
        }

        public List<CommandInfoEntry> EnumerateCommands(CommandPermission permission)
        {
            var allCommands = _commandRegistry.GetAllCommands();
            var result = new List<CommandInfoEntry>();

            foreach (var cmd in allCommands)
            {
                if (cmd.Permission <= permission)
                {
                    result.Add(new CommandInfoEntry
                    {
                        Name = cmd.Name,
                        Description = cmd.Description,
                        ModuleName = cmd.ModuleName,
                        Permission = cmd.Permission,
                        Scope = cmd.Scope,
                        RequireAt = cmd.RequireAt,
                        RequireSlash = cmd.RequireSlash,
                        Parameters = cmd.Parameters?.Select(MapParamEntry).ToList() ?? new List<CommandParamEntry>(),
                        AlternativeGroups = cmd.AlternativeGroups?.Select(b => new ParameterBranchEntry
                        {
                            Name = b.Name,
                            Parameters = b.Parameters?.Select(MapParamEntry).ToList() ?? new List<CommandParamEntry>()
                        }).ToList() ?? new List<ParameterBranchEntry>()
                    });
                }
            }

            return result;
        }

        private static CommandParamEntry MapParamEntry(CommandParameter p)
        {
            return new CommandParamEntry
            {
                Name = p.Name,
                Description = p.Description,
                IsRequired = p.IsRequired,
                Type = p.Type,
                AllowedTypes = p.AllowedTypes ?? new List<ParameterType>(),
                DefaultValue = p.DefaultValue,
                SubParameters = p.SubParameters?.Select(MapParamEntry).ToList() ?? new List<CommandParamEntry>()
            };
        }

        public List<CommandInfoEntry> EnumerateCommands()
        {
            return EnumerateCommands(CommandPermission.Everyone);
        }

        public async Task<PluginCommandResult> ExecuteAsNormal(PlatformMessage message, string commandLine)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(CommandPermission.Everyone, commandLine, message);
        }

        public async Task<PluginCommandResult> ExecuteAsNormal(PlatformMessage message, string commandName, params string[] args)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(CommandPermission.Everyone, commandName, args, message);
        }

        public async Task<PluginCommandResult> ExecuteAsGroupAdmin(PlatformMessage message, string commandLine)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(CommandPermission.GroupAdmin, commandLine, message);
        }

        public async Task<PluginCommandResult> ExecuteAsGroupAdmin(PlatformMessage message, string commandName, params string[] args)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(CommandPermission.GroupAdmin, commandName, args, message);
        }

        public async Task<PluginCommandResult> ExecuteAsBotOwner(PlatformMessage message, string commandLine)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(CommandPermission.BotOwner, commandLine, message);
        }

        public async Task<PluginCommandResult> ExecuteAsBotOwner(PlatformMessage message, string commandName, params string[] args)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(CommandPermission.BotOwner, commandName, args, message);
        }

        public async Task<PluginCommandResult> ExecuteWithPermission(PlatformMessage message, CommandPermission permission, string commandLine)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(permission, commandLine, message);
        }

        public async Task<PluginCommandResult> ExecuteWithPermission(PlatformMessage message, CommandPermission permission, string commandName, params string[] args)
        {
            return await _commandRegistry.ExecuteCommandAsPlugin(permission, commandName, args, message);
        }
    }
}
