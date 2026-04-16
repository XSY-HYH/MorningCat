using System;
using System.Collections.Generic;
using System.IO;
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
    }

    public class PluginConfigManager : IPluginConfigManager
    {
        private readonly string _configDirectory;
        private readonly IDeserializer _deserializer;
        private readonly ISerializer _serializer;

        public PluginConfigManager()
        {
            string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
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
            try
            {
                string filePath = GetConfigFilePath(pluginName, configName);
                
                if (!File.Exists(filePath))
                {
                    Log.Debug($"配置文件不存在，使用默认值: {filePath}");
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
                return defaultValue ?? new T();
            }
        }

        public async Task SetConfigAsync<T>(string pluginName, string configName, T config) where T : class
        {
            try
            {
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

        private string GenerateYamlWithComments<T>(string pluginName, string configName, T config)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {pluginName} 插件配置文件");
            sb.AppendLine($"# 配置名称: {configName}");
            sb.AppendLine($"# 最后更新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            string yaml = _serializer.Serialize(config);
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
                    File.Delete(filePath);
                    Log.Debug($"删除配置文件成功: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"删除配置文件失败: {ex.Message}");
                throw;
            }
        }
    }
}