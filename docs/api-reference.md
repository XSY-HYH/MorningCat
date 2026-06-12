# MorningCat API 参考

本文档提供 MorningCat 框架的核心 API 参考。

## 目录

- [命名空间引用](#命名空间引用)
- [MorningCatBot API](#morningcatbot-api)
- [CommandRegistry API](#commandregistry-api)
- [MessageHelper API](#messagehelper-api)
- [PluginConfigManager API](#pluginconfigmanager-api)
- [PluginCommandAPI API](#plugincommandapi-api)
- [PluginDatabaseAPI API](#plugindatabaseapi-api)
- [BotConfig API](#botconfig-api)
- [OneBotClient API](#onebotclient-api)
- [MessageObject API](#messageobject-api)
- [CommandContext API](#commandcontext-api)
- [ApiResult API](#apiresult-api)
- [事件参数类](#事件参数类)

## 命名空间引用

在使用以下 API 时，需要在文件顶部添加相应的 using 语句：

```csharp
// MorningCatBot
using MorningCat;

// CommandRegistry, CommandContext, CommandParameter, ParameterType, CommandPermission, CommandScope
using MorningCat.Commands;

// PluginConfigManager, IPluginConfigManager
using MorningCat.Config;

// UnhandledMessageEventArgs
using MorningCat.Events;

// PluginCommandAPI, PluginCommandResult, CommandInfoEntry, CommandParamEntry
using MorningCat.PluginAPI;

// PluginDatabaseAPI, IPluginDatabase, DatabaseEntry, ColumnInfo
using MorningCat.PluginAPI;

// ModuleBase, InjectAttribute
using ModuleManagerLib;

// Log
using Logging;

// OneBotClient, ApiResult
using OneBotLib;

// MessageObject, SenderInfo, GroupInfo 等数据模型
using OneBotLib.Models;

// MessageSegment
using OneBotLib.MessageSegment;
```

## MorningCatBot API

机器人主类，负责协调所有组件的工作。

### 生命周期方法

```csharp
// 启动机器人
Task StartAsync()

// 停止机器人
Task StopAsync()

// 重启机器人
Task RestartAsync()

// 请求退出（触发停止流程）
void RequestExit()

// 启动 WebUI
Task StartWebUIAsync()

// 停止 WebUI
Task StopWebUIAsync()
```

### 事件

```csharp
// 未处理消息事件（当消息不匹配任何命令时触发）
public event EventHandler<UnhandledMessageEventArgs>? OnUnhandledMessage;
```

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsNewConfig` | `bool` | 是否是新配置文件 |
| `WebUI` | `WebUIManager?` | WebUI 管理器实例 |

### 获取插件元数据

```csharp
PluginMetadata GetPluginMetadata(string moduleName)
```

### 获取模块映射

```csharp
string GetModuleNameByAssemblyName(string assemblyName)
```

### 获取 WebUI 登录 URL

```csharp
string GetWebUILoginUrl()
```

### 使用示例

```csharp
public class MyPluginModule : ModuleBase
{
    private MorningCatBot _bot = null!;
    
    public MorningCatBot Bot
    {
        get => _bot;
        set => _bot = value;
    }

    public override async Task Init()
    {
        // 订阅未处理消息事件
        _bot.OnUnhandledMessage += OnUnhandledMessage;
        
        // 获取其他插件的元数据
        var metadata = _bot.GetPluginMetadata("OtherPluginModule");
        if (metadata != null)
        {
            Log.Info($"插件: {metadata.DisplayName}");
        }
    }

    private void OnUnhandledMessage(object? sender, UnhandledMessageEventArgs e)
    {
        // 处理未匹配命令的消息
    }

    public override async Task Exit()
    {
        _bot.OnUnhandledMessage -= OnUnhandledMessage;
    }
}
```

## CommandRegistry API

命令注册与执行的核心类。

### 注册命令

```csharp
bool RegisterCommand(
    string commandName,              // 命令名称
    string description,              // 简短描述
    string helpText,                 // 帮助文本
    List<CommandParameter> parameters, // 参数列表
    Func<CommandContext, Task> handler, // 处理函数
    string moduleName,               // 模块名
    CommandPermission permission = CommandPermission.Everyone,
    CommandScope scope = CommandScope.All,
    bool requireAt = false,
    bool requireSlash = true
)
```

### 注销命令

```csharp
bool UnregisterCommand(string commandName)
void UnregisterModuleCommands(string moduleName)
```

### 查询命令

```csharp
CommandInfo GetCommand(string commandName)
List<CommandInfo> GetAllCommands()
bool HasCommand(string commandName)
```

### 执行命令

```csharp
Task<bool> ExecuteCommandAsync(MessageObject message, string commandText)
```

### 更新客户端引用

```csharp
void SetClient(OneBotClient client)
```

> 重连后框架会自动调用此方法更新客户端引用，插件通常不需要手动调用。

### 权限检查

```csharp
bool IsBotOwner(long userId)
bool IsAdmin(long userId)
```

### CommandParameter 类

```csharp
public class CommandParameter
{
    public string Name { get; set; }           // 参数名
    public string Description { get; set; }    // 参数描述
    public bool IsRequired { get; set; }       // 是否必需
    public ParameterType Type { get; set; }    // 参数类型
    public string DefaultValue { get; set; }   // 默认值
    public List<CommandParameter> SubParameters { get; set; }  // 子参数
}
```

### ParameterType 枚举

| 值 | 说明 |
|----|------|
| `String` | 字符串 |
| `Integer` | 整数 |
| `Float` | 浮点数 |
| `Boolean` | 布尔值 |
| `At` | @某人 |
| `Reply` | 回复消息 |

### CommandPermission 枚举

| 值 | 说明 |
|----|------|
| `Everyone` | 所有人可用 |
| `GroupAdmin` | 群管理员及以上 |
| `Owner` | 群主及以上 |
| `BotOwner` | 仅机器人主人 |

### CommandScope 枚举

| 值 | 说明 |
|----|------|
| `All` | 私聊和群聊 |
| `PrivateOnly` | 仅私聊 |
| `GroupOnly` | 仅群聊 |

## MessageHelper API

消息处理工具类，提供消息清理、@检测、发送等共享方法。

**文件**: `MorningCat/Commands/MessageHelper.cs`

**命名空间**: `MorningCat.Commands`

### 消息清理方法

| 方法 | 说明 |
|------|------|
| `CleanMessageText(string text)` | 清理消息文本（去引号、去 CQ 回复码） |
| `RemoveAtSegments(string text, long selfId)` | 移除消息中的 @ 段（@机器人、@所有人、@任意人） |
| `CheckIsAtBot(MessageObject message)` | 检查消息是否@了机器人 |

### 消息发送方法（推荐）

| 方法 | 说明 |
|------|------|
| `SendAsync(MDC mdc, PlatformMessage message, Action<IMessageBuilder> configure)` | 使用 IMessageBuilder 构建并发送消息 |
| `BuildReplyMessage(messageId, userId, text, reply, at)` | 构建回复消息体（MessageBody） |

### 旧版方法（兼容）

| 方法 | 说明 |
|------|------|
| `SendMessageAsync(OneBotClient client, MessageObject message, string text)` | 根据消息类型自动发送私聊或群消息 |
| `BuildReplyMessageCQ(...)` | 构建完整回复 CQ 码字符串（仅 OneBot） |

### 使用示例

```csharp
using MorningCat.Commands;

// 检查是否@机器人
bool isAtBot = MessageHelper.CheckIsAtBot(message);

// 清理消息文本
string cleaned = MessageHelper.CleanMessageText(message.PlainText);

// 使用 IMessageBuilder 发送消息（推荐）
await MessageHelper.SendAsync(_mdc, message, builder => builder
    .Reply(message.MessageId)
    .At(message.SenderId)
    .Text("回复内容"));

// 构建消息体
var body = MessageHelper.BuildReplyMessage(message.MessageId, message.SenderId, "你好", reply: true, at: true);
await _mdc.SendAsync(message, body);
```

## PluginConfigManager API

插件配置管理类。

### 获取配置

```csharp
Task<T> GetConfigAsync<T>(string pluginName, string configName, T defaultValue = default) where T : class, new()
```

**参数**:
- `pluginName`: 插件名称（模块名）
- `configName`: 配置名称
- `defaultValue`: 默认值（可选）

**返回**: 配置对象

### 保存配置

```csharp
Task SetConfigAsync<T>(string pluginName, string configName, T config) where T : class
```

### 获取嵌套值

```csharp
Task<T> GetValueAsync<T>(string pluginName, string configName, string keyPath, T defaultValue = default)
```

**参数**:
- `pluginName`: 插件名称（模块名）
- `configName`: 配置名称
- `keyPath`: 嵌套路径，如 `"database.host"`

### 设置嵌套值

```csharp
Task SetValueAsync<T>(string pluginName, string configName, string keyPath, T value)
```

### 其他方法

```csharp
Task<bool> ConfigExistsAsync(string pluginName, string configName)
Task DeleteConfigAsync(string pluginName, string configName)
```

### 模块名映射方法

```csharp
// 设置当前正在加载的模块名（框架内部使用）
void SetCurrentModule(string moduleName)

// 清除当前模块名
void ClearCurrentModule()

// 将模块名解析为插件名
string ResolvePluginName(string name)
```

### 获取已注册配置

```csharp
List<RegisteredConfigInfo> GetRegisteredConfigs(string pluginName)
```

**RegisteredConfigInfo 类**:

| 属性 | 类型 | 说明 |
|------|------|------|
| `ConfigName` | `string` | 配置名称 |
| `FilePath` | `string` | 配置文件路径 |
| `LastModified` | `DateTime` | 最后修改时间 |
| `FileSize` | `long` | 文件大小 |

### 自动创建默认配置

当调用 `GetConfigAsync` 时，如果配置文件不存在且提供了 `defaultValue`，框架会自动创建默认配置文件。这确保 WebUI 等外部工具始终能获取到配置信息。

## PluginCommandAPI API

插件命令执行 API，允许插件在代码中执行已注册的命令。

**文件**: `MorningCat/PluginAPI/PluginCommandAPI.cs`

**命名空间**: `MorningCat.PluginAPI`

### 注入方式

```csharp
public PluginCommandAPI PluginCommandAPI { get; set; }
```

### 枚举命令

```csharp
// 枚举 Everyone 权限的命令
List<CommandInfoEntry> EnumerateCommands()

// 枚举指定权限及以下的命令
List<CommandInfoEntry> EnumerateCommands(CommandPermission permission)
```

### 执行命令

```csharp
// 以普通用户权限执行命令
Task<PluginCommandResult> ExecuteAsNormal(MessageObject message, string commandLine)
Task<PluginCommandResult> ExecuteAsNormal(MessageObject message, string commandName, params string[] args)

// 以群管理员权限执行命令
Task<PluginCommandResult> ExecuteAsGroupAdmin(MessageObject message, string commandLine)
Task<PluginCommandResult> ExecuteAsGroupAdmin(MessageObject message, string commandName, params string[] args)

// 以机器人主人权限执行命令
Task<PluginCommandResult> ExecuteAsBotOwner(MessageObject message, string commandLine)
Task<PluginCommandResult> ExecuteAsBotOwner(MessageObject message, string commandName, params string[] args)

// 以自定义权限执行命令
Task<PluginCommandResult> ExecuteWithPermission(MessageObject message, CommandPermission permission, string commandLine)
Task<PluginCommandResult> ExecuteWithPermission(MessageObject message, CommandPermission permission, string commandName, params string[] args)
```

### CommandInfoEntry 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 命令名称 |
| `Description` | `string` | 命令描述 |
| `ModuleName` | `string` | 所属模块名 |
| `Permission` | `CommandPermission` | 权限级别 |
| `Scope` | `CommandScope` | 作用域 |
| `RequireAt` | `bool` | 是否需要@ |
| `RequireSlash` | `bool` | 是否需要/前缀 |
| `Parameters` | `List<CommandParamEntry>` | 参数列表 |

### 注册命令

```csharp
// 通过 PluginCommandAPI 注册命令
PluginCommandAPI.RegisterCommand(
    string commandName,      // 命令名称
    string description,      // 命令描述
    string helpText,        // 帮助文本
    List<CommandParameter> parameters,  // 参数列表
    Func<CommandContext, Task> handler, // 处理函数
    string moduleName,      // 模块名称
    CommandPermission permission = CommandPermission.Everyone,  // 权限
    CommandScope scope = CommandScope.All,  // 作用域
    bool requireAt = false, // 是否需要@（默认不需要）
    bool requireSlash = true  // 是否需要/前缀（默认需要）
);

// 注销命令
bool success = PluginCommandAPI.UnregisterCommand(string commandName);

// 注销模块所有命令
PluginCommandAPI.UnregisterModuleCommands(string moduleName);
```

### 命令前缀规则

命令匹配支持以下四种模式：

| RequireAt | RequireSlash | 触发条件 |
|-----------|-------------|---------|
| true | true | 群聊中必须@机器人且消息必须以/开头 |
| true | false | 群聊中必须@机器人，消息无需/前缀 |
| false | true | 消息必须以/开头，无需@ |
| **false** | **false** | **无需@也无需/**，直接输入命令名即可触发 |

> 注意：设置为 `requireAt=false && requireSlash=false` 时，命令会对所有消息进行匹配，请确保命令名足够独特以避免误触发。

### CommandParamEntry 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 参数名 |
| `Description` | `string` | 参数描述 |
| `IsRequired` | `bool` | 是否必需 |
| `Type` | `ParameterType` | 参数类型 |
| `DefaultValue` | `string` | 默认值 |

### PluginCommandResult 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `Success` | `bool` | 是否执行成功 |
| `ErrorMessage` | `string` | 错误信息（失败时） |

### 使用示例

```csharp
public class MyPlugin : ModuleBase
{
    private PluginCommandAPI _pluginCommandAPI = null!;
    
    public PluginCommandAPI PluginCommandAPI
    {
        get => _pluginCommandAPI;
        set => _pluginCommandAPI = value;
    }

    private async Task DoSomething(MessageObject message)
    {
        // 枚举命令
        var commands = _pluginCommandAPI.EnumerateCommands(CommandPermission.Everyone);
        foreach (var cmd in commands)
        {
            Log.Info($"命令: {cmd.Name} - {cmd.Description}");
        }
        
        // 执行命令
        var result = await _pluginCommandAPI.ExecuteAsNormal(message, "help");
        if (!result.Success)
        {
            Log.Warning($"命令执行失败: {result.ErrorMessage}");
        }
        
        // 以指定权限执行命令（带参数）
        result = await _pluginCommandAPI.ExecuteAsBotOwner(message, "plugin", "list");
    }
}
```

## PluginDatabaseAPI API

插件数据库 API，允许插件使用数据库存储数据。

**文件**: `MorningCat/PluginAPI/PluginDatabaseAPI.cs`

**命名空间**: `MorningCat.PluginAPI`

### 注入方式

```csharp
public PluginDatabaseAPI PluginDatabaseAPI { get; set; }
```

### 获取数据库

```csharp
IPluginDatabase GetDatabase(string id, string pluginClassName)
```

**参数**:
- `id`: 数据库标识（如 "data"、"cache"）
- `pluginClassName`: 插件类名（如 "MyPluginModule"）

**返回**: `IPluginDatabase` 实例

**数据库文件位置**:
- SQLite: `Database/{id}-{pluginClassName}.db`
- SQL Server: 使用配置的连接字符串

### IPluginDatabase 接口

#### 执行非查询语句

```csharp
int ExecuteNonQuery(string sql, params DbParameter[] parameters)
Task<int> ExecuteNonQueryAsync(string sql, params DbParameter[] parameters)
```

**返回**: 受影响的行数

#### 执行标量查询

```csharp
object ExecuteScalar(string sql, params DbParameter[] parameters)
Task<object> ExecuteScalarAsync(string sql, params DbParameter[] parameters)
```

**返回**: 查询结果的第一行第一列

#### 查询数据（字典列表）

```csharp
List<Dictionary<string, object>> Query(string sql, params DbParameter[] parameters)
Task<List<Dictionary<string, object>>> QueryAsync(string sql, params DbParameter[] parameters)
```

**返回**: 字典列表，键为列名，值为数据

#### 查询数据（DataTable）

```csharp
DataTable QueryTable(string sql, params DbParameter[] parameters)
Task<DataTable> QueryTableAsync(string sql, params DbParameter[] parameters)
```

**返回**: `DataTable` 对象

#### 创建参数

```csharp
DbParameter CreateParameter(string name, object value)
```

**返回**: 数据库对应的参数对象（SQLite 为 `SqliteParameter`，SQL Server 为 `SqlParameter`）

#### 获取表信息

```csharp
List<string> GetTableNames()
List<ColumnInfo> GetColumns(string tableName)
```

#### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `DatabasePath` | `string` | 数据库路径（SQLite 为文件路径，SQL Server 为 `sql://id-className`） |

### ColumnInfo 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `string` | 列名 |
| `Type` | `string` | 数据类型 |
| `NotNull` | `bool` | 是否非空 |
| `IsPrimaryKey` | `bool` | 是否主键 |
| `DefaultValue` | `string` | 默认值 |

### DatabaseEntry 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `Key` | `string` | 数据库键（`id-pluginClassName`） |
| `Id` | `string` | 数据库标识 |
| `PluginClassName` | `string` | 插件类名 |
| `DatabasePath` | `string` | 数据库路径 |
| `DatabaseType` | `string` | 数据库类型（sqlite/sql） |
| `FileSize` | `long` | 文件大小（字节） |
| `Tables` | `List<string>` | 表名列表 |

### 使用示例

```csharp
public class MyPlugin : ModuleBase
{
    private PluginDatabaseAPI _dbAPI = null!;
    private IPluginDatabase _db = null!;

    public PluginDatabaseAPI PluginDatabaseAPI
    {
        get => _dbAPI;
        set => _dbAPI = value;
    }

    public override async Task Init()
    {
        _db = _dbAPI.GetDatabase("data", "MyPluginModule");

        await _db.ExecuteNonQueryAsync(
            "CREATE TABLE IF NOT EXISTS users (qq INTEGER PRIMARY KEY, count INTEGER DEFAULT 0)"
        );

        var param = _db.CreateParameter("@qq", 123456);
        var result = await _db.QueryAsync("SELECT * FROM users WHERE qq = @qq", param);
        foreach (var row in result)
        {
            Log.Info($"QQ: {row["qq"]}, Count: {row["count"]}");
        }
    }
}
```

### 数据库配置

在 `config.yml` 中配置：

```yaml
database:
  type: "sqlite"              # sqlite 或 sql
  connection_string: ""        # SQL Server 连接字符串（sql 类型时必填）
```

**DatabaseConfig 类**:

| 属性 | 类型 | Yaml 别名 | 说明 |
|------|------|-----------|------|
| `Type` | `string` | `type` | 数据库类型（`sqlite` / `sql`），默认 `sqlite` |
| `ConnectionString` | `string` | `connection_string` | SQL Server 连接字符串 |

## BotConfig API

机器人主配置类。

**文件**: `MorningCat/Config/BotConfig.cs`

**命名空间**: `MorningCat.Config`

### 属性

| 属性 | 类型 | Yaml 别名 | 说明 |
|------|------|-----------|------|
| `NapCatServerUrl` | `string` | - | NapCat WebSocket 地址 |
| `NapCatToken` | `string` | - | NapCat 访问令牌 |
| `ReconnectDelay` | `int` | - | 重连延迟（秒） |
| `ModulesDirectory` | `string` | - | 插件目录路径 |
| `AutoLoadModules` | `bool` | - | 是否自动加载模块 |
| `PluginSignaturePublicKey` | `string` | `plugin_signature_public_key` | 插件签名验证公钥 |
| `OwnerQQ` | `long` | `owner_qq` | 机器人主人 QQ 号 |
| `AdminQQs` | `List<long>` | `admin_qqs` | 管理员 QQ 号列表 |
| `BlockedUsers` | `List<long>` | `blocked_users` | 屏蔽的用户列表 |
| `BlockedGroups` | `List<long>` | `blocked_groups` | 屏蔽的群列表 |
| `WebUI` | `WebUIConfig` | `webui` | WebUI 配置 |

### 辅助方法

```csharp
// 判断是否为机器人主人
bool IsOwner(long qq)

// 判断是否为管理员（包含主人）
bool IsAdmin(long qq)
```

### WebUIConfig 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `Enabled` | `bool` | 是否启用 WebUI |
| `Port` | `int` | WebUI 端口 |
| `Username` | `string` | WebUI 用户名 |
| `Password` | `string` | WebUI 密码 |

### DatabaseConfig 类

| 属性 | 类型 | Yaml 别名 | 说明 |
|------|------|-----------|------|
| `Type` | `string` | `type` | 数据库类型（`sqlite` / `sql`），默认 `sqlite` |
| `ConnectionString` | `string` | `connection_string` | SQL Server 连接字符串（sql 类型时必填） |

## OneBotClient API

OneBot 协议客户端，详见 `Lib/OneBotLib.md`。

### 连接管理

```csharp
Task ConnectAsync(string wsUrl, string token)
bool ConnectSync(string wsUrl, string token, int timeoutSeconds)
Task CloseAsync()
```

### 发送消息

```csharp
// 发送私聊消息
Task<ApiResult<string>> SendPrivateMsgAsync(long userId, string message)
Task<ApiResult<string>> SendPrivateMsgAsync(long userId, List<MessageSegment> segments)

// 发送群消息
Task<ApiResult<string>> SendGroupMsgAsync(long groupId, string message)
Task<ApiResult<string>> SendGroupMsgAsync(long groupId, List<MessageSegment> segments)

// 通用发送
Task<ApiResult<string>> SendMsgAsync(string messageType, long targetId, string message)
```

### 消息操作

```csharp
// 撤回消息
Task<ApiResult> DeleteMsgAsync(long messageId)

// 获取消息
Task<ApiResult<MessageObject>> GetMsgAsync(long messageId)

// 标记已读
Task<ApiResult> MarkMsgAsReadAsync(long messageId)

// 表情回应
Task<ApiResult> SetMsgEmojiLikeAsync(long messageId, string emojiId, bool set = true)
```

### 群组操作

```csharp
// 获取群列表
Task<ApiResult<List<GroupInfo>>> GetGroupListAsync(bool noCache = false)

// 获取群信息
Task<ApiResult<GroupInfo>> GetGroupInfoAsync(long groupId, bool noCache = false)

// 获取群成员列表
Task<ApiResult<List<GroupMemberInfo>>> GetGroupMemberListAsync(long groupId, bool noCache = false)

// 获取群成员信息
Task<ApiResult<GroupMemberInfo>> GetGroupMemberInfoAsync(long groupId, long userId, bool noCache = false)

// 禁言
Task<ApiResult> SetGroupBanAsync(long groupId, long userId, long duration = 1800)

// 全员禁言
Task<ApiResult> SetGroupWholeBanAsync(long groupId, bool enable = true)

// 踢出群
Task<ApiResult> SetGroupKickAsync(long groupId, long userId, bool rejectAddRequest = false)

// 设置管理员
Task<ApiResult> SetGroupAdminAsync(long groupId, long userId, bool enable = true)

// 设置群名片
Task<ApiResult> SetGroupCardAsync(long groupId, long userId, string card = "")

// 退出群
Task<ApiResult> SetGroupLeaveAsync(long groupId, bool isDismiss = false)

// 群戳一戳
Task<ApiResult> GroupPokeAsync(long groupId, long userId)
```

### 用户操作

```csharp
// 获取登录信息
Task<ApiResult<AccountInfo>> GetLoginInfoAsync()

// 获取好友列表
Task<ApiResult<List<FriendInfo>>> GetFriendListAsync(bool noCache = false)

// 获取陌生人信息
Task<ApiResult<StrangerInfo>> GetStrangerInfoAsync(long userId, bool noCache = false)

// 发送点赞
Task<ApiResult> SendLikeAsync(long userId, int times = 1)

// 好友戳一戳
Task<ApiResult> FriendPokeAsync(long userId)
```

### 事件

```csharp
event EventHandler<MessageEventArgs> OnMessage
event EventHandler<MessageEventArgs> OnPrivateMessage
event EventHandler<MessageEventArgs> OnGroupMessage
event EventHandler<LifecycleEventArgs> OnLifecycle
event EventHandler<HeartbeatEventArgs> OnHeartbeat
event EventHandler<ConnectionStateChangedEventArgs> OnConnectionStateChanged
event EventHandler<GroupMemberChangeEventArgs> OnGroupMemberChange
event EventHandler<GroupAdminEventArgs> OnGroupAdmin
event EventHandler<GroupBanEventArgs> OnGroupBan
event EventHandler<FriendRequestEventArgs> OnFriendRequest
event EventHandler<GroupRequestEventArgs> OnGroupRequest
```

## MessageObject API

消息对象类。

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `MessageId` | `long?` | 消息 ID |
| `UserId` | `long?` | 发送者 ID |
| `GroupId` | `long?` | 群 ID（群消息时） |
| `MessageType` | `string` | 消息类型（private/group） |
| `PlainText` | `string` | 纯文本内容 |
| `MessageSegments` | `List<MessageSegment>` | 消息段列表 |
| `Sender` | `SenderInfo` | 发送者信息 |
| `SelfId` | `long` | 机器人 ID |
| `Time` | `long` | 消息时间戳 |

### SenderInfo 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `UserId` | `long?` | 用户 ID |
| `Nickname` | `string` | 昵称 |
| `Card` | `string` | 群名片 |
| `Role` | `string` | 群角色（owner/admin/member） |
| `Title` | `string` | 群头衔 |
| `Level` | `string` | 群等级 |

### MessageSegment 类

```csharp
// 文本
MessageSegment.Text(string text)

// @某人
MessageSegment.At(long userId)
MessageSegment.AtAll()

// 表情
MessageSegment.Face(int id)

// 图片
MessageSegment.Image(string url, bool cache = true, bool proxy = true, int timeout = 0)

// 语音
MessageSegment.Record(string url, bool magic = false)

// 视频
MessageSegment.Video(string url)

// 回复
MessageSegment.Reply(long messageId)

// 位置
MessageSegment.Location(double lat, double lon, string title, string content)

// 链接分享
MessageSegment.Share(string url, string title, string content, string image)

// 音乐分享
MessageSegment.Music(long id, string type)
MessageSegment.MusicCustom(string url, string audio, string title, string content, string image)

// XML/JSON
MessageSegment.Xml(string data)
MessageSegment.Json(string data)

// 文件
MessageSegment.File(string url, string name)

// 骰子
MessageSegment.Dice()

// 石头剪刀布
MessageSegment.Rps()
```

## CommandContext API

命令上下文类。

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Message` | `MessageObject` | 原始消息对象 |
| `Parameters` | `Dictionary<string, string>` | 解析后的参数 |
| `RawCommand` | `string` | 原始命令文本 |
| `Client` | `OneBotClient` | OneBot 客户端 |

### 使用示例

```csharp
private async Task HandleCommand(CommandContext context)
{
    var message = context.Message;
    var userId = message.UserId ?? 0;
    var groupId = message.GroupId;
    var isGroup = message.MessageType == "group";
    
    // 获取参数
    if (context.Parameters.TryGetValue("参数名", out var value))
    {
        // 使用参数
    }
    
    // 发送回复
    if (isGroup)
    {
        await context.Client.SendGroupMsgAsync(groupId ?? 0, "回复内容");
    }
    else
    {
        await context.Client.SendPrivateMsgAsync(userId, "回复内容");
    }
}
```

## ApiResult API

API 调用结果类。

### ApiResult（无数据）

| 属性 | 类型 | 说明 |
|------|------|------|
| `Success` | `bool` | 是否成功 |
| `ErrorMessage` | `string?` | 错误信息 |
| `StackTrace` | `string?` | 堆栈跟踪 |

### ApiResult\<T\>（有数据）

| 属性 | 类型 | 说明 |
|------|------|------|
| `Success` | `bool` | 是否成功 |
| `Data` | `T?` | 返回数据 |
| `ErrorMessage` | `string?` | 错误信息 |
| `StackTrace` | `string?` | 堆栈跟踪 |

### 使用示例

```csharp
var result = await _client.SendPrivateMsgAsync(userId, "Hello");
if (result.Success)
{
    Log.Info($"消息发送成功，ID: {result.Data}");
}
else
{
    Log.Error($"发送失败: {result.ErrorMessage}");
}
```

## 事件参数类

### UnhandledMessageEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `Message` | `MessageObject` | 原始消息 |
| `UserId` | `long` | 用户 ID |
| `UserNickname` | `string` | 用户昵称 |
| `GroupId` | `long?` | 群 ID |
| `GroupName` | `string` | 群名 |
| `PlainText` | `string` | 纯文本 |
| `IsGroupMessage` | `bool` | 是否群消息 |
| `IsPrivateMessage` | `bool` | 是否私聊 |

### MessageEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `Message` | `MessageObject` | 消息对象 |

### ConnectionStateChangedEventArgs

| 属性 | 类型 | 说明 |
|------|------|------|
| `OldState` | `ConnectionState` | 旧状态 |
| `NewState` | `ConnectionState` | 新状态 |
| `Message` | `string?` | 状态消息 |

### ConnectionState 枚举

| 值 | 说明 |
|----|------|
| `Connecting` | 正在连接 |
| `Connected` | 已连接 |
| `Disconnected` | 已断开 |
