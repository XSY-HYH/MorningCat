using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Logging;
using MorningCat.I18n;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;


namespace MorningCat.Config
{
    public interface IPluginConfigManager
    {
        Task<T> GetConfigAsync<T>(string pluginName, string configName, T defaultValue = default(T)) where T : class, new();
        Task SetConfigAsync<T>(string pluginName, string configName, T config) where T : class;
        Task<T> GetValueAsync<T>(string pluginName, string configName, string keyPath, T defaultValue = default(T));
        Task SetValueAsync<T>(string pluginName, string configName, string keyPath, T value);
        Task<bool> ConfigExistsAsync(string pluginName, string configName);
        Task DeleteConfigAsync(string pluginName, string configName);
        List<RegisteredConfigInfo> GetRegisteredConfigs(string pluginName);
        string ResolvePluginName(string name);
        void SetCurrentModule(string moduleName);
        void ClearCurrentModule();
    }

    public class RegisteredConfigInfo
    {
        public string ConfigName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
    }

    public class PluginConfigManager : IPluginConfigManager
    {
        private readonly string _configDirectory;
        private readonly IDeserializer _deserializer;
        private readonly ISerializer _serializer;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _registeredConfigs = new();
        private readonly ConcurrentDictionary<string, string> _moduleNameToPluginName = new();
        
        private string? _currentModuleName;

        public PluginConfigManager()
        {
            Log.Name("PluginConfig");
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = string.IsNullOrEmpty(location)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(location);
            _configDirectory = Path.Combine(exeDir, "Config");
            
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
                Log.Info(I18nManager.S("config.dir_created", _configDirectory));
            }

            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            _serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
        }

        private string GetConfigFilePath(string pluginName, string configName)
        {
            string fileName = $"{pluginName}-{configName}.yml";
            return Path.Combine(_configDirectory, fileName);
        }
        
        private void SetNestedValue<T>(Dictionary<string, object> dict, string keyPath, T value)
        {
            string[] keys = keyPath.Split('.');
            object current = dict;
            
            for (int i = 0; i < keys.Length - 1; i++)
            {
                string key = keys[i];
                if (current is Dictionary<string, object> currentDict)
                {
                    if (!currentDict.ContainsKey(key))
                    {
                        currentDict[key] = new Dictionary<string, object>();
                    }
                    current = currentDict[key];
                }
            }
            
            if (current is Dictionary<string, object> finalDict)
            {
                finalDict[keys[keys.Length - 1]] = value;
            }
        }

        public async Task<T> GetConfigAsync<T>(string pluginName, string configName, T defaultValue = default(T)) where T : class, new()
        {
            string filePath = GetConfigFilePath(pluginName, configName);
            try
            {
                RegisterConfig(pluginName, configName);
                
                if (!File.Exists(filePath))
                {
                    Log.Debug(I18nManager.S("config.file_not_found_default", filePath));
                    
                    if (defaultValue != null)
                    {
                        try
                        {
                            string defaultYaml = GenerateYamlWithComments(pluginName, configName, defaultValue);
                            await File.WriteAllTextAsync(filePath, defaultYaml);
                            Log.Info(I18nManager.S("config.default_created", filePath));
                        }
                        catch (Exception writeEx)
                        {
                            Log.Warning(I18nManager.S("config.default_create_failed", writeEx.Message));
                        }
                    }
                    
                    return defaultValue ?? new T();
                }

                string yaml = await File.ReadAllTextAsync(filePath);
                var config = _deserializer.Deserialize<T>(yaml);
                
                Log.Debug(I18nManager.S("config.load_success", filePath));
                return config ?? defaultValue ?? new T();
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.load_failed", ex.Message));

                if (defaultValue != null)
                {
                    try
                    {
                        string defaultYaml = GenerateYamlWithComments(pluginName, configName, defaultValue);
                        await File.WriteAllTextAsync(filePath, defaultYaml);
                        Log.Info(I18nManager.S("config.corrupted_overwritten", filePath));
                    }
                    catch (Exception writeEx)
                    {
                        Log.Warning(I18nManager.S("config.corrupted_overwrite_failed", writeEx.Message));
                    }
                }

                return defaultValue ?? new T();
            }
        }

        public async Task SetConfigAsync<T>(string pluginName, string configName, T config) where T : class
        {
            try
            {
                RegisterConfig(pluginName, configName);
                
                string filePath = GetConfigFilePath(pluginName, configName);
                string yaml = GenerateYamlWithComments(pluginName, configName, config);
                
                await File.WriteAllTextAsync(filePath, yaml);
                Log.Debug(I18nManager.S("config.save_success", filePath));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.save_failed", ex.Message));
                throw;
            }
        }

        private object ConvertJsonElement(object value)
        {
            if (value is JsonElement jsonElement)
            {
                return jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString(),
                    JsonValueKind.Number => jsonElement.TryGetInt64(out var l) ? l : jsonElement.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Array => jsonElement.EnumerateArray().Select(e => ConvertJsonElement(e)).ToList(),
                    JsonValueKind.Object => jsonElement.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
                    JsonValueKind.Null => null,
                    _ => value
                };
            }

            if (value is Dictionary<string, object> dict)
            {
                var result = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    result[kvp.Key] = ConvertJsonElement(kvp.Value);
                }
                return result;
            }

            if (value is List<object> list)
            {
                return list.Select(ConvertJsonElement).ToList();
            }

            return value;
        }

        private string GenerateYamlWithComments<T>(string pluginName, string configName, T config)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {pluginName} 插件配置文件");
            sb.AppendLine($"# 配置名称: {configName}");
            sb.AppendLine($"# 最后更新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            object configToSerialize = config;
            if (config is Dictionary<string, object>)
            {
                configToSerialize = ConvertJsonElement(config);
            }

            string yaml = _serializer.Serialize(configToSerialize);
            sb.Append(yaml);

            return sb.ToString();
        }

        public async Task<T> GetValueAsync<T>(string pluginName, string configName, string keyPath, T defaultValue = default(T))
        {
            try
            {
                string filePath = GetConfigFilePath(pluginName, configName);
                bool configExists = File.Exists(filePath);
                
                if (!configExists)
                {
                    Log.Debug(I18nManager.S("config.value_not_found_creating", filePath));
                    var defaultConfig = new Dictionary<string, object>();
                    
                    SetNestedValue(defaultConfig, keyPath, defaultValue);
                    
                    await SetConfigAsync(pluginName, configName, defaultConfig);
                    
                    Log.Debug(I18nManager.S("config.default_value_set", keyPath, defaultValue));
                    return defaultValue;
                }
                
                var configDict = await GetConfigAsync<Dictionary<string, object>>(pluginName, configName, new Dictionary<string, object>());
                
                string[] keys = keyPath.Split('.');
                object current = configDict;
                
                foreach (string key in keys)
                {
                    if (current is Dictionary<string, object> dict && dict.ContainsKey(key))
                    {
                        current = dict[key];
                    }
                    else
                    {
                        Log.Debug(I18nManager.S("config.key_not_found", keyPath));
                        
                        SetNestedValue(configDict, keyPath, defaultValue);
                        await SetConfigAsync(pluginName, configName, configDict);
                        Log.Info(I18nManager.S("config.key_updated_default", keyPath, defaultValue));
                        
                        return defaultValue;
                    }
                }

                if (current is JsonElement jsonElement)
                {
                    return jsonElement.Deserialize<T>();
                }
                
                return (T)Convert.ChangeType(current, typeof(T));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.get_value_failed", ex.Message));
                return defaultValue;
            }
        }

        public async Task SetValueAsync<T>(string pluginName, string configName, string keyPath, T value)
        {
            try
            {
                var configDict = await GetConfigAsync<Dictionary<string, object>>(pluginName, configName, new Dictionary<string, object>());
                
                string[] keys = keyPath.Split('.');
                Dictionary<string, object> current = configDict;
                
                for (int i = 0; i < keys.Length - 1; i++)
                {
                    string key = keys[i];
                    if (!current.ContainsKey(key) || current[key] is not Dictionary<string, object>)
                    {
                        current[key] = new Dictionary<string, object>();
                    }
                    current = current[key] as Dictionary<string, object>;
                }
                
                current[keys[keys.Length - 1]] = value;
                
                await SetConfigAsync(pluginName, configName, configDict);
                
                Log.Debug(I18nManager.S("config.set_value_success", keyPath, value));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.set_value_failed", ex.Message));
                throw;
            }
        }

        public async Task<bool> ConfigExistsAsync(string pluginName, string configName)
        {
            string filePath = GetConfigFilePath(pluginName, configName);
            return File.Exists(filePath);
        }

        public async Task DeleteConfigAsync(string pluginName, string configName)
        {
            try
            {
                string filePath = GetConfigFilePath(pluginName, configName);
                
                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath));
                    Log.Debug(I18nManager.S("config.delete_success", filePath));
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.delete_failed", ex.Message));
                throw;
            }
        }

        private void RegisterConfig(string pluginName, string configName)
        {
            var pluginConfigs = _registeredConfigs.GetOrAdd(pluginName, _ => new ConcurrentDictionary<string, bool>());
            var added = pluginConfigs.TryAdd(configName, true);
            if (added)
            {
                Log.Debug(I18nManager.S("config.register_config", pluginName, configName, _currentModuleName ?? "(null)"));
            }
            
            if (_currentModuleName != null && _currentModuleName != pluginName)
            {
                _moduleNameToPluginName.TryAdd(_currentModuleName, pluginName);
                Log.Debug(I18nManager.S("config.module_name_mapping", _currentModuleName, pluginName));
            }
        }

        public List<RegisteredConfigInfo> GetRegisteredConfigs(string pluginName)
        {
            var result = new List<RegisteredConfigInfo>();
            
            var actualPluginName = pluginName;
            if (_moduleNameToPluginName.TryGetValue(pluginName, out var mappedName))
            {
                actualPluginName = mappedName;
                Log.Debug(I18nManager.S("config.get_registered_mapping", pluginName, actualPluginName));
            }
            
            Log.Debug(I18nManager.S("config.get_registered_lookup", actualPluginName));
            
            if (!_registeredConfigs.TryGetValue(actualPluginName, out var pluginConfigs))
            {
                Log.Debug(I18nManager.S("config.get_registered_none", actualPluginName, string.Join(", ", _registeredConfigs.Keys)));
                return result;
            }
            
            Log.Debug(I18nManager.S("config.get_registered_found", actualPluginName, pluginConfigs.Count, string.Join(", ", pluginConfigs.Keys)));
            
            foreach (var configName in pluginConfigs.Keys)
            {
                var filePath = GetConfigFilePath(actualPluginName, configName);
                var info = new RegisteredConfigInfo
                {
                    ConfigName = configName,
                    FilePath = filePath
                };
                
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    info.LastModified = fileInfo.LastWriteTime;
                    info.FileSize = fileInfo.Length;
                    Log.Debug(I18nManager.S("config.file_exists", filePath, info.FileSize));
                }
                else
                {
                    Log.Debug(I18nManager.S("config.file_not_exists", filePath));
                }
                
                result.Add(info);
            }
            
            return result;
        }

        public void SetCurrentModule(string moduleName)
        {
            _currentModuleName = moduleName;
        }

        public void ClearCurrentModule()
        {
            _currentModuleName = null;
        }

        public string ResolvePluginName(string name)
        {
            if (_moduleNameToPluginName.TryGetValue(name, out var mappedName))
                return mappedName;
            return name;
        }

        public Dictionary<string, object>? GetPluginConfigAsJson(string moduleName, string configName)
        {
            var pluginName = ResolvePluginName(moduleName);
            var filePath = GetConfigFilePath(pluginName, configName);

            if (!File.Exists(filePath))
                return null;

            try
            {
                string yaml = File.ReadAllText(filePath);
                var raw = _deserializer.Deserialize(yaml);
                return ConvertYamlObject(raw) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.read_plugin_failed", ex.Message));
                return null;
            }
        }

        private object? ConvertYamlObject(object? obj)
        {
            if (obj == null) return null;

            if (obj is Dictionary<object, object> dict)
            {
                var result = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    result[kvp.Key.ToString() ?? ""] = ConvertYamlObject(kvp.Value)!;
                }
                return result;
            }

            if (obj is List<object> list)
            {
                return list.Select(ConvertYamlObject).ToList();
            }

            return obj;
        }
    }
}