namespace MorningCat.WebUI
{
    public interface IWebUIServer
    {
        int Port { get; }
        bool IsRunning { get; }
        Task StartAsync(int port = 8080, string listenAddress = "127.0.0.1");
        Task StopAsync();
    }

    public interface IAccountService
    {
        event Action<string, string>? CredentialsChanged;
        bool ValidateToken(string token);
        string GenerateToken();
        bool ChangePassword(string oldPassword, string newPassword);
        bool ChangeUsername(string newUsername);
        bool IsPasswordChanged { get; }
        (string Username, string Password) GetDefaultCredentials();
        string GetPassword();
    }

    public interface IConfigProvider
    {
        WebUIConfigData GetConfig();
        void UpdateConfig(Action<WebUIConfigData> updateAction);
    }

    public class WebUIConfigData
    {
        public string OneBotServerUrl { get; set; } = "ws://127.0.0.1:7892";
        public string OneBotToken { get; set; } = "";
        public string ModulesDirectory { get; set; } = "Modules";
        public bool AutoLoadModules { get; set; } = true;
        public bool EnableMctStatus { get; set; } = true;
        public long OwnerQQ { get; set; }
        public List<long> BlockedUsers { get; set; } = new List<long>();
        public List<long> BlockedGroups { get; set; } = new List<long>();
        public string Lang { get; set; } = "zh";
        public DatabaseConfigData Database { get; set; } = new DatabaseConfigData();
        public string PluginStoreUrl { get; set; } = "";
        public WebUISettings WebUI { get; set; } = new WebUISettings();
    }

    public class DatabaseConfigData
    {
        public string Type { get; set; } = "sqlite";
        public string ConnectionString { get; set; } = "";
    }

    public class WebUISettings
    {
        public bool Enabled { get; set; } = true;
        public string ListenAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin123";
    }

    public interface ISystemInfoProvider
    {
        SystemInfo GetSystemInfo();
        void SetRestartCallback(Func<Task> restartCallback);
        void SetShutdownCallback(Action shutdownCallback);
        void RequestRestart();
        void RequestShutdown();
    }

    public interface IBotInfoProvider
    {
        BotInfo? GetBotInfo();
        void SetConnectionStatus(bool isConnected);
    }

    public interface IPluginInfoProvider
    {
        List<PluginInfo> GetPlugins();
        PluginInfo? GetPlugin(string moduleName);
        bool DisablePlugin(string moduleName);
        bool EnablePlugin(string moduleName);
        bool UnloadPlugin(string moduleName);
        bool DeletePlugin(string moduleName);
        PluginDetail? GetPluginDetail(string moduleName);
        List<PluginConfigInfo> GetPluginConfigs(string moduleName);
        Dictionary<string, object>? GetPluginConfig(string moduleName, string configName);
        bool SavePluginConfig(string moduleName, string configName, Dictionary<string, object> config);
    }

    public class PluginConfigInfo
    {
        public string ConfigName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
    }

    public interface ILogProvider
    {
        List<LogEntry> GetLogs(int count = 100, string? level = null);
        void ClearLogs();
        void SubscribeToLogs(Action<LogEntry> callback);
        void UnsubscribeFromLogs(Action<LogEntry> callback);
        void SubscribeToRawLogs(Action<string> callback);
        void UnsubscribeFromRawLogs(Action<string> callback);
        List<string> GetRecentRawLogs(int count = 50);
    }

    public class SystemInfo
    {
        public string Version { get; set; } = "";
        public long MemoryUsedMB { get; set; }
        public long MemoryTotalMB { get; set; }
        public double CpuUsage { get; set; }
        public string CpuModel { get; set; } = "";
        public string CpuSpeed { get; set; } = "";
        public string Arch { get; set; } = "";
        public int PluginCount { get; set; }
        public int RunningPluginCount { get; set; }
        public DateTime StartTime { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    public class BotInfo
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = "";
        public string Qid { get; set; } = "";
        public int Level { get; set; }
        public bool IsOnline { get; set; }
        public bool IsOneBotConnected { get; set; } = true;
    }

    public class PluginInfo
    {
        public string ModuleName { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string Status { get; set; } = "";
        public bool IsBuiltin { get; set; }
        public string? AssemblyPath { get; set; }
        public string? IconBase64 { get; set; }
        public string? SignatureStatus { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class PluginDetail
    {
        public string ModuleName { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Website { get; set; }
        public string Status { get; set; } = "";
        public bool IsBuiltin { get; set; }
        public string? ModuleType { get; set; }
        public string? AssemblyPath { get; set; }
        public bool HasInstance { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public List<string> Dependents { get; set; } = new List<string>();
        public string? IconBase64 { get; set; }
        public string? SignatureStatus { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class LogEntry
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "";
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public interface IMessageProvider
    {
        List<MessageEntry> GetRecentMessages(int count = 50);
        void SubscribeToMessages(Action<MessageEntry> callback);
        void UnsubscribeFromMessages(Action<MessageEntry> callback);
    }

    public class MessageEntry
    {
        public string GroupName { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string Content { get; set; } = "";
        public string MessageType { get; set; } = "";
        public long UserId { get; set; }
        public long? GroupId { get; set; }
        public DateTime Time { get; set; }
        public bool HasUnsupportedContent { get; set; }
    }

    public interface IDatabaseInfoProvider
    {
        List<DatabaseEntryInfo> GetDatabases();
        DatabaseDetailInfo? GetDatabaseDetail(string key);
        List<Dictionary<string, object>> QueryDatabase(string key, string sql);
        int ExecuteDatabaseNonQuery(string key, string sql);
    }

    public class DatabaseEntryInfo
    {
        public string Key { get; set; } = "";
        public string Id { get; set; } = "";
        public string PluginClassName { get; set; } = "";
        public string DatabasePath { get; set; } = "";
        public string DatabaseType { get; set; } = "";
        public long FileSize { get; set; }
        public List<string> Tables { get; set; } = new();
    }

    public class DatabaseDetailInfo
    {
        public string Key { get; set; } = "";
        public string Id { get; set; } = "";
        public string PluginClassName { get; set; } = "";
        public string DatabasePath { get; set; } = "";
        public string DatabaseType { get; set; } = "";
        public long FileSize { get; set; }
        public List<string> Tables { get; set; } = new();
        public Dictionary<string, List<ColumnInfo>> TableColumns { get; set; } = new();
    }

    public class ColumnInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool NotNull { get; set; }
        public bool IsPrimaryKey { get; set; }
        public string DefaultValue { get; set; } = "";
    }

    public interface II18nProvider
    {
        string CurrentLang { get; }
        Dictionary<string, string> GetTranslations();
        Dictionary<string, string>? GetTranslationsForLang(string lang);
        List<string> GetAvailableLanguages();
    }

    public interface IMessageSendProvider
    {
        Task<bool> SendPrivateMessageAsync(long userId, string message);
        Task<bool> SendGroupMessageAsync(long groupId, string message);
        Task<List<GroupInfo>> GetGroupListAsync();
        Task<List<FriendInfo>> GetFriendListAsync();
    }

    public class GroupInfo
    {
        public long GroupId { get; set; }
        public string GroupName { get; set; } = "";
    }

    public class FriendInfo
    {
        public long UserId { get; set; }
        public string Nickname { get; set; } = "";
        public string Remark { get; set; } = "";
    }
}
