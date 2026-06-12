# MorningCat 架构详解

本文档详细介绍 MorningCat 框架的架构设计和核心组件。

## 目录

- [项目引用说明](#项目引用说明)
- [整体架构](#整体架构)
- [核心组件](#核心组件)
- [消息处理流程](#消息处理流程)
- [事件系统](#事件系统)
- [插件元数据](#插件元数据)
- [配置系统](#配置系统)
- [重连机制](#重连机制)
- [插件签名验证](#插件签名验证)
- [PluginCommandAPI](#plugincommandapi)
- [PluginDatabaseAPI](#plugindatabaseapi)
- [WebUI 管理界面](#webui-管理界面)
- [Electron GUI](#electron-gui)

## 项目引用说明

### 核心库引用

| 库文件 | 说明 |
|--------|------|
| `MorningCat.dll` | MorningCat 核心库，包含命令系统、配置管理等 |
| `ModuleManagerLib.dll` | 模块管理器库，包含 ModuleBase 基类 |
| `logs.dll` | 日志库，用于输出日志 |
| `OneBotLib.dll` | OneBot 协议实现库（可选） |

### 常用命名空间

```csharp
// 核心组件
using MorningCat;                    // MorningCatBot
using MorningCat.Commands;           // CommandRegistry, CommandContext, CommandParameter, MessageHelper
using MorningCat.Config;             // PluginConfigManager, ConfigManager
using MorningCat.Events;             // UnhandledMessageEventArgs

// 模块管理
using ModuleManagerLib;              // ModuleBase, InjectAttribute

// 日志
using Logging;                       // Log

// OneBot（可选）
using OneBotLib;                     // OneBotClient, ApiResult
using OneBotLib.Models;              // MessageObject, SenderInfo
using OneBotLib.MessageSegment;      // MessageSegment
```

## 整体架构

```
┌──────────────────────────────────────────────────────────────────────┐
│                            MorningCatBot                            │
│  ┌─────────────┐  ┌─────────────┐  ┌───────────────────────────┐   │
│  │ OneBotClient│  │ConfigManager│  │     CommandRegistry       │   │
│  └──────┬──────┘  └──────┬──────┘  └─────────────┬─────────────┘   │
│         │                │                       │                  │
│  ┌──────┴────────────────┼───────────────────────┘                  │
│  │                       │                                          │
│  │  ┌────────────────────┴────────────────────┐                    │
│  │  │          PluginConfigManager             │                    │
│  │  │  (moduleName→pluginName 映射)            │                    │
│  │  └─────────────────────────────────────────┘                    │
│  │                                                                  │
│  │  ┌──────────────────────────────────────────┐                   │
│  │  │            ModuleManager                  │                   │
│  │  │  ┌─────────────┐  ┌─────────────┐        │                   │
│  │  │  │ 内置模块    │  │ 外部插件    │        │                   │
│  │  │  │ - Help      │  │ - PluginA   │        │                   │
│  │  │  │ - System    │  │ - PluginB   │        │                   │
│  │  │  │ - Set       │  │ - ...       │        │                   │
│  │  │  │ - Plugin    │  │             │        │                   │
│  │  │  └─────────────┘  └─────────────┘        │                   │
│  │  └──────────────────────────────────────────┘                   │
│  │                                                                  │
│  │  ┌──────────────────┐  ┌───────────────────────────────┐        │
│  │  │ PluginCommandAPI │  │ PluginSignatureVerifier       │        │
│  │  └──────────────────┘  └───────────────────────────────┘        │
│  │                                                                  │
│  │  ┌──────────────────┐  ┌───────────────────────────────┐        │
│  │  │ PluginDatabaseAPI│  │  WebUIManager                 │        │
│  │  └──────────────────┘  └───────────────────────────────┘        │
│  └──────────────────────────────────────────────────────────────────┘
│                              │                                      │
│                              ▼                                      │
│                    ┌─────────────────┐                               │
│                    │   NapCat/OneBot │                               │
│                    │   (WebSocket)   │                               │
│                    └─────────────────┘                               │
└──────────────────────────────────────────────────────────────────────┘
```

## 核心组件

### 1. MorningCatBot（主类）

机器人主类，负责协调所有组件的工作。

**文件**: `MorningCat/MorningCatBot.cs`

**主要职责**:
- 初始化和管理所有组件
- 处理 WebSocket 连接
- 分发消息到命令系统
- 管理插件生命周期

**关键属性**:

```csharp
public partial class MorningCatBot
{
    private OneBotClient _client;                          // OneBot 客户端
    private ModuleManager _moduleManager;                  // 模块管理器
    private ConfigManager _configManager;                  // 配置管理器
    private PluginConfigManager _pluginConfigManager;      // 插件配置管理
    private CommandRegistry _commandRegistry;              // 命令注册表
    private PluginCommandAPI _pluginCommandAPI;            // 插件命令 API
    private PluginDatabaseAPI _pluginDatabaseAPI;          // 插件数据库 API
    private PluginSignatureVerifier _signatureVerifier;    // 插件签名验证器
    private WebUIManager? _webUIManager;                   // WebUI 管理器
    
    public event EventHandler<UnhandledMessageEventArgs>? OnUnhandledMessage;
    public WebUIManager? WebUI => _webUIManager;
    public bool IsNewConfig => _configManager.IsNewConfig;
}
```

**生命周期方法**:

| 方法 | 说明 |
|------|------|
| `StartAsync()` | 启动机器人（初始化模块、连接 NapCat） |
| `StopAsync()` | 停止机器人（卸载模块、关闭连接） |
| `RestartAsync()` | 重启机器人（完全重建所有组件） |
| `RequestExit()` | 请求退出（触发停止流程） |
| `StartWebUIAsync()` | 启动 WebUI 管理界面 |
| `StopWebUIAsync()` | 停止 WebUI |

**其他方法**:

| 方法 | 说明 |
|------|------|
| `GetPluginMetadata(string moduleName)` | 获取指定插件的元数据 |
| `GetModuleNameByAssemblyName(string assemblyName)` | 通过程序集名获取模块名 |
| `GetWebUILoginUrl()` | 获取 WebUI 登录 URL |

### 2. CommandRegistry（命令注册表）

命令系统的核心，负责命令的注册、验证和执行。

**文件**: `MorningCat/Commands/CommandRegistry.cs`

**主要功能**:
- 命令注册与注销
- 参数解析与验证
- 权限检查
- 命令执行
- 插件命令执行（通过 PluginCommandAPI）

**命令注册示例**:

```csharp
_commandRegistry.RegisterCommand(
    "example",                    // 命令名称
    "示例命令",                   // 描述
    "example <参数> - 示例说明",  // 帮助文本
    new List<CommandParameter>    // 参数定义
    {
        new CommandParameter
        {
            Name = "参数",
            Description = "参数说明",
            IsRequired = true,
            Type = ParameterType.String
        }
    },
    HandleExampleCommand,         // 处理函数
    "ModuleName",                 // 模块名
    CommandPermission.Everyone,   // 权限级别
    CommandScope.All,             // 作用域
    requireAt: false,             // 是否需要@
    requireSlash: true            // 是否需要/前缀
);
```

**命令前缀规则**:

命令匹配支持四种模式，通过 `requireAt` 和 `requireSlash` 参数控制：

| requireAt | requireSlash | 触发条件 |
|-----------|-------------|---------|
| true | true | 群聊中必须@机器人且消息必须以/开头 |
| true | false | 群聊中必须@机器人，消息无需/前缀 |
| false | true | 消息必须以/开头，无需@ |
| false | false | 无需@也无需/，直接输入命令名即可触发 |

> 注意：设置为 `requireAt=false && requireSlash=false` 时，命令会对所有消息进行匹配，请确保命令名足够独特。

**插件命令 API**:

| 类型 | 说明 |
|------|------|
| `String` | 字符串 |
| `Integer` | 整数 |
| `Float` | 浮点数 |
| `Boolean` | 布尔值 |
| `At` | @某人 |
| `Reply` | 回复消息 |

**权限级别**:

| 级别 | 说明 |
|------|------|
| `Everyone` | 所有人可用 |
| `GroupAdmin` | 群管理员及以上 |
| `Owner` | 群主及以上 |
| `BotOwner` | 仅机器人主人 |

**作用域**:

| 作用域 | 说明 |
|--------|------|
| `All` | 私聊和群聊 |
| `PrivateOnly` | 仅私聊 |
| `GroupOnly` | 仅群聊 |

### 3. ModuleManager（模块管理器）

插件系统的核心，负责插件的加载、初始化和管理。

**文件**: `ModuleManager/Classmain.cs`

**主要功能**:
- 扫描和加载插件 DLL
- 依赖注入
- 依赖关系解析
- 插件生命周期管理

**加载流程**:

```
LoadAllModulesAsync()
    │
    ├── 1. LoadLibraries() - 加载 Library/ 目录下的依赖库
    │
    ├── 2. ScanModulesAsync() - 扫描模块目录下的 DLL
    │
    ├── 3. ParseDependenciesAsync() - 解析依赖关系
    │
    ├── 4. TopologicalSortWithLibraries() - 拓扑排序
    │
    └── 5. 初始化每个模块
            │
            ├── InjectDependencies() - 依赖注入
            │
            └── Init() - 调用初始化方法
```

**依赖注入机制**:

```csharp
// 注册服务
_moduleManager.RegisterService<OneBotClient>(_client);
_moduleManager.RegisterService<CommandRegistry>(_commandRegistry);
_moduleManager.RegisterService("ConfigManager", _pluginConfigManager);
_moduleManager.RegisterService<PluginDatabaseAPI>(_pluginDatabaseAPI);

// 插件中接收注入
public OneBotClient Client { get; set; }
public CommandRegistry CommandRegistry { get; set; }
```

### 4. PluginConfigManager（插件配置管理）

管理插件的配置文件，支持 YAML 格式。实现了 `IPluginConfigManager` 接口。

**文件**: `MorningCat/Config/PluginConfigManager.cs`

**主要方法**:

| 方法 | 说明 |
|------|------|
| `GetConfigAsync<T>()` | 获取配置对象（不存在时自动创建默认配置文件） |
| `SetConfigAsync<T>()` | 保存配置对象 |
| `GetValueAsync<T>()` | 获取配置值（支持嵌套路径） |
| `SetValueAsync<T>()` | 设置配置值 |
| `ConfigExistsAsync()` | 检查配置是否存在 |
| `DeleteConfigAsync()` | 删除配置 |
| `GetRegisteredConfigs()` | 获取插件已注册的配置列表 |
| `SetCurrentModule()` | 设置当前加载的模块名（用于映射） |
| `ClearCurrentModule()` | 清除当前模块名 |
| `ResolvePluginName()` | 将模块名解析为插件名 |

**ModuleName 到 PluginName 映射机制**:

当插件的类名（ModuleName）与配置中使用的插件名不一致时，PluginConfigManager 会自动建立映射关系。这在 WebUI 获取插件配置时尤为重要。

```
模块加载过程:
    │
    ├── ModuleManager 触发 OnProgressUpdated 事件
    │   └── 状态为 "Initializing" 时，调用 SetCurrentModule(moduleName)
    │
    ├── 插件在 Init() 中调用 GetConfigAsync("PluginName", "config")
    │   └── RegisterConfig() 检测到 _currentModuleName != "PluginName"
    │       └── 自动建立映射: _moduleNameToPluginName[moduleName] = pluginName
    │
    └── ModuleManager 触发状态为 "Done" 时，调用 ClearCurrentModule()
```

**配置文件位置**: `Config/{PluginName}-{ConfigName}.yml`

**自动创建默认配置**: 当调用 `GetConfigAsync` 时配置文件不存在，会自动使用 `defaultValue` 创建默认配置文件，确保 WebUI 等外部工具始终能获取到配置。

**使用示例**:

```csharp
// 获取配置（不存在时自动创建默认配置文件）
var config = await _configManager.GetConfigAsync<MyConfig>("MyPlugin", "config");

// 保存配置
await _configManager.SetConfigAsync("MyPlugin", "config", config);

// 获取嵌套值
var value = await _configManager.GetValueAsync<string>("MyPlugin", "config", "database.host");
```

### 5. OneBotClient（OneBot 客户端）

OneBot 协议的客户端实现，负责与 NapCat 通信。

**文件**: `Lib/OneBotLib.md`（文档）

**主要功能**:
- WebSocket 连接管理
- API 调用
- 事件处理

**常用 API**:

| API | 说明 |
|-----|------|
| `SendPrivateMsgAsync()` | 发送私聊消息 |
| `SendGroupMsgAsync()` | 发送群消息 |
| `GetGroupListAsync()` | 获取群列表 |
| `GetGroupMemberListAsync()` | 获取群成员列表 |
| `SetGroupBanAsync()` | 禁言群成员 |
| `DeleteMsgAsync()` | 撤回消息 |
| `SetMsgEmojiLikeAsync()` | 消息表情回应 |

## 消息处理流程

```
收到消息
    │
    ▼
OnMessageReceived()
    │
    ├── INFO: 接受来自昵称(QQ号), 群组:群号 的消息：内容
    │
    ├── DEBUG: [群名字]发送人: 内容
    │
    ▼
HandleMessageAsync()
    │
    ├── 检查消息是否为空 → 空消息直接返回
    │
    ├── 检查屏蔽用户 → BlockedUsers 包含 UserId 则直接忽略
    │
    ├── 检查屏蔽群 → BlockedGroups 包含 GroupId 则直接忽略
    │
    ├── MessageHelper.CleanMessageText() 清理消息文本
    │
    ├── MessageHelper.RemoveAtSegments() 移除 @ 和 CQ 码
    │
    ├── MessageHelper.CheckIsAtBot() 检查是否@机器人
    │
    ├── 检查是否有 / 前缀或 @机器人
    │
    ▼
CommandRegistry.ExecuteCommandAsync()
    │
    ├── MessageHelper.CleanMessageText() 清理命令文本
    │
    ├── MessageHelper.CheckIsAtBot() 检查是否@机器人
    │
    ├── 查找命令
    │
    ├── 检查权限
    │
    ├── 检查作用域
    │
    ├── 验证参数
    │
    ├── 解析参数
    │
    ▼
执行命令处理函数
    │
    ├── 如果命令已处理 → DEBUG: 处理PlainText=..., UserId=..., GroupId=..., Command=...
    │
    ├── 如果命令未处理
    │
    ▼
触发 OnUnhandledMessage 事件
```

## 事件系统

### 内置事件

| 事件 | 说明 |
|------|------|
| `OnMessage` | 收到消息 |
| `OnPrivateMessage` | 收到私聊消息 |
| `OnGroupMessage` | 收到群消息 |
| `OnLifecycle` | 生命周期事件 |
| `OnHeartbeat` | 心跳事件 |
| `OnConnectionStateChanged` | 连接状态变更 |

### 未处理消息事件

当消息不匹配任何命令时，触发 `OnUnhandledMessage` 事件：

```csharp
_bot.OnUnhandledMessage += (sender, e) =>
{
    // e.Message - 原始消息对象
    // e.PlainText - 纯文本内容
    // e.UserId - 用户 ID
    // e.GroupId - 群 ID（群消息时）
    // e.IsGroupMessage - 是否群消息
};
```

## 插件元数据

### 通过 PluginMetadataAttribute 声明（推荐）

使用 `[PluginMetadata]` 特性在插件类上声明元数据：

```csharp
using MorningCat.PluginAPI;

[PluginMetadata(
    DisplayName = "我的插件",
    Author = "作者名",
    Website = "https://github.com/example",
    Description = "插件描述",
    IconBase64 = ""
)]
public class MyPluginModule : ModuleBase
{
    // ...
}
```

### 通过回调上报（旧版，已弃用）

插件也可以通过回调上报元数据，此方式仍可使用但不推荐：

```csharp
_setMetadata?.Invoke(
    "ModuleName",        // 模块类名
    "显示名称",          // 展示名称
    "作者",              // 作者
    "https://...",       // 网站
    "描述",              // 描述
    "base64_icon",       // Base64 图标
    new[] { "DepPlugin" }, // 插件依赖
    new[] { "Lib.dll" }    // 库依赖
);
```

元数据会被缓存到 `Modules/.plugin_metadata.json`。

### PluginMetadata 类

| 属性 | 类型 | 说明 |
|------|------|------|
| `ModuleName` | `string` | 模块类名 |
| `DisplayName` | `string` | 展示名称 |
| `Author` | `string` | 作者 |
| `Website` | `string` | 网站 |
| `Description` | `string` | 描述 |
| `IconBase64` | `string` | Base64 编码的图标 |
| `PluginDependencies` | `List<string>` | 插件依赖列表 |
| `LibraryDependencies` | `List<string>` | 库依赖列表 |

## 配置系统

### 主配置文件 (config.yml)

```yaml
napcat_server_url: "ws://127.0.0.1:7892"
napcat_token: "your_token"
owner_qq: 123456789
admin_qqs:
  - 987654321
blocked_users:
  - 111111111
blocked_groups:
  - 222222222
modules_directory: "Modules"
reconnect_delay: 5
auto_load_modules: true
plugin_signature_public_key: ""
webui:
  enabled: true
  port: 8080
  username: "admin"
  password: "admin123"
database:
  type: "sqlite"
  connection_string: ""
```

**配置项说明**:

| 配置项 | 类型 | 说明 |
|--------|------|------|
| `napcat_server_url` | `string` | NapCat WebSocket 地址 |
| `napcat_token` | `string` | NapCat 访问令牌 |
| `owner_qq` | `long` | 机器人主人 QQ 号 |
| `admin_qqs` | `List<long>` | 管理员 QQ 号列表 |
| `blocked_users` | `List<long>` | 屏蔽的用户 QQ 号列表（消息将被忽略） |
| `blocked_groups` | `List<long>` | 屏蔽的群号列表（群消息将被忽略） |
| `modules_directory` | `string` | 插件目录路径 |
| `reconnect_delay` | `int` | 重连延迟（秒） |
| `auto_load_modules` | `bool` | 是否自动加载模块 |
| `plugin_signature_public_key` | `string` | 插件签名验证公钥（自动从远程拉取） |
| `webui.enabled` | `bool` | 是否启用 WebUI |
| `webui.port` | `int` | WebUI 端口 |
| `webui.username` | `string` | WebUI 用户名 |
| `webui.password` | `string` | WebUI 密码 |
| `database.type` | `string` | 数据库类型（sqlite/sql），默认 sqlite |
| `database.connection_string` | `string` | SQL Server 连接字符串 |

**BotConfig 辅助方法**:

| 方法 | 说明 |
|------|------|
| `IsOwner(long qq)` | 判断是否为机器人主人 |
| `IsAdmin(long qq)` | 判断是否为管理员（包含主人） |

### 插件配置文件

插件配置存放在 `Config/` 目录：

```yaml
# Config/MyPlugin-config.yml
# MyPlugin 插件配置文件
# 配置名称: config
# 最后更新: 2026-04-19 22:00:00
setting1: value1
setting2: 123
nested:
  key: value
```

## 重连机制

当 WebSocket 连接断开时，MorningCat 会自动尝试重连并更新所有组件的客户端引用。

```
连接断开
    │
    ├── OnConnectionStateChanged 触发
    │   └── 检测到 Disconnected 状态
    │
    ├── StartReconnectTimer() 启动重连定时器
    │   └── 间隔: config.ReconnectDelay 秒
    │
    ▼
TryReconnectAsync()
    │
    ├── 1. 取消旧客户端事件订阅
    │
    ├── 2. 关闭旧客户端连接
    │
    ├── 3. 创建新 OneBotClient 实例
    │
    ├── 4. 重新订阅事件
    │
    ├── 5. 更新 CommandRegistry 的客户端引用
    │
    ├── 6. UpdateBuiltinModulesClient() 更新所有模块的客户端引用
    │   ├── 更新内置模块 (Help, Plugin, System, Set)
    │   └── 遍历所有已加载插件，通过反射更新 Client 属性
    │       └── 查找类型为 OneBotClient 且可写的 Client 属性
    │
    ├── 7. ConnectSync() 尝试连接
    │
    └── 8. 等待认证（15秒超时）
        ├── 成功 → 停止重连定时器
        └── 失败 → 下次定时器触发时重试
```

**UpdateBuiltinModulesClient 的工作原理**:

重连后，新创建的 `OneBotClient` 实例需要更新到所有已加载的插件中。框架通过反射查找插件中类型为 `OneBotClient` 且具有 `public set` 访问器的 `Client` 属性，并自动更新其值。这确保了插件在重连后仍能正常发送消息。

## 插件签名验证

MorningCat 支持对插件 DLL 进行 RSA 签名验证，确保插件来源可信。

**文件**: `MorningCat/Security/PluginSignatureVerifier.cs`

**验证流程**:

```
启动时
    │
    ├── FetchPublicKeyAsync() 从远程拉取公钥
    │   └── 保存到 config.yml 的 plugin_signature_public_key
    │
    ├── VerifyPluginSignatures() 遍历所有插件 DLL
    │   ├── 跳过 Library/ 目录下的库文件
    │   ├── 跳过 .disabled 文件
    │   └── 对每个 DLL 调用 VerifyDll()
    │       ├── 读取 DLL 文件末尾的签名数据
    │       ├── 使用 RSA 公钥验证 SHA256 签名
    │       ├── 验证通过 → 正常加载
    │       └── 验证失败 → 重命名为 .dll.disabled 禁用插件
    │
    └── 测试模式下跳过所有签名验证
```

**签名数据格式**: 签名数据附加在 DLL 文件末尾，格式为 `[DLL内容][Base64签名数据][4字节签名长度]`。

## PluginCommandAPI

允许插件在代码中执行已注册的命令，支持指定权限级别。

**文件**: `MorningCat/PluginAPI/PluginCommandAPI.cs`

**主要功能**:
- 枚举已注册命令（按权限过滤）
- 以指定权限级别执行命令
- 支持命令行和命令名+参数两种调用方式

**执行方式**:

| 方法 | 说明 |
|------|------|
| `ExecuteAsNormal(message, commandLine)` | 以普通用户权限执行 |
| `ExecuteAsGroupAdmin(message, commandLine)` | 以群管理员权限执行 |
| `ExecuteAsBotOwner(message, commandLine)` | 以机器人主人权限执行 |
| `ExecuteWithPermission(message, permission, commandLine)` | 以自定义权限执行 |

**返回值**: `PluginCommandResult`，包含 `Success`（是否成功）和 `ErrorMessage`（错误信息）。

## PluginDatabaseAPI

插件数据库 API，允许插件使用数据库存储数据，支持 SQLite 和 SQL Server。

**文件**: `MorningCat/PluginAPI/PluginDatabaseAPI.cs`

**主要功能**:
- 通过 ID 和插件类名获取数据库实例
- 自动创建 SQLite 数据库文件
- 提供标准的 SQL 执行接口（同步/异步）
- 支持参数化查询

**数据库类型**:

| 类型 | 配置值 | 说明 |
|------|--------|------|
| SQLite | `sqlite` | 默认类型，自动在 `Database/` 目录创建 `.db` 文件 |
| SQL Server | `sql` | 需配置连接字符串，所有插件共享同一数据库 |

**获取数据库**:

```csharp
IPluginDatabase db = _pluginDatabaseAPI.GetDatabase("data", "MyPluginModule");
```

**数据库文件命名规则**: `{id}-{pluginClassName}.db`

**IPluginDatabase 接口方法**:

| 方法 | 说明 |
|------|------|
| `ExecuteNonQuery(sql, params)` | 执行非查询语句，返回受影响行数 |
| `ExecuteScalar(sql, params)` | 执行标量查询，返回第一行第一列 |
| `Query(sql, params)` | 查询数据，返回字典列表 |
| `QueryTable(sql, params)` | 查询数据，返回 DataTable |
| `CreateParameter(name, value)` | 创建数据库参数 |
| `GetTableNames()` | 获取所有表名 |
| `GetColumns(tableName)` | 获取表列信息 |

以上方法均有对应的 `Async` 版本。

**配置**:

```yaml
database:
  type: "sqlite"              # sqlite 或 sql
  connection_string: ""        # SQL Server 连接字符串
```

## WebUI 管理界面

MorningCat 内置 Web 管理界面，提供可视化的机器人管理功能。

**文件**: `MorningCat/WebUI/WebUIManager.cs`

**主要功能**:
- 插件管理（查看、启用、禁用、卸载）
- 实时日志查看
- 系统配置管理
- 机器人状态监控
- 插件市场（安装、更新插件）
- 数据库管理（查看插件数据库、在线执行 SQL）
- 消息监控与发送（实时消息、群聊/私聊发送、CQ 码支持）

**架构**:

```
WebUIManager
    ├── WebUIServer (ASP.NET Core Minimal API)
    │   ├── 认证 API (/api/auth/*)
    │   ├── 系统 API (/api/system/*)
    │   ├── 插件 API (/api/plugin/*)
    │   ├── 配置 API (/api/config/*)
    │   ├── 日志 API (/api/log/*)
    │   ├── 数据库 API (/api/database/*)
    │   └── 消息 API (/api/message/*)
    │
    ├── ISystemInfoProvider - 系统信息提供者
    ├── IBotInfoProvider - 机器人信息提供者
    ├── IPluginInfoProvider - 插件信息提供者
    ├── ILogProvider - 日志提供者
    ├── IConfigProvider - 配置提供者
    ├── IDatabaseInfoProvider - 数据库信息提供者
    └── IMessageSendProvider - 消息发送提供者
```

**前端技术栈**: React + TypeScript + Vite + HeroUI

## Electron GUI

MorningCat 支持通过 Electron 提供原生桌面 GUI，加载 WebUI 编译产物实现本地管理界面。

**文件**: `MorningCat.GUI/GuiBridge.cs`、`MorningCat.GUI/electron/`

**架构**:

```
MorningCatBot
    └── GuiManager (C# 进程管理)
            │
            ├── FindElectronApp() - 查找 Electron 可执行文件和 app 目录
            ├── Show() - 启动 Electron 进程，传入 --webui-port 参数
            └── Shutdown() - 终止 Electron 进程

Electron 主进程 (main.js)
    ├── 性能优化（禁用 GPU、限制渲染进程、限制 V8 内存）
    ├── 加载 WebUI（http://127.0.0.1:{port}/webui/）
    ├── preload.js - 安全桥接（窗口控制 API）
    └── 系统原生边框（可拖动、缩放）
```

**配置**: 在 `config.yml` 中设置 `enable_gui: true` 启用。

**性能优化项**:
- 禁用硬件加速和 GPU 进程
- 渲染进程限制为 1
- V8 内存限制 128MB
- 禁用拼写检查、开发者工具、扩展、插件等非必要功能
