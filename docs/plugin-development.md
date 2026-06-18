# MorningCat 插件开发指南

本文档详细介绍如何为 MorningCat 开发插件。

## 目录

- [快速开始](#快速开始)
- [项目引用说明](#项目引用说明)
- [命名空间说明](#命名空间说明)
- [插件结构](#插件结构)
- [核心概念](#核心概念)
- [命令开发](#命令开发)
- [配置管理](#配置管理)
  - [配置问题排查指南](#配置问题排查指南)
- [数据库使用](#数据库使用)
- [事件处理](#事件处理)
- [插件间依赖与通信](#插件间依赖与通信)
- [完整示例](#完整示例)
- [模块管理器（ModuleManager）](#模块管理器modulemanager)
- [日志系统](#日志系统)

## 快速开始

### 1. 创建项目

创建一个新的类库项目：

```bash
dotnet new classlib -n MyPlugin
cd MyPlugin
```

## 项目引用说明

### 必需引用

插件项目需要引用以下核心库：

| 库文件 | 说明 | 必需 |
|--------|------|------|
| `MorningCat.dll` | MorningCat 核心库，包含命令系统、配置管理等 | 是 |
| `ModuleManagerLib.dll` | 模块管理器库，包含 ModuleBase 基类 | 是 |
| `logs.dll` | 日志库，用于输出日志 | 是 |

### 可选引用

| 库文件 | 说明 | 使用场景 |
|--------|------|----------|
| `OneBotLib.dll` | OneBot 协议实现库 | 需要调用 OneBot API（发消息、群管理等） |

### 引用方式

#### 方式一：直接引用 DLL 文件

编辑 `.csproj` 文件：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- 必需引用 -->
    <Reference Include="MorningCat.dll">
      <HintPath>..\Lib\MorningCat.dll</HintPath>
    </Reference>
    <Reference Include="ModuleManagerLib.dll">
      <HintPath>..\Lib\ModuleManagerLib.dll</HintPath>
    </Reference>
    <Reference Include="logs.dll">
      <HintPath>..\Lib\logs.dll</HintPath>
    </Reference>
    
    <!-- 可选引用（需要调用 OneBot API 时） -->
    <Reference Include="OneBotLib.dll">
      <HintPath>..\Lib\OneBotLib.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

#### 方式二：引用项目（开发时推荐）

如果插件和 MorningCat 在同一个解决方案中：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MorningCat\MorningCat\MorningCat.csproj" />
    <ProjectReference Include="..\ModuleManager\ModuleManagerLib.csproj" />
  </ItemGroup>
</Project>
```

### 部署说明

编译后，将生成的插件 DLL 文件复制到 `Modules/` 目录：

```
MorningCat/
├── bin/Debug/net10.0/
│   ├── MorningCat.exe
│   └── Modules/
│       ├── Library/          # 依赖库目录
│       │   ├── OneBotLib.dll
│       │   └── logs.dll
│       └── MyPlugin.dll      # 插件文件放这里
```

**注意**：
- 插件 DLL 直接放在 `Modules/` 目录下，**不要**放在子目录中
- 依赖库（如 OneBotLib.dll）放在 `Modules/Library/` 目录下
- 如果插件依赖其他库，也需要放在 `Modules/Library/` 目录

## 命名空间说明

### 常用命名空间

| 命名空间 | 包含内容 | 使用场景 |
|----------|----------|----------|
| `MorningCat.Commands` | CommandRegistry, CommandParameter, CommandContext, ParameterType, CommandPermission, CommandScope | 注册命令、处理命令参数 |
| `MorningCat.Config` | PluginConfigManager, IPluginConfigManager | 插件配置管理 |
| `MorningCat.Events` | UnhandledMessageEventArgs | 未处理消息事件 |
| `MorningCat` | MorningCatBot | 机器人主类 |
| `ModuleManagerLib` | ModuleBase, InjectAttribute | 插件基类、依赖注入特性 |
| `Logging` | Log | 日志输出 |
| `OneBotLib` | OneBotClient, ApiResult | OneBot 客户端、API 结果 |
| `OneBotLib.Models` | MessageObject, SenderInfo, GroupInfo 等 | 消息对象、用户信息等数据模型 |
| `OneBotLib.MessageSegment` | MessageSegment | 消息段构建器 |
| `OneBotLib.Events` | MessageEventArgs, ConnectionStateChangedEventArgs 等 | 事件参数类 |

### 示例 using 语句

#### 基础插件（不需要调用 API）

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
```

#### 完整功能插件（需要调用 API）

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.Events;
using OneBotLib;
using OneBotLib.MessageSegment;
using OneBotLib.Models;
```

#### 仅处理未处理消息的插件

```csharp
using System;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat;
using MorningCat.Events;
using OneBotLib;
using OneBotLib.Models;
```

## 插件结构

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.PluginAPI;
using OneBotLib;

namespace MorningCat.Modules
{
    [PluginMetadata(
        DisplayName = "我的插件",
        Author = "作者名",
        Description = "插件描述"
    )]
    public class MyPluginModule : ModuleBase
    {
        private MessageDistributionCore _mdc = null!;
        private CommandRegistry _commandRegistry = null!;
        private PluginConfigManager _configManager = null!;
        
        public MessageDistributionCore MDC
        {
            get => _mdc;
            set => _mdc = value;
        }

        public CommandRegistry CommandRegistry
        {
            get => _commandRegistry;
            set => _commandRegistry = value;
        }

        public PluginConfigManager ConfigManager
        {
            get => _configManager;
            set => _configManager = value;
        }

        public override async Task Init()
        {
            RegisterCommands();
            Log.Info("[MyPlugin] 插件已加载");
            await Task.CompletedTask;
        }

        private void RegisterCommands()
        {
            _commandRegistry?.RegisterCommand(
                "hello",
                "打招呼",
                "hello - 向机器人打招呼",
                new List<CommandParameter>(),
                HandleHelloCommand,
                "MyPluginModule",
                CommandPermission.Everyone,
                CommandScope.All,
                requireAt: false,
                requireSlash: true
            );
        }

        private async Task HandleHelloCommand(CommandContext context)
        {
            var message = context.Message;
            await _mdc.SendAsync(message, builder => builder.Text("你好！"));
        }

        public override async Task Exit()
        {
            Log.Info("[MyPlugin] 插件已卸载");
            await Task.CompletedTask;
        }
    }
}
```

### 4. 编译和部署

```bash
dotnet build -c Debug
```

将编译生成的 `MyPlugin.dll` 复制到 `Modules/` 目录：

```bash
copy bin/Debug/net10.0/MyPlugin.dll ../MorningCat/bin/Debug/net10.0/Modules/
```

## 插件结构

### 必需实现

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `Init()` | `Task` | 模块初始化入口，**必须实现** |

### 可选实现

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `Exit()` | `Task` | 模块卸载时的清理方法 |
| `GetDependencies()` | `IEnumerable<string>` | 返回依赖的插件类名列表 |
| `GetLibraryDependencies()` | `IEnumerable<string>` | 返回依赖的库文件名列表 |

### 依赖注入属性

框架使用**类型匹配**进行依赖注入。属性的**类型**必须与框架注册的服务类型匹配，**属性名可以自定义**（但建议使用约定名称以便阅读）。

框架注册的服务类型：

| 服务类型 | 约定属性名 | 说明 |
|----------|------------|------|
| `MessageDistributionCore` | `MDC` | 消息分发核心，跨平台消息发送（推荐） |
| `CommandRegistry` | `CommandRegistry` | 命令注册表 |
| `PluginConfigManager` | `ConfigManager` | 配置管理器 |
| `MorningCatBot` | `Bot` | 机器人实例 |
| `ModuleManager` | - | 模块管理器 |
| `ConfigManager` (主配置) | - | 主配置管理器 |
| `PluginCommandAPI` | - | 插件命令 API |
| `PluginDatabaseAPI` | - | 插件数据库 API |
| `PluginApiService` | - | 插件 API 服务 |

**注入示例**：

```csharp
// 类型匹配注入（推荐）
// 属性名可以是任意的，但类型必须匹配
public MessageDistributionCore MDC { get; set; }           // 推荐：跨平台消息发送
public CommandRegistry CommandRegistry { get; set; }        // 类型匹配，自动注入
public PluginConfigManager ConfigManager { get; set; }      // 类型匹配，自动注入
public MorningCatBot Bot { get; set; }                      // 类型匹配，自动注入
public PluginDatabaseAPI PluginDatabaseAPI { get; set; }    // 类型匹配，自动注入

// 也可以使用字段（不推荐，但有效）
private MessageDistributionCore _mdc;
public MessageDistributionCore MDC
{
    get => _mdc;
    set => _mdc = value;  // 框架会调用 setter
}
```

**注意**：
- 属性必须有 `public` 的 `set` 访问器，框架才能注入
- 推荐使用 `MessageDistributionCore`（MDC）代替 `OneBotClient` 发送消息，避免跨 ALC 类型不兼容和平台耦合
- 如果属性全名匹配但注入仍失败，检查 `Modules/Library/` 下是否有多余的核心 DLL 副本

### 元数据声明

使用 `[PluginMetadata]` 特性声明插件元数据（推荐）：

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

**PluginMetadata 特性属性：**

| 属性 | 类型 | 说明 |
|------|------|------|
| DisplayName | string | 展示名称 |
| Author | string | 作者 |
| Website | string | 网站 |
| Description | string | 描述 |
| IconBase64 | string | Base64 编码的图标数据 |
| Tags | string[] | 标签列表 |

**旧版回调方式（已移除）：**

旧版通过 `SetMetadataCallback` 属性回调上报元数据，此方式已被移除，框架不再注入此属性。请使用 `[PluginMetadata]` 特性代替。

## 核心概念

### ModuleBase 基类

建议继承 `ModuleBase` 基类：

```csharp
public abstract class ModuleBase
{
    public abstract Task Init();
    public virtual Task Exit() => Task.CompletedTask;
    public virtual IEnumerable<string> GetDependencies() => Array.Empty<string>();
    public virtual IEnumerable<string> GetLibraryDependencies() => Array.Empty<string>();
}
```

### 依赖声明

#### 插件依赖

依赖其他插件模块：

```csharp
public override IEnumerable<string> GetDependencies()
{
    return new[] { "OtherPluginModule" };
}
```

#### 库依赖

依赖 Library 目录下的 DLL：

```csharp
public override IEnumerable<string> GetLibraryDependencies()
{
    return new[] { "Newtonsoft.Json.dll", "SomeLibrary.dll" };
}
```

## 命令开发

### 注册命令

```csharp
_commandRegistry.RegisterCommand(
    "commandName",              // 命令名称（不包含/）
    "命令描述",                 // 简短描述
    "详细帮助文本",             // 帮助文本
    parameters,                 // 参数列表
    handler,                    // 处理函数
    "ModuleName",               // 模块名
    permission,                 // 权限级别
    scope,                      // 作用域
    requireAt,                  // 是否需要@
    requireSlash                // 是否需要/前缀
);
```

### 参数定义

```csharp
new List<CommandParameter>
{
    new CommandParameter
    {
        Name = "用户",
        Description = "目标用户",
        IsRequired = true,
        Type = ParameterType.At
    },
    new CommandParameter
    {
        Name = "次数",
        Description = "重复次数",
        IsRequired = false,
        Type = ParameterType.Integer,
        DefaultValue = "1"
    }
}
```

### 参数类型

| 类型 | 说明 | 示例 |
|------|------|------|
| `String` | 字符串 | `hello` |
| `Integer` | 整数 | `123` |
| `Float` | 浮点数 | `3.14` |
| `Boolean` | 布尔值 | `true`/`false` |
| `At` | @某人 | `@用户` |
| `Reply` | 回复消息 | 回复某条消息 |

### 嵌套参数

支持多级嵌套参数：

```csharp
new List<CommandParameter>
{
    new CommandParameter
    {
        Name = "操作",
        Description = "操作类型",
        IsRequired = true,
        Type = ParameterType.String,
        SubParameters = new List<CommandParameter>
        {
            new CommandParameter
            {
                Name = "子操作",
                Description = "子操作类型",
                IsRequired = false,
                Type = ParameterType.String,
                SubParameters = new List<CommandParameter>
                {
                    new CommandParameter
                    {
                        Name = "值",
                        Description = "操作值",
                        IsRequired = false,
                        Type = ParameterType.String
                    }
                }
            }
        }
    }
}
```

### 命令处理函数

```csharp
private async Task HandleCommand(CommandContext context)
{
    var message = context.Message;      // 原始消息
    var parameters = context.Parameters; // 解析后的参数
    var rawCommand = context.RawCommand; // 原始命令文本
    var client = context.Client;         // OneBot 客户端
    
    // 获取参数
    if (parameters.TryGetValue("参数名", out var value))
    {
        // 处理参数
    }
    
    // 发送回复
    await _mdc.SendAsync(message, builder => builder.Text("回复内容"));
}
```

### 权限和作用域

#### 权限级别

```csharp
CommandPermission.Everyone    // 所有人
CommandPermission.GroupAdmin  // 群管理员及以上
CommandPermission.Owner       // 群主及以上
CommandPermission.BotOwner    // 仅机器人主人
```

#### 作用域

```csharp
CommandScope.All          // 私聊和群聊
CommandScope.PrivateOnly  // 仅私聊
CommandScope.GroupOnly    // 仅群聊
```

#### 触发方式

| requireAt | requireSlash | 触发方式 |
|-----------|--------------|----------|
| false | true | `/命令` |
| true | false | `@机器人 命令` 或 `@机器人/命令` |
| true | true | `@机器人 /命令` |
| **false** | **false** | **直接输入命令名**，无需任何前缀 |

> ⚠️ **重要提示**：当 `requireAt=false` 且 `requireSlash=false` 时，命令会对所有消息进行匹配。为避免误触发，请确保命令名足够独特（如使用前缀 `my_` 或 `插件名_`）。

## 配置管理

### 定义配置类

```csharp
public class MyPluginConfig
{
    public string Setting1 { get; set; } = "default";
    public int Setting2 { get; set; } = 100;
    public Dictionary<long, int> UserData { get; set; } = new();
}
```

### 加载配置

```csharp
private MyPluginConfig _config = new();

private async Task LoadConfigAsync()
{
    _config = await _configManager.GetConfigAsync<MyPluginConfig>("MyPluginModule", "config") ?? new MyPluginConfig();
}
```

> **自动创建默认配置**: 当配置文件不存在时，如果提供了 `defaultValue` 参数，框架会自动创建默认配置文件。这使得 WebUI 等外部工具始终能获取到插件的配置信息。

### 配置名与模块名映射

如果插件的类名（ModuleName）与配置中使用的插件名不一致，框架会在模块加载时自动建立映射关系。例如，插件类名为 `PetPetPlugin`，但配置使用 `"摸摸头"` 作为插件名，框架会自动将 `PetPetPlugin` 映射到 `摸摸头`，确保 WebUI 能正确获取配置。

> ⚠️ **重要：不要跨插件读取配置**。插件在初始化期间，框架会记录当前模块名与配置插件名的映射关系。如果插件 A 调用 `ConfigManager.GetValueAsync("OtherPlugin", "config", ...)` 读取其他插件的配置，会导致模块名映射被错误覆盖——插件 A 的模块名会被映射到 `OtherPlugin`，之后 WebUI 请求插件 A 的配置时会被路由到 `OtherPlugin` 的配置文件，返回错误数据。
>
> **正确做法**：如果需要读取主配置（如 `owner_qq`），应在自己的配置类中定义对应字段，或通过 `Bot` 实例获取，不要使用 `PluginConfigManager` 读取其他插件的配置名。

### 保存配置

```csharp
private async Task SaveConfigAsync()
{
    await _configManager.SetConfigAsync("MyPluginModule", "config", _config);
}
```

### 使用配置

```csharp
// 读取配置值
var value = _config.Setting1;

// 修改配置
_config.Setting2 = 200;
_config.UserData[userId] = count;

// 保存配置
await SaveConfigAsync();
```

### 配置问题排查指南

本章节详细说明插件配置使用中的常见问题及解决方案。

#### 1. 反序列化失败：配置文件存在但加载返回空对象

**症状**：
- 配置文件存在且有内容
- `GetConfigAsync` 返回空对象或默认值
- 日志显示 `加载配置文件成功` 但属性值全为默认值

**原因**：
MorningCat 使用 `YamlDotNet` 的 `UnderscoredNamingConvention`（下划线命名约定）进行反序列化。这意味着：
- C# 属性 `MySetting` 需要 YAML 中使用 `my_setting`
- 驼峰命名 `MySetting` 不会自动匹配

**排查步骤**：
1. 检查 YAML 配置文件中的键名是否使用下划线命名
2. 对比 C# 类属性名与 YAML 键名

**解决方案**：

**方案一：使用下划线命名（推荐）**
```csharp
// C# 类
public class MyPluginConfig
{
    public string MySetting { get; set; } = "default";
    public int MaxCount { get; set; } = 10;
}
```
```yaml
# 对应的 YAML 配置
my_setting: "hello"
max_count: 100
```

**方案二：使用 YamlMember 特性指定别名**
```csharp
using YamlDotNet.Serialization;

public class MyPluginConfig
{
    [YamlMember(Alias = "mySetting")]
    public string MySetting { get; set; } = "default";
    
    [YamlMember(Alias = "maxCount")]
    public int MaxCount { get; set; } = 10;
}
```
```yaml
# 驼峰命名也可以工作
mySetting: "hello"
maxCount: 100
```

**方案三：修改全局命名约定**
```csharp
// 在 DeserializerBuilder 中使用 CamelCaseNamingConvention
_deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();
```
> ⚠️ 注意：修改全局约定会影响所有插件，不建议这样做。

#### 2. 默认配置没生成

**症状**：
- 配置文件不存在，期望自动生成默认配置
- 但配置文件没有被创建
- 或创建了但内容为空

**原因**：
`GetConfigAsync` 的默认参数 `defaultValue` 在配置文件不存在时会被使用，但**只有当 `defaultValue` 不为 `null` 时**才会生成默认文件。

```csharp
// 正确：会生成默认配置
var config = await GetConfigAsync<MyPluginConfig>("MyPlugin", "config");

// 问题：这个 defaultValue 是 null，不会生成默认配置！
var config = await GetConfigAsync<MyPluginConfig>("MyPlugin", "config", null);

// 正确：明确传递默认实例
var config = await GetConfigAsync<MyPluginConfig>("MyPlugin", "config", new MyPluginConfig());
```

**排查步骤**：
1. 确认调用 `GetConfigAsync` 时是否传递了默认实例
2. 检查日志中是否有 `已创建默认配置文件` 的记录
3. 检查配置文件目录是否有写入权限

**解决方案**：
```csharp
private MyPluginConfig _config = new();

private async Task LoadConfigAsync()
{
    // 确保传递默认实例！
    _config = await _configManager.GetConfigAsync<MyPluginConfig>(
        "MyPluginModule", 
        "config",
        new MyPluginConfig()  // 必须传递默认实例
    ) ?? new MyPluginConfig();
}
```

#### 3. 配置加载失败但无错误提示

**症状**：
- 配置文件存在
- 反序列化抛出异常
- 但程序没有明显报错，返回了默认值

**原因**：
框架在 `catch` 块中返回了默认值，但没有详细错误信息：
```csharp
catch (Exception ex)
{
    Log.Error($"加载配置文件失败: {ex.Message}");
    return defaultValue ?? new T();  // 静默返回默认值
}
```

**排查步骤**：
1. 开启调试日志：查看 `加载配置文件失败` 相关日志
2. 检查 YAML 语法是否正确（缩进、引号、列表格式）
3. 检查 C# 类型与 YAML 值是否匹配

**常见 YAML 语法错误**：
```yaml
# 错误：布尔值应该用小写
enabled: True    # ❌
enabled: true    # ✅

# 错误：整数不能有逗号
count: 1,000     # ❌
count: 1000      # ✅

# 错误：字符串有特殊字符需要引号
message: hello world  # ❌ 可能被解析为多个词
message: "hello world"  # ✅

# 错误：嵌套结构缩进错误
settings:
enabled: true    # ❌ 缩进不正确
  enabled: true   # ✅
```

#### 4. 嵌套配置项访问失败

**症状**：
- 使用 `GetValueAsync` 访问嵌套配置（如 `database.host`）
- 返回默认值，原始配置未被读取

**解决方案**：
确保 YAML 中使用下划线命名：
```yaml
# C# 中访问: GetValueAsync("database.host", "localhost")
database:
  host: "localhost"
  port: 3306
```

#### 5. 构建警告：Nullable 警告

**症状**：
编译时出现大量 `warning CS8618: xxx 必须包含非 null 值` 警告

**解决方案**：
1. 使用 `= null!` 初始化依赖注入字段：
```csharp
private IPluginConfigManager _configManager = null!;
```

2. 或在 csproj 中调整警告级别：
```xml
<PropertyGroup>
    <Nullable>enable</Nullable>
    <NullableReferenceTypes>enable</NullableReferenceTypes>
    <!-- 将警告视为错误，但排除特定的 nullable 警告 -->
    <WarningsAsErrors>$(WarningsAsErrors);CS8618</WarningsAsErrors>
    <!-- 或者完全禁用 -->
    <NoWarn>$(NoWarn);CS8618</NoWarn>
</PropertyGroup>
```

#### 6. YamlDotNet 版本兼容性

**推荐版本**：
```xml
<PackageReference Include="YamlDotNet" Version="15.1.0" />
```
> **注意**：版本 16+ 存在一些破坏性变更，可能导致序列化/反序列化行为变化。如果遇到奇怪的问题，尝试降级到 15.1.0。

#### 7. 完整配置类示例（避免常见问题）

```csharp
using System.Collections.Generic;
using YamlDotNet.Serialization;

public class MyPluginConfig
{
    // 使用 YamlMember 指定别名，避免下划线命名问题
    [YamlMember(Alias = "enabled")]
    public bool Enabled { get; set; } = true;
    
    [YamlMember(Alias = "maxCount")]
    public int MaxCount { get; set; } = 10;
    
    [YamlMember(Alias = "adminQQ")]
    public long AdminQQ { get; set; } = 0;
    
    // 嵌套对象也需要 YamlMember
    [YamlMember(Alias = "database")]
    public DatabaseConfig Database { get; set; } = new();
    
    // 字典类型
    public Dictionary<string, int> UserLimits { get; set; } = new();
}

public class DatabaseConfig
{
    [YamlMember(Alias = "host")]
    public string Host { get; set; } = "localhost";
    
    [YamlMember(Alias = "port")]
    public int Port { get; set; } = 3306;
}
```

对应的 YAML 配置：
```yaml
enabled: true
max_count: 100
admin_qq: 123456789
database:
  host: "localhost"
  port: 3306
user_limits:
  user1: 10
  user2: 20
```

## 数据库使用

MorningCat 提供了 PluginDatabaseAPI，允许插件使用数据库存储数据，支持 SQLite 和 SQL Server。

### 注入 PluginDatabaseAPI

```csharp
private PluginDatabaseAPI _dbAPI = null!;

public PluginDatabaseAPI PluginDatabaseAPI
{
    get => _dbAPI;
    set => _dbAPI = value;
}
```

### 获取数据库实例

```csharp
private IPluginDatabase _db = null!;

public override async Task Init()
{
    _db = _dbAPI.GetDatabase("data", "MyPluginModule");
}
```

**参数说明**:
- `id`: 数据库标识，用于区分同一插件的不同数据库（如 "data"、"cache"）
- `pluginClassName`: 插件类名

**数据库文件位置**:
- SQLite: `Database/{id}-{pluginClassName}.db`（自动创建）
- SQL Server: 使用 `config.yml` 中配置的连接字符串

### 创建表

```csharp
await _db.ExecuteNonQueryAsync(
    "CREATE TABLE IF NOT EXISTS users (qq INTEGER PRIMARY KEY, count INTEGER DEFAULT 0, last_active TEXT)"
);
```

### 插入数据

```csharp
var qqParam = _db.CreateParameter("@qq", userId);
var countParam = _db.CreateParameter("@count", 1);
var timeParam = _db.CreateParameter("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

await _db.ExecuteNonQueryAsync(
    "INSERT OR REPLACE INTO users (qq, count, last_active) VALUES (@qq, @count, @time)",
    qqParam, countParam, timeParam
);
```

### 查询数据

```csharp
var param = _db.CreateParameter("@qq", userId);
var result = await _db.QueryAsync("SELECT * FROM users WHERE qq = @qq", param);
if (result.Count > 0)
{
    var count = result[0]["count"];
    Log.Info($"用户 {userId} 的计数: {count}");
}
```

### 更新数据

```csharp
var qqParam = _db.CreateParameter("@qq", userId);
var countParam = _db.CreateParameter("@count", newCount);

int affected = await _db.ExecuteNonQueryAsync(
    "UPDATE users SET count = @count WHERE qq = @qq",
    countParam, qqParam
);
```

### 查询标量值

```csharp
var param = _db.CreateParameter("@qq", userId);
var result = await _db.ExecuteScalarAsync("SELECT count FROM users WHERE qq = @qq", param);
if (result != null && result != DBNull.Value)
{
    int count = Convert.ToInt32(result);
}
```

### 使用 DataTable

```csharp
var dt = await _db.QueryTableAsync("SELECT * FROM users ORDER BY count DESC LIMIT 10");
foreach (System.Data.DataRow row in dt.Rows)
{
    Log.Info($"QQ: {row["qq"]}, Count: {row["count"]}");
}
```

### 数据库配置

在 `config.yml` 中配置数据库类型：

```yaml
database:
  type: "sqlite"              # sqlite 或 sql
  connection_string: ""        # SQL Server 连接字符串（sql 类型时必填）
```

**SQLite（默认）**: 无需额外配置，数据库文件自动创建在 `Database/` 目录。

**SQL Server**: 需要配置连接字符串：

```yaml
database:
  type: "sql"
  connection_string: "Server=localhost;Database=MorningCat;User Id=sa;Password=your_password;"
```

### 完整数据库示例

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.PluginAPI;

[PluginMetadata(DisplayName = "计数器", Author = "Demo", Description = "用户计数示例")]
public class CounterPlugin : ModuleBase
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
        _db = _dbAPI.GetDatabase("data", "CounterPlugin");

        await _db.ExecuteNonQueryAsync(
            "CREATE TABLE IF NOT EXISTS counters (qq INTEGER PRIMARY KEY, count INTEGER DEFAULT 0)"
        );

        Log.Info("[CounterPlugin] 插件已加载");
    }

    public async Task IncrementAsync(long qq)
    {
        var qqParam = _db.CreateParameter("@qq", qq);
        var existing = await _db.QueryAsync("SELECT count FROM counters WHERE qq = @qq", qqParam);

        if (existing.Count > 0)
        {
            int currentCount = Convert.ToInt32(existing[0]["count"]);
            var countParam = _db.CreateParameter("@count", currentCount + 1);
            await _db.ExecuteNonQueryAsync("UPDATE counters SET count = @count WHERE qq = @qq", countParam, qqParam);
        }
        else
        {
            await _db.ExecuteNonQueryAsync("INSERT INTO counters (qq, count) VALUES (@qq, 1)", qqParam);
        }
    }

    public async Task<int> GetCountAsync(long qq)
    {
        var param = _db.CreateParameter("@qq", qq);
        var result = await _db.ExecuteScalarAsync("SELECT count FROM counters WHERE qq = @qq", param);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }
}
```

## 事件处理

### 未处理消息事件

当消息不匹配任何命令时，会触发 `OnUnhandledMessage` 事件。插件可以通过注入的 `MorningCatBot` 实例订阅此事件。

#### 订阅方式

```csharp
public class MyPluginModule : ModuleBase
{
    private MorningCatBot _bot = null!;  // 通过类型匹配注入
    
    public MorningCatBot Bot
    {
        get => _bot;
        set => _bot = value;
    }

    public override async Task Init()
    {
        // ... 其他初始化代码 ...
        
        // 订阅未处理消息事件
        _bot.OnUnhandledMessage += OnUnhandledMessage;
        
        Log.Info("[MyPlugin] 插件已加载");
    }

    private void OnUnhandledMessage(object? sender, UnhandledMessageEventArgs e)
    {
        // 异步处理，不要阻塞事件
        _ = Task.Run(async () =>
        {
            if (e.PlainText.Contains("你好"))
            {
                await _mdc.SendAsync(e.Message, builder => builder.Text("你好呀！"));
            }
        });
    }

    public override async Task Exit()
    {
        // 重要：卸载时取消订阅
        _bot.OnUnhandledMessage -= OnUnhandledMessage;
        Log.Info("[MyPlugin] 插件已卸载");
    }
}
```

#### UnhandledMessageEventArgs 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Message` | `MessageObject` | 原始消息对象 |
| `UserId` | `long` | 用户 ID |
| `UserNickname` | `string` | 用户昵称 |
| `GroupId` | `long?` | 群 ID（群消息时） |
| `GroupName` | `string` | 群名 |
| `PlainText` | `string` | 纯文本内容 |
| `IsGroupMessage` | `bool` | 是否群消息 |
| `IsPrivateMessage` | `bool` | 是否私聊 |

#### 注意事项

1. **事件处理是同步的**：不要在事件处理函数中直接 `await`，使用 `_ = Task.Run()` 异步执行
2. **记得取消订阅**：在 `Exit()` 方法中取消订阅，避免内存泄漏
3. **检查消息来源**：使用 `IsGroupMessage` 或 `IsPrivateMessage` 判断消息类型

### 重连处理

推荐使用 `MessageDistributionCore`（MDC）发送消息，MDC 内部会自动处理重连后的客户端引用更新，插件无需关心重连逻辑。

```csharp
// 推荐：使用 MDC + IMessageBuilder 发送消息
public MessageDistributionCore MDC
{
    get => _mdc;
    set => _mdc = value;
}

await _mdc.SendAsync(message, builder => builder.Text(text));  // 自动处理重连

// 或发送纯文本（兼容旧API）
await _mdc.SendMessageAsync(message, text);
```

如果仍需使用 `OneBotClient`，确保属性有 `public set` 访问器，框架重连时会自动更新引用。不要缓存客户端引用到其他变量中。

### 消息屏蔽

框架在消息处理流程的最早阶段检查屏蔽列表。被屏蔽的用户或群的消息会被直接忽略，不会触发命令处理或未处理消息事件。插件无需关心屏蔽逻辑。

屏蔽配置在 `config.yml` 中设置：

```yaml
blocked_users:
  - 123456789
  - 987654321

blocked_groups:
  - 111111111
```

## 插件间依赖与通信

### 依赖声明

当你的插件需要依赖另一个插件时，通过 `GetDependencies()` 方法声明。模块管理器会自动解析依赖图，确保被依赖的插件先加载。

```csharp
public override IEnumerable<string> GetDependencies()
{
    return new[] { "LoggerPlugin", "DatabasePlugin" };
}
```

**依赖声明的作用：**

| 作用 | 说明 |
|------|------|
| 加载顺序保证 | 被依赖的插件一定先于当前插件初始化 |
| 缺失依赖检查 | 依赖的插件不存在时，当前插件加载失败 |
| 级联卸载 | 卸载被依赖的插件时，依赖它的插件也会被卸载 |
| 循环依赖检测 | 检测到循环依赖时，相关模块全部加载失败 |

### 通信方式一：通过服务注册与注入（推荐）

框架端和插件都可以通过 `ModuleManager.RegisterService<T>()` 注册服务，其他插件通过依赖注入获取。

```csharp
// 插件 A：在 Init 中注册自己的 API 服务
public class DatabasePlugin : ModuleBase
{
    public override async Task Init()
    {
        // 注册服务供其他插件使用
        // _moduleManager.RegisterService("DatabaseAPI", new DatabaseAPI(this));
        await Task.CompletedTask;
    }
}

// 插件 B：通过类型匹配注入获取插件 A 的服务
public class MyPlugin : ModuleBase
{
    [Inject("DatabaseAPI")]
    public object DbApi { get; set; }

    public override IEnumerable<string> GetDependencies()
    {
        return new[] { "DatabasePlugin" };
    }

    public override async Task Init()
    {
        // 使用 DbApi...
        await Task.CompletedTask;
    }
}
```

也可以通过类型匹配属性注入获取框架服务：

```csharp
public class MyPlugin : ModuleBase
{
    private MessageDistributionCore _mdc = null!;
    private CommandRegistry _commandRegistry = null!;
    private PluginCommandAPI _pluginCommandAPI = null!;

    public MessageDistributionCore MDC
    {
        get => _mdc;
        set => _mdc = value;
    }

    public CommandRegistry CommandRegistry
    {
        get => _commandRegistry;
        set => _commandRegistry = value;
    }

    public PluginCommandAPI PluginCommandAPI
    {
        get => _pluginCommandAPI;
        set => _pluginCommandAPI = value;
    }
}
```

### 通信方式二：通过 PluginCommandAPI 调用命令

通过注入的 `PluginCommandAPI` 可以枚举和执行其他插件注册的命令，完全解耦，无需引用目标插件类型。

```csharp
public class MyPlugin : ModuleBase
{
    private PluginCommandAPI _pluginCommandAPI = null!;

    public PluginCommandAPI PluginCommandAPI
    {
        get => _pluginCommandAPI;
        set => _pluginCommandAPI = value;
    }

    public override async Task Init()
    {
        // 枚举所有 BotOwner 权限及以下的命令
        var commands = _pluginCommandAPI.EnumerateCommands(CommandPermission.BotOwner);
        foreach (var cmd in commands)
        {
            Log.Info($"命令: {cmd.Name} - {cmd.Description}");
            foreach (var param in cmd.Parameters)
            {
                Log.Info($"  参数: {param.Name} ({param.Type}) - {param.Description}");
            }
        }

        // 执行命令
        var result = await _pluginCommandAPI.ExecuteAsBotOwner(message, "help");
        if (result.Success)
        {
            Log.Info("命令执行成功");
        }
    }
}
```

**PluginCommandAPI 方法列表：**

| 方法 | 说明 |
|------|------|
| `RegisterCommand(...)` | 注册新命令，支持指定 requireAt/requireSlash |
| `UnregisterCommand(commandName)` | 注销指定命令 |
| `UnregisterModuleCommands(moduleName)` | 注销指定模块的所有命令 |
| `EnumerateCommands()` | 枚举 Everyone 权限的命令 |
| `EnumerateCommands(CommandPermission)` | 枚举指定权限及以下的命令 |
| `ExecuteAsNormal(message, commandLine)` | 以普通用户权限执行命令（命令行方式） |
| `ExecuteAsNormal(message, commandName, args)` | 以普通用户权限执行命令（参数数组方式） |
| `ExecuteAsGroupAdmin(message, commandLine)` | 以群管理员权限执行命令（命令行方式） |
| `ExecuteAsGroupAdmin(message, commandName, args)` | 以群管理员权限执行命令（参数数组方式） |
| `ExecuteAsBotOwner(message, commandLine)` | 以机器人主人权限执行命令（命令行方式） |
| `ExecuteAsBotOwner(message, commandName, args)` | 以机器人主人权限执行命令（参数数组方式） |
| `ExecuteWithPermission(message, permission, commandLine)` | 以自定义权限执行命令（命令行方式） |
| `ExecuteWithPermission(message, permission, commandName, args)` | 以自定义权限执行命令（参数数组方式） |

**PluginCommandResult 返回值：**

| 属性 | 类型 | 说明 |
|------|------|------|
| `Success` | `bool` | 是否执行成功 |
| `ErrorMessage` | `string` | 错误信息（失败时） |

### 通信方式三：通过 ModuleInfo 获取插件实例

通过注入的 `ModuleManager` 查询目标插件的 `ModuleInstance`，使用反射调用其公开方法。

```csharp
public class MyPlugin : ModuleBase
{
    private ModuleManager _moduleManager = null!;

    public ModuleManager ModuleManager
    {
        get => _moduleManager;
        set => _moduleManager = value;
    }

    public override IEnumerable<string> GetDependencies()
    {
        return new[] { "TargetPlugin" };
    }

    public override async Task Init()
    {
        var targetInfo = _moduleManager.GetModuleInfo("TargetPlugin");
        if (targetInfo != null)
        {
            var method = targetInfo.ModuleInstance.GetType().GetMethod("SomePublicMethod");
            var result = method?.Invoke(targetInfo.ModuleInstance, new object[] { "arg" });
        }
        await Task.CompletedTask;
    }
}
```

### 跨 AssemblyLoadContext 注意事项

MorningCat 的插件在独立的 `AssemblyLoadContext` 中加载，这带来以下影响：

| 问题 | 说明 | 解决方案 |
|------|------|----------|
| 类型不兼容 | 同一类型在不同 ALC 中可能被视为不同类型 | 框架已通过类型全名匹配自动处理注入 |
| 接口共享 | 跨 ALC 无法直接进行强类型转换 | 将共享接口放在 `Modules/Library/` 目录的 DLL 中 |
| 反射安全 | 反射调用不受 ALC 隔离限制 | 使用反射是最通用的跨插件通信方式 |
| 命令解耦 | PluginCommandAPI 完全基于字符串 | 无需类型引用，最松耦合的通信方式 |

**推荐实践：**

1. **优先使用 PluginCommandAPI**：如果只需调用目标插件的命令功能，这是最简单最安全的方式
2. **服务注册 + 接口 DLL**：如需强类型交互，将共享接口定义放在 Library 目录
3. **反射作为兜底**：当上述方式都不适用时，使用反射调用目标插件的公开方法
4. **始终声明依赖**：即使不直接调用目标插件方法，只要运行逻辑依赖其存在，就应该在 `GetDependencies()` 中声明

## 完整示例

以下是一个完整的插件示例，包含命令、配置和事件处理：

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.Config;
using MorningCat.PluginAPI;
using OneBotLib;
using OneBotLib.Models;

namespace MorningCat.Modules
{
    public class CounterPluginConfig
    {
        public Dictionary<long, int> UserCounts { get; set; } = new();
        public int MaxCount { get; set; } = 100;
    }

    [PluginMetadata(
        DisplayName = "计数器插件",
        Author = "MorningCat",
        Description = "用户计数功能"
    )]
    public class CounterPluginModule : ModuleBase
    {
        private MessageDistributionCore _mdc = null!;
        private CommandRegistry _commandRegistry = null!;
        private PluginConfigManager _configManager = null!;
        private MorningCatBot _bot = null!;
        private CounterPluginConfig _config = new();
        
        public MessageDistributionCore MDC
        {
            get => _mdc;
            set => _mdc = value;
        }

        public CommandRegistry CommandRegistry
        {
            get => _commandRegistry;
            set => _commandRegistry = value;
        }

        public PluginConfigManager ConfigManager
        {
            get => _configManager;
            set => _configManager = value;
        }
        
        public MorningCatBot Bot
        {
            get => _bot;
            set => _bot = value;
        }

        public override IEnumerable<string> GetLibraryDependencies()
        {
            return new[] { "MorningCat.PlatformAbstraction.dll" };
        }

        public override async Task Init()
        {
            await LoadConfigAsync();
            RegisterCommands();
            
            _bot.OnUnhandledMessage += OnUnhandledMessage;
            
            Log.Info("[CounterPlugin] 插件已加载");
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                _config = await _configManager.GetConfigAsync<CounterPluginConfig>(
                    "CounterPluginModule", 
                    "config"
                ) ?? new CounterPluginConfig();
                Log.Debug($"[CounterPlugin] 加载配置，共 {_config.UserCounts.Count} 条记录");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[CounterPlugin] 加载配置失败: {ex.Message}");
                _config = new CounterPluginConfig();
            }
        }

        private async Task SaveConfigAsync()
        {
            try
            {
                await _configManager.SetConfigAsync("CounterPluginModule", "config", _config);
                Log.Debug("[CounterPlugin] 配置已保存");
            }
            catch (System.Exception ex)
            {
                Log.Error($"[CounterPlugin] 保存配置失败: {ex.Message}");
            }
        }

        private void RegisterCommands()
        {
            _commandRegistry?.RegisterCommand(
                "count",
                "计数",
                "count - 增加计数\ncount query - 查询计数",
                new List<CommandParameter>
                {
                    new CommandParameter
                    {
                        Name = "操作",
                        Description = "操作类型",
                        IsRequired = false,
                        Type = ParameterType.String
                    }
                },
                HandleCountCommand,
                "CounterPluginModule",
                CommandPermission.Everyone,
                CommandScope.All,
                requireAt: false,
                requireSlash: true
            );
            
            _commandRegistry?.RegisterCommand(
                "countset",
                "设置计数上限",
                "countset <数值> - 设置计数上限",
                new List<CommandParameter>
                {
                    new CommandParameter
                    {
                        Name = "数值",
                        Description = "上限值",
                        IsRequired = true,
                        Type = ParameterType.Integer
                    }
                },
                HandleCountSetCommand,
                "CounterPluginModule",
                CommandPermission.GroupAdmin,
                CommandScope.All,
                requireAt: true,
                requireSlash: false
            );
        }

        private async Task HandleCountCommand(CommandContext context)
        {
            var message = context.Message;
            var parameters = context.Parameters;
            
            var userId = message.UserId ?? 0;
            
            if (parameters.TryGetValue("操作", out var action) && action == "query")
            {
                if (_config.UserCounts.TryGetValue(userId, out var count))
                {
                    await SendMessageAsync(message, $"你当前的计数: {count}");
                }
                else
                {
                    await SendMessageAsync(message, "你还没有计数记录");
                }
                return;
            }
            
            if (!_config.UserCounts.ContainsKey(userId))
            {
                _config.UserCounts[userId] = 0;
            }
            
            if (_config.UserCounts[userId] >= _config.MaxCount)
            {
                await SendMessageAsync(message, $"已达到上限 ({_config.MaxCount})");
                return;
            }
            
            _config.UserCounts[userId]++;
            await SaveConfigAsync();
            
            await SendMessageAsync(message, $"计数 +1，当前: {_config.UserCounts[userId]}");
        }

        private async Task HandleCountSetCommand(CommandContext context)
        {
            var message = context.Message;
            var parameters = context.Parameters;
            
            if (!parameters.TryGetValue("数值", out var valueStr) || !int.TryParse(valueStr, out var value))
            {
                await SendMessageAsync(message, "请提供有效的数值");
                return;
            }
            
            if (value < 1 || value > 10000)
            {
                await SendMessageAsync(message, "数值必须在 1-10000 之间");
                return;
            }
            
            _config.MaxCount = value;
            await SaveConfigAsync();
            
            await SendMessageAsync(message, $"已设置计数上限为 {value}");
            Log.Info($"[CounterPlugin] 用户 {message.UserId} 设置计数上限为 {value}");
        }

        private void OnUnhandledMessage(object? sender, UnhandledMessageEventArgs e)
        {
            if (e.PlainText.Contains("计数"))
            {
                _ = Task.Run(async () =>
                {
                    await SendMessageAsync(e.Message, "使用 /count 命令来计数哦~");
                });
            }
        }

        private async Task SendMessageAsync(PlatformMessage message, string text)
        {
            try
            {
                await _mdc.SendAsync(message, builder => builder.Text(text));
            }
            catch (System.Exception ex)
            {
                Log.Error($"发送消息失败: {ex.Message}");
            }
        }

        public override async Task Exit()
        {
            _bot.OnUnhandledMessage -= OnUnhandledMessage;
            _commandRegistry?.UnregisterModuleCommands("CounterPluginModule");
            await SaveConfigAsync();
            Log.Info("[CounterPlugin] 插件已卸载");
        }
    }
}
```

## 调试技巧

### 日志输出

使用 `Log` 类输出日志：

```csharp
Log.Debug("调试信息");
Log.Info("普通信息");
Log.Warning("警告信息");
Log.Error("错误信息");
```

### 检查依赖注入

```csharp
public override async Task Init()
{
    if (_client == null)
        Log.Warning("[Plugin] MDC 未注入");
    if (_commandRegistry == null)
        Log.Warning("[Plugin] CommandRegistry 未注入");
    if (_configManager == null)
        Log.Warning("[Plugin] PluginConfigManager 未注入");
    
    // ... 其他代码 ...
}
```

### 命令注册检查

```csharp
var success = _commandRegistry?.RegisterCommand(...);
if (success != true)
{
    Log.Error("[Plugin] 命令注册失败");
}
```

## FAQ

### Q1: 如何使用 UnhandledMessageEventArgs 处理未匹配命令的消息？

**问题**：我想在用户发送的消息不匹配任何命令时进行响应，应该如何实现？

**答案**：

1. **注入 MorningCatBot 实例**：

```csharp
private MorningCatBot _bot = null!;

public MorningCatBot Bot
{
    get => _bot;
    set => _bot = value;
}
```

2. **订阅事件**：

在 `Init()` 方法中订阅 `OnUnhandledMessage` 事件：

```csharp
public override async Task Init()
{
    // ... 其他初始化代码 ...
    
    _bot.OnUnhandledMessage += OnUnhandledMessage;
}
```

3. **处理事件**：

```csharp
private void OnUnhandledMessage(object? sender, UnhandledMessageEventArgs e)
{
    // 使用 Task.Run 异步处理，避免阻塞事件
    _ = Task.Run(async () =>
    {
        // 访问事件参数
        long userId = e.UserId;                    // 用户 ID
        string nickname = e.UserNickname;          // 用户昵称
        string text = e.PlainText;                 // 消息纯文本
        bool isGroup = e.IsGroupMessage;           // 是否群消息
        long? groupId = e.GroupId;                 // 群 ID
        MessageObject message = e.Message;         // 原始消息对象
        
        // 示例：回复"你好"
        if (text.Contains("你好"))
        {
            await SendMessageAsync(message, $"你好，{nickname}！");
        }
    });
}
```

4. **取消订阅**：

在 `Exit()` 方法中取消订阅：

```csharp
public override async Task Exit()
{
    _bot.OnUnhandledMessage -= OnUnhandledMessage;
}
```

5. **完整示例**：

```csharp
using System;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat;
using MorningCat.Events;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Modules
{
    public class MyPluginModule : ModuleBase
    {
        private MessageDistributionCore _mdc = null!;
        private MorningCatBot _bot = null!;
        
        public MessageDistributionCore MDC
        {
            get => _mdc;
            set => _mdc = value;
        }
        
        public MorningCatBot Bot
        {
            get => _bot;
            set => _bot = value;
        }

        public override async Task Init()
        {
            _bot.OnUnhandledMessage += OnUnhandledMessage;
            Log.Info("[MyPlugin] 插件已加载");
        }

        private void OnUnhandledMessage(object? sender, UnhandledMessageEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                if (e.PlainText.Contains("你好"))
                {
                    await _mdc.SendMessageAsync(e.Message, $"你好，{e.UserNickname}！");
                }
            });
        }

        private async Task SendMessageAsync(PlatformMessage message, string text)
        {
            try
            {
                await _mdc.SendMessageAsync(message, text);
            }
            catch (Exception ex)
            {
                Log.Error($"发送消息失败: {ex.Message}");
            }
        }

        public override async Task Exit()
        {
            _bot.OnUnhandledMessage -= OnUnhandledMessage;
            Log.Info("[MyPlugin] 插件已卸载");
        }
    }
}
```

**注意事项**：
- 事件处理函数是同步的，不要直接使用 `await`，应该使用 `_ = Task.Run()` 异步执行
- 必须在 `Exit()` 中取消订阅，否则会导致内存泄漏
- 使用 `e.IsGroupMessage` 或 `e.IsPrivateMessage` 判断消息类型
- `e.Message` 是完整的消息对象，可以访问所有消息属性

### Q2: 为什么在私聊使用命令时提示"此命令仅限私聊使用"？

**问题**：我在私聊中使用 `wv` 命令（该命令注册时使用了 `CommandScope.GroupOnly`），但系统提示"此命令仅限私聊使用"，这明显不对，是框架问题还是插件问题？

**答案**：这是 **MCT 框架的 Bug**（已修复）。

**原因分析**：

框架的 `CommandRegistry` 在 `SendScopeDeniedMessageAsync` 方法中，错误消息是硬编码的：

```csharp
// 修复前的错误代码
private Task SendScopeDeniedMessageAsync(MessageObject message)
{
    string errorText = "此命令仅限私聊使用";  // ← 硬编码，不根据实际 scope 变化
    // ...
}
```

所以无论命令是 `CommandScope.GroupOnly` 还是 `CommandScope.PrivateOnly`，都会显示同样的错误消息 `"此命令仅限私聊使用"`。

**修复方案**（框架层面）：

框架已修复此问题，现在 `SendScopeDeniedMessageAsync` 方法接收 `CommandScope` 参数，动态生成错误消息：

```csharp
// 修复后的正确代码
private Task SendScopeDeniedMessageAsync(MessageObject message, CommandScope scope)
{
    string errorText = scope == CommandScope.PrivateOnly 
        ? "此命令仅限私聊使用" 
        : "此命令仅限群聊使用";  // ← 根据 scope 动态生成
    // ...
}
```

**插件开发者注意事项**：

如果你使用的是旧版本的 MorningCat，可能会遇到此问题。请确保使用最新版本的框架。

**CommandScope 说明**：

| CommandScope 值 | 说明 | 适用场景 |
|-----------------|------|----------|
| `CommandScope.All` | 私聊和群聊都可以使用 | 通用命令（如帮助、设置等） |
| `CommandScope.GroupOnly` | 仅群聊可用 | 群管理命令（如踢人、禁言等） |
| `CommandScope.PrivateOnly` | 仅私聊可用 | 私聊专用命令 |

**排查步骤**：

1. 确认你使用的是最新版本的 MorningCat 框架
2. 找到插件中注册该命令的代码
3. 检查 `RegisterCommand` 的 `scope` 参数是否正确
4. 如果问题仍然存在，检查框架的 `CommandRegistry.cs` 中 `SendScopeDeniedMessageAsync` 方法是否正确实现了动态错误消息

**注意事项**：
- 群管理相关命令（如踢人、禁言）应该使用 `CommandScope.GroupOnly`
- 通用命令（如帮助、查询）通常使用 `CommandScope.All`
- 如果命令依赖群 ID（如 `message.GroupId`），则必须使用 `CommandScope.GroupOnly`，否则私聊时 `GroupId` 为 `null` 会导致错误

---

## 模块管理器（ModuleManager）

ModuleManager 是 MorningCat 的插件加载与管理核心，负责模块的扫描、加载、卸载、依赖解析和生命周期管理。

### 依赖注入

ModuleManager 支持多种依赖注入方式，插件可以通过以下方式获取所需服务：

**1. 属性注入（按类型）**

```csharp
[Inject]
public ILogger Logger { set => _logger = value; }
```

**2. 字段注入（按类型）**

```csharp
[Inject]
private IMessageService _messageService;
```

**3. 按名称注入**

```csharp
[Inject("MDC")]
public MessageDistributionCore MDC { set => _mdc = value; }
```

**4. 按类型注入（显式指定）**

```csharp
[Inject(typeof(CommandRegistry))]
public CommandRegistry CmdRegistry { set => _cmdRegistry = value; }
```

**5. SetServices 方法**

```csharp
public void SetServices(MessageDistributionCore mdc, CommandRegistry cmdReg)
{
    _mdc = mdc;
    _cmdReg = cmdReg;
}
```

### 模块间 API 调用

ModuleManager 内置了模块间 API 调用系统，插件可以注册和调用 API：

```csharp
// 注入 ModuleManager
[Inject]
public ModuleManager ModuleManager { get; set; }

// 注册 API
ModuleManager.RegisterApi("MyPlugin", "GetData", (string key) => {
    return $"Data for {key}";
});

// 调用其他插件的 API
var data = ModuleManager.CallApi<string>("OtherPlugin", "GetData", "user123");

// 检查 API 是否存在
if (ModuleManager.HasApi("OtherPlugin", "GetData"))
{
    // ...
}
```

### 模块间事件通信

```csharp
// 注册事件
ModuleManager.RegisterEvent("MyPlugin", "DataUpdated");

// 发布事件
ModuleManager.PublishEvent<string>("MyPlugin", "DataUpdated", "Hello World");

// 订阅其他插件的事件
ModuleManager.SubscribeEvent<string>("OtherPlugin", "DataUpdated", (sender, data) => {
    Log.Info($"收到来自 {sender} 的数据: {data}");
});
```

### 加载控制机制

ModuleManager 提供了 `OnBeforeModuleLoad` 事件，允许宿主在模块加载前进行干预：

- `ModuleLoadAction.Continue` - 继续加载（默认）
- `ModuleLoadAction.Skip` - 跳过此模块
- `ModuleLoadAction.Interrupt` - 中断加载，执行自定义逻辑

MorningCat 使用此机制实现插件签名验证：未通过验证的插件会被跳过，不再需要重命名文件。

### 动态加载与卸载

```csharp
// 动态加载插件 DLL
var result = await ModuleManager.DynamicLoadModuleAsync("./Plugins/NewModule.dll");
if (result.Success)
    Log.Info($"动态加载成功: {string.Join(",", result.LoadedModules)}");

// 动态卸载模块（会递归卸载依赖它的模块）
bool ok = await ModuleManager.DynamicUnloadModuleAsync("TargetPlugin");
```

**注意**：动态卸载会检查所有待卸载模块的 `AllowDynamicLoad` 标志，任一为 `false` 则整体失败。

### 模块声明（ModuleDeclaration）

通过 `RegisterDeclarationProvider` 提供模块声明，声明包含：

| 属性 | 类型 | 说明 | 必需 |
|------|------|------|------|
| PluginDependencies | List\<string\> | 插件依赖类名列表 | ✓ |
| LibraryDependencies | List\<string\> | 库文件名列表 | ✓ |
| AllowDynamicLoad | bool | 是否允许动态加载/卸载（默认 true） | - |

### 模块状态

| 状态 | 说明 |
|------|------|
| NotFound | 模块不存在 |
| Scanned | 已扫描 |
| Initializing | 初始化中 |
| Running | 运行中 |
| Error | 错误 |
| Unloaded | 已卸载 |
| Skipped | 被跳过（未加载） |
| Interrupted | 被中断 |

---

## 日志系统

MorningCat 使用 `Logging` 命名空间下的日志系统，每个插件拥有独立的 AssemblyLoadContext（ALC），因此可以使用 `Log.Name` 特性来标识日志来源。

### Log.Name 用法

**推荐做法**：在模块初始化时设置 `Log.Name`，后续所有日志自动带上模块标识：

```csharp
public async Task Init()
{
    Log.Name("MyPlugin");
    Log.Info("初始化完成");       // 输出: [MyPlugin] 初始化完成
    Log.Debug("调试信息");        // 输出: [MyPlugin] 调试信息
    Log.Warning("警告信息");      // 输出: [MyPlugin] 警告信息
    Log.Error("错误信息");        // 输出: [MyPlugin] 错误信息
}
```

**不推荐做法**：在每条日志中手动添加模块名前缀：

```csharp
// 不推荐 - 不优雅且增加开发成本
Log.Info("[MyPlugin] 初始化完成");
Log.Debug("[MyPlugin] 调试信息");
Log.Warning("[MyPlugin] 警告信息");
```

### 日志级别

| 方法 | 级别 | 用途 |
|------|------|------|
| `Log.Debug()` | DEBUG | 调试信息，仅在调试模式下输出 |
| `Log.Info()` | INFO | 常规信息 |
| `Log.Warning()` | WARNING | 警告信息 |
| `Log.Error()` | ERROR | 错误信息 |

### 注意事项

- 每个插件有独立 ALC，`Log.Name` 设置互不影响
- `Log.Name` 只需在 `Init()` 中设置一次，后续该 ALC 中的所有日志调用都会自动使用该名称
- 不要在日志消息中手动拼接 `[插件名]` 前缀，这既不优雅也增加维护成本
