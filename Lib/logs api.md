# Logs 日志类库 API 文档

## 概述

Logs 是一个轻量级的 C# 日志记录类库，支持彩色控制台输出、自动文件记录、调用者信息追踪，并支持分别设置控制台输出级别和文件记录级别。新增**日志输出回调**功能，可获取控制台的实际输出（包含 ANSI 码）。

## 命名空间

```csharp
using Logging;
```

## 快速开始

```csharp
using Logging;

// 订阅日志输出回调
Log.OnLogOutput += (logMessage) =>
{
    Console.WriteLine($"回调收到: {logMessage}");
};

// 设置日志来源
Log.Name("MyApplication");

// 设置控制台只输出 Info 及以上级别
Log.SetConsoleLevel(LogLevel.Info);

// 文件记录所有级别
Log.SetFileLevel(LogLevel.Debug);

// 记录日志
Log.Debug("调试信息");   // 只写文件
Log.Info("普通信息");    // 控制台 + 文件
Log.Warning("警告信息"); // 控制台 + 文件
Log.Error("错误信息");   // 控制台 + 文件
```

---

## 枚举类型

### LogLevel - 日志级别

| 值 | 说明 | 优先级 |
|----|------|--------|
| Debug | 调试信息 | 0（最低） |
| Info | 普通信息 | 1 |
| Warning | 警告信息 | 2 |
| Error | 错误信息 | 3 |
| Critical | 严重错误 | 4 |
| None | 不输出任何日志 | 5（最高） |

**优先级说明**：设置为 `Warning` 时，`Warning`、`Error`、`Critical` 会输出，`Debug` 和 `Info` 不会输出。

---

## 静态类 - Log

### 事件

| 事件 | 类型 | 说明 |
|------|------|------|
| `OnLogOutput` | Action\<string\>? | 日志输出回调，参数为格式化后的日志消息（包含 ANSI 颜色码） |

### 配置方法

| 方法 | 说明 |
|------|------|
| `Name(string source)` | 设置日志来源名称 |
| `SetLogDirectory(string directory)` | 设置日志文件存放目录 |
| `SetConsoleLevel(LogLevel level)` | 设置控制台输出级别 |
| `SetFileLevel(LogLevel level)` | 设置文件记录级别 |

### 日志记录方法

| 方法 | 说明 |
|------|------|
| `Debug(string message)` | 记录调试日志 |
| `Info(string message)` | 记录信息日志 |
| `Warning(string message)` | 记录警告日志 |
| `Error(string message)` | 记录错误日志 |
| `Critical(string message)` | 记录严重错误日志 |
| `Exception(Exception ex, string message = null)` | 记录异常日志 |

所有日志方法都会自动记录调用者的文件名和行号。

---

## 日志格式

### 控制台输出格式（带颜色）

```
时间戳 - 级别 - [来源] [文件名:行号] - 消息内容
```

### 文件输出格式（无颜色）

```
时间戳 - 级别 - [来源] [文件名:行号] - 消息内容
```

### 颜色映射（控制台）

| 级别 | ConsoleColor | ANSI 码 |
|------|--------------|---------|
| Debug | Cyan（青色） | `\u001b[36m` |
| Info | Green（绿色） | `\u001b[32m` |
| Warning | Yellow（黄色） | `\u001b[33m` |
| Error | Red（红色） | `\u001b[31m` |
| Critical | Magenta（紫色） | `\u001b[35m` |

### 回调输出格式（包含 ANSI 码）

回调输出的消息包含完整的 ANSI 转义序列，例如：
```
\u001b[32m2026-04-16 10:30:45,123 - INFO - [MyApp] [Program.cs:25] - 应用程序启动\u001b[0m
```

---

## 详细使用指南

### 1. 基础使用

```csharp
using Logging;

Log.Name("MyApp");
Log.Info("应用程序启动");
Log.Debug("正在加载配置...");
Log.Warning("配置文件不存在，使用默认配置");
Log.Error("加载失败");
Log.Critical("系统崩溃");
```

### 2. 设置日志级别

```csharp
// 控制台只输出 Warning 及以上级别
Log.SetConsoleLevel(LogLevel.Warning);

// 文件记录所有级别
Log.SetFileLevel(LogLevel.Debug);

Log.Debug("调试信息");   // ❌ 不显示控制台 ✅ 写入文件
Log.Info("普通信息");    // ❌ 不显示控制台 ✅ 写入文件
Log.Warning("警告信息"); // ✅ 显示控制台 ✅ 写入文件
Log.Error("错误信息");   // ✅ 显示控制台 ✅ 写入文件
```

### 3. 关闭控制台输出

```csharp
// 关闭控制台输出，只记录到文件
Log.SetConsoleLevel(LogLevel.None);
Log.SetFileLevel(LogLevel.Debug);

Log.Info("这条不会显示在控制台，但会写入文件");
```

### 4. 设置日志目录

```csharp
// 使用自定义目录
Log.SetLogDirectory(@"C:\MyApp\Logs");

// 恢复默认目录（程序目录下的 logs 文件夹）
Log.SetLogDirectory(null);
```

### 5. 记录异常

```csharp
try
{
    // 可能抛出异常的代码
}
catch (Exception ex)
{
    // 只记录异常
    Log.Exception(ex);
    
    // 带附加信息记录异常
    Log.Exception(ex, "处理用户请求时出错");
}
```

### 6. 订阅日志输出回调

```csharp
// 订阅日志输出回调
Log.OnLogOutput += (logMessage) =>
{
    // 通过 WebSocket 推送到前端
    // await webSocket.SendAsync(logMessage);
    
    // 或者保存到数据库
    // await SaveLogToDatabase(logMessage);
    
    // 或者显示在 UI 上
    Console.WriteLine($"回调收到: {logMessage}");
};

Log.Info("这条日志会触发回调");
```

### 7. WebSocket 推送日志示例

```csharp
using System.Net.WebSockets;
using System.Text;
using Logging;

public class LogWebSocketHandler
{
    private WebSocket _webSocket;
    
    public LogWebSocketHandler(WebSocket webSocket)
    {
        _webSocket = webSocket;
        
        // 订阅日志输出
        Log.OnLogOutput += async (logMessage) =>
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(logMessage);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None
                );
            }
        };
    }
}
```

### 8. 实时日志面板示例（WPF）

```csharp
// 收集最近100条日志
var recentLogs = new List<string>();

Log.OnLogOutput += (logMessage) =>
{
    // 移除ANSI码用于显示
    var plainText = System.Text.RegularExpressions.Regex.Replace(
        logMessage, 
        "\u001b\\[[0-9;]*m", 
        ""
    );
    
    recentLogs.Add(plainText);
    if (recentLogs.Count > 100)
    {
        recentLogs.RemoveAt(0);
    }
    
    // 更新UI（需要在UI线程上执行）
    Application.Current.Dispatcher.Invoke(() =>
    {
        LogTextBox.AppendText(plainText + Environment.NewLine);
        LogTextBox.ScrollToEnd();
    });
};
```

### 9. 多模块使用

```csharp
// Module A
Log.Name("UserModule");
Log.Info("用户登录成功");

// Module B
Log.Name("DatabaseModule");
Log.Info("数据库连接成功");

// 输出示例：
// 2024-01-15 10:30:45,123 - INFO - [UserModule] [UserService.cs:25] - 用户登录成功
// 2024-01-15 10:30:45,456 - INFO - [DatabaseModule] [DbManager.cs:18] - 数据库连接成功
```

### 10. 写入额外日志文件

```csharp
using (var writer = new StreamWriter("special.log", true))
{
    Log.OnLogOutput += (logMessage) =>
    {
        // 移除ANSI码后写入
        var plainText = System.Text.RegularExpressions.Regex.Replace(
            logMessage, 
            "\u001b\\[[0-9;]*m", 
            ""
        );
        writer.WriteLine(plainText);
    };
}
```

---

## 日志级别配置场景

| 场景 | 控制台级别 | 文件级别 | 说明 |
|------|-----------|----------|------|
| 开发调试 | Debug | Debug | 控制台和文件都输出所有日志 |
| 生产环境 | Info | Debug | 控制台只显示重要信息，文件记录所有 |
| 静默运行 | None | Error | 控制台无输出，只记录错误到文件 |
| 性能测试 | Warning | Debug | 控制台只显示警告以上，文件记录所有 |

---

## 回调消息格式

### 带 ANSI 码的完整消息示例

```
\u001b[32m2026-04-16 10:30:45,123 - INFO - [MyApp] [Program.cs:25] - 应用程序启动\u001b[0m
```

### ANSI 颜色码对照表

| 级别 | ANSI 码 | 颜色 |
|------|---------|------|
| Debug | `\u001b[36m` | 青色 |
| Info | `\u001b[32m` | 绿色 |
| Warning | `\u001b[33m` | 黄色 |
| Error | `\u001b[31m` | 红色 |
| Critical | `\u001b[35m` | 紫色 |
| 重置 | `\u001b[0m` | - |

---

## 自动特性

### 1. 自动调用者信息

日志会自动记录调用者的文件名和行号，无需手动传递：

```csharp
// 在任意文件中调用
Log.Info("测试");

// 自动显示：[Utils.cs:15]
```

### 2. 自动日志目录创建

如果指定的日志目录不存在，会自动创建。

### 3. 自动日志文件切换

每小时自动创建新的日志文件，避免单个文件过大。

### 4. 线程安全

所有日志操作都是线程安全的，可在多线程环境中使用。

---

## 注意事项

1. **控制台颜色**：使用 C# 原生 `ConsoleColor` API，所有 Windows 控制台都支持
2. **回调 ANSI 码**：回调输出的消息包含 ANSI 转义序列，适用于支持 ANSI 的终端或前端日志组件
3. **回调线程安全**：回调事件在多线程环境下是安全的
4. **回调性能**：回调中避免耗时操作，建议异步处理
5. **回调异常**：回调中的异常不会影响日志系统正常工作
6. **文件权限**：确保程序有日志目录的写入权限
7. **日志文件增长**：自动每小时切换文件，建议定期清理旧日志
8. **级别独立**：控制台和文件的级别设置是独立的，互不影响

---

## 完整示例

```csharp
using Logging;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        // 订阅日志输出
        Log.OnLogOutput += (logMessage) =>
        {
            // 移除ANSI码用于显示
            var plainText = Regex.Replace(logMessage, "\u001b\\[[0-9;]*m", "");
            Console.WriteLine($"[回调] {plainText}");
        };
        
        // 配置日志
        Log.Name("启动程序");
        Log.SetLogDirectory(@"D:\AppLogs");
        Log.SetConsoleLevel(LogLevel.Info);
        Log.SetFileLevel(LogLevel.Debug);
        
        Log.Info("应用程序启动");
        
        try
        {
            RunApplication();
        }
        catch (Exception ex)
        {
            Log.Exception(ex, "应用程序运行失败");
        }
        
        Log.Info("应用程序退出");
    }
    
    static void RunApplication()
    {
        Log.Name("业务处理");
        Log.Debug("开始处理数据");
        // 业务逻辑
        Log.Info("数据处理完成");
    }
}
```

---

## 版本信息

| 项目 | 内容 |
|------|------|
| 版本 | 1.2.0 |
| 命名空间 | Logging |
| 依赖 | .NET Standard 2.0+ |

---

**更新日期：** 2026-04-16