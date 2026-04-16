# MorningCat.WebUI

MorningCat.WebUI 是 MorningCat 的 Web 管理界面，提供可视化的机器人管理功能。

## 特性

- 现代化响应式界面
- 插件管理（启用、禁用、卸载）
- 实时日志查看
- 系统配置管理
- 机器人状态监控
- 主题自定义

## 技术栈

- 后端：ASP.NET Core Minimal API
- 前端：React + TypeScript + Vite
- UI 框架：HeroUI

## 目录结构

```
MorningCat.WebUI/
├── Services/
│   └── AccountService.cs      # 账户服务
├── Resources/
│   └── about.md               # 关于页面内容
├── wwwroot/
│   └── webui/                 # 前端静态文件
├── Interfaces.cs              # 接口定义
└── WebUIServer.cs             # WebUI 服务器
```

## API 接口

### 认证相关

| 端点 | 方法 | 说明 |
|------|------|------|
| /api/auth/login | POST | 登录 |
| /api/auth/logout | POST | 登出 |
| /api/auth/update_password | POST | 修改密码 |
| /api/auth/check | GET | 检查登录状态 |

### 系统信息

| 端点 | 方法 | 说明 |
|------|------|------|
| /api/system/info | GET | 获取系统信息 |
| /api/system/version | GET | 获取版本信息 |
| /api/system/restart | POST | 重启程序 |
| /api/system/shutdown | POST | 停止程序 |
| /api/about | GET | 获取关于页面内容 |

### 配置管理

| 端点 | 方法 | 说明 |
|------|------|------|
| /api/config | GET | 获取配置 |
| /api/config | POST | 更新配置 |

### 插件管理

| 端点 | 方法 | 说明 |
|------|------|------|
| /api/plugins | GET | 获取插件列表 |
| /api/plugins/{name} | GET | 获取插件详情 |
| /api/plugins/{name}/enable | POST | 启用插件 |
| /api/plugins/{name}/disable | POST | 禁用插件 |
| /api/plugins/{name}/unload | POST | 卸载插件 |
| /api/plugins/{name}/configs | GET | 获取插件配置列表 |
| /api/plugins/{name}/configs/{config} | GET | 获取插件配置内容 |
| /api/plugins/{name}/configs/{config} | POST | 保存插件配置 |

### 日志

| 端点 | 方法 | 说明 |
|------|------|------|
| /api/logs | GET | 获取日志 |
| /api/logs/clear | POST | 清空日志 |
| /api/logs/stream | GET | WebSocket 日志流 |

### 机器人信息

| 端点 | 方法 | 说明 |
|------|------|------|
| /api/bot/info | GET | 获取机器人信息 |

## 接口定义

### IWebUIServer

WebUI 服务器接口：

```csharp
public interface IWebUIServer
{
    int Port { get; }
    bool IsRunning { get; }
    Task StartAsync(int port = 8080);
    Task StopAsync();
}
```

### IConfigProvider

配置提供者接口：

```csharp
public interface IConfigProvider
{
    WebUIConfigData GetConfig();
    void UpdateConfig(Action<WebUIConfigData> updateAction);
}
```

### ISystemInfoProvider

系统信息提供者接口：

```csharp
public interface ISystemInfoProvider
{
    SystemInfo GetSystemInfo();
    void SetRestartCallback(Func<Task> restartCallback);
    void SetShutdownCallback(Action shutdownCallback);
    void RequestRestart();
    void RequestShutdown();
}
```

### IBotInfoProvider

机器人信息提供者接口：

```csharp
public interface IBotInfoProvider
{
    BotInfo? GetBotInfo();
    void SetConnectionStatus(bool isConnected);
}
```

### IPluginInfoProvider

插件信息提供者接口：

```csharp
public interface IPluginInfoProvider
{
    List<PluginInfo> GetPlugins();
    PluginInfo? GetPlugin(string moduleName);
    bool DisablePlugin(string moduleName);
    bool EnablePlugin(string moduleName);
    bool UnloadPlugin(string moduleName);
    PluginDetail? GetPluginDetail(string moduleName);
    List<PluginConfigInfo> GetPluginConfigs(string moduleName);
    Dictionary<string, object>? GetPluginConfig(string moduleName, string configName);
    bool SavePluginConfig(string moduleName, string configName, Dictionary<string, object> config);
}
```

### ILogProvider

日志提供者接口：

```csharp
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
```

## 数据模型

### SystemInfo

```csharp
public class SystemInfo
{
    public string Version { get; set; }         // 版本号
    public long MemoryUsedMB { get; set; }      // 已用内存 (MB)
    public long MemoryTotalMB { get; set; }     // 总内存 (MB)
    public double CpuUsage { get; set; }        // CPU 使用率
    public string CpuModel { get; set; }        // CPU 型号
    public string CpuSpeed { get; set; }        // CPU 频率
    public string Arch { get; set; }            // 架构
    public int PluginCount { get; set; }        // 插件总数
    public int RunningPluginCount { get; set; } // 运行中插件数
    public DateTime StartTime { get; set; }     // 启动时间
    public TimeSpan Uptime { get; set; }        // 运行时长
}
```

### BotInfo

```csharp
public class BotInfo
{
    public long UserId { get; set; }            // QQ 号
    public string Nickname { get; set; }        // 昵称
    public string Qid { get; set; }             // QID
    public int Level { get; set; }              // 等级
    public bool IsOnline { get; set; }          // 是否在线
    public bool IsNapCatConnected { get; set; } // 是否连接到 OneBot
}
```

### PluginInfo

```csharp
public class PluginInfo
{
    public string ModuleName { get; set; }      // 模块名
    public string? DisplayName { get; set; }    // 显示名称
    public string? Author { get; set; }         // 作者
    public string? Description { get; set; }    // 描述
    public string Status { get; set; }          // 状态
    public bool IsBuiltin { get; set; }         // 是否内置
    public string? AssemblyPath { get; set; }   // 程序集路径
    public string? IconBase64 { get; set; }     // 图标 (Base64)
}
```

### PluginDetail

```csharp
public class PluginDetail
{
    public string ModuleName { get; set; }      // 模块名
    public string? DisplayName { get; set; }    // 显示名称
    public string? Author { get; set; }         // 作者
    public string? Description { get; set; }    // 描述
    public string? Website { get; set; }        // 网站
    public string Status { get; set; }          // 状态
    public bool IsBuiltin { get; set; }         // 是否内置
    public string? ModuleType { get; set; }     // 模块类型
    public string? AssemblyPath { get; set; }   // 程序集路径
    public bool HasInstance { get; set; }       // 是否有实例
    public List<string> Dependencies { get; set; }   // 依赖列表
    public List<string> Dependents { get; set; }     // 被依赖列表
    public string? IconBase64 { get; set; }     // 图标 (Base64)
}
```

## 前端开发

### 环境要求

- Node.js 18+
- pnpm 或 npm

### 开发命令

```bash
cd napcat-webui-frontend

# 安装依赖
npm install

# 开发模式
npm run dev

# 构建
npm run build
```

### 构建部署

```bash
# 构建
npm run build

# 复制到 WebUI 目录
cp -r dist/* ../MorningCat.WebUI/wwwroot/webui/
```

## 集成到主程序

```csharp
// 创建 WebUI 服务器
var webUIServer = new WebUIServer(config.WebUI.Username, config.WebUI.Password);

// 设置提供者
webUIServer.SetConfigProvider(configManager);
webUIServer.SetSystemInfoProvider(systemInfoProvider);
webUIServer.SetBotInfoProvider(botInfoProvider);
webUIServer.SetPluginInfoProvider(pluginInfoProvider);
webUIServer.SetLogProvider(logProvider);

// 启动
await webUIServer.StartAsync(config.WebUI.Port);
```

## 相关文档

- [MorningCat 主项目文档](./README.md)
- [插件开发 API 文档](./PluginDevelopment.md)
