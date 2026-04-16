using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Logging;
using OneBotLib;
using OneBotLib.Events;
using ModuleManagerLib;
using MorningCat.Config;
using MorningCat.Commands;
using MorningCat.Modules;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private string _metadataCachePath;
        private Dictionary<string, string> _assemblyNameToModuleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        public string GetModuleNameByAssemblyName(string assemblyName)
        {
            return _assemblyNameToModuleName.TryGetValue(assemblyName, out var moduleName) ? moduleName : null;
        }
        
        private async Task InitializeModuleManagerAsync()
        {
            try
            {
                Log.Info("猫猫正在殴打模块管理器...");
                
                var config = _configManager.GetConfig();
                
                string modulesDirectory = ConfigManager.GetCorrectedPath(config.ModulesDirectory);
                
                if (!Directory.Exists(modulesDirectory))
                {
                    Directory.CreateDirectory(modulesDirectory);
                    Log.Info($"猫猫创建了模块目录: {modulesDirectory}");
                }
                
                _metadataCachePath = Path.Combine(modulesDirectory, ".plugin_metadata.json");
                LoadCachedMetadata();
                
                ProcessDisabledPlugins(modulesDirectory);
                
                _moduleManager.Init(modulesDirectory);
                
                await LoadBuiltinModulesAsync();
                
                await LoadExternalModulesAndReportStatusAsync();
                
                Log.Info("猫猫已制服模块管理器喵AWA");
                Log.Debug($"模块目录路径: {modulesDirectory}");
            }
            catch (Exception ex)
            {
                Log.Error($"猫猫被模块管理器反制了QAQ: {ex.Message}");
                throw;
            }
        }
        
        private void ProcessDisabledPlugins(string modulesDirectory)
        {
            try
            {
                var disabledMarkers = Directory.GetFiles(modulesDirectory, "*.dll.disabled");
                foreach (var marker in disabledMarkers)
                {
                    var fileInfo = new FileInfo(marker);
                    if (fileInfo.Length == 0)
                    {
                        var dllPath = marker.Substring(0, marker.Length - ".disabled".Length);
                        if (File.Exists(dllPath))
                        {
                            try
                            {
                                File.Delete(marker);
                                File.Move(dllPath, marker);
                                Log.Debug($"已禁用插件: {Path.GetFileName(dllPath)}");
                            }
                            catch
                            {
                            }
                        }
                        else
                        {
                            File.Delete(marker);
                        }
                    }
                }
            }
            catch
            {
            }
        }
        
        private async Task LoadBuiltinModulesAsync()
        {
            Log.Info("猫猫正在加载内置模块...");
            
            _moduleManager.RegisterService<IPluginConfigManager>(_pluginConfigManager);
            _moduleManager.RegisterService<PluginConfigManager>(_pluginConfigManager);
            _moduleManager.RegisterService<OneBotClient>(_client);
            _moduleManager.RegisterService<CommandRegistry>(_commandRegistry);
            _moduleManager.RegisterService<ModuleManager>(_moduleManager);
            _moduleManager.RegisterService<ConfigManager>(_configManager);
            
            _moduleManager.RegisterService("ConfigManager", _pluginConfigManager);
            _moduleManager.RegisterService("Client", _client);
            _moduleManager.RegisterService("CommandRegistry", _commandRegistry);
            _moduleManager.RegisterService("ModuleManager", _moduleManager);
            
            var helpModule = new HelpModule();
            helpModule.SetServices(_client, _pluginConfigManager, _commandRegistry);
            await helpModule.Init();
            
            var pluginModule = new PluginModule();
            pluginModule.SetServices(_client, _commandRegistry, _moduleManager, this);
            await pluginModule.Init();
            
            var systemModule = new SystemModule();
            systemModule.SetServices(_client, _commandRegistry, RequestExit, RestartAsync);
            await systemModule.Init();
            
            var setModule = new SetModule();
            setModule.SetServices(_client, _commandRegistry, _configManager, _pluginConfigManager, _moduleManager);
            await setModule.Init();
            
            Log.Info("内置模块加载完成");
        }
        
        private async Task LoadExternalModulesAndReportStatusAsync()
        {
            try
            {
                Log.Info("猫猫正在检查外部模块...");
                
                _moduleManager.OnProgressUpdated += (progress) =>
                {
                    Log.Debug($"[{progress.Status}] {progress.Completed}/{progress.Total} - {progress.CurrentModule}");
                };
                
                _moduleManager.OnPluginMetadata += (metadata) =>
                {
                    _pluginMetadata[metadata.ModuleName] = metadata;
                    Log.Debug($"收到插件元数据: {metadata.DisplayName} ({metadata.ModuleName})");
                    SaveMetadataCache();
                };
                
                Log.Debug("开始加载外部模块...");
                
                var loadResult = await _moduleManager.LoadAllModulesAsync();
                
                var loadedModules = _moduleManager.GetLoadedModuleNames();
                var allModules = _moduleManager.GetAllModuleNames();
                
                var failedModules = allModules.Except(loadedModules).ToList();
                
                string statusReport = "已识别的模块：";
                
                if (loadedModules.Count > 0)
                {
                    statusReport += string.Join("，", loadedModules);
                }
                else
                {
                    statusReport += "棍母";
                }
                
                if (failedModules.Count > 0)
                {
                    statusReport += $"，加载失败的模块：{string.Join("，", failedModules)}";
                }
                
                Log.Info(statusReport);
                
                if (!loadResult.Success && loadResult.Errors != null)
                {
                    foreach (var error in loadResult.Errors)
                    {
                        if (error.Contains("InitException:") || error.Contains("InitFailed:"))
                        {
                            Log.Warning($"插件加载错误: {error}");
                            Log.Debug($"插件加载错误详情: {error}");
                        }
                        else
                        {
                            Log.Warning($"模块加载错误: {error}");
                        }
                    }
                }
                
                foreach (var module in _moduleManager.GetAllModules())
                {
                    if (!string.IsNullOrEmpty(module.AssemblyPath))
                    {
                        var assemblyName = Path.GetFileNameWithoutExtension(module.AssemblyPath);
                        _assemblyNameToModuleName[assemblyName] = module.ModuleName;
                    }
                }
                
                SaveMetadataCache();
                
                LogRegisteredCommands();
            }
            catch (Exception ex)
            {
                Log.Error($"检查外部模块状态失败: {ex.Message}");
            }
        }
        
        private void LogRegisteredCommands()
        {
            try
            {
                var commands = _commandRegistry.GetAllCommands();
                
                if (commands.Count == 0)
                {
                    Log.Debug("当前没有注册任何命令");
                    return;
                }
                
                Log.Debug($"已注册 {commands.Count} 个命令:");
                
                var groupedCommands = commands.GroupBy(c => c.ModuleName);
                
                foreach (var group in groupedCommands)
                {
                    Log.Debug($"  [{group.Key}]");
                    foreach (var cmd in group)
                    {
                        var paramInfo = cmd.Parameters.Count > 0 
                            ? $" ({cmd.Parameters.Count}个参数)" 
                            : "";
                        Log.Debug($"    /{cmd.Name} - {cmd.Description}{paramInfo}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"显示命令列表失败: {ex.Message}");
            }
        }
        
        private void LoadCachedMetadata()
        {
            try
            {
                if (string.IsNullOrEmpty(_metadataCachePath) || !File.Exists(_metadataCachePath))
                    return;
                
                var json = File.ReadAllText(_metadataCachePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("metadata", out var metadataElement))
                {
                    var cached = JsonSerializer.Deserialize<Dictionary<string, PluginMetadata>>(metadataElement.GetRawText());
                    if (cached != null)
                    {
                        foreach (var kvp in cached)
                        {
                            if (!_pluginMetadata.ContainsKey(kvp.Key))
                            {
                                _pluginMetadata[kvp.Key] = kvp.Value;
                            }
                        }
                        Log.Debug($"从缓存加载了 {cached.Count} 个插件元数据");
                    }
                }
                
                if (root.TryGetProperty("assemblyMapping", out var mappingElement))
                {
                    var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingElement.GetRawText());
                    if (mapping != null)
                    {
                        foreach (var kvp in mapping)
                        {
                            _assemblyNameToModuleName[kvp.Key] = kvp.Value;
                        }
                        Log.Debug($"从缓存加载了 {mapping.Count} 个程序集映射");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"加载元数据缓存失败: {ex.Message}");
            }
        }
        
        private void SaveMetadataCache()
        {
            try
            {
                if (string.IsNullOrEmpty(_metadataCachePath))
                    return;
                
                var cache = new
                {
                    metadata = _pluginMetadata,
                    assemblyMapping = _assemblyNameToModuleName
                };
                
                var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_metadataCachePath, json);
            }
            catch (Exception ex)
            {
                Log.Debug($"保存元数据缓存失败: {ex.Message}");
            }
        }
    }
}