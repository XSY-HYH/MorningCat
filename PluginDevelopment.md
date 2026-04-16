# MorningCat 插件开发 API 文档

本文档介绍如何为 MorningCat 开发插件。

## 目录

1. [快速开始](#快速开始)
2. [模块基类](#模块基类)
3. [命令系统](#命令系统)
4. [依赖注入](#依赖注入)
5. [消息处理](#消息处理)
6. [配置管理](#配置管理)
7. [插件元数据](#插件元数据)
8. [完整示例](#完整示例)

---

## 快速开始

### 项目文件 (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="icon.png">
      <LogicalName>icon.png</LogicalName>
    </EmbeddedResource>
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Logging">
      <HintPath>..\..\Lib\logs.dll</HintPath>
    </Reference>
    <Reference Include="ModuleManagerLib">
      <HintPath>..\..\Lib\ModuleManagerLib.dll</HintPath>
    </Reference>
    <Reference Include="MorningCat">
      <HintPath>..\..\Lib\MorningCat.dll</HintPath>
    </Reference>
    <Reference Include="OneBotLib">
      <HintPath>..\..\Lib\OneBotLib.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
```

### 最小模块示例

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModuleManagerLib;

public class MyPlugin : ModuleBase
{
    public override async Task Init()
    {
        // 初始化代码
        await Task.CompletedTask;
    }

    public override IEnumerable<string> GetDependencies()
    {
        return Array.Empty<string>();
    }
}
```

---

## 模块基类

### ModuleBase

所有插件都应继承 `ModuleBase` 抽象类：

```csharp
using ModuleManagerLib;

public abstract class ModuleBase
{
    // 必须实现：模块初始化
    public abstract Task Init();
    
    // 可选重写：模块卸载时的清理
    public virtual Task Exit() => Task.CompletedTask;
    
    // 可选重写：返回依赖的模块列表
    public virtual IEnumerable<string> GetDependencies() => Array.Empty<string>();
}
```

### 生命周期

```
加载 DLL -> 扫描模块类 -> 依赖注入 -> Init() -> 运行中 -> Exit() -> 卸载
```

---

## 命令系统

### 注册命令

通过 `CommandRegistry` 注册命令：

```csharp
using MorningCat.Commands;

public class MyPlugin : ModuleBase
{
    private CommandRegistry _commandRegistry = null!;
    
    public CommandRegistry CommandRegistry
    {
        get => _commandRegistry;
        set => _commandRegistry = value;
    }

    public override async Task Init()
    {
        RegisterCommands();
        await Task.CompletedTask;
    }

    private void RegisterCommands()
    {
        var parameters = new List<CommandParameter>
        {
            new CommandParameter
            {
                Name = "参数名",
                Description = "参数描述",
                IsRequired = true,
                Type = ParameterType.String,
                DefaultValue = "默认值"
            }
        };

        _commandRegistry?.RegisterCommand(
            "mycommand",                    // 命令名称
            "命令描述",                      // 简短描述
            "mycommand <参数>\n详细帮助文本", // 帮助文本
            parameters,                      // 参数列表
            HandleMyCommand,                 // 处理函数
            "MyPlugin"                       // 模块名
        );
    }

    private async Task HandleMyCommand(CommandContext context)
    {
        var message = context.Message;
        var parameters = context.Parameters;
        
        // 获取参数
        if (parameters.TryGetValue("参数名", out var value))
        {
            // 处理命令
        }
        
        await Task.CompletedTask;
    }

    public override async Task Exit()
    {
        _commandRegistry?.UnregisterModuleCommands("MyPlugin");
        await Task.CompletedTask;
    }
}
```

### CommandParameter

```csharp
public class CommandParameter
{
    public string Name { get; set; }              // 参数名
    public string Description { get; set; }       // 参数描述
    public bool IsRequired { get; set; }          // 是否必需
    public ParameterType Type { get; set; }       // 参数类型
    public string DefaultValue { get; set; }      // 默认值
    public List<CommandParameter> SubParameters { get; set; }  // 子参数
}
```

### ParameterType 枚举

| 值 | 说明 |
|----|------|
| String | 字符串 |
| Integer | 整数 |
| Float | 浮点数 |
| Boolean | 布尔值 |
| At | @某人 |

### CommandPermission 枚举

| 值 | 说明 |
|----|------|
| Everyone | 所有人可用 |
| GroupAdmin | 群管理员可用 |
| Owner | 群主可用 |
| BotOwner | 机器人持有者可用 |

### CommandScope 枚举

| 值 | 说明 |
|----|------|
| All | 所有场景可用 |
| PrivateOnly | 仅私聊可用 |

### 注册命令选项

```csharp
_commandRegistry?.RegisterCommand(
    "command",
    "描述",
    "帮助文本",
    parameters,
    handler,
    "ModuleName",
    permission: CommandPermission.BotOwner,  // 权限
    scope: CommandScope.All,                  // 范围
    requireAt: false,                         // 是否需要 @机器人
    requireSlash: true                        // 是否需要 / 前缀
);
```

### CommandContext

```csharp
public class CommandContext
{
    public MessageObject Message { get; set; }              // 消息对象
    public Dictionary<string, string> Parameters { get; set; } // 解析后的参数
    public string RawCommand { get; set; }                  // 原始命令
    public OneBotClient Client { get; set; }                // OneBot 客户端
}
```

---

## 依赖注入

### 可注入的服务

通过属性注入获取框架服务：

```csharp
public class MyPlugin : ModuleBase
{
    // OneBot 客户端
    public OneBotClient Client { get; set; } = null!;
    
    // 命令注册器
    public CommandRegistry CommandRegistry { get; set; } = null!;
    
    // 元数据回调
    private Action<string, string, string, string, string, string> _setMetadata = null!;
    public Action<string, string, string, string, string, string> SetMetadataCallback
    {
        set => _setMetadata = value;
    }

    public override async Task Init()
    {
        // 使用 Client 发送消息
        await Client.SendPrivateMsgAsync(123456789, "Hello!");
        
        // 使用 CommandRegistry 注册命令
        CommandRegistry?.RegisterCommand(...);
        
        await Task.CompletedTask;
    }
}
```

### 模块依赖

如果插件依赖其他模块，在 `GetDependencies` 中声明：

```csharp
public override IEnumerable<string> GetDependencies()
{
    return new List<string> { "OtherModule", "AnotherModule" };
}
```

---

## 消息处理

### 发送文本消息

```csharp
// 私聊
await Client.SendPrivateMsgAsync(userId, "消息内容");

// 群消息
await Client.SendGroupMsgAsync(groupId, "消息内容");
```

### 发送复杂消息

```csharp
using OneBotLib.MessageSegment;

var segments = new List<MessageSegment>
{
    MessageSegment.At(userId),           // @某人
    MessageSegment.Text(" 你好！"),      // 文本
    MessageSegment.Image("https://...")  // 图片
};

await Client.SendGroupMsgAsync(groupId, segments);
```

### 消息段类型

| 方法 | 说明 |
|------|------|
| `Text(text)` | 文本消息 |
| `At(userId)` | @某人 |
| `AtAll()` | @全体成员 |
| `Face(id)` | QQ 表情 |
| `Image(url)` | 图片 |
| `Record(url)` | 语音 |
| `Video(url)` | 视频 |
| `Reply(messageId)` | 回复消息 |
| `Json(json)` | JSON 消息 |
| `Xml(xml)` | XML 消息 |
| `Location(lat, lon, title, desc)` | 位置 |
| `Share(url, title, desc, image)` | 链接分享 |
| `Dice()` | 骰子 |
| `Rps()` | 石头剪刀布 |
| `Poke(type, userId)` | 戳一戳 |

### 其他常用 API

```csharp
// 获取登录信息
var loginInfo = await Client.GetLoginInfoAsync();

// 获取群列表
var groups = await Client.GetGroupListAsync();

// 获取群成员列表
var members = await Client.GetGroupMemberListAsync(groupId);

// 禁言
await Client.SetGroupBanAsync(groupId, userId, duration: 600);

// 撤回消息
await Client.DeleteMsgAsync(messageId);

// 消息表情回应
await Client.SetMsgEmojiLikeAsync(messageId, "66");
```

---

## 配置管理

### 插件配置文件

插件配置存放在 `Plugins/<ModuleName>/` 目录：

```csharp
using MorningCat.Config;

public class MyPlugin : ModuleBase
{
    private PluginConfigManager _configManager = null!;
    
    public PluginConfigManager ConfigManager
    {
        get => _configManager;
        set => _configManager = value;
    }

    private void LoadConfig()
    {
        var config = _configManager.GetConfig<MyConfig>("MyPlugin", "config.json");
        if (config == null)
        {
            config = new MyConfig { Setting1 = "default" };
            _configManager.SaveConfig("MyPlugin", "config.json", config);
        }
    }
}
```

---

## 插件元数据

### 上报元数据

在 `Init` 方法中上报插件信息：

```csharp
public override async Task Init()
{
    var iconBase64 = LoadIconAsBase64();
    
    _setMetadata?.Invoke(
        "MyPlugin",           // 模块名（类名）
        "我的插件",           // 显示名称
        "作者名",             // 作者
        "https://...",        // 网站
        "插件描述",           // 描述
        iconBase64            // 图标 (Base64)
    );
    
    await Task.CompletedTask;
}

private string? LoadIconAsBase64()
{
    try
    {
        var assembly = typeof(MyPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream("icon.png");
        if (stream != null)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
    }
    catch { }
    return null;
}
```

### 元数据字段

| 字段 | 类型 | 说明 |
|------|------|------|
| ModuleName | string | 模块类名（唯一标识） |
| DisplayName | string | 显示名称 |
| Author | string | 作者 |
| Website | string | 网站 |
| Description | string | 描述 |
| IconBase64 | string | Base64 编码的图标 |

---

## 完整示例

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Logging;
using ModuleManagerLib;
using MorningCat.Commands;
using OneBotLib;
using OneBotLib.MessageSegment;
using OneBotLib.Models;

namespace MorningCat.Modules
{
    public class ExampleModule : ModuleBase
    {
        private OneBotClient _client = null!;
        private CommandRegistry _commandRegistry = null!;
        private Action<string, string, string, string, string, string> _setMetadata = null!;

        public Action<string, string, string, string, string, string> SetMetadataCallback
        {
            set => _setMetadata = value;
        }

        public OneBotClient Client
        {
            get => _client;
            set => _client = value;
        }

        public CommandRegistry CommandRegistry
        {
            get => _commandRegistry;
            set => _commandRegistry = value;
        }

        public override async Task Init()
        {
            var iconBase64 = LoadIconAsBase64();
            
            _setMetadata?.Invoke(
                "ExampleModule",
                "示例插件",
                "MorningCat",
                "",
                "这是一个示例插件",
                iconBase64
            );

            RegisterCommands();
            Log.Info("ExampleModule 初始化完成");
            await Task.CompletedTask;
        }

        private string? LoadIconAsBase64()
        {
            try
            {
                var assembly = typeof(ExampleModule).Assembly;
                using var stream = assembly.GetManifestResourceStream("icon.png");
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"加载图标失败: {ex.Message}");
            }
            return null;
        }

        public override IEnumerable<string> GetDependencies()
        {
            return Array.Empty<string>();
        }

        public override async Task Exit()
        {
            _commandRegistry?.UnregisterModuleCommands("ExampleModule");
            Log.Info("ExampleModule 已卸载");
            await Task.CompletedTask;
        }

        private void RegisterCommands()
        {
            var helloParams = new List<CommandParameter>
            {
                new CommandParameter
                {
                    Name = "名字",
                    Description = "要打招呼的名字",
                    IsRequired = false,
                    Type = ParameterType.String,
                    DefaultValue = "世界"
                }
            };

            _commandRegistry?.RegisterCommand(
                "hello",
                "打招呼",
                "hello [名字]\n向指定名字打招呼",
                helloParams,
                HandleHelloCommand,
                "ExampleModule"
            );
        }

        private async Task HandleHelloCommand(CommandContext context)
        {
            var message = context.Message;
            var parameters = context.Parameters;
            
            var name = parameters.TryGetValue("名字", out var n) ? n : "世界";
            var response = $"你好，{name}！";
            
            if (message.MessageType == "private")
            {
                await _client.SendPrivateMsgAsync(message.UserId ?? 0, response);
            }
            else if (message.MessageType == "group")
            {
                var segments = new List<MessageSegment>
                {
                    MessageSegment.At(message.UserId ?? 0),
                    MessageSegment.Text($" {response}")
                };
                await _client.SendGroupMsgAsync(message.GroupId ?? 0, segments);
            }
        }
    }
}
```

---

## 构建与部署

### 构建

```bash
cd YourPlugin
dotnet build -c Debug
```

### 部署

将生成的 DLL 文件复制到 MorningCat 的 `Modules` 目录：

```bash
cp bin/Debug/net10.0/YourPlugin.dll ../MorningCat/bin/Debug/net10.0/Modules/
```

### 目录结构

```
MorningCat/
└── bin/Debug/net10.0/
    └── Modules/
        ├── YourPlugin.dll      # 插件 DLL
        └── Library/            # 依赖库（可选）
            └── SomeDependency.dll
```

---

## 相关文档

- [MorningCat 主项目文档](./README.md)
- [MorningCat.WebUI 文档](./MorningCat.WebUI.md)
- [OneBotLib API 文档](./Lib/OneBotLib.md)
- [ModuleManagerLib API 文档](./Lib/ModuleManager.md)
