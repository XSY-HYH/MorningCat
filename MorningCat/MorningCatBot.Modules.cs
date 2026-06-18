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
using MorningCat.I18n;
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
        private LangModule _langModule;
        
        public string GetModuleNameByAssemblyName(string assemblyName)
        {
            return _assemblyNameToModuleName.TryGetValue(assemblyName, out var moduleName) ? moduleName : null;
        }
        
        private async Task InitializeModuleManagerAsync()
        {
            try
            {
                Log.Name("Modules");
                Log.Info(_i18n.T("module.initializing"));
                
                var config = _configManager.GetConfig();
                
                string modulesDirectory = ConfigManager.GetCorrectedPath(config.ModulesDirectory);
                
                if (!Directory.Exists(modulesDirectory))
                {
                    Directory.CreateDirectory(modulesDirectory);
                    Log.Debug(_i18n.T("module.directory_created", modulesDirectory));
                }
                
                _metadataCachePath = Path.Combine(modulesDirectory, ".plugin_metadata.json");
                //插件元数据缓存有点垃圾，有待提升
                LoadCachedMetadata();
                
                ProcessDisabledPlugins(modulesDirectory);
                
                _moduleManager.Init(modulesDirectory);
                
                await LoadBuiltinModulesAsync();
                
                await LoadExternalModulesAndReportStatusAsync();
                
                Log.Info(_i18n.T("bot.started"));
                Log.Debug(_i18n.T("module.directory_path", modulesDirectory));
            }
            catch (Exception ex)
            {
                Log.Error(_i18n.T("module.init_failed", ex.Message));
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
                        Log.Debug(_i18n.T("module.plugin_disabled", Path.GetFileName(dllPath)));
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
            Log.Info(_i18n.T("module.builtin_loading"));
            
            _moduleManager.RegisterService<MessageDistributionCore>(_mdc);
            _moduleManager.RegisterService<CommandRegistry>(_commandRegistry);
            _moduleManager.RegisterService<ModuleManager>(_moduleManager);
            _moduleManager.RegisterService<ConfigManager>(_configManager);
            _moduleManager.RegisterService<PluginConfigManager>(_pluginConfigManager);
            _moduleManager.RegisterService<PluginCommandAPI>(_pluginCommandAPI);
            _moduleManager.RegisterService<PluginDatabaseAPI>(_pluginDatabaseAPI);
            _moduleManager.RegisterService<MorningCatBot>(this);
            
            _moduleManager.RegisterService("ConfigManager", _pluginConfigManager);
            _moduleManager.RegisterService("MDC", _mdc);
            _moduleManager.RegisterService("CommandRegistry", _commandRegistry);
            _moduleManager.RegisterService("ModuleManager", _moduleManager);
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

            _langModule = new LangModule();
            _langModule.SetServices(_mdc, _commandRegistry, _configManager);
            await _langModule.Init();
            
            Log.Debug(_i18n.T("module.builtin_loaded"));
        }
        
        private void UpdateBuiltinModulesMDC()
        {
            _helpModule?.UpdateMDC(_mdc);
            _pluginModule?.UpdateMDC(_mdc);
            _systemModule?.UpdateMDC(_mdc);
            _setModule?.UpdateMDC(_mdc);
            _messageRelayModule?.UpdateMDC(_mdc);
            _langModule?.UpdateMDC(_mdc);
            
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
                        Log.Debug(_i18n.T("module.mdc_updated", moduleInfo.ModuleName));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning(_i18n.T("module.mdc_update_error", ex.Message));
            }
            
            Log.Debug(_i18n.T("module.mdc_all_updated"));
        }
        
        private bool _moduleEventsSubscribed = false;
        
        private void SubscribeModuleManagerEvents()
        {
            if (_moduleEventsSubscribed) return;
            _moduleEventsSubscribed = true;
            
            _moduleManager.OnBeforeModuleLoad += (context) =>
            {
                if (!string.IsNullOrEmpty(context.AssemblyPath))
                {
                    if (!_signatureVerifier.VerifyDllByAssemblyPath(context.AssemblyPath))
                    {
                        var fileName = Path.GetFileName(context.AssemblyPath);
                        Log.Warning(_i18n.T("module.signature_failed", fileName));
                        _signatureFailedModules.Add(fileName);
                        return Task.FromResult(ModuleLoadAction.Skip);
                    }
                }
                return Task.FromResult(ModuleLoadAction.Continue);
            };
            
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
                Log.Debug(_i18n.T("module.loaded", info.ModuleName));

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
                Log.Debug(_i18n.T("module.unloaded", info.ModuleName));

                _pluginMetadata.Remove(info.ModuleName);
            };
            
            _moduleManager.ModuleFailed += (info, ex) =>
            {
                Log.Warning(_i18n.T("module.load_failed", info.ModuleName, ex.Message));
            };
        }
        
        private async Task LoadExternalModulesAndReportStatusAsync()
        {
            try
            {
                Log.Info(_i18n.T("module.external_checking"));
                
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
                
                Log.Debug(_i18n.T("module.external_loading"));
                
                var loadResult = await _moduleManager.LoadAllModulesAsync();
                
                var loadedModules = _moduleManager.GetLoadedModuleNames();
                var allModules = _moduleManager.GetAllModuleNames();
                
                var failedModules = allModules.Except(loadedModules).ToList();
                
                string statusReport = I18nManager.S("module.status_recognized");
                
                if (loadedModules.Count > 0)
                {
                    statusReport += string.Join(I18nManager.S("module.separator"), loadedModules);
                }
                else
                {
                    statusReport += I18nManager.S("module.status_no_modules");
                }
                
                if (failedModules.Count > 0)
                {
                    statusReport += I18nManager.S("module.status_load_failed", string.Join(I18nManager.S("module.separator"), failedModules));
                }
                
                Log.Info(statusReport);
                
                if (!loadResult.Success && loadResult.Errors != null)
                {
                    foreach (var error in loadResult.Errors)
                    {
                        if (error.Contains("InitException:") || error.Contains("InitFailed:"))
                        {
                            Log.Warning(_i18n.T("module.plugin_load_error", error));
                            Log.Debug(_i18n.T("module.plugin_load_error_detail", error));
                        }
                        else
                        {
                            Log.Warning(_i18n.T("module.module_load_error", error));
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
                Log.Debug(_i18n.T("module.external_check_failed", ex.Message));
            }
        }
        
        private void LogRegisteredCommands()
        {
            try
            {
                var commands = _commandRegistry.GetAllCommands();
                
                if (commands.Count == 0)
                {
                    Log.Debug(_i18n.T("command.no_commands"));
                    return;
                }
                
                Log.Info(_i18n.T("command.registered_count", commands.Count));
                
                var groupedCommands = commands.GroupBy(c => c.ModuleName);
                
                foreach (var group in groupedCommands)
                {
                    Log.Debug($"  [{group.Key}]");
                    foreach (var cmd in group)
                    {
                        var paramInfo = cmd.Parameters.Count > 0 
                            ? I18nManager.S("command.param_count", cmd.Parameters.Count)
                            : "";
                        var prefix = cmd.RequireSlash ? "/" : "";
                        Log.Debug($"    {prefix}{cmd.Name} - {cmd.Description}{paramInfo}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(_i18n.T("command.list_failed", ex.Message));
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
                Log.Error(_i18n.T("module.metadata_load_failed", ex.Message));
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
                Log.Error(_i18n.T("module.metadata_save_failed", ex.Message));
            }
        }
    }
}