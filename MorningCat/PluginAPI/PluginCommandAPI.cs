using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MorningCat.Commands;
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
    }

    public class CommandParamEntry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool IsRequired { get; set; }
        public ParameterType Type { get; set; }
        public string DefaultValue { get; set; } = "";
    }

    public class PluginCommandAPI
    {
        private readonly CommandRegistry _commandRegistry;

        public PluginCommandAPI(CommandRegistry commandRegistry)
        {
            _commandRegistry = commandRegistry;
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
            return _commandRegistry.RegisterCommand(
                commandName, description, helpText, parameters, handler,
                moduleName, permission, scope, requireAt, requireSlash);
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
                        Parameters = cmd.Parameters?.Select(p => new CommandParamEntry
                        {
                            Name = p.Name,
                            Description = p.Description,
                            IsRequired = p.IsRequired,
                            Type = p.Type,
                            DefaultValue = p.DefaultValue
                        }).ToList() ?? new List<CommandParamEntry>()
                    });
                }
            }

            return result;
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
