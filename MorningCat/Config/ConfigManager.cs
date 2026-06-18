using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Logging;
using MorningCat.I18n;
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
            "onebot_server_url",
            "onebot_token",
            "modules_directory",
            "auto_load_modules",
            "plugin_signature_public_key",
            "owner_qq",
            "blocked_users",
            "blocked_groups",
            "plugin_store_url",
            "webui",
            "enable_gui",
            "enable_mct_status",
            "lang",
            "database"
        };

        public ConfigManager()
        {
            _configPath = GetCorrectedPath(CONFIG_FILE);
            LoadConfig();
        }
        
        public static string GetCorrectedPath(string relativePath)
        {
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string baseDirectory = string.IsNullOrEmpty(location)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(location);
            return Path.Combine(baseDirectory, relativePath);
        }
        
        public BotConfig GetConfig() => _config;
        
        private void LoadConfig()
        {
            Log.Name("Config");
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
                        Log.Info(I18nManager.S("config.loaded"));
                        Log.Debug(I18nManager.S("config.path", _configPath));
                        Log.Debug($"OneBotServerUrl: {_config.OneBotServerUrl}");
                        Log.Debug($"OneBotToken: {(_config.OneBotToken.Length > 5 ? _config.OneBotToken[..5] + "***" : "(空)")}");
                        Log.Debug($"ModulesDirectory: {_config.ModulesDirectory}");
                        Log.Debug($"AutoLoadModules: {_config.AutoLoadModules}");
                        Log.Debug($"OwnerQQ: {_config.OwnerQQ}");
                        Log.Debug($"BlockedUsers: [{string.Join(", ", _config.BlockedUsers)}]");
                        Log.Debug($"BlockedGroups: [{string.Join(", ", _config.BlockedGroups)}]");
                        Log.Debug($"PluginStoreUrl: '{_config.PluginStoreUrl}'");
                        Log.Debug($"WebUI.Enabled: {_config.WebUI.Enabled}");
                        Log.Debug($"WebUI.ListenAddress: {_config.WebUI.ListenAddress}");
                        Log.Debug($"WebUI.Port: {_config.WebUI.Port}");
                        Log.Debug($"EnableGui: {_config.EnableGui}");
                    }
                    else
                    {
                        Log.Info(I18nManager.S("config.keys_missing"));
                        MergeMissingKeys(yaml);
                    }
                }
                else
                {
                    _config = new BotConfig();
                    SaveConfig();
                    IsNewConfig = true;
                    Log.Error(I18nManager.S("config.not_found"));
                    Log.Error(I18nManager.S("config.edit_required", _configPath));
                }
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.load_failed", ex.Message));
                _config = new BotConfig();
                Log.Debug(I18nManager.S("config.using_default"));
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
                        Log.Warning(I18nManager.S("config.key_missing", requiredKey));
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.validate_failed", ex.Message));
                return false;
            }
        }
        
        private void MergeMissingKeys(string yaml)
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                // 先反序列化已有值
                _config = deserializer.Deserialize<BotConfig>(yaml) ?? new BotConfig();

                // 用默认值补全缺失字段
                var defaults = new BotConfig();
                if (_config.WebUI == null) _config.WebUI = defaults.WebUI;
                if (_config.Database == null) _config.Database = defaults.Database;
                if (_config.BlockedUsers == null) _config.BlockedUsers = new List<long>();
                if (_config.BlockedGroups == null) _config.BlockedGroups = new List<long>();

                // 保存补全后的配置
                SaveConfig();
                Log.Info(I18nManager.S("config.completed"));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.complete_failed", ex.Message));
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
                Log.Debug(I18nManager.S("config.saved"));
                Log.Debug(I18nManager.S("config.path", _configPath));
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("config.save_failed", ex.Message));
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
            
            sb.AppendLine("# OneBot服务器配置");
            sb.AppendLine($"onebot_server_url: \"{_config.OneBotServerUrl}\"");
            sb.AppendLine($"onebot_token: \"{_config.OneBotToken}\"");
            sb.AppendLine();
            
            sb.AppendLine("# 模块配置");
            sb.AppendLine($"modules_directory: \"{_config.ModulesDirectory}\"");
            sb.AppendLine($"auto_load_modules: {_config.AutoLoadModules.ToString().ToLower()}");
            sb.AppendLine();
            
            sb.AppendLine("# 插件签名验证公钥(系统填写)");
            sb.AppendLine($"plugin_signature_public_key: \"{_config.PluginSignaturePublicKey}\"");
            sb.AppendLine();
            
            sb.AppendLine("# 机器人持有者配置");
            sb.AppendLine("# 持有者拥有最高权限");
            sb.AppendLine("# 设置为0表示未设置持有者");
            sb.AppendLine($"owner_qq: {_config.OwnerQQ}");
            sb.AppendLine();
            sb.AppendLine("# 屏蔽的用户列表（这些用户的消息将被忽略）");
            if (_config.BlockedUsers != null && _config.BlockedUsers.Count > 0)
            {
                sb.AppendLine("blocked_users:");
                foreach (var qq in _config.BlockedUsers)
                {
                    sb.AppendLine($"  - {qq}");
                }
            }
            else
            {
                sb.AppendLine("blocked_users: []");
            }
            
            sb.AppendLine();
            sb.AppendLine("# 屏蔽的群列表（这些群的消息将被忽略）");
            if (_config.BlockedGroups != null && _config.BlockedGroups.Count > 0)
            {
                sb.AppendLine("blocked_groups:");
                foreach (var qq in _config.BlockedGroups)
                {
                    sb.AppendLine($"  - {qq}");
                }
            }
            else
            {
                sb.AppendLine("blocked_groups: []");
            }
            
            sb.AppendLine();
            sb.AppendLine("# 第三方插件市场地址（留空使用默认）");
            sb.AppendLine($"plugin_store_url: \"{_config.PluginStoreUrl}\"");
            
            sb.AppendLine();
            sb.AppendLine("# WebUI 配置");
            sb.AppendLine("webui:");
            sb.AppendLine("  # 是否启用 WebUI");
            sb.AppendLine($"  enabled: {_config.WebUI.Enabled.ToString().ToLower()}");
            sb.AppendLine("  # 监听地址（默认 127.0.0.1，设为 0.0.0.0 可允许外部访问）");
            sb.AppendLine($"  listen_address: \"{_config.WebUI.ListenAddress}\"");
            sb.AppendLine("  # WebUI 端口");
            sb.AppendLine($"  port: {_config.WebUI.Port}");
            sb.AppendLine("  # 登录用户名");
            sb.AppendLine($"  username: \"{_config.WebUI.Username}\"");
            sb.AppendLine("  # 登录密码");
            sb.AppendLine($"  password: \"{_config.WebUI.Password}\"");
            
            sb.AppendLine();
            sb.AppendLine("# 是否启用 GUI 窗口（需要 MorningCatGUI.dll）");
            sb.AppendLine($"enable_gui: {_config.EnableGui.ToString().ToLower()}");
            
            sb.AppendLine();
            sb.AppendLine("# 是否启用 #Mct 状态查询功能");
            sb.AppendLine($"enable_mct_status: {_config.EnableMctStatus.ToString().ToLower()}");
            
            sb.AppendLine();
            sb.AppendLine("# 语言配置（zh=中文, en=English, 也可使用第三方语言包如 zh-cn）");
            sb.AppendLine($"lang: \"{_config.Lang}\"");
            
            sb.AppendLine();
            sb.AppendLine("# 数据库配置");
            sb.AppendLine("database:");
            sb.AppendLine("  # 数据库类型: sqlite 或 sql");
            sb.AppendLine($"  type: \"{_config.Database.Type}\"");
            sb.AppendLine("  # SQL数据库连接字符串（sqlite模式无需填写）");
            sb.AppendLine($"  connection_string: \"{_config.Database.ConnectionString}\"");
            
            return sb.ToString();
        }
        
        public void UpdateConfig(Action<BotConfig> updateAction)
        {
            updateAction(_config);
            SaveConfig();
        }
    }
}