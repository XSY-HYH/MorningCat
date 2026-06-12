using System.Collections.Generic;
using MorningCat.PlatformAbstraction;
using YamlDotNet.Serialization;

namespace MorningCat.Config
{
    public class BotConfig
    {
        public string NapCatServerUrl { get; set; } = "ws://127.0.0.1:7892";
        public string NapCatToken { get; set; } = "your_token_here";
        public int ReconnectDelay { get; set; } = 5;
        public string ModulesDirectory { get; set; } = "Modules";
        public bool AutoLoadModules { get; set; } = true;
        
        [YamlMember(Alias = "plugin_signature_public_key")]
        public string PluginSignaturePublicKey { get; set; } = "";
        
        [YamlMember(Alias = "owner_qq")]
        public long OwnerQQ { get; set; } = 0;
        
        [YamlMember(Alias = "blocked_users")]
        public List<long> BlockedUsers { get; set; } = new List<long>();
        
        [YamlMember(Alias = "blocked_groups")]
        public List<long> BlockedGroups { get; set; } = new List<long>();
        
        [YamlMember(Alias = "plugin_store_url")]
        public string PluginStoreUrl { get; set; } = "";
        
        [YamlMember(Alias = "webui")]
        public WebUIConfig WebUI { get; set; } = new WebUIConfig();
        
        [YamlMember(Alias = "enable_gui")]
        public bool EnableGui { get; set; } = false;
        
        [YamlMember(Alias = "enable_mct_status")]
        public bool EnableMctStatus { get; set; } = true;
        
        [YamlMember(Alias = "database")]
        public DatabaseConfig Database { get; set; } = new DatabaseConfig();

        [YamlMember(Alias = "discord")]
        public DiscordConfig Discord { get; set; } = new DiscordConfig();

        [YamlMember(Alias = "dingtalk")]
        public DingTalkConfig DingTalk { get; set; } = new DingTalkConfig();

        [YamlMember(Alias = "twitter")]
        public TwitterConfig Twitter { get; set; } = new TwitterConfig();

        public bool IsOwner(long qq)
        {
            return OwnerQQ == qq && OwnerQQ != 0;
        }
    }

    public class WebUIConfig
    {
        public bool Enabled { get; set; } = true;
        public string ListenAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin123";
    }

    public class DatabaseConfig
    {
        [YamlMember(Alias = "type")]
        public string Type { get; set; } = "sqlite";

        [YamlMember(Alias = "connection_string")]
        public string ConnectionString { get; set; } = "";
    }
}
