using System.Collections.Generic;
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
        
        [YamlMember(Alias = "owner_qq")]
        public long OwnerQQ { get; set; } = 0;
        
        [YamlMember(Alias = "admin_qqs")]
        public List<long> AdminQQs { get; set; } = new List<long>();
        
        [YamlMember(Alias = "webui")]
        public WebUIConfig WebUI { get; set; } = new WebUIConfig();
        
        public bool IsOwner(long qq)
        {
            return OwnerQQ == qq && OwnerQQ != 0;
        }
        
        public bool IsAdmin(long qq)
        {
            return AdminQQs.Contains(qq) || IsOwner(qq);
        }
    }

    public class WebUIConfig
    {
        public bool Enabled { get; set; } = true;
        public int Port { get; set; } = 8080;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin123";
    }
}
