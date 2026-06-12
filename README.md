<div align="center">
  <img src="icon.png" alt="MorningCat" width="128" height="128">
</div>

# MorningCat

[![GitHub](https://img.shields.io/badge/GitHub-XSY--HYH/MorningCat-blue)](https://github.com/XSY-HYH/MorningCat)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![OneBot](https://img.shields.io/badge/OneBot-11-orange)](https://github.com/botuniverse/onebot-11)

A high-performance, cross-platform bot based on .NET 10, supporting platforms such as QQ, Discord, and more.

基于 .NET 10 的高性能跨平台机器人，支持 QQ、Discord 等多平台。

---

## Features / 特性

- **Cross-Platform / 跨平台** - Unified message abstraction (MDC + IMessageBuilder) across QQ (OneBot 11), Discord, DingTalk, and more / 统一消息抽象层，支持 QQ、Discord、钉钉等多平台
- **Plugin System / 插件系统** - Dynamic loading/unloading with dependency injection, API registry, and event bus / 动态加载卸载，支持依赖注入、API 注册和事件总线
- **Command System / 命令系统** - Tree-structured parameters with permission and scope control / 树形参数结构，权限和作用域控制
- **WebUI / 管理界面** - Modern web dashboard for plugin management, logging, and configuration / 现代化 Web 管理界面
- **Plugin Store / 插件市场** - Browse, install, and update plugins from third-party stores / 从第三方市场浏览、安装、更新插件
- **Auto-Update / 自动更新** - Built-in launcher with self-update and core update via WebSocket push / 内置启动器，支持自更新和 WebSocket 推送核心更新
- **Plugin Signature / 插件签名** - Optional signature verification for plugin security / 可选的插件签名验证

## Architecture / 架构

```
MorningCat/
├── MorningCat/                     # Core / 核心库
│   ├── MDC/                        # Message Distribution Core / 消息分发核心
│   │   ├── MessageDistributionCore.cs
│   │   └── OneBotPlatformAdapter.cs
│   ├── Commands/                   # Command system / 命令系统
│   ├── Config/                     # Configuration / 配置管理
│   ├── Modules/                    # Built-in modules / 内置模块
│   ├── PluginAPI/                  # Plugin APIs / 插件 API
│   ├── Security/                   # Signature verification / 签名验证
│   └── WebUI/                      # WebUI manager / WebUI 管理
├── MorningCat.PlatformAbstraction/ # Cross-platform models / 跨平台模型
├── MorningCat.GUI/                 # Electron GUI / 图形界面
├── MorningCat.WebUI/               # Web dashboard / Web 管理界面
├── MorningCat.PPC/                 # Plugin project creator / 插件项目创建器
├── MorningCatLaunch/               # Launcher / 启动器
├── MorningCatLaunchCore/           # Update manager / 更新管理器
├── ModuleManager/                  # Module manager source / 模块管理器源码
├── PetPet/                         # Example plugin / 示例插件
├── Lib/                            # Dependency libraries / 依赖库
└── docs/                           # Documentation / 文档
```

## Quick Start / 快速开始

### Prerequisites / 前置要求

- .NET 10 SDK
- OneBot 11 implementation (e.g., [NapCat](https://github.com/NapNeko/NapCatQQ))

### Configuration / 配置

Edit `config.yml` in the runtime directory:

编辑运行目录下的 `config.yml`：

```yaml
napcat_server_url: "ws://127.0.0.1:7892"
napcat_token: "your_token"
owner_qq: 123456789
modules_directory: "Modules"
auto_load_modules: true
webui:
  enabled: true
  port: 8080
  username: "admin"
  password: "admin123"
```

## Plugin Development / 插件开发

### Minimal Example / 最小示例

```csharp
using ModuleManagerLib;
using MorningCat.Commands;
using MorningCat.PluginAPI;
using Logging;

[PluginMetadata(DisplayName = "My Plugin", Author = "Author", Description = "Description")]
public class MyPlugin : ModuleBase
{
    private MessageDistributionCore _mdc = null!;
    private CommandRegistry _commandRegistry = null!;

    public MessageDistributionCore MDC { get => _mdc; set => _mdc = value; }
    public CommandRegistry CommandRegistry { get => _commandRegistry; set => _commandRegistry = value; }

    public override async Task Init()
    {
        _commandRegistry?.RegisterCommand(
            "hello", "Greet", "hello - Say hello",
            new List<CommandParameter>(), HandleHello, "MyPlugin",
            CommandPermission.Everyone, CommandScope.All,
            requireAt: false, requireSlash: true
        );
    }

    private async Task HandleHello(CommandContext context)
    {
        await _mdc.SendAsync(context.Message, builder => builder.Text("Hello!"));
    }
}
```

### Sending Messages / 发送消息

Use `MDC.SendAsync` with `IMessageBuilder` for cross-platform message construction:

使用 `MDC.SendAsync` + `IMessageBuilder` 构建跨平台消息：

```csharp
// Text / 文本
await _mdc.SendAsync(message, builder => builder.Text("Hello"));

// Reply + At / 回复 + 艾特
await _mdc.SendAsync(message, builder => builder
    .Reply(message.MessageId)
    .At(message.SenderId)
    .Text("Reply content"));
```

### Key Injected Services / 主要注入服务

| Type / 类型 | Name / 名称 | Description / 说明 |
|---|---|---|
| `MessageDistributionCore` | MDC | Cross-platform message sending / 跨平台消息发送 |
| `CommandRegistry` | CommandRegistry | Command registration / 命令注册 |
| `PluginConfigManager` | ConfigManager | Plugin configuration / 插件配置 |
| `PluginDatabaseAPI` | PluginDatabaseAPI | Plugin database / 插件数据库 |
| `MorningCatBot` | Bot | Bot instance / 机器人实例 |

## Documentation / 文档

| Document / 文档 | Description / 说明 |
|---|---|
| [Architecture / 架构详解](docs/architecture.md) | Core components and message flow / 核心组件与消息流程 |
| [Plugin Development / 插件开发](docs/plugin-development.md) | Plugin development guide / 插件开发指南 |
| [Plugin Cookbook / 插件手册](docs/plugin-cookbook.md) | Best practices and troubleshooting / 最佳实践与避坑 |
| [API Reference / API 参考](docs/api-reference.md) | Core API reference / 核心 API 参考 |
| [Startup Flow / 启动任务流](docs/MCT启动任务流.md) | Complete startup process / 完整启动流程 |
| [Best Practices / 最佳实践](docs/best-practices.md) | Development notes / 开发注意事项 |
| [OneBotLib API](Lib/OneBotLib.md) | OneBot 11 protocol library / OneBot 11 协议库 |
| [ModuleManagerLib API](Lib/ModuleManagerLib.md) | Module manager library / 模块管理器库 |
| [Logs API](Lib/logs%20api.md) | Logging library / 日志库 |
| [PlatformAbstraction](Lib/MorningCat.PlatformAbstraction.md) | Cross-platform abstraction / 跨平台抽象 |

## Built-in Commands / 内置命令

| Command / 命令 | Description / 说明 | Permission / 权限 |
|---|---|---|
| `/help` | Show help / 显示帮助 | Everyone / 所有人 |
| `/status` | Show status / 显示状态 | Everyone / 所有人 |
| `/stop` | Stop bot / 停止运行 | BotOwner / 持有者 |
| `/plugin list` | List plugins / 列出插件 | GroupAdmin / 管理员 |
| `/plugin enable <name>` | Enable plugin / 启用插件 | GroupAdmin / 管理员 |
| `/plugin disable <name>` | Disable plugin / 禁用插件 | GroupAdmin / 管理员 |

## License / 许可证

[MIT](LICENSE)
