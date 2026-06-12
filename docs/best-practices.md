# MorningCat 开发注意事项与最佳实践

本文档总结了在开发 MorningCat 插件时需要注意的问题和最佳实践。

## 目录

- [项目引用与命名空间](#项目引用与命名空间)
- [常见问题与解决方案](#常见问题与解决方案)
- [依赖管理](#依赖管理)
- [性能优化](#性能优化)
- [安全考虑](#安全考虑)
- [代码规范](#代码规范)
- [调试技巧](#调试技巧)
- [常见错误码](#常见错误码)
- [重连处理](#重连处理)
- [PluginCommandAPI 使用](#plugincommandapi-使用)
- [PluginDatabaseAPI 使用](#plugindatabaseapi-使用)
- [消息屏蔽](#消息屏蔽)
- [插件元数据声明](#插件元数据声明)

## 项目引用与命名空间

### 必需引用

| 库文件 | 说明 |
|--------|------|
| `MorningCat.dll` | MorningCat 核心库 |
| `MorningCat.PlatformAbstraction.dll` | 跨平台消息抽象层 |
| `ModuleManagerLib.dll` | 模块管理器库 |
| `logs.dll` | 日志库 |

### 可选引用

| 库文件 | 说明 | 使用场景 |
|--------|------|----------|
| `OneBotLib.dll` | OneBot 协议实现库 | 需要使用 OneBot 特有消息段类型时 |

### 常用命名空间

```csharp
// 插件基类
using ModuleManagerLib;

// 日志
using Logging;

// 命令系统
using MorningCat.Commands;

// 配置管理
using MorningCat.Config;

// 事件参数
using MorningCat.Events;

// 插件命令 API
using MorningCat.PluginAPI;

// 机器人主类
using MorningCat;

// OneBot 客户端和模型（可选）
using OneBotLib;
using OneBotLib.Models;
using OneBotLib.MessageSegment;
```

## 常见问题与解决方案

### 1. 插件加载失败：找不到依赖库

**问题现象**：
```
模块加载错误: LoadFailed:MyPlugin.dll|Unable to load one or more of the requested types.
Could not load file or assembly 'OneBotLib, Version=1.0.3.0, Culture=neutral, PublicKeyToken=null'.
系统找不到指定的文件。
```

**原因**：
- 插件依赖的库没有放到 `Modules/Library/` 目录
- 插件没有正确声明库依赖

**解决方案**：

1. 确保依赖库放在正确位置：
```
Modules/
├── Library/
│   ├── OneBotLib.dll    ← 依赖库放这里
│   └── OtherLib.dll
└── MyPlugin.dll         ← 插件放这里
```

2. 在插件中声明库依赖：
```csharp
public override IEnumerable<string> GetLibraryDependencies()
{
    return new[] { "OneBotLib.dll" };
}
```

### 2. 依赖注入失败

**问题现象**：
- 插件中的 `_mdc`、`_commandRegistry` 等属性为 `null`
- 调用这些属性时抛出 `NullReferenceException`

**原因**：
- 属性类型与框架注册的服务类型不匹配
- 属性没有 `public set` 访问器
- 跨 ALC 类型不兼容（同一全名但来自不同程序集副本）

**解决方案**：

框架使用**类型匹配**进行依赖注入，属性的类型必须与框架注册的服务类型匹配，属性名可以自定义：

```csharp
// 正确：类型匹配，自动注入
public MessageDistributionCore MDC { get; set; }              // 推荐：跨平台消息发送
public CommandRegistry CommandRegistry { get; set; }           // 类型匹配 CommandRegistry
public PluginConfigManager ConfigManager { get; set; }         // 类型匹配 PluginConfigManager
public MorningCatBot Bot { get; set; }                         // 类型匹配 MorningCatBot
public PluginCommandAPI PluginCommandAPI { get; set; }         // 类型匹配 PluginCommandAPI

// 错误：set 访问器不是 public
public MessageDistributionCore MDC { get; private set; }

// 错误：类型不匹配
public object MDC { get; set; }  // 类型是 object，不是 MessageDistributionCore
```

**注意**：
- 属性必须有 `public` 的 `set` 访问器，框架才能注入
- 推荐使用 `MessageDistributionCore`（MDC）代替 `OneBotClient` 发送消息，避免跨 ALC 类型不兼容和平台耦合
- 如果属性全名匹配但注入仍失败，检查 `Modules/Library/` 下是否有多余的核心 DLL 副本

### 3. 命令无法触发

**问题现象**：
- 发送命令后没有响应
- 命令没有被识别

**排查步骤**：

1. **检查命令注册**：
```csharp
var success = _commandRegistry.RegisterCommand(...);
if (!success)
{
    Log.Error("命令注册失败");
}
```

2. **检查触发条件**：
   - `requireAt: true` 需要艾特机器人
   - `requireSlash: true` 需要 `/` 前缀
   - 群消息需要艾特或使用 `/` 前缀

3. **检查权限**：
   - `CommandPermission.BotOwner` 只有机器人主人可用
   - `CommandPermission.GroupAdmin` 只有群管理员可用

4. **检查作用域**：
   - `CommandScope.GroupOnly` 只在群聊有效
   - `CommandScope.PrivateOnly` 只在私聊有效

### 4. Reply 参数无法获取消息 ID

**问题现象**：
- 使用 `ParameterType.Reply` 类型参数时，获取不到消息 ID

**原因**：
- 用户没有回复消息
- 消息段解析问题

**解决方案**：

```csharp
private async Task HandleCommand(CommandContext context)
{
    if (!context.Parameters.TryGetValue("目标消息", out var messageIdStr))
    {
        await SendMessageAsync(context.Message, "请回复要操作的消息");
        return;
    }
    
    if (!long.TryParse(messageIdStr, out var targetMessageId))
    {
        await SendMessageAsync(context.Message, "目标消息ID无效");
        return;
    }
    
    // 使用 targetMessageId ...
}
```

### 5. 配置文件加载失败

**问题现象**：
```
加载配置文件失败: Exception during deserialization
```

**原因**：
- 配置文件格式错误
- 配置类结构与文件不匹配

**解决方案**：

1. 删除旧配置文件，让框架重新生成
2. 确保配置类有默认值：

```csharp
public class MyConfig
{
    public string Setting1 { get; set; } = "default";  // 提供默认值
    public int Setting2 { get; set; } = 100;
    public Dictionary<long, int> Data { get; set; } = new();  // 初始化集合
}
```

### 6. AI 无法执行命令

**问题现象**：
- ButterCat AI 可以列出命令，但无法执行
- AI 回复"我无法执行命令"

**原因**：
- 使用的 AI 模型太小（如 qwen2.5:0.5b）
- 模型工具调用能力不足

**解决方案**：

1. 使用更大的模型：
```bash
ollama pull qwen2.5:7b
```

2. 在配置中切换模型：
```
/aicfg set model qwen2.5:7b
```

## 依赖管理

### 插件依赖

插件可以依赖其他插件：

```csharp
public override IEnumerable<string> GetDependencies()
{
    return new[] { "OtherPluginModule" };  // 依赖的插件类名
}
```

**注意事项**：
- 依赖的插件必须存在，否则加载失败
- 避免循环依赖

### 库依赖

插件可以依赖共享库：

```csharp
public override IEnumerable<string> GetLibraryDependencies()
{
    return new[] { "Newtonsoft.Json.dll", "SomeLibrary.dll" };
}
```

**注意事项**：
- 库文件必须放在 `Modules/Library/` 目录
- 多个插件可以共享同一个库

### 依赖加载顺序

框架按以下顺序加载：

1. 加载 `Library/` 目录下的所有 DLL
2. 扫描模块目录下的插件
3. 解析依赖关系
4. 按拓扑顺序初始化插件

## 性能优化

### 1. 异步操作

所有 I/O 操作使用异步方法：

```csharp
// 正确
await _mdc.SendAsync(message, builder => builder.Text(text));
await _configManager.SetConfigAsync("Plugin", "config", config);

// 错误（会阻塞线程）
_mdc.SendAsync(message, builder => builder.Text(text)).Wait();
```

### 2. 避免频繁保存配置

```csharp
// 错误：每次操作都保存
public async Task IncrementCount(long userId)
{
    _config.Counts[userId]++;
    await SaveConfigAsync();  // 太频繁
}

// 正确：批量保存或定时保存
public async Task IncrementCount(long userId)
{
    _config.Counts[userId]++;
    _dirty = true;
}

public async Task Exit()
{
    if (_dirty)
        await SaveConfigAsync();
}
```

### 3. 使用 IMessageBuilder 发送复杂消息

```csharp
// 使用 IMessageBuilder 发送图片（跨平台）
await _mdc.SendAsync(message, builder => builder.ImageBase64(base64Data));

// 回复+AT+文本
await _mdc.SendAsync(message, builder => builder
    .Reply(message.MessageId)
    .At(message.SenderId)
    .Text("Hello!"));
```

### 4. 避免在循环中调用 API

```csharp
// 错误：循环调用 API
for (int i = 0; i < 100; i++)
{
    await _client.SetMsgEmojiLikeAsync(messageId, "297");
}

// 正确：限制循环次数或使用批量操作
int maxCount = Math.Min(count, 10);
for (int i = 0; i < maxCount; i++)
{
    await _client.SetMsgEmojiLikeAsync(messageId, "297");
    await Task.Delay(100);  // 添加延迟避免频率限制
}
```

## 安全考虑

### 1. 权限检查

始终检查用户权限：

```csharp
// 检查是否是管理员
private bool IsAdmin(MessageObject message)
{
    if (_commandRegistry.IsBotOwner(message.UserId ?? 0))
        return true;
    
    if (message.MessageType == "group" && message.Sender != null)
    {
        var role = message.Sender.Role;
        if (role == "owner" || role == "admin")
            return true;
    }
    
    return false;
}
```

### 2. 输入验证

验证所有用户输入：

```csharp
private async Task HandleCommand(CommandContext context)
{
    if (!context.Parameters.TryGetValue("数值", out var valueStr))
    {
        await SendMessageAsync(context.Message, "请提供数值");
        return;
    }

    if (!int.TryParse(valueStr, out var value))
    {
        await SendMessageAsync(context.Message, "无效的数值");
        return;
    }

    if (value < 0 || value > 10000)
    {
        await SendMessageAsync(context.Message, "数值必须在 0-10000 之间");
        return;
    }

    // 安全使用 value
}
```

### 3. 敏感信息保护

不要在日志中输出敏感信息：

```csharp
// 错误
Log.Info($"用户密码: {password}");

// 正确
Log.Info($"用户 {userId} 已登录");
```

### 4. 错误处理

妥善处理异常，避免泄露敏感信息：

```csharp
try
{
    await DoSomethingAsync();
}
catch (Exception ex)
{
    Log.Error($"操作失败: {ex.Message}");
    await SendMessageAsync(message, "操作失败，请稍后重试");
    // 不要把 ex.StackTrace 发送给用户
}
```

## 代码规范

### 1. 命名约定

| 类型 | 命名风格 | 示例 |
|------|----------|------|
| 类名 | PascalCase | `MyPluginModule` |
| 方法名 | PascalCase | `HandleCommand` |
| 属性名 | PascalCase | `ConfigManager` |
| 私有字段 | _camelCase | `_client` |
| 参数 | camelCase | `message` |
| 常量 | UPPER_CASE | `MAX_COUNT` |

### 2. 日志格式

使用统一的日志前缀：

```csharp
Log.Info($"[MyPlugin] 操作成功");
Log.Error($"[MyPlugin] 操作失败: {ex.Message}");
```

### 3. 异步方法命名

异步方法以 `Async` 结尾：

```csharp
private async Task SendMessageAsync(MessageObject message, string text);
private async Task LoadConfigAsync();
private async Task SaveConfigAsync();
```

### 4. 资源清理

在 `Exit()` 方法中清理资源：

```csharp
public override async Task Exit()
{
    // 取消订阅事件
    _bot.OnUnhandledMessage -= OnUnhandledMessage;
    
    // 注销命令
    _commandRegistry?.UnregisterModuleCommands("MyPluginModule");
    
    // 保存配置
    await SaveConfigAsync();
    
    // 释放资源
    _timer?.Dispose();
    
    Log.Info("[MyPlugin] 插件已卸载");
}
```

## 调试技巧

### 1. 启用详细日志

检查日志输出，了解加载过程：

```
[Scanning] 0/0 - 
[Initializing] 0/3 - ButterCatModule
已识别的模块：MyPlugin
已注册 5 个命令:
  [MyPlugin]
    /hello - 打招呼 (0个参数)
```

### 2. 检查命令注册

```csharp
public override async Task Init()
{
    var success = _commandRegistry.RegisterCommand(...);
    Log.Debug($"[MyPlugin] 命令注册结果: {success}");
    
    var cmd = _commandRegistry.GetCommand("hello");
    Log.Debug($"[MyPlugin] 命令是否存在: {cmd != null}");
}
```

### 3. 测试依赖注入

```csharp
public override async Task Init()
{
    Log.Debug($"[MyPlugin] MDC: {(_mdc != null ? "已注入" : "未注入")}");
    Log.Debug($"[MyPlugin] CommandRegistry: {(_commandRegistry != null ? "已注入" : "未注入")}");
    Log.Debug($"[MyPlugin] ConfigManager: {(_configManager != null ? "已注入" : "未注入")}");
}
```

### 4. 使用 try-catch 捕获异常

```csharp
private async Task HandleCommand(CommandContext context)
{
    try
    {
        // 命令处理逻辑
    }
    catch (Exception ex)
    {
        Log.Error($"[MyPlugin] 命令处理异常: {ex.Message}");
        Log.Debug($"[MyPlugin] 堆栈追踪:\n{ex.StackTrace}");
        await SendMessageAsync(context.Message, "命令执行出错");
    }
}
```

### 5. 检查配置文件

配置文件位于 `Config/{PluginName}-{ConfigName}.yml`：

```yaml
# MyPlugin-config.yml
# MyPlugin 插件配置文件
# 配置名称: config
# 最后更新: 2026-04-19 22:00:00

setting1: value1
setting2: 123
```

## 常见错误码

| 错误码 | 说明 | 解决方案 |
|--------|------|----------|
| `NoInitMethod:{ClassName}` | 模块缺少 Init 方法 | 实现 Init 方法 |
| `LoadFailed:{DllName}\|{Message}` | DLL 加载失败 | 检查依赖库 |
| `MissingPluginDependency:{Module}->{Dep}` | 依赖的插件不存在 | 安装依赖插件 |
| `MissingLibraryDependency:{Module}->{Lib}` | 依赖的库不存在 | 将库放入 Library 目录 |
| `CircularDependencyDetected` | 循环依赖 | 检查依赖关系 |
| `InitFailed:{Module}\|{Message}` | 初始化失败 | 检查 Init 方法 |
| `InitException:{Module}\|{Message}` | 初始化异常 | 检查 Init 方法中的异常 |

## 重连处理

### 使用 MDC 发送消息（推荐）

推荐使用 `MessageDistributionCore`（MDC）发送消息，MDC 内部会自动处理重连后的客户端引用更新：

```csharp
public MessageDistributionCore MDC
{
    get => _mdc;
    set => _mdc = value;
}

// 使用 MDC 发送消息，无需关心重连
await _mdc.SendAsync(message, builder => builder.Text(text));
```

### 避免缓存客户端引用

不要将客户端的引用缓存到其他变量中，重连后旧引用会失效：

```csharp
// 错误：缓存了客户端引用
private OneBotClient _cachedClient;

public override async Task Init()
{
    _cachedClient = Client;  // 重连后此引用过期
}

// 正确：始终通过 MDC 发送消息
public override async Task Init()
{
    await _mdc.SendAsync(message, builder => builder.Text("Hello"));
}
```

## PluginCommandAPI 使用

### 选择合适的权限级别

```csharp
// 查询类操作：使用 Normal 权限
var result = await _pluginCommandAPI.ExecuteAsNormal(message, "help");

// 管理类操作：使用 GroupAdmin 权限
var result = await _pluginCommandAPI.ExecuteAsGroupAdmin(message, "plugin list");

// 危险操作：使用 BotOwner 权限
var result = await _pluginCommandAPI.ExecuteAsBotOwner(message, "plugin unload SomePlugin");
```

### 处理执行结果

始终检查 `PluginCommandResult` 的 `Success` 属性：

```csharp
var result = await _pluginCommandAPI.ExecuteAsNormal(message, "help");
if (!result.Success)
{
    Log.Warning($"命令执行失败: {result.ErrorMessage}");
    await MessageHelper.SendAsync(_mdc, message, builder => builder.Text("命令执行失败"));
}
```

### 命令行方式 vs 参数数组方式

```csharp
// 命令行方式：适合用户输入的完整命令字符串
await _pluginCommandAPI.ExecuteAsNormal(message, "/help");

// 参数数组方式：适合代码中精确调用
await _pluginCommandAPI.ExecuteAsNormal(message, "help");
await _pluginCommandAPI.ExecuteAsBotOwner(message, "plugin", "list");
```

## PluginDatabaseAPI 使用

### 始终使用参数化查询

```csharp
// 正确：使用参数化查询，防止 SQL 注入
var param = _db.CreateParameter("@qq", userId);
await _db.QueryAsync("SELECT * FROM users WHERE qq = @qq", param);

// 错误：字符串拼接，存在 SQL 注入风险
await _db.QueryAsync($"SELECT * FROM users WHERE qq = {userId}");
```

### 使用异步方法

```csharp
// 正确：使用异步方法
await _db.ExecuteNonQueryAsync("INSERT INTO users (qq) VALUES (@qq)", param);

// 错误：使用同步方法会阻塞线程
_db.ExecuteNonQuery("INSERT INTO users (qq) VALUES (@qq)", param);
```

### 在 Init 中创建表

```csharp
public override async Task Init()
{
    _db = _dbAPI.GetDatabase("data", "MyPluginModule");

    // 使用 IF NOT EXISTS 避免重复创建
    await _db.ExecuteNonQueryAsync(
        "CREATE TABLE IF NOT EXISTS users (qq INTEGER PRIMARY KEY, count INTEGER DEFAULT 0)"
    );
}
```

### 处理 DBNull 值

```csharp
var result = await _db.QueryAsync("SELECT count FROM users WHERE qq = @qq", param);
if (result.Count > 0)
{
    var value = result[0]["count"];
    int count = value != null && value != DBNull.Value ? Convert.ToInt32(value) : 0;
}
```

### 注意 SQLite 和 SQL Server 的语法差异

```csharp
// SQLite 特有语法
"CREATE TABLE IF NOT EXISTS users (...)"           // IF NOT EXISTS
"INSERT OR REPLACE INTO users (...)"               // OR REPLACE
"SELECT * FROM users LIMIT 10"                     // LIMIT

// SQL Server 对应语法
"IF NOT EXISTS (SELECT * FROM ...)"                // 需要手动判断
"MERGE INTO ..."                                    // 或使用 IF EXISTS
"SELECT TOP 10 * FROM users"                       // TOP 代替 LIMIT
```

如果需要同时支持两种数据库，建议使用通用 SQL 语法或在配置中指定数据库类型。

### 避免频繁的数据库操作

```csharp
// 错误：每条消息都写入数据库
private async Task OnMessage(MessageObject message)
{
    await _db.ExecuteNonQueryAsync("INSERT INTO logs ...");
}

// 正确：批量写入或定时写入
private List<string> _pendingLogs = new();

private async Task OnMessage(MessageObject message)
{
    _pendingLogs.Add($"INSERT INTO logs ...");
}

private async Task FlushLogsAsync()
{
    if (_pendingLogs.Count == 0) return;
    foreach (var sql in _pendingLogs)
    {
        await _db.ExecuteNonQueryAsync(sql);
    }
    _pendingLogs.Clear();
}
```

## 消息屏蔽

框架在消息处理流程的最早阶段检查屏蔽列表，被屏蔽的用户或群的消息会被直接忽略。插件无需关心屏蔽逻辑，所有命令和事件都不会收到被屏蔽用户的消息。

在 `config.yml` 中配置屏蔽：

```yaml
blocked_users:
  - 123456789

blocked_groups:
  - 111111111
```

## 插件元数据声明

推荐使用 `[PluginMetadata]` 特性声明插件元数据，而非旧版的回调方式：

```csharp
using MorningCat.PluginAPI;

[PluginMetadata(
    DisplayName = "我的插件",
    Author = "作者名",
    Website = "https://github.com/example",
    Description = "插件描述"
)]
public class MyPluginModule : ModuleBase
{
    // ...
}
```

元数据会显示在 WebUI 插件管理页面和 `/plugin list` 命令中。
