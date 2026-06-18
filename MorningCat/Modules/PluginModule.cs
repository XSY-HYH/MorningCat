using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.I18n;
using MorningCat.PluginAPI;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Modules
{
    public class PluginModule
    {
        private MessageDistributionCore _mdc;
        private CommandRegistry _commandRegistry;
        private ModuleManager _moduleManager;
        private MorningCatBot _bot;

        public void SetServices(MessageDistributionCore mdc, CommandRegistry commandRegistry, ModuleManager moduleManager, MorningCatBot bot)
        {
            _mdc = mdc;
            _commandRegistry = commandRegistry;
            _moduleManager = moduleManager;
            _bot = bot;
        }

        public void UpdateMDC(MessageDistributionCore mdc)
        {
            _mdc = mdc;
        }

        public async Task Init()
        {
            Log.Debug(I18nManager.S("plugin_module.initializing"));

            if (_mdc == null || _commandRegistry == null || _moduleManager == null)
            {
                Log.Debug(I18nManager.S("plugin_module.di_incomplete"));
                return;
            }

            RegisterPluginCommand();

            Log.Info(I18nManager.S("plugin_module.initialized"));
            await Task.CompletedTask;
        }

        private void RegisterPluginCommand()
        {
            var subParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "action",
                    Description = I18nManager.S("plugin_module.param_action"),
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "id",
                    Description = I18nManager.S("plugin_module.param_id"),
                    IsRequired = false,
                    Type = ParameterType.String
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "plugin",
                I18nManager.S("plugin_module.desc"),
                I18nManager.S("plugin_module.help_text"),
                subParams,
                HandlePluginCommand,
                "PluginModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info(I18nManager.S("plugin_module.registered"));
            }
            else
            {
                Log.Error(I18nManager.S("plugin_module.register_failed"));
            }
        }

        private async Task HandlePluginCommand(CommandContext context)
        {
            var parameters = context.Parameters;
            var message = context.Message;

            if (!parameters.TryGetValue("action", out var action) || string.IsNullOrEmpty(action))
            {
                await SendPluginHelpAsync(message);
                return;
            }

            action = action.ToLower();

            switch (action)
            {
                case "list":
                    await ListPluginsAsync(message);
                    break;
                case "view":
                    await ViewPluginAsync(message, parameters);
                    break;
                case "uninstall":
                    await UninstallPluginAsync(message, parameters);
                    break;
                case "disable":
                    await DisablePluginAsync(message, parameters);
                    break;
                case "enable":
                    await EnablePluginAsync(message, parameters);
                    break;
                case "library":
                    await HandleLibraryCommand(message, parameters);
                    break;
                case "install":
                    await InstallPluginAsync(message, parameters);
                    break;
                default:
                    await SendMessageAsync(message, I18nManager.S("plugin_module.unknown_action", action));
                    break;
            }
        }

        private async Task SendPluginHelpAsync(PlatformMessage message)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(I18nManager.S("plugin_module.help_title"));
            sb.AppendLine();
            sb.AppendLine(I18nManager.S("plugin_module.help_list"));
            sb.AppendLine(I18nManager.S("plugin_module.help_view"));
            sb.AppendLine(I18nManager.S("plugin_module.help_uninstall"));
            sb.AppendLine(I18nManager.S("plugin_module.help_disable_list"));
            sb.AppendLine(I18nManager.S("plugin_module.help_disable"));
            sb.AppendLine(I18nManager.S("plugin_module.help_enable"));
            sb.AppendLine(I18nManager.S("plugin_module.help_library"));
            sb.AppendLine(I18nManager.S("plugin_module.help_install"));
            await SendMessageAsync(message, sb.ToString());
        }

        private async Task HandleLibraryCommand(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var subAction) || string.IsNullOrEmpty(subAction))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.specify_library_action"));
                return;
            }

            subAction = subAction.ToLower();

            switch (subAction)
            {
                case "list":
                    await ListLibrariesAsync(message);
                    break;
                default:
                    await SendMessageAsync(message, I18nManager.S("plugin_module.unknown_library_action", subAction));
                    break;
            }
        }

        private async Task ListLibrariesAsync(PlatformMessage message)
        {
            var libraryPaths = _moduleManager.GetLoadedLibraryPaths();

            if (libraryPaths == null || libraryPaths.Count == 0)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.no_libraries"));
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(I18nManager.S("plugin_module.library_list", libraryPaths.Count));
            sb.AppendLine();

            int index = 1;
            foreach (var path in libraryPaths)
            {
                var fileName = Path.GetFileName(path);
                sb.AppendLine($"{index}. {fileName}");
                index++;
            }

            await SendMessageAsync(message, sb.ToString());
        }

        private async Task ListPluginsAsync(PlatformMessage message)
        {
            var allModules = _moduleManager.GetAllModules();
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            
            var disabledPlugins = new List<string>();
            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        disabledPlugins.Add(fileName);
                    }
                }
            }
            catch
            {
            }

            var total = allModules.Count + disabledPlugins.Count;

            if (total == 0)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.no_libraries"));
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(I18nManager.S("plugin_module.plugin_list", total));
            sb.AppendLine();

            int index = 1;
            foreach (var module in allModules)
            {
                var status = module.Status switch
                {
                    ModuleStatus.Running => I18nManager.S("plugin_module.status_running"),
                    ModuleStatus.Error => I18nManager.S("plugin_module.status_error"),
                    ModuleStatus.Unloaded => I18nManager.S("plugin_module.status_unloaded"),
                    ModuleStatus.Initializing => I18nManager.S("plugin_module.status_initializing"),
                    ModuleStatus.Scanned => I18nManager.S("plugin_module.status_scanned"),
                    _ => I18nManager.S("plugin_module.status_unknown")
                };

                var isBuiltin = IsBuiltinModule(module.ModuleName);
                var builtinTag = isBuiltin ? I18nManager.S("plugin_module.builtin_tag") : "";

                sb.AppendLine($"{index}. {module.ModuleName}{builtinTag}");
                sb.AppendLine(I18nManager.S("plugin_module.status_label", status));

                index++;
            }

            foreach (var disabledName in disabledPlugins)
            {
                sb.AppendLine($"{index}. {disabledName}");
                sb.AppendLine(I18nManager.S("plugin_module.status_label", I18nManager.S("plugin_module.status_disabled")));

                index++;
            }

            //sb.AppendLine("使用 /plugin view <ID> 查看详情");
            //sb.AppendLine("使用 /plugin uninstall <ID> 卸载插件");

            await SendMessageAsync(message, sb.ToString());
        }

        private async Task ViewPluginAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var idStr) || string.IsNullOrEmpty(idStr))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.specify_plugin_id", "view"));
                return;
            }

            if (!int.TryParse(idStr, out int id) || id < 1)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.id_must_be_positive"));
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            
            var disabledPlugins = new List<(string Name, string Path)>();
            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        disabledPlugins.Add((fileName, file));
                    }
                }
            }
            catch
            {
            }

            var total = allModules.Count + disabledPlugins.Count;

            if (id > total)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.plugin_not_found", id));
                return;
            }

            if (id <= allModules.Count)
            {
                var module = allModules[id - 1];

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(I18nManager.S("plugin_module.plugin_detail"));
                sb.AppendLine();
                sb.AppendLine(I18nManager.S("plugin_module.name_label", module.ModuleName));
                sb.AppendLine(I18nManager.S("plugin_module.status_detail", module.Status));
                sb.AppendLine(I18nManager.S("plugin_module.type_label", module.ModuleType?.FullName ?? "未知"));
                
                var displayPath = GetRelativePath(module.AssemblyPath);
                sb.AppendLine(I18nManager.S("plugin_module.assembly_label", displayPath));

                if (module.ModuleInstance != null)
                {
                    sb.AppendLine(I18nManager.S("plugin_module.instance_created"));
                }

                var deps = _moduleManager.GetModuleDependencies(module.ModuleName);
                if (deps != null && deps.Count > 0)
                {
                    sb.AppendLine(I18nManager.S("plugin_module.deps_label", string.Join(", ", deps)));
                }

                var dependents = _moduleManager.GetModulesDependentOn(module.ModuleName);
                if (dependents != null && dependents.Count > 0)
                {
                    sb.AppendLine(I18nManager.S("plugin_module.dependents_label", string.Join(", ", dependents)));
                }

                if (_bot != null)
                {
                    var metadata = _bot.GetPluginMetadata(module.ModuleName);
                    if (metadata != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine(I18nManager.S("plugin_module.metadata_section"));
                        if (!string.IsNullOrEmpty(metadata.DisplayName))
                            sb.AppendLine(I18nManager.S("plugin_module.display_name", metadata.DisplayName));
                        if (!string.IsNullOrEmpty(metadata.Author))
                            sb.AppendLine(I18nManager.S("plugin_module.author", metadata.Author));
                        if (!string.IsNullOrEmpty(metadata.Website))
                            sb.AppendLine(I18nManager.S("plugin_module.website", metadata.Website));
                        if (!string.IsNullOrEmpty(metadata.Description))
                            sb.AppendLine(I18nManager.S("plugin_module.description", metadata.Description));
                    }
                }

                await SendMessageAsync(message, sb.ToString());
            }
            else
            {
                var disabledIndex = id - allModules.Count - 1;
                var disabledPlugin = disabledPlugins[disabledIndex];

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(I18nManager.S("plugin_module.plugin_detail"));
                sb.AppendLine();
                sb.AppendLine(I18nManager.S("plugin_module.name_label", disabledPlugin.Name));
                sb.AppendLine(I18nManager.S("plugin_module.status_disabled_label"));
                
                var displayPath = GetRelativePath(disabledPlugin.Path);
                sb.AppendLine(I18nManager.S("plugin_module.assembly_label", displayPath));

                await SendMessageAsync(message, sb.ToString());
            }
        }

        private async Task UninstallPluginAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var idStr) || string.IsNullOrEmpty(idStr))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.specify_plugin_id", "uninstall"));
                return;
            }

            if (!int.TryParse(idStr, out int id) || id < 1)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.id_must_be_positive"));
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            
            Log.Debug(I18nManager.S("plugin_module.uninstall_target", id, allModules.Count));
            
            var disabledPlugins = new List<(string Name, string Path)>();
            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        disabledPlugins.Add((fileName, file));
                    }
                }
            }
            catch
            {
            }

            Log.Debug(I18nManager.S("plugin_module.uninstall_disabled_count", disabledPlugins.Count));
            var total = allModules.Count + disabledPlugins.Count;

            if (id > total)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.plugin_not_found", id));
                return;
            }

            if (id <= allModules.Count)
            {
                var module = allModules[id - 1];
                Log.Debug(I18nManager.S("plugin_module.uninstall_target_plugin", module.ModuleName, module.Status, module.AssemblyPath));

                if (IsBuiltinModule(module.ModuleName))
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.cannot_unload_builtin"));
                    return;
                }

                var dependents = _moduleManager.GetModulesDependentOn(module.ModuleName);
                if (dependents != null && dependents.Count > 0)
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.cannot_unload_dependent", string.Join("\n", dependents)));
                    return;
                }

                try
                {
                    Log.Debug(I18nManager.S("plugin_module.uninstall_trying"));
                    var success = await _moduleManager.UnloadModuleAsync(module.ModuleName);
                    Log.Debug(I18nManager.S("plugin_module.uninstall_result", success));

                    if (success)
                    {
                        _commandRegistry.UnregisterModuleCommands(module.ModuleName);
                        Log.Debug(I18nManager.S("plugin_module.uninstall_commands_unregistered"));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.unloaded", module.ModuleName));
                    }
                    else
                    {
                        await SendMessageAsync(message, I18nManager.S("plugin_module.unload_failed", module.ModuleName));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(I18nManager.S("plugin_module.uninstall_error", ex.Message));
                    await SendMessageAsync(message, I18nManager.S("plugin_module.unload_error", ex.Message));
                }
            }
            else
            {
                var disabledIndex = id - allModules.Count - 1;
                var disabledPlugin = disabledPlugins[disabledIndex];
                Log.Debug(I18nManager.S("plugin_module.uninstall_disabled_target", disabledPlugin.Name, disabledPlugin.Path));

                try
                {
                    if (File.Exists(disabledPlugin.Path))
                    {
                        Log.Debug(I18nManager.S("plugin_module.uninstall_file_exists_deleting"));
                        File.Delete(disabledPlugin.Path);
                        Log.Debug(I18nManager.S("plugin_module.uninstall_delete_success"));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.deleted", disabledPlugin.Name));
                    }
                    else
                    {
                        Log.Debug(I18nManager.S("plugin_module.uninstall_file_not_exist"));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.file_not_exist"));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(I18nManager.S("plugin_module.uninstall_error", ex.Message));
                    await SendMessageAsync(message, I18nManager.S("plugin_module.delete_error", ex.Message));
                }
            }
        }

        private async Task DisablePluginAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var arg) || string.IsNullOrEmpty(arg))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.specify_disable_target"));
                return;
            }

            if (arg.ToLower() == "list")
            {
                await ListDisabledPluginsAsync(message);
                return;
            }

            var className = arg;
            var allModules = _moduleManager.GetAllModules();
            var module = allModules.FirstOrDefault(m => m.ModuleName == className);

            if (module == null)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.plugin_not_exist", className));
                return;
            }

            Log.Debug(I18nManager.S("plugin_module.disable_target", module.ModuleName, module.Status, module.AssemblyPath));

            if (IsBuiltinModule(module.ModuleName))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.cannot_disable_builtin"));
                return;
            }

            if (string.IsNullOrEmpty(module.AssemblyPath))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.invalid_plugin_path"));
                return;
            }

            if (module.AssemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.already_disabled", module.ModuleName));
                return;
            }

            try
            {
                if (module.Status == ModuleStatus.Running)
                {
                    Log.Debug(I18nManager.S("plugin_module.disable_unloading"));
                    var success = await _moduleManager.UnloadModuleAsync(module.ModuleName);
                    Log.Debug(I18nManager.S("plugin_module.disable_unload_result", success));
                    if (success)
                    {
                        _commandRegistry.UnregisterModuleCommands(module.ModuleName);
                        Log.Debug(I18nManager.S("plugin_module.disable_commands_unregistered"));
                    }
                }

                var disabledPath = module.AssemblyPath + ".disabled";
                Log.Debug(I18nManager.S("plugin_module.disable_check_file", module.AssemblyPath));
                if (File.Exists(module.AssemblyPath))
                {
                    Log.Debug(I18nManager.S("plugin_module.disable_rename_to", disabledPath));
                    try
                    {
                        File.Move(module.AssemblyPath, disabledPath);
                        Log.Debug(I18nManager.S("plugin_module.disable_rename_success"));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.disabled", module.ModuleName));
                    }
                    catch (IOException ex)
                    {
                        Log.Debug(I18nManager.S("plugin_module.disable_file_locked", ex.Message));
                        File.WriteAllText(disabledPath, "");
                        await SendMessageAsync(message, I18nManager.S("plugin_module.disabled_marked", module.ModuleName));
                    }
                }
                else
                {
                    Log.Debug(I18nManager.S("plugin_module.disable_file_not_exist"));
                    await SendMessageAsync(message, I18nManager.S("plugin_module.disable_file_not_exist"));
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.disable_error", ex.Message));
            }
        }

        private async Task ListDisabledPluginsAsync(PlatformMessage message)
        {
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            var disabledPlugins = new List<(string Name, string Path)>();

            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        disabledPlugins.Add((fileName, file));
                    }
                }
            }
            catch
            {
            }

            if (disabledPlugins.Count == 0)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.no_disabled"));
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(I18nManager.S("plugin_module.disabled_list", disabledPlugins.Count));
            sb.AppendLine();

            int index = 1;
            foreach (var plugin in disabledPlugins)
            {
                sb.AppendLine($"{index}. {plugin.Name}");
                index++;
            }

            await SendMessageAsync(message, sb.ToString());
        }

        private async Task InstallPluginAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var fileName) || string.IsNullOrEmpty(fileName))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.install_specify_file"));
                return;
            }

            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".dll";
            }

            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            var dllPath = Path.Combine(modulesDir, fileName);

            if (!File.Exists(dllPath))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.install_file_not_found", fileName));
                return;
            }

            var assemblyName = Path.GetFileNameWithoutExtension(fileName);
            var allModules = _moduleManager.GetAllModules();
            if (allModules.Any(m => m.ModuleName == assemblyName || m.AssemblyPath?.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) == true))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.install_already_loaded", fileName));
                return;
            }

            try
            {
                var loadResult = await _moduleManager.DynamicLoadModuleAsync(dllPath);
                if (loadResult.Success)
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.install_success", fileName));
                }
                else
                {
                    var errors = string.Join("\n", loadResult.Errors);
                    await SendMessageAsync(message, I18nManager.S("plugin_module.install_failed", fileName, errors));
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.install_error", fileName, ex.Message));
            }
        }

        private async Task EnablePluginAsync(PlatformMessage message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var className) || string.IsNullOrEmpty(className))
            {
                await SendMessageAsync(message, I18nManager.S("plugin_module.specify_enable_target"));
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            
            Log.Debug(I18nManager.S("plugin_module.enable_target_class", className));
            
            var disabledPlugins = new List<(string Name, string Path)>();
            try
            {
                if (Directory.Exists(modulesDir))
                {
                    var disabledFiles = Directory.GetFiles(modulesDir, "*.dll.disabled");
                    foreach (var file in disabledFiles)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                        disabledPlugins.Add((fileName, file));
                    }
                }
            }
            catch
            {
            }

            var module = allModules.FirstOrDefault(m => m.ModuleName == className);
            if (module != null)
            {
                Log.Debug(I18nManager.S("plugin_module.enable_found_loaded", module.ModuleName, module.AssemblyPath));

                if (IsBuiltinModule(module.ModuleName))
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.builtin_no_need_enable"));
                    return;
                }

                if (string.IsNullOrEmpty(module.AssemblyPath))
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.enable_invalid_path"));
                    return;
                }

                if (module.AssemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var disabledPath = module.AssemblyPath;
                    var normalPath = module.AssemblyPath.Substring(0, module.AssemblyPath.Length - ".disabled".Length);
                    Log.Debug(I18nManager.S("plugin_module.enable_disabled_path", disabledPath));
                    Log.Debug(I18nManager.S("plugin_module.enable_target_path", normalPath));

                    try
                    {
                        if (File.Exists(disabledPath))
                        {
                            Log.Debug(I18nManager.S("plugin_module.enable_file_exists_renaming"));
                            File.Move(disabledPath, normalPath);
                            Log.Debug(I18nManager.S("plugin_module.enable_rename_success"));
                            await SendMessageAsync(message, I18nManager.S("plugin_module.enabled", module.ModuleName));
                        }
                        else
                        {
                            Log.Debug(I18nManager.S("plugin_module.enable_file_not_exist"));
                            await SendMessageAsync(message, I18nManager.S("plugin_module.enable_file_not_exist"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(I18nManager.S("plugin_module.enable_error", ex.Message));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.enable_error", ex.Message));
                    }
                }
                else
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.not_disabled", module.ModuleName));
                }
            }
            else
            {
                var disabledPlugin = disabledPlugins.FirstOrDefault(p => p.Name == className);
                if (disabledPlugin.Name == null)
                {
                    await SendMessageAsync(message, I18nManager.S("plugin_module.plugin_not_exist", className));
                    return;
                }

                Log.Debug(I18nManager.S("plugin_module.enable_found_disabled", disabledPlugin.Name, disabledPlugin.Path));

                var disabledPath = disabledPlugin.Path;
                var normalPath = disabledPath.Substring(0, disabledPath.Length - ".disabled".Length);
                Log.Debug(I18nManager.S("plugin_module.enable_disabled_path", disabledPath));
                Log.Debug(I18nManager.S("plugin_module.enable_target_path", normalPath));

                try
                {
                    if (File.Exists(disabledPath))
                    {
                        Log.Debug(I18nManager.S("plugin_module.enable_file_exists_renaming"));
                        File.Move(disabledPath, normalPath);
                        Log.Debug(I18nManager.S("plugin_module.enable_rename_success"));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.enabled", disabledPlugin.Name));
                    }
                    else
                    {
                        Log.Debug(I18nManager.S("plugin_module.enable_file_not_exist_log"));
                        await SendMessageAsync(message, I18nManager.S("plugin_module.enable_file_not_exist"));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(I18nManager.S("plugin_module.enable_error_log", ex.Message));
                    await SendMessageAsync(message, I18nManager.S("plugin_module.enable_error", ex.Message));
                }
            }
        }

        private bool IsBuiltinModule(string moduleName)
        {
            return moduleName == "HelpModule" || moduleName == "PluginModule" || moduleName == "SystemModule" || moduleName == "SetModule";
        }

        private string GetRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return I18nManager.S("plugin_module.builtin_module");

            try
            {
                var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
                var fullModulesDir = Path.GetFullPath(modulesDir);
                var fullPath = Path.GetFullPath(absolutePath);

                if (fullPath.StartsWith(fullModulesDir, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = fullPath.Substring(fullModulesDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return $"Modules/{relativePath}";
                }
            }
            catch
            {
            }

            return absolutePath;
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
            Log.Info(I18nManager.S("plugin_module.cleaning"));
            _commandRegistry?.UnregisterCommand("plugin");
            Log.Info(I18nManager.S("plugin_module.cleaned"));
            await Task.CompletedTask;
        }
    }
}
