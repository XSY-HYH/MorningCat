using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MorningCat.Config
{
    public class ConfigManager
    {
        private const string CONFIG_FILE = "config.yml";
        private BotConfig _config = null!;
        private string _configPath;
        public bool IsNewConfig { get; private set; } = false;
        
        private static readonly HashSet<string> RequiredConfigKeys = new HashSet<string>
        {
            "nap_cat_server_url",
            "nap_cat_token",
            "modules_directory",
            "auto_load_modules",
            "owner_qq",
            "admin_qqs"
        };
        
        public ConfigManager()
        {
            _configPath = GetCorrectedPath(CONFIG_FILE);
            LoadConfig();
        }
        
        public static string GetCorrectedPath(string relativePath)
        {
            string baseDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return Path.Combine(baseDirectory, relativePath);
        }
        
        public BotConfig GetConfig() => _config;
        
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string yaml = File.ReadAllText(_configPath);
                    
                    if (ValidateConfigCompleteness(yaml))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(UnderscoredNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();
                        _config = deserializer.Deserialize<BotConfig>(yaml);
                        Log.Info("猫猫配置加载成功AWA");
                        Log.Debug($"配置文件路径: {_configPath}");
                        Log.Debug($"OwnerQQ: {_config.OwnerQQ}");
                    }
                    else
                    {
                        Log.Debug("配置文件不完整，正在备份并重新创建...");
                        BackupAndRecreateConfig(yaml);
                    }
                }
                else
                {
                    _config = new BotConfig();
                    SaveConfig();
                    IsNewConfig = true;
                    Log.Error($"配置文件不存在，已创建默认配置");
                    Log.Error($"请修改 \"{_configPath}\" 后重新启动！");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"猫猫配置加载失败QAQ:{ex.Message}");
                _config = new BotConfig();
                Log.Debug("使用默认配置");
            }
        }
        
        private bool ValidateConfigCompleteness(string yaml)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();
                var configDict = deserializer.Deserialize<Dictionary<string, object>>(yaml);
                
                if (configDict == null)
                    return false;
                
                foreach (var requiredKey in RequiredConfigKeys)
                {
                    if (!configDict.ContainsKey(requiredKey))
                    {
                        Log.Warning($"配置文件缺少必要键: {requiredKey}");
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"验证配置完整性失败: {ex.Message}");
                return false;
            }
        }
        
        private void BackupAndRecreateConfig(string oldYaml)
        {
            try
            {
                string backupPath = GetBackupPath();
                File.WriteAllText(backupPath, oldYaml);
                Log.Debug($"已备份旧配置文件到: {backupPath}");
                
                _config = new BotConfig();
                SaveConfig();
                IsNewConfig = true;
                Log.Debug("已创建新的完整配置文件");
                Log.Error($"请修改 \"{_configPath}\" 后重新启动！");
            }
            catch (Exception ex)
            {
                Log.Error($"备份并重新创建配置失败: {ex.Message}");
                _config = new BotConfig();
            }
        }
        
        private string GetBackupPath()
        {
            string baseDirectory = Path.GetDirectoryName(_configPath);
            string baseName = Path.GetFileNameWithoutExtension(_configPath);
            string extension = Path.GetExtension(_configPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupName = $"{baseName}_backup_{timestamp}{extension}";
            return Path.Combine(baseDirectory, backupName);
        }
        
        public void SaveConfig()
        {
            try
            {
                string yaml = GenerateYamlWithComments();
                File.WriteAllText(_configPath, yaml);
                Log.Debug("配置文件已保存");
                Log.Debug($"配置文件路径: {_configPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"保存配置文件失败: {ex.Message}");
            }
        }
        
        private string GenerateYamlWithComments()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# MorningCat 配置文件");
            sb.AppendLine("# 作者: 小七喵");
            sb.AppendLine($"# 最后更新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("#请勿删除任何配置项！否则配置加载失败可能导致配置丢失");
            sb.AppendLine();
            
            sb.AppendLine("# NapCat服务器配置");
            sb.AppendLine($"nap_cat_server_url: \"{_config.NapCatServerUrl}\"");
            sb.AppendLine($"nap_cat_token: \"{_config.NapCatToken}\"");
            sb.AppendLine();
            
            sb.AppendLine("# 模块配置");
            sb.AppendLine($"modules_directory: \"{_config.ModulesDirectory}\"");
            sb.AppendLine($"auto_load_modules: {_config.AutoLoadModules.ToString().ToLower()}");
            sb.AppendLine();
            
            sb.AppendLine("# 机器人持有者配置");
            sb.AppendLine("# 持有者拥有最高权限");
            sb.AppendLine("# 设置为0表示未设置持有者");
            sb.AppendLine($"owner_qq: {_config.OwnerQQ}");
            sb.AppendLine();
            
            sb.AppendLine("# 管理员配置（可设置多个）");
            sb.AppendLine("# 管理员拥有部分特权命令权限");
            if (_config.AdminQQs != null && _config.AdminQQs.Count > 0)
            {
                sb.AppendLine("admin_qqs:");
                foreach (var qq in _config.AdminQQs)
                {
                    sb.AppendLine($"  - {qq}");
                }
            }
            else
            {
                sb.AppendLine("admin_qqs: []");
            }
            
            sb.AppendLine();
            sb.AppendLine("# WebUI 配置");
            sb.AppendLine("webui:");
            sb.AppendLine("  # 是否启用 WebUI");
            sb.AppendLine($"  enabled: {_config.WebUI.Enabled.ToString().ToLower()}");
            sb.AppendLine("  # WebUI 端口");
            sb.AppendLine($"  port: {_config.WebUI.Port}");
            sb.AppendLine("  # 登录用户名");
            sb.AppendLine($"  username: \"{_config.WebUI.Username}\"");
            sb.AppendLine("  # 登录密码");
            sb.AppendLine($"  password: \"{_config.WebUI.Password}\"");
            
            return sb.ToString();
        }
        
        public void UpdateConfig(Action<BotConfig> updateAction)
        {
            updateAction(_config);
            SaveConfig();
        }
    }
}