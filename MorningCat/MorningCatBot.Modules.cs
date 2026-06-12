using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Config;
using MorningCat.Commands;
using MorningCat.Modules;
using MorningCat.PluginAPI;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat
{
    public partial class MorningCatBot
    {
        private string _metadataCachePath;
        private Dictionary<string, string> _assemblyNameToModuleName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        private HelpModule _helpModule;
        private PluginModule _pluginModule;
        private SystemModule _systemModule;
        private SetModule _setModule;
        private MessageRelayModule _messageRelayModule;
        
        public string GetModuleNameByAssemblyName(string assemblyName)
        {
            return _assemblyNameToModuleName.TryGetValue(assemblyName, out var moduleName) ? moduleName : null;
        }
        
        private async Task InitializeModuleManagerAsync()
        {
            try
            {
                Log.Name("Modules");
                Log.Info("猫猫正在殴打模块管理器...");
                
                var config = _configManager.GetConfig();
                
                string modulesDirectory = ConfigManager.GetCorrectedPath(config.ModulesDirectory);
                
                if (!Directory.Exists(modulesDirectory))
                {
                    Directory.CreateDirectory(modulesDirectory);
                    Log.Debug($"猫猫创建了模块目录: {modulesDirectory}");
                }
                
                _metadataCachePath = Path.Combine(modulesDirectory, ".plugin_metadata.json");
                //插件元数据缓存有点垃圾，有待提升
                LoadCachedMetadata();
                
                ProcessDisabledPlugins(modulesDirectory);
                
                _moduleManager.Init(modulesDirectory);
                
                VerifyPluginSignatures(modulesDirectory);
                
                await LoadBuiltinModulesAsync();
                
                await LoadExternalModulesAndReportStatusAsync();
                
                Log.Info("猫猫已制服模块管理器喵");
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
        
        private void VerifyPluginSignatures(string modulesDirectory)
        {
            try
            {
                if (!Directory.Exists(modulesDirectory))
                    return;
                
                var dllFiles = Directory.GetFiles(modulesDirectory, "*.dll");
                var libraryDir = Path.Combine(modulesDirectory, "Library");
                
                foreach (var dllPath in dllFiles)
                {
                    if (!string.IsNullOrEmpty(libraryDir) && dllPath.StartsWith(libraryDir, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    if (dllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    var fileName = Path.GetFileName(dllPath);
                    
                    if (!_signatureVerifier.VerifyDll(dllPath))
                    {
                        Log.Warning($"插件 {fileName} 签名验证失败，已禁用");
                        _signatureFailedModules.Add(fileName);
                        try
                        {
                            var disabledPath = dllPath + ".disabled";
                            if (File.Exists(disabledPath))
                                File.Delete(disabledPath);
                            File.Move(dllPath, disabledPath);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"禁用未签名插件失败: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"插件签名验证过程出错了喵: {ex.Message}");
            }
        }
        
        private async Task LoadBuiltinModulesAsync()
        {
            Log.Info("猫猫正在加载内置模块...");
            
            _moduleManager.RegisterService<MessageDistributionCore>(_mdc);
            _moduleManager.RegisterService<CommandRegistry>(_commandRegistry);
            _moduleManager.RegisterService<ModuleManager>(_moduleManager);
            _moduleManager.RegisterService<PluginApiService>(_moduleManager.PluginApi);
            _moduleManager.RegisterService<ConfigManager>(_configManager);
            _moduleManager.RegisterService<PluginConfigManager>(_pluginConfigManager);
            _moduleManager.RegisterService<PluginCommandAPI>(_pluginCommandAPI);
            _moduleManager.RegisterService<PluginDatabaseAPI>(_pluginDatabaseAPI);
            _moduleManager.RegisterService<MorningCatBot>(this);
            
            _moduleManager.RegisterService("ConfigManager", _pluginConfigManager);
            _moduleManager.RegisterService("MDC", _mdc);
            _moduleManager.RegisterService("CommandRegistry", _commandRegistry);
            _moduleManager.RegisterService("ModuleManager", _moduleManager);
            _moduleManager.RegisterService("PluginApiService", _moduleManager.PluginApi);
            _moduleManager.RegisterService("PluginCommandAPI", _pluginCommandAPI);
            _moduleManager.RegisterService("PluginDatabaseAPI", _pluginDatabaseAPI);
            _moduleManager.RegisterService("MorningCatBot", this);
            
            _helpModule = new HelpModule();
            _helpModule.SetServices(_mdc, _pluginConfigManager, _commandRegistry);
            await _helpModule.Init();
            
            _pluginModule = new PluginModule();
            _pluginModule.SetServices(_mdc, _commandRegistry, _moduleManager, this);
            await _pluginModule.Init();
            
            _systemModule = new SystemModule();
            _systemModule.SetServices(_mdc, _commandRegistry, RequestExit, RestartAsync);
            await _systemModule.Init();
            
            _setModule = new SetModule();
            _setModule.SetServices(_mdc, _commandRegistry, _configManager, _pluginConfigManager, _moduleManager);
            await _setModule.Init();
            
            _messageRelayModule = new MessageRelayModule();
            _messageRelayModule.SetMDC(_mdc);
            await _messageRelayModule.Init();
            
            Log.Debug("内置模块加载完成");
        }
        
        private void UpdateBuiltinModulesMDC()
        {
            _helpModule?.UpdateMDC(_mdc);
            _pluginModule?.UpdateMDC(_mdc);
            _systemModule?.UpdateMDC(_mdc);
            _setModule?.UpdateMDC(_mdc);
            _messageRelayModule?.UpdateMDC(_mdc);
            
            try
            {
                var loadedModules = _moduleManager.GetLoadedModules();
                foreach (var moduleInfo in loadedModules)
                {
                    if (moduleInfo.ModuleInstance == null) continue;
                    
                    var mdcProp = moduleInfo.ModuleInstance.GetType().GetProperty("MDC");
                    if (mdcProp != null && mdcProp.PropertyType == typeof(MessageDistributionCore) && mdcProp.CanWrite)
                    {
                        mdcProp.SetValue(moduleInfo.ModuleInstance, _mdc);
                        Log.Debug($"已更新插件 {moduleInfo.ModuleName} 的MDC引用");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"更新插件MDC引用时出错: {ex.Message}");
            }
            
            Log.Debug("已更新所有模块的MDC引用");
        }
        
        private bool _moduleEventsSubscribed = false;
        
        private void SubscribeModuleManagerEvents()
        {
            if (_moduleEventsSubscribed) return;
            _moduleEventsSubscribed = true;
            
            _moduleManager.OnProgressUpdated += (progress) =>
            {
                Log.Debug($"[{progress.Status}] {progress.Completed}/{progress.Total} - {progress.CurrentModule}");
                
                if (progress.Status == "Initializing" && !string.IsNullOrEmpty(progress.CurrentModule))
                {
                    _pluginConfigManager.SetCurrentModule(progress.CurrentModule);
                }
                else if (progress.Status == "Done" || progress.Status == "Unloaded")
                {
                    _pluginConfigManager.ClearCurrentModule();
                }
            };
            
            _moduleManager.ModuleLoaded += (info) =>
            {
                Log.Debug($"{info.ModuleName}模块已加载");

                if (!string.IsNullOrEmpty(info.AssemblyPath))
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(info.AssemblyPath);
                    _assemblyNameToModuleName[assemblyName] = info.ModuleName;
                }
                if (info.Declaration != null)
                {
                    if (!_pluginMetadata.ContainsKey(info.ModuleName))
                    {
                        _pluginMetadata[info.ModuleName] = new PluginMetadata
                        {
                            ModuleName = info.ModuleName,
                            PluginDependencies = info.Declaration.PluginDependencies ?? new List<string>(),
                            LibraryDependencies = info.Declaration.LibraryDependencies ?? new List<string>()
                        };
                    }
                    else
                    {
                        _pluginMetadata[info.ModuleName].PluginDependencies = info.Declaration.PluginDependencies ?? new List<string>();
                        _pluginMetadata[info.ModuleName].LibraryDependencies = info.Declaration.LibraryDependencies ?? new List<string>();
                    }
                }
            };
            
            _moduleManager.ModuleUnloaded += (info) =>
            {
                Log.Debug($"{info.ModuleName}模块已卸载");

                _pluginMetadata.Remove(info.ModuleName);
            };
            
            _moduleManager.ModuleFailed += (info, ex) =>
            {
                Log.Warning($"{info.ModuleName} - {ex.Message}模块失败");
            };
        }
        
        private async Task LoadExternalModulesAndReportStatusAsync()
        {
            try
            {
                Log.Info("猫猫正在检查外部模块...");
                
                SubscribeModuleManagerEvents();
                
                _moduleManager.RegisterDeclarationProvider(async (type, instance) =>
                {
                    var pluginDeps = new List<string>();
                    var libDeps = new List<string>();
                    
                    var getDeps = type.GetMethod("GetDependencies", Type.EmptyTypes);
                    if (getDeps != null)
                    {
                        var result = getDeps.Invoke(instance, null) as IEnumerable<string>;
                        if (result != null)
                            pluginDeps = result.ToList();
                    }
                    
                    var getLibDeps = type.GetMethod("GetLibraryDependencies", Type.EmptyTypes);
                    if (getLibDeps != null)
                    {
                        var result = getLibDeps.Invoke(instance, null) as IEnumerable<string>;
                        if (result != null)
                            libDeps = result.ToList();
                    }
                    
                    var metadataAttr = type.GetCustomAttribute<PluginMetadataAttribute>();
                    if (metadataAttr != null || !_pluginMetadata.ContainsKey(type.Name))
                    {
                        var metadata = new PluginMetadata
                        {
                            ModuleName = type.Name,
                            DisplayName = metadataAttr?.DisplayName ?? type.Name,
                            Author = metadataAttr?.Author ?? "",
                            Website = metadataAttr?.Website ?? "",
                            Description = metadataAttr?.Description ?? "",
                            IconBase64 = metadataAttr?.IconBase64 ?? "",
                            Tags = metadataAttr?.Tags?.ToList() ?? new List<string>(),
                            PluginDependencies = pluginDeps,
                            LibraryDependencies = libDeps
                        };
                        _pluginMetadata[type.Name] = metadata;
                    }
                    
                    return new ModuleDeclaration
                    {
                        PluginDependencies = pluginDeps,
                        LibraryDependencies = libDeps,
                        AllowDynamicLoad = true
                    };
                });
                
                Log.Debug("开始加载外部模块...");
                
                var loadResult = await _moduleManager.LoadAllModulesAsync();
                
                var loadedModules = _moduleManager.GetLoadedModuleNames();
                var allModules = _moduleManager.GetAllModuleNames();
                
                var failedModules = allModules.Except(loadedModules).ToList();
                
                string statusReport = "已识别";
                
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
                    statusReport += $"加载失败:{string.Join("，", failedModules)}";
                }
                
                Log.Info(statusReport);
                
                if (!loadResult.Success && loadResult.Errors != null)
                {
                    foreach (var error in loadResult.Errors)
                    {
                        if (error.Contains("InitException:") || error.Contains("InitFailed:"))
                        {
                            Log.Warning($"插件加载错误QAQ: {error}");
                            Log.Debug($"插件加载错误: {error}");
                        }
                        else
                        {
                            Log.Warning($"模块加载错误QAQ: {error}");
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
                Log.Debug($"检查外部模块状态失败: {ex.Message}");
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
                
                Log.Info($"已注册 {commands.Count} 个命令");
                
                var groupedCommands = commands.GroupBy(c => c.ModuleName);
                
                foreach (var group in groupedCommands)
                {
                    Log.Debug($"  [{group.Key}]");
                    foreach (var cmd in group)
                    {
                        var paramInfo = cmd.Parameters.Count > 0 
                            ? $" ({cmd.Parameters.Count}个参数)" 
                            : "";
                        var prefix = cmd.RequireSlash ? "/" : "";
                        Log.Debug($"    {prefix}{cmd.Name} - {cmd.Description}{paramInfo}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"显示命令列表失败喵:{ex.Message}");
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
                        //Log.Debug($"从缓存加载了 {cached.Count} 个插件元数据");
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
                        //Log.Debug($"从缓存加载了 {mapping.Count} 个程序集映射");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"加载元数据缓存失败: {ex.Message}");
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
                Log.Error($"保存元数据缓存失败喵:{ex.Message}");
            }
        }
    }
}