# MorningCat

[![GitHub](https://img.shields.io/badge/GitHub-XSY--HYH/MorningCat-blue)](https://github.com/XSY-HYH/MorningCat)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![OneBot](https://img.shields.io/badge/OneBot-11-orange)](https://github.com/botuniverse/onebot-11)
[![NapCat](https://img.shields.io/badge/NapCat-orange)](https://github.com/NapNeko/NapCatQQ)
一个C#的QQ机器人框架

## 特性

- 模块化设计：支持动态加载、卸载插件
- WebUI 管理：提供现代化的 Web 管理界面
- 命令系统：灵活的命令注册和处理机制
- OneBot 11 协议：标准化机器人协议支持
- WebSocket 重连：断线自动重连机制

*其实不说别的，MorningCat真的只是我闲的蛋疼做的QAQ*

## 目录结构

```
MorningCat/
├── MorningCat/                 # 主程序
│   ├── Commands/               # 命令系统
│   ├── Config/                 # 配置管理
│   ├── Modules/                # 内置模块
│   ├── WebUI/                  # WebUI 管理
│   └── bin/Debug/net10.0/
│       └── Modules/            # 插件目录
├── MorningCat.WebUI/           # WebUI 服务
├── Lib/                        # 依赖库
│   ├── OneBotLib.dll           # OneBot 11 协议库
│   ├── ModuleManagerLib.dll    # 模块管理库
│   └── logs.dll                # 日志库
└── MCServerQuery/              # 示例插件
```

## 配置说明

配置文件 `config.yml` 位于程序运行目录：

```yaml
napCatServerUrl: ws://127.0.0.1:7892
napCatToken: your_token_here
reconnectDelay: 5
modulesDirectory: Modules
autoLoadModules: true
owner_qq: 0
admin_qqs: []
webui:
  enabled: true
  port: 8080
  username: admin
  password: admin123
```

### 配置项说明

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| napCatServerUrl | string | ws://127.0.0.1:7892 | OneBot 服务 WebSocket 地址 |
| napCatToken | string | your_token_here | OneBot 访问令牌 |
| reconnectDelay | int | 5 | WebSocket 断开后重连延迟（秒） |
| modulesDirectory | string | Modules | 插件目录 |
| autoLoadModules | bool | true | 是否自动加载插件 |
| owner_qq | long | 0 | 持有者 QQ 号 |
| admin_qqs | list | [] | 管理员 QQ 号列表 |
| webui.enabled | bool | true | 是否启用 WebUI |
| webui.port | int | 8080 | WebUI 端口 |
| webui.username | string | admin | WebUI 登录用户名 |
| webui.password | string | admin123 | WebUI 登录密码 |

## 内置命令

### 系统命令

| 命令 | 说明 | 权限 |
|------|------|------|
| help | 显示帮助信息 | 所有人 |
| status | 显示系统状态 | 所有人 |
| stop | 停止运行 | 管理员 |
| restart | 重启程序 | 管理员 |

### 插件命令

| 命令 | 说明 | 权限 |
|------|------|------|
| plugin list | 列出所有插件 | 管理员 |
| plugin info <名称> | 查看插件详情 | 管理员 |
| plugin enable <名称> | 启用插件 | 管理员 |
| plugin disable <名称> | 禁用插件 | 管理员 |
| plugin unload <名称> | 卸载插件 | 管理员 |
| plugin reload <名称> | 重载插件 | 管理员 |

### 设置命令

| 命令 | 说明 | 权限 |
|------|------|------|
| set <配置项> <值> | 设置配置 | 管理员 |
| get <配置项> | 获取配置 | 管理员 |

## 权限系统

### 权限级别

1. **持有者 (Owner)**：拥有最高权限，由 `owner_qq` 配置
2. **管理员 (Admin)**：拥有管理权限，由 `admin_qqs` 配置
3. **普通用户**：只能使用基础命令

### 权限检查

```csharp
// 检查是否为持有者
bool isOwner = config.IsOwner(userId);

// 检查是否为管理员（包含持有者）
bool isAdmin = config.IsAdmin(userId);
```

## WebSocket 重连机制

当 WebSocket 连接断开时，MorningCat 会自动尝试重连：

1. 检测到断开连接（通过生命周期事件或连接状态变化）
2. 启动重连定时器，间隔由 `reconnectDelay` 配置
3. 每次定时器触发时尝试重新连接
4. 连接成功后停止定时器
5. 连接失败则继续等待下次定时器触发

## 运行要求

- .NET 10.0 Runtime
- OneBot 11 协议实现（如 NapCat、Lagrange 等）

## 构建与运行

```bash
# 构建
cd MorningCat
dotnet build -c Debug

# 运行
dotnet run

# 发布
dotnet publish -c Release -r win-x64 --self-contained
```

## 相关链接

- [MorningCat.WebUI 文档](./MorningCat.WebUI.md)
- [插件开发 API 文档](./PluginDevelopment.md)
- [从仓库下载](https://110.42.98.47:59113/?path=MorningCatSetup)