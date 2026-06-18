# MorningCat 插件开发避坑索引

本文档整理 MCT 框架插件开发中的常见坑点，按症状速查。

> 注意：部分问题可能不是框架本身的 bug，而是插件代码的逻辑错误，请注意区分。

---

## 目录

- [插件加载类](#插件加载类)
- [配置类](#配置类)
- [命令类](#命令类)
- [数据库类](#数据库类)
- [依赖注入类](#依赖注入类)
- [HTTP 服务类](#http-服务类)
- [JSON 序列化类](#json-序列化类)
- [平台适配器类](#平台适配器类)
- [消息发送类](#消息发送类)

---

## 插件加载类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 插件 DLL 存在但不被加载 | 签名验证失败，被重命名为 `.disabled` | 检查日志，确认签名 |
| `LoadFailed:xxx.dll\|Unable to load...` | NuGet 依赖的 DLL 不在 Library 目录 | 复制 DLL 到 Library + 声明 `GetLibraryDependencies` |
| `MissingLibraryDependency:Module->Lib` | `GetLibraryDependencies` 声明了但文件不存在 | 确保 DLL 在 `Modules/Library/` |
| `MissingPluginDependency:Module->Dep` | 依赖的插件不存在 | 安装依赖插件或移除依赖声明 |
| `CircularDependencyDetected` | A 依赖 B，B 依赖 A | 消除循环依赖 |
| `InitFailed:Module\|...` | Init 方法中抛出异常 | 检查 Init 逻辑 |
| 插件加载成功但属性全为 null | 依赖注入失败 | 检查属性类型和 set 访问器 |

### NuGet 依赖未声明导致加载失败

这是最常见的坑。插件通过 NuGet 引用第三方包后，编译输出的 DLL 部署时不在 `Modules/Library/` 目录下，运行时找不到程序集。

**症状**：
```
LoadFailed:MyPlugin.dll|Unable to load one or more of the requested types.
Could not load file or assembly 'SixLabors.ImageSharp, Version=...'.
```

**解决方案**：

1. 将 NuGet 包的 DLL 复制到 `Modules/Library/` 目录
2. 在插件中声明库依赖：

```csharp
public override IEnumerable<string> GetLibraryDependencies()
{
    return new[] { "SixLabors.ImageSharp.dll", "SixLabors.ImageSharp.Drawing.dll" };
}
```

### 插件类名与文件名不一致

模块管理器通过反射扫描 DLL 中的类，类名是模块的唯一标识。如果两个 DLL 中有同名类，只有先加载的会生效。

### AssemblyLoadContext 隔离

插件在独立的 `AssemblyLoadContext` 中加载，这意味着：

- 同一类型在不同 ALC 中可能被视为不同类型
- 插件间的强类型交互需要通过 `Modules/Library/` 中的共享接口 DLL
- 反射调用不受 ALC 隔离限制

---

## 配置类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 配置文件没生成 | 没传 `new MyConfig()` 作为 defaultValue | 传默认实例 |
| 配置文件存在但属性值全是默认 | YAML 键名与 C# 属性名不匹配 | 使用下划线命名或 `[YamlMember]` |
| 配置修改后不生效 | 修改了内存对象但没调用 `SetConfigAsync` | 修改后保存 |
| 反序列化异常但无报错 | 框架 catch 吞掉了异常 | 检查日志中 `加载配置文件失败` |
| YamlDotNet 版本冲突 | 插件 NuGet 引用的版本与框架不同 | 不要 NuGet 引用 YamlDotNet |
| 保存配置后 YAML 变成 value_kind 垃圾 | `Dictionary<string, object>` 中含 `JsonElement` | 框架已内置转换；已损坏的配置需删除重新生成 |

### 反序列化失败：配置文件存在但加载返回空对象

MCT 使用 `YamlDotNet` 的 `UnderscoredNamingConvention`（下划线命名约定）进行反序列化：

- C# 属性 `MySetting` → YAML 中需要 `my_setting`
- 驼峰命名 `MySetting` 不会自动匹配

**方案一：使用下划线命名（推荐）**
```csharp
public class MyPluginConfig
{
    public string MySetting { get; set; } = "default";
}
```
```yaml
my_setting: "hello"
```

**方案二：使用 YamlMember 特性指定别名**
```csharp
[YamlMember(Alias = "mySetting")]
public string MySetting { get; set; } = "default";
```

### 默认配置没生成

`GetConfigAsync` 的 `defaultValue` 在配置文件不存在时会被使用，但**只有当 `defaultValue` 不为 `null` 时**才会生成默认文件。

```csharp
// 正确：会生成默认配置
var config = await GetConfigAsync<MyPluginConfig>("MyPlugin", "config");

// 错误：defaultValue 是 null，不会生成默认配置
var config = await GetConfigAsync<MyPluginConfig>("MyPlugin", "config", null);
```

### 不要跨插件读取配置

插件在初始化期间，框架会记录当前模块名与配置插件名的映射关系。如果插件 A 调用 `ConfigManager.GetValueAsync("OtherPlugin", "config", ...)` 读取其他插件的配置，会导致模块名映射被错误覆盖。

**正确做法**：如果需要读取主配置（如 `owner_qq`），应在自己的配置类中定义对应字段，或通过 `Bot` 实例获取。

---

## 命令类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 命令注册成功但不响应 | `requireAt`/`requireSlash` 条件不满足 | 检查触发方式 |
| 无前缀命令不响应 | 旧版代码有强制拒绝逻辑 | 确保使用最新版框架 |
| 命令权限不足但用户是管理员 | `GroupAdmin` 权限在私聊中只有 BotOwner 能用 | 改用 `Everyone` 或在群聊中使用 |
| 命令重复注册失败 | 同名命令已存在 | 更换命令名 |
| 插件卸载后命令仍存在 | Exit 中没调用 `UnregisterModuleCommands` | 添加注销调用 |
| 命令执行报错但用户无感知 | handler 异常被框架捕获但不通知用户 | handler 中自行 try-catch 并发送消息 |
| `GroupId` 为 null 导致报错 | 命令作用域不是 `GroupOnly`，私聊时 GroupId 为 null | 使用 `GroupOnly` 或判空 |

### 无前缀命令的坑

当 `requireAt=false` 且 `requireSlash=false` 时，命令会对所有消息进行前缀匹配，容易误触发。必须确保命令名足够独特（如 `#服务器`、`#玩家列表`），不要用常见词如 `help`、`status`。

### 命令名大小写

注册时自动转小写存储，匹配时也自动转小写。用户输入 `/BanList` 和 `/banlist` 效果相同。

### Exit 不注销命令

```csharp
public override async Task Exit()
{
    // 必须调用！否则命令残留
    _commandRegistry?.UnregisterModuleCommands("MyPluginModule");
}
```

如果忘记调用 `UnregisterModuleCommands`，插件卸载后命令仍然存在，但 handler 引用的插件实例已被释放，执行时会抛异常。

### handler 中的异常

handler 中抛出的异常会被框架捕获并记录日志，但**不会向用户发送任何提示**。如果需要用户感知错误，在 handler 中自行 try-catch 并发送消息。

---

## 数据库类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 数据库文件找不到 | 文件在 `{运行目录}/Database/` 不在 `Modules/` | 检查正确路径 |
| `ExecuteScalar` 返回空字符串而非 null | 返回了 `DBNull.Value` | 判断 `result == null \|\| result == DBNull.Value` |
| SQL 语法错误 | SQLite 和 SQL Server 语法差异 | 使用通用 SQL 或按类型分支 |
| 数据库锁 | SQLite 并发写入 | 使用异步方法 + 避免频繁写入 |

### DBNull 处理

`Query` 返回的字典中，数据库 NULL 值会被转为 `null`。但 `ExecuteScalar` 可能返回 `DBNull.Value`：

```csharp
// 安全做法
if (result == null || result == DBNull.Value)
    return null;
return result.ToString();
```

### SQLite 与 SQL Server 语法差异

| 功能 | SQLite | SQL Server |
|------|--------|------------|
| 建表不存在才创建 | `CREATE TABLE IF NOT EXISTS` | 需要手动 `IF NOT EXISTS` 判断 |
| 插入或替换 | `INSERT OR REPLACE` | `MERGE` 或 `IF EXISTS` |
| 分页 | `LIMIT n` | `TOP n` 或 `OFFSET-FETCH` |

### 避免频繁的数据库操作

不要每条消息都写入数据库，应批量写入或定时写入。

---

## 依赖注入类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 属性为 null | 类型不匹配或没有 public set | 精确匹配类型 + public set |
| MDC 为 null | 跨 ALC 类型不兼容 | 确保插件 ALC 优先从主 ALC 解析核心程序集 |
| 重连后发消息失败 | 缓存了旧的 Client 引用 | 使用 MDC.SendAsync 代替直接引用 Client |
| 构造函数中属性为 null | 注入在 Init 之前，构造函数之后 | 在 Init 中使用注入属性 |
| 全名相同但注入失败 | 同一程序集在不同 ALC 中被加载为不同类型 | 检查 Modules/Library/ 下是否有核心 DLL 副本 |

### 不要直接使用 OneBotClient 发消息

推荐使用 `MessageDistributionCore`（MDC）代替 `OneBotClient` 发送消息，原因：

1. **跨 ALC 类型不兼容**：`OneBotClient` 定义在 `OneBotLib.dll` 中，如果 `Modules/Library/` 下有一份副本，插件 ALC 可能加载到不同副本
2. **平台耦合**：直接使用 `OneBotClient` 将插件绑定到 OneBot 平台，无法跨平台
3. **重连问题**：重连后旧的 Client 引用会失效，MDC 内部自动处理

```csharp
// 错误
public OneBotClient Client { get; set; }
await Client.SendGroupMsgAsync(groupId, text);

// 正确
public MessageDistributionCore MDC { get; set; }
await MDC.SendAsync(message, builder => builder.Text(text));
```

### 注入顺序

依赖注入在 `Init()` 调用之前完成。`Init()` 中可以安全使用注入的属性，但构造函数中不能（还是 null）。

### 属性名约定

虽然注入是按类型匹配的，但某些核心服务要求特定的属性名才能正确注入：

```csharp
// 错误：使用 Bot 作为属性名
public MorningCatBot Bot { get; set; }  // 注入失败

// 正确：使用 MorningCatBot
public MorningCatBot MorningCatBot { get; set; }  // 注入成功
```

---

## HTTP 服务类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| Linux 上 HttpListener 启动失败 | 使用了 `http://0.0.0.0:port/` 前缀 | 改用 `http://+:{port}/` 前缀 |
| 插件卸载时 "Cannot access a disposed object" | HttpListener 已 Dispose 但仍在被访问 | Stop/Close 时加 try-catch |
| 前端 JS 发送 JSON 中数字字段被后端反序列化报错 | OneBot 的 `group_id` 等字段是 long，但 C# 模型定义为 string | 使用自定义 `JsonConverter` 或前端确保发送字符串 |

---

## JSON 序列化类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| `The JSON value could not be converted to System.String. Path: $.group_id` | OneBot API 返回的 `group_id` 是数字类型，但 C# 模型定义为 `string` | 添加 `[JsonConverter(typeof(FlexibleStringConverter))]` 或前端确保发送字符串 |
| 反序列化时数字自动丢失精度 | `long` 值超过 JS `Number.MAX_SAFE_INTEGER` | 前端用字符串传递，后端用 `FlexibleStringConverter` 兼容 |

### FlexibleStringConverter

兼容数字和字符串的自定义转换器：

```csharp
public class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetInt64().ToString();
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString();
        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
```

---

## 平台适配器类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 命名空间冲突："GroupJoinRequest is an ambiguous reference" | OneBotLib 和 PlatformAbstraction 都定义了同名类 | 使用完全限定名 `MorningCat.PlatformAbstraction.GroupJoinRequest` |
| `PlatformId` 无法隐式转 string | `PlatformId` 是枚举，不能直接赋值给 string | 使用 `.ToString()` 转换 |
| `Enum.Parse` 编译错误 | `IsPlatformEnabled` 要求 `PlatformId` 枚举而非 string | 使用 `Enum.Parse<PlatformId>(str)` |

---

## 消息发送类

| 症状 | 可能原因 | 解决方案 |
|------|----------|----------|
| 旧代码 `SendGroupMsgAsync` 编译失败 | MDC 重构后不再暴露 OneBotClient 直接发消息 | 使用 `MDC.SendAsync()` + `IMessageBuilder` |
| DI 属性注入为 null | 属性名与文档约定不一致 | 严格按文档约定命名 DI 属性 |
