using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Logging;
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
            Log.Name("插件配置");
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = string.IsNullOrEmpty(location)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(location);
            _configDirectory = Path.Combine(exeDir, "Config");
            
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
                Log.Info($"创建配置目录: {_configDirectory}");
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
                    Log.Debug($"配置文件不存在，使用默认值: {filePath}");
                    
                    if (defaultValue != null)
                    {
                        try
                        {
                            string defaultYaml = GenerateYamlWithComments(pluginName, configName, defaultValue);
                            await File.WriteAllTextAsync(filePath, defaultYaml);
                            Log.Info($"已创建默认配置文件: {filePath}");
                        }
                        catch (Exception writeEx)
                        {
                            Log.Warning($"创建默认配置文件失败: {writeEx.Message}");
                        }
                    }
                    
                    return defaultValue ?? new T();
                }

                string yaml = await File.ReadAllTextAsync(filePath);
                var config = _deserializer.Deserialize<T>(yaml);
                
                Log.Debug($"加载配置文件成功: {filePath}");
                return config ?? defaultValue ?? new T();
            }
            catch (Exception ex)
            {
                Log.Error($"加载配置文件失败: {ex.Message}");

                if (defaultValue != null)
                {
                    try
                    {
                        string defaultYaml = GenerateYamlWithComments(pluginName, configName, defaultValue);
                        await File.WriteAllTextAsync(filePath, defaultYaml);
                        Log.Info($"已用默认值覆盖损坏的配置文件: {filePath}");
                    }
                    catch (Exception writeEx)
                    {
                        Log.Warning($"覆盖损坏配置文件失败: {writeEx.Message}");
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
                Log.Debug($"保存配置文件成功: {filePath}");
            }
            catch (Exception ex)
            {
                Log.Error($"保存配置文件失败: {ex.Message}");
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
                    Log.Debug($"配置文件不存在，创建默认配置: {filePath}");
                    var defaultConfig = new Dictionary<string, object>();
                    
                    SetNestedValue(defaultConfig, keyPath, defaultValue);
                    
                    await SetConfigAsync(pluginName, configName, defaultConfig);
                    
                    Log.Debug($"已创建默认配置并设置默认值: {keyPath} = {defaultValue}");
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
                        Log.Debug($"配置键路径不存在: {keyPath}");
                        
                        SetNestedValue(configDict, keyPath, defaultValue);
                        await SetConfigAsync(pluginName, configName, configDict);
                        Log.Info($"已更新配置并设置默认值: {keyPath} = {defaultValue}");
                        
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
                Log.Error($"获取配置值失败: {ex.Message}");
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
                
                Log.Debug($"设置配置值成功: {keyPath} = {value}");
            }
            catch (Exception ex)
            {
                Log.Error($"设置配置值失败: {ex.Message}");
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
                    Log.Debug($"删除配置文件成功: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"删除配置文件失败: {ex.Message}");
                throw;
            }
        }

        private void RegisterConfig(string pluginName, string configName)
        {
            var pluginConfigs = _registeredConfigs.GetOrAdd(pluginName, _ => new ConcurrentDictionary<string, bool>());
            var added = pluginConfigs.TryAdd(configName, true);
            if (added)
            {
                Log.Debug($"[RegisterConfig] 注册配置: 插件='{pluginName}', 配置名='{configName}' (当前模块: '{_currentModuleName ?? "(无)"}')");
            }
            
            if (_currentModuleName != null && _currentModuleName != pluginName)
            {
                _moduleNameToPluginName.TryAdd(_currentModuleName, pluginName);
                Log.Debug($"[RegisterConfig] 模块名映射: '{_currentModuleName}' -> '{pluginName}'");
            }
        }

        public List<RegisteredConfigInfo> GetRegisteredConfigs(string pluginName)
        {
            var result = new List<RegisteredConfigInfo>();
            
            var actualPluginName = pluginName;
            if (_moduleNameToPluginName.TryGetValue(pluginName, out var mappedName))
            {
                actualPluginName = mappedName;
                Log.Debug($"[GetRegisteredConfigs] 模块名映射: '{pluginName}' -> '{actualPluginName}'");
            }
            
            Log.Debug($"[GetRegisteredConfigs] 查找插件 '{actualPluginName}' 的已注册配置");
            
            if (!_registeredConfigs.TryGetValue(actualPluginName, out var pluginConfigs))
            {
                Log.Debug($"[GetRegisteredConfigs] 插件 '{actualPluginName}' 没有已注册的配置 (已注册插件: [{string.Join(", ", _registeredConfigs.Keys)}])");
                return result;
            }
            
            Log.Debug($"[GetRegisteredConfigs] 插件 '{actualPluginName}' 有 {pluginConfigs.Count} 个已注册配置: [{string.Join(", ", pluginConfigs.Keys)}]");
            
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
                    Log.Debug($"[GetRegisteredConfigs] 配置文件存在: {filePath} (大小: {info.FileSize} 字节)");
                }
                else
                {
                    Log.Debug($"[GetRegisteredConfigs] 配置文件不存在: {filePath}");
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
                Log.Error($"读取插件配置失败: {ex.Message}");
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