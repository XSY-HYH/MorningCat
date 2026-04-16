using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;

namespace MorningCat.Modules
{
    public class PluginModule
    {
        private OneBotClient _client;
        private CommandRegistry _commandRegistry;
        private ModuleManager _moduleManager;
        private MorningCatBot _bot;

        public void SetServices(OneBotClient client, CommandRegistry commandRegistry, ModuleManager moduleManager, MorningCatBot bot)
        {
            _client = client;
            _commandRegistry = commandRegistry;
            _moduleManager = moduleManager;
            _bot = bot;
        }

        public async Task Init()
        {
            Log.Info("插件管理模块初始化中...");

            if (_client == null || _commandRegistry == null || _moduleManager == null)
            {
                Log.Error("依赖注入不完整，无法初始化插件管理模块");
                return;
            }

            RegisterPluginCommand();

            Log.Info("插件管理模块初始化完成");
            await Task.CompletedTask;
        }

        private void RegisterPluginCommand()
        {
            var subParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "action",
                    Description = "操作: list/uninstall/view/disable/enable/library",
                    IsRequired = false,
                    Type = ParameterType.String
                },
                new CommandParameter
                {
                    Name = "id",
                    Description = "插件ID或类名",
                    IsRequired = false,
                    Type = ParameterType.String
                }
            };

            var success = _commandRegistry.RegisterCommand(
                "plugin",
                "插件管理",
                "@机器人 plugin 或 @机器人 plugin list 列出所有插件\n@机器人 plugin view <ID> 查看插件详情\n@机器人 plugin uninstall <ID> 卸载插件\n@机器人 plugin disable list 列出已禁用插件\n@机器人 plugin disable <类名> 禁用插件\n@机器人 plugin enable <类名> 启用插件\n@机器人 plugin library list 列出已加载的库",
                subParams,
                HandlePluginCommand,
                "PluginModule",
                CommandPermission.BotOwner,
                CommandScope.All,
                requireAt: true
            );

            if (success)
            {
                Log.Info("plugin命令注册成功");
            }
            else
            {
                Log.Error("plugin命令注册失败");
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
                default:
                    await SendMessageAsync(message, $"未知操作: {action}\n可用操作: list, view, uninstall, disable, enable, library");
                    break;
            }
        }

        private async Task SendPluginHelpAsync(MessageObject message)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("插件管理命令帮助:");
            sb.AppendLine();
            sb.AppendLine("/plugin list - 列出所有插件");
            sb.AppendLine("/plugin view <ID> - 查看插件详情");
            sb.AppendLine("/plugin uninstall <ID> - 卸载插件");
            sb.AppendLine("/plugin disable list - 列出已禁用插件");
            sb.AppendLine("/plugin disable <类名> - 禁用插件（重启后生效）");
            sb.AppendLine("/plugin enable <类名> - 启用插件（重启后生效）");
            sb.AppendLine("/plugin library list - 列出已加载的库");
            await SendMessageAsync(message, sb.ToString());
        }

        private async Task HandleLibraryCommand(MessageObject message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var subAction) || string.IsNullOrEmpty(subAction))
            {
                await SendMessageAsync(message, "请指定库操作\n用法: /plugin library list");
                return;
            }

            subAction = subAction.ToLower();

            switch (subAction)
            {
                case "list":
                    await ListLibrariesAsync(message);
                    break;
                default:
                    await SendMessageAsync(message, $"未知库操作: {subAction}\n可用操作: list");
                    break;
            }
        }

        private async Task ListLibrariesAsync(MessageObject message)
        {
            var libraryPaths = _moduleManager.GetLoadedLibraryPaths();

            if (libraryPaths == null || libraryPaths.Count == 0)
            {
                await SendMessageAsync(message, "没有加载任何库");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"已加载库列表 (共 {libraryPaths.Count} 个):");
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

        private async Task ListPluginsAsync(MessageObject message)
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
                await SendMessageAsync(message, "没有安装任何插件");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"已安装插件列表 (共 {total} 个):");
            sb.AppendLine();

            int index = 1;
            foreach (var module in allModules)
            {
                var status = module.Status switch
                {
                    ModuleStatus.Running => "✓ 运行中",
                    ModuleStatus.Error => "✗ 加载失败",
                    ModuleStatus.Unloaded => "- 已卸载",
                    ModuleStatus.Initializing => "○ 初始化中",
                    ModuleStatus.Scanned => "○ 待初始化",
                    _ => "? 未知"
                };

                var isBuiltin = IsBuiltinModule(module.ModuleName);
                var builtinTag = isBuiltin ? " [内置]" : "";

                sb.AppendLine($"{index}. {module.ModuleName}{builtinTag}");
                sb.AppendLine($"   状态: {status}");
                //sb.AppendLine();

                index++;
            }

            foreach (var disabledName in disabledPlugins)
            {
                sb.AppendLine($"{index}. {disabledName}");
                sb.AppendLine($"   状态: ✗ 已禁用");
                //sb.AppendLine();

                index++;
            }

            //sb.AppendLine("使用 /plugin view <ID> 查看详情");
            //sb.AppendLine("使用 /plugin uninstall <ID> 卸载插件");

            await SendMessageAsync(message, sb.ToString());
        }

        private async Task ViewPluginAsync(MessageObject message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var idStr) || string.IsNullOrEmpty(idStr))
            {
                await SendMessageAsync(message, "请指定插件ID\n用法: /plugin view <ID>");
                return;
            }

            if (!int.TryParse(idStr, out int id) || id < 1)
            {
                await SendMessageAsync(message, "ID必须是正整数");
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
                await SendMessageAsync(message, $"插件ID {id} 不存在");
                return;
            }

            if (id <= allModules.Count)
            {
                var module = allModules[id - 1];

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"插件详情:");
                sb.AppendLine();
                sb.AppendLine($"名称: {module.ModuleName}");
                sb.AppendLine($"状态: {module.Status}");
                sb.AppendLine($"类型: {module.ModuleType?.FullName ?? "未知"}");
                
                var displayPath = GetRelativePath(module.AssemblyPath);
                sb.AppendLine($"程序集: {displayPath}");

                if (module.ModuleInstance != null)
                {
                    sb.AppendLine($"实例: 已创建");
                }

                var deps = _moduleManager.GetModuleDependencies(module.ModuleName);
                if (deps != null && deps.Count > 0)
                {
                    sb.AppendLine($"依赖: {string.Join(", ", deps)}");
                }

                var dependents = _moduleManager.GetModulesDependentOn(module.ModuleName);
                if (dependents != null && dependents.Count > 0)
                {
                    sb.AppendLine($"被依赖: {string.Join(", ", dependents)}");
                }

                if (_bot != null)
                {
                    var metadata = _bot.GetPluginMetadata(module.ModuleName);
                    if (metadata != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("--- 元数据 ---");
                        if (!string.IsNullOrEmpty(metadata.DisplayName))
                            sb.AppendLine($"显示名称: {metadata.DisplayName}");
                        if (!string.IsNullOrEmpty(metadata.Author))
                            sb.AppendLine($"作者: {metadata.Author}");
                        if (!string.IsNullOrEmpty(metadata.Website))
                            sb.AppendLine($"网站: {metadata.Website}");
                        if (!string.IsNullOrEmpty(metadata.Description))
                            sb.AppendLine($"描述: {metadata.Description}");
                    }
                }

                await SendMessageAsync(message, sb.ToString());
            }
            else
            {
                var disabledIndex = id - allModules.Count - 1;
                var disabledPlugin = disabledPlugins[disabledIndex];

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"插件详情:");
                sb.AppendLine();
                sb.AppendLine($"名称: {disabledPlugin.Name}");
                sb.AppendLine($"状态: 已禁用");
                
                var displayPath = GetRelativePath(disabledPlugin.Path);
                sb.AppendLine($"程序集: {displayPath}");

                await SendMessageAsync(message, sb.ToString());
            }
        }

        private async Task UninstallPluginAsync(MessageObject message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var idStr) || string.IsNullOrEmpty(idStr))
            {
                await SendMessageAsync(message, "请指定插件ID\n用法: /plugin uninstall <ID>");
                return;
            }

            if (!int.TryParse(idStr, out int id) || id < 1)
            {
                await SendMessageAsync(message, "ID必须是正整数");
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            
            Log.Debug($"[Uninstall] 目标ID: {id}, 已加载模块数: {allModules.Count}");
            
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

            Log.Debug($"[Uninstall] 禁用插件数: {disabledPlugins.Count}");
            var total = allModules.Count + disabledPlugins.Count;

            if (id > total)
            {
                await SendMessageAsync(message, $"插件ID {id} 不存在");
                return;
            }

            if (id <= allModules.Count)
            {
                var module = allModules[id - 1];
                Log.Debug($"[Uninstall] 目标插件: {module.ModuleName}, 状态: {module.Status}, 路径: {module.AssemblyPath}");

                if (IsBuiltinModule(module.ModuleName))
                {
                    await SendMessageAsync(message, "无法卸载内置模块");
                    return;
                }

                var dependents = _moduleManager.GetModulesDependentOn(module.ModuleName);
                if (dependents != null && dependents.Count > 0)
                {
                    await SendMessageAsync(message, $"无法卸载: 以下插件依赖此模块:\n{string.Join("\n", dependents)}");
                    return;
                }

                try
                {
                    Log.Debug($"[Uninstall] 尝试卸载模块...");
                    var success = await _moduleManager.UnloadModuleAsync(module.ModuleName);
                    Log.Debug($"[Uninstall] 卸载结果: {success}");

                    if (success)
                    {
                        _commandRegistry.UnregisterModuleCommands(module.ModuleName);
                        Log.Debug($"[Uninstall] 已注销命令");
                        await SendMessageAsync(message, $"插件 {module.ModuleName} 已卸载");
                    }
                    else
                    {
                        await SendMessageAsync(message, $"卸载插件 {module.ModuleName} 失败");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"[Uninstall] 错误: {ex.Message}");
                    await SendMessageAsync(message, $"卸载插件时发生错误:\n{ex.Message}");
                }
            }
            else
            {
                var disabledIndex = id - allModules.Count - 1;
                var disabledPlugin = disabledPlugins[disabledIndex];
                Log.Debug($"[Uninstall] 目标是禁用列表中的插件: {disabledPlugin.Name}, 路径: {disabledPlugin.Path}");

                try
                {
                    if (File.Exists(disabledPlugin.Path))
                    {
                        Log.Debug($"[Uninstall] 文件存在，尝试删除...");
                        File.Delete(disabledPlugin.Path);
                        Log.Debug($"[Uninstall] 删除成功");
                        await SendMessageAsync(message, $"插件 {disabledPlugin.Name} 已删除");
                    }
                    else
                    {
                        Log.Debug($"[Uninstall] 文件不存在");
                        await SendMessageAsync(message, $"插件文件不存在");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"[Uninstall] 错误: {ex.Message}");
                    await SendMessageAsync(message, $"删除插件时发生错误:\n{ex.Message}");
                }
            }
        }

        private async Task DisablePluginAsync(MessageObject message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var arg) || string.IsNullOrEmpty(arg))
            {
                await SendMessageAsync(message, "请指定插件名或使用 list 查看已禁用列表\n用法: /plugin disable <类名>\n用法: /plugin disable list");
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
                await SendMessageAsync(message, $"插件 {className} 不存在");
                return;
            }

            Log.Debug($"[Disable] 目标插件: {module.ModuleName}, 状态: {module.Status}, 路径: {module.AssemblyPath}");

            if (IsBuiltinModule(module.ModuleName))
            {
                await SendMessageAsync(message, "无法禁用内置模块");
                return;
            }

            if (string.IsNullOrEmpty(module.AssemblyPath))
            {
                await SendMessageAsync(message, "无法禁用: 插件路径无效");
                return;
            }

            if (module.AssemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                await SendMessageAsync(message, $"插件 {module.ModuleName} 已经被禁用");
                return;
            }

            try
            {
                if (module.Status == ModuleStatus.Running)
                {
                    Log.Debug($"[Disable] 插件正在运行，尝试卸载...");
                    var success = await _moduleManager.UnloadModuleAsync(module.ModuleName);
                    Log.Debug($"[Disable] 卸载结果: {success}");
                    if (success)
                    {
                        _commandRegistry.UnregisterModuleCommands(module.ModuleName);
                        Log.Debug($"[Disable] 已注销命令");
                    }
                }

                var disabledPath = module.AssemblyPath + ".disabled";
                Log.Debug($"[Disable] 检查文件: {module.AssemblyPath}");
                if (File.Exists(module.AssemblyPath))
                {
                    Log.Debug($"[Disable] 文件存在，尝试重命名为: {disabledPath}");
                    try
                    {
                        File.Move(module.AssemblyPath, disabledPath);
                        Log.Debug($"[Disable] 重命名成功");
                        await SendMessageAsync(message, $"插件 {module.ModuleName} 已禁用\n重启后将不会加载此插件");
                    }
                    catch (IOException ex)
                    {
                        Log.Debug($"[Disable] 文件被占用: {ex.Message}，创建标记文件");
                        File.WriteAllText(disabledPath, "");
                        await SendMessageAsync(message, $"插件 {module.ModuleName} 已标记为禁用\n文件被占用，将在重启后完成禁用");
                    }
                }
                else
                {
                    Log.Debug($"[Disable] 文件不存在");
                    await SendMessageAsync(message, $"插件文件不存在，无法禁用");
                }
            }
            catch (Exception ex)
            {
                await SendMessageAsync(message, $"禁用插件时发生错误:\n{ex.Message}");
            }
        }

        private async Task ListDisabledPluginsAsync(MessageObject message)
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
                await SendMessageAsync(message, "没有已禁用的插件");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"已禁用插件列表 (共 {disabledPlugins.Count} 个):");
            sb.AppendLine();

            int index = 1;
            foreach (var plugin in disabledPlugins)
            {
                sb.AppendLine($"{index}. {plugin.Name}");
                index++;
            }

            await SendMessageAsync(message, sb.ToString());
        }

        private async Task EnablePluginAsync(MessageObject message, Dictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("id", out var className) || string.IsNullOrEmpty(className))
            {
                await SendMessageAsync(message, "请指定插件类名\n用法: /plugin enable <类名>");
                return;
            }

            var allModules = _moduleManager.GetAllModules();
            var modulesDir = Config.ConfigManager.GetCorrectedPath("Modules");
            
            Log.Debug($"[Enable] 目标类名: {className}");
            
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
                Log.Debug($"[Enable] 找到已加载的插件: {module.ModuleName}, 路径: {module.AssemblyPath}");

                if (IsBuiltinModule(module.ModuleName))
                {
                    await SendMessageAsync(message, "内置模块无需启用");
                    return;
                }

                if (string.IsNullOrEmpty(module.AssemblyPath))
                {
                    await SendMessageAsync(message, "无法启用: 插件路径无效");
                    return;
                }

                if (module.AssemblyPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var disabledPath = module.AssemblyPath;
                    var normalPath = module.AssemblyPath.Substring(0, module.AssemblyPath.Length - ".disabled".Length);
                    Log.Debug($"[Enable] 禁用路径: {disabledPath}");
                    Log.Debug($"[Enable] 目标路径: {normalPath}");

                    try
                    {
                        if (File.Exists(disabledPath))
                        {
                            Log.Debug($"[Enable] 文件存在，尝试重命名...");
                            File.Move(disabledPath, normalPath);
                            Log.Debug($"[Enable] 重命名成功");
                            await SendMessageAsync(message, $"插件 {module.ModuleName} 已启用\n使用 /restart 重启后生效");
                        }
                        else
                        {
                            Log.Debug($"[Enable] 文件不存在");
                            await SendMessageAsync(message, $"插件文件不存在，无法启用");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"[Enable] 错误: {ex.Message}");
                        await SendMessageAsync(message, $"启用插件时发生错误:\n{ex.Message}");
                    }
                }
                else
                {
                    await SendMessageAsync(message, $"插件 {module.ModuleName} 未被禁用");
                }
            }
            else
            {
                var disabledPlugin = disabledPlugins.FirstOrDefault(p => p.Name == className);
                if (disabledPlugin.Name == null)
                {
                    await SendMessageAsync(message, $"插件 {className} 不存在");
                    return;
                }

                Log.Debug($"[Enable] 找到禁用列表中的插件: {disabledPlugin.Name}, 路径: {disabledPlugin.Path}");

                var disabledPath = disabledPlugin.Path;
                var normalPath = disabledPath.Substring(0, disabledPath.Length - ".disabled".Length);
                Log.Debug($"[Enable] 禁用路径: {disabledPath}");
                Log.Debug($"[Enable] 目标路径: {normalPath}");

                try
                {
                    if (File.Exists(disabledPath))
                    {
                        Log.Debug($"[Enable] 文件存在，尝试重命名...");
                        File.Move(disabledPath, normalPath);
                        Log.Debug($"[Enable] 重命名成功");
                        await SendMessageAsync(message, $"插件 {disabledPlugin.Name} 已启用\n使用 /restart 重启后生效");
                    }
                    else
                    {
                        Log.Debug($"[Enable] 文件不存在");
                        await SendMessageAsync(message, $"插件文件不存在，无法启用");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"[Enable] 错误: {ex.Message}");
                    await SendMessageAsync(message, $"启用插件时发生错误:\n{ex.Message}");
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
                return "内置模块";

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

        public IEnumerable<string> GetDependencies()
        {
            return Array.Empty<string>();
        }

        public async Task Exit()
        {
            Log.Info("插件管理模块正在清理...");
            _commandRegistry?.UnregisterCommand("plugin");
            Log.Info("插件管理模块清理完成");
            await Task.CompletedTask;
        }
    }
}
