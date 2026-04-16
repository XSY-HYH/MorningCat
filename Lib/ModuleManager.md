# ModuleManagerLib 完整 API 文档

## 命名空间
```
ModuleManagerLib
```

---

## 目录

1. [模块开发要求](#一模块开发要求)
2. [模块管理器使用文档](#二模块管理器使用文档)
3. [依赖注入 API](#三依赖注入-api)
4. [模块查询 API](#四模块查询-api)
5. [Library 查询 API](#五library-查询-api)
6. [插件元数据 API](#六插件元数据-api)
7. [完整使用示例](#七完整使用示例)
8. [错误码说明](#八错误码说明)
9. [注意事项](#九注意事项)

---

## 一、模块开发要求

### 1.1 必需实现的方法

| 方法名 | 返回类型 | 参数 | 说明 |
|--------|----------|------|------|
| Init | Task | 无 | 模块初始化入口，必须存在 |
| GetDependencies | IEnumerable\<string\> | 无 | 返回依赖的模块类名列表 |

### 1.2 可选实现的方法

| 方法名 | 返回类型 | 参数 | 说明 |
|--------|----------|------|------|
| Exit | Task | 无 | 模块卸载时的清理方法 |
| SetServices | void | params object[] | 依赖注入回调方法 |

### 1.3 模块示例（不继承任何基类）

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class LoggerModule
{
    public async Task Init()
    {
        await Task.CompletedTask;
    }
    
    public IEnumerable<string> GetDependencies()
    {
        return Array.Empty<string>();
    }
    
    public async Task Exit()
    {
        await Task.CompletedTask;
    }
}
```

### 1.4 模块示例（使用依赖注入）

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModuleManagerLib;

public class DatabaseModule
{
    public IConfigManager ConfigManager { get; set; }
    public NapCatClient Client { get; set; }
    
    [Inject("Logger")]
    public object Logger { get; set; }
    
    [Inject]
    public ILogService LogService;
    
    public async Task Init()
    {
        var config = ConfigManager?.GetConfig();
    }
    
    public IEnumerable<string> GetDependencies()
    {
        return new List<string> { "LoggerModule" };
    }
    
    public async Task Exit()
    {
        await Task.CompletedTask;
    }
}
```

### 1.5 模块示例（上报元数据含图标）

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ModuleManagerLib;

public class MyPlugin : ModuleBase
{
    private Action<string, string, string, string, string, string> _setMetadata;
    
    public Action<string, string, string, string, string, string> SetMetadataCallback
    {
        set => _setMetadata = value;
    }
    
    public override async Task Init()
    {
        string iconBase64 = LoadIconAsBase64("icon.png");
        
        _setMetadata?.Invoke(
            "MyPlugin",                 // 模块名（类名）
            "我的插件",                 // 展示名称
            "夜羽",                     // 作者
            "https://github.com/xxx",   // 网站
            "这是一个示例插件",          // 描述
            iconBase64                  // Base64 图标
        );
        
        await Task.CompletedTask;
    }
    
    private string LoadIconAsBase64(string iconPath)
    {
        if (!File.Exists(iconPath)) return null;
        byte[] bytes = File.ReadAllBytes(iconPath);
        return Convert.ToBase64String(bytes);
    }
}
```

### 1.6 验证规则

| 检查项 | 失败处理 |
|--------|----------|
| Init 方法不存在 | 模块被忽略，记录错误 |
| Init 返回类型不是 Task | 模块被忽略，记录错误 |
| GetDependencies 返回的依赖不存在 | 该模块加载失败，被卸载 |
| 循环依赖 | 所有模块加载失败 |
| Init 方法抛出异常 | 模块标记为 Error，被卸载 |

---

## 二、模块管理器使用文档

### 2.1 类：ModuleManager

主管理类，负责扫描、加载、初始化和管理所有模块。

#### 属性

| 属性名 | 类型 | 说明 |
|--------|------|------|
| CurrentProgress | ProgressInfo | 当前加载进度信息 |

#### 事件

| 事件名 | 类型 | 说明 |
|--------|------|------|
| OnProgressUpdated | Action\<ProgressInfo\> | 进度更新时触发 |
| OnPluginMetadata | Action\<PluginMetadata\> | 插件元数据上报时触发 |

#### 核心方法

| 方法名 | 返回类型 | 参数 | 说明 |
|--------|----------|------|------|
| Init | void | string folderPath | 设置模块文件夹路径 |
| LoadAllModulesAsync | Task\<LoadResult\> | 无 | 扫描并加载所有模块 |
| UnloadAllModulesAsync | Task | 无 | 卸载所有模块 |
| UnloadModuleAsync | Task\<bool\> | string moduleName | 卸载指定模块 |
| GetModuleStatus | ModuleStatus | string moduleName | 查询模块状态 |

### 2.2 类：ProgressInfo

进度信息，用于外部进度条显示。

| 属性名 | 类型 | 说明 |
|--------|------|------|
| Status | string | 状态词：LoadingLibs, Scanning, ParsingDeps, Initializing, Done, Unloaded |
| Completed | int | 已完成模块数 |
| Total | int | 总模块数 |
| CurrentModule | string | 当前处理的模块名 |
| Percentage | int | 完成百分比 (0-100) |

### 2.3 类：LoadResult

加载结果。

| 属性名 | 类型 | 说明 |
|--------|------|------|
| Success | bool | 是否全部加载成功 |
| Errors | List\<string\> | 错误列表 |

### 2.4 枚举：ModuleStatus

| 值 | 说明 |
|----|------|
| NotFound | 模块不存在 |
| Scanned | 已扫描，待初始化 |
| Initializing | 正在初始化 |
| Running | 正常运行 |
| Error | 初始化失败 |
| Unloaded | 已卸载 |

### 2.5 类：ModuleInfo

模块详细信息。

| 属性名 | 类型 | 说明 |
|--------|------|------|
| ModuleName | string | 模块类名 |
| ModuleType | Type | 模块类型 |
| ModuleInstance | object | 模块实例 |
| AssemblyPath | string | 程序集路径 |
| AssemblyContext | AssemblyLoadContext | 程序集加载上下文 |
| InitMethod | MethodInfo | Init 方法信息 |
| ExitMethod | MethodInfo | Exit 方法信息 |
| GetDependenciesMethod | MethodInfo | GetDependencies 方法信息 |
| Status | ModuleStatus | 当前状态 |

### 2.6 抽象类：ModuleBase

可选的基类，提供默认实现。

| 方法名 | 返回类型 | 说明 |
|--------|----------|------|
| Init | abstract Task | 必须实现 |
| Exit | virtual Task | 可选重写，默认返回 CompletedTask |
| GetDependencies | virtual IEnumerable\<string\> | 可选重写，默认返回空数组 |

---

## 三、依赖注入 API

### 3.1 特性：InjectAttribute

用于标记需要注入的属性或字段。

| 属性 | 类型 | 说明 |
|------|------|------|
| Name | string | 按名称注入时的服务名称 |
| Type | Type | 按类型注入时的服务类型 |

**使用示例：**

```csharp
[Inject("ConfigManager")]
public object Config { get; set; }

[Inject(typeof(ILogger))]
public ILogger Logger;

[Inject]
public NapCatClient Client { get; set; }
```

### 3.2 注册服务方法

| 方法 | 说明 |
|------|------|
| `RegisterService<T>(T service)` | 按类型注册服务 |
| `RegisterService(string name, object service)` | 按名称注册服务 |
| `RegisterServices(Dictionary<Type, object> services)` | 批量注册服务 |

### 3.3 获取服务方法

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetService<T>()` | T | 获取已注册的服务（按类型） |
| `GetService(string name)` | object | 获取已注册的服务（按名称） |
| `GetRegisteredServiceTypes()` | List\<Type\> | 获取所有已注册的服务类型 |
| `GetRegisteredServiceNames()` | List\<string\> | 获取所有已注册的服务名称 |

### 3.4 注入顺序

```
InjectDependencies(moduleInstance)
    │
    ├── 1. 注入元数据回调（属性/字段）
    │
    ├── 2. 按类型注入属性
    │
    ├── 3. 按类型注入字段
    │
    ├── 4. 按名称注入属性（使用 InjectAttribute）
    │
    ├── 5. 按名称注入字段（使用 InjectAttribute）
    │
    └── 6. 调用 SetServices() 方法（如果存在）
```

---

## 四、模块查询 API

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| GetLoadedModuleNames | List\<string\> | 获取已成功加载的模块名称列表 |
| GetLoadedModules | List\<ModuleInfo\> | 获取已成功加载的模块信息列表 |
| GetAllModuleNames | List\<string\> | 获取所有已扫描的模块名称（含失败的） |
| GetAllModules | List\<ModuleInfo\> | 获取所有已扫描的模块信息（含失败的） |
| GetModuleNamesByStatus | List\<string\> | 根据状态获取模块名称列表 |
| GetModuleInfo | ModuleInfo | 获取指定模块的详细信息 |
| IsModuleLoaded | bool | 检查模块是否已加载并运行 |
| GetModuleDependencies | List\<string\> | 获取模块的依赖列表 |
| GetModulesDependentOn | List\<string\> | 获取依赖该模块的其他模块 |
| GetLoadingOrder | List\<string\> | 获取拓扑排序后的加载顺序 |

---

## 五、Library 查询 API

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| GetLoadedLibraries | List\<string\> | 获取所有已加载的依赖库名称列表 |
| GetLoadedLibraryPaths | List\<string\> | 获取所有已加载的依赖库完整路径列表 |
| GetLoadedLibraryCount | int | 获取已加载的依赖库数量 |
| IsLibraryLoaded | bool | 检查指定的依赖库是否已加载 |

---

## 六、插件元数据 API

### 6.1 类：PluginMetadata

插件元数据实体。

| 属性 | 类型 | 说明 |
|------|------|------|
| ModuleName | string | 模块类名 |
| DisplayName | string | 展示名称 |
| Author | string | 作者 |
| Website | string | 网站 |
| Description | string | 描述 |
| IconBase64 | string | Base64 编码的图标数据 |

### 6.2 元数据上报方式

插件必须在 `Init()` 方法中通过回调上报元数据：

```csharp
public Action<string, string, string, string, string, string> SetMetadataCallback { set; }

// 在 Init 中调用
_setMetadata?.Invoke(
    "ModuleName",     // 模块名（类名）
    "显示名称",       // 展示名称
    "作者",           // 作者
    "https://...",    // 网站
    "描述文本",       // 描述
    "Base64图标数据"  // Base64 图标（可选，可为 null）
);
```

### 6.3 事件：OnPluginMetadata

框架端订阅此事件接收元数据：

```csharp
manager.OnPluginMetadata += (metadata) =>
{
    Console.WriteLine($"模块: {metadata.DisplayName}");
    Console.WriteLine($"作者: {metadata.Author}");
    
    if (!string.IsNullOrEmpty(metadata.IconBase64))
    {
        byte[] imageBytes = Convert.FromBase64String(metadata.IconBase64);
        // 显示图标
    }
};
```

### 6.4 图标建议

| 格式 | 推荐尺寸 | Base64 大小 |
|------|----------|-------------|
| PNG | 64x64 | ~1KB |
| PNG | 128x128 | ~3KB |
| PNG | 256x256 | ~8KB |
| JPG | 128x128 | ~2KB |
| SVG | 任意 | 可变 |

---

## 七、完整使用示例

### 7.1 框架端代码

```csharp
using System;
using System.Threading.Tasks;
using ModuleManagerLib;

namespace MyFramework
{
    public class Framework
    {
        private ModuleManager _moduleManager;
        
        public async Task StartAsync()
        {
            _moduleManager = new ModuleManager();
            _moduleManager.Init(@"./Modules");
            
            // 订阅进度事件
            _moduleManager.OnProgressUpdated += (progress) =>
            {
                Console.WriteLine($"[{progress.Status}] {progress.Completed}/{progress.Total} - {progress.Percentage}%");
            };
            
            // 订阅元数据事件
            _moduleManager.OnPluginMetadata += (metadata) =>
            {
                Console.WriteLine($"插件: {metadata.DisplayName}");
                Console.WriteLine($"  作者: {metadata.Author}");
                Console.WriteLine($"  网站: {metadata.Website}");
                Console.WriteLine($"  描述: {metadata.Description}");
                Console.WriteLine($"  图标: {(string.IsNullOrEmpty(metadata.IconBase64) ? "无" : "有")}");
            };
            
            // 注册服务
            _moduleManager.RegisterService<IConfigManager>(configManager);
            _moduleManager.RegisterService<NapCatClient>(napCatClient);
            _moduleManager.RegisterService<ILogger>(logger);
            
            // 加载模块
            LoadResult result = await _moduleManager.LoadAllModulesAsync();
            
            if (result.Success)
            {
                var modules = _moduleManager.GetLoadedModuleNames();
                Console.WriteLine($"已加载模块: {string.Join(", ", modules)}");
                
                var libraries = _moduleManager.GetLoadedLibraries();
                Console.WriteLine($"已加载库: {string.Join(", ", libraries)}");
            }
            else
            {
                foreach (var err in result.Errors)
                {
                    Console.WriteLine($"错误: {err}");
                }
            }
        }
        
        public async Task StopAsync()
        {
            await _moduleManager.UnloadAllModulesAsync();
        }
    }
}
```

### 7.2 插件端代码（完整示例）

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ModuleManagerLib;

public class SamplePlugin : ModuleBase
{
    private Action<string, string, string, string, string, string> _setMetadata;
    
    public Action<string, string, string, string, string, string> SetMetadataCallback
    {
        set => _setMetadata = value;
    }
    
    public IConfigManager ConfigManager { get; set; }
    public ILogger Logger { get; set; }
    
    public override async Task Init()
    {
        string iconBase64 = LoadIcon();
        
        _setMetadata?.Invoke(
            "SamplePlugin",
            "示例插件",
            "开发组",
            "https://github.com/example/sample-plugin",
            "提供示例功能的演示插件",
            iconBase64
        );
        
        Logger?.Info("SamplePlugin 初始化完成");
        await Task.CompletedTask;
    }
    
    private string LoadIcon()
    {
        // 方法1：从文件加载
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "icon.png");
        if (File.Exists(iconPath))
        {
            byte[] bytes = File.ReadAllBytes(iconPath);
            return Convert.ToBase64String(bytes);
        }
        
        // 方法2：返回 null 表示无图标
        return null;
    }
    
    public override IEnumerable<string> GetDependencies()
    {
        return Array.Empty<string>();
    }
    
    public override async Task Exit()
    {
        Logger?.Info("SamplePlugin 已卸载");
        await Task.CompletedTask;
    }
}
```

---

## 八、错误码说明

| 错误信息格式 | 说明 |
|--------------|------|
| `NoInitMethod:{ClassName}` | 模块缺少 Init 方法 |
| `LoadFailed:{DllName}\|{Message}` | DLL 加载失败 |
| `GetDepsFailed:{ModuleName}\|{Message}` | 获取依赖失败 |
| `MissingDependency:{ModuleName}->{DepName}` | 依赖的模块不存在 |
| `ModuleDependencyFailed:{ModuleName}` | 因依赖问题被移除 |
| `CircularDependencyDetected` | 检测到循环依赖 |
| `InitFailed:{ModuleName}\|{Message}` | 模块初始化失败 |
| `InitException:{ModuleName}\|{Message}` | 模块初始化异常 |

---

## 九、注意事项

1. **类名即模块标识**：模块管理器使用类的名称作为模块的唯一标识

2. **依赖方向**：A 依赖 B 意味着 B 必须在 A 之前初始化

3. **异步要求**：Init、Exit 方法必须返回 Task，支持 async/await

4. **程序集加载**：使用 `AssemblyLoadContext` 隔离加载，支持完全卸载

5. **错误恢复**：单个模块失败不会导致整个加载流程崩溃

6. **Library 目录**：`Modules/Library/` 下的 DLL 仅被加载，不会被扫描为模块

7. **依赖注入时机**：依赖注入发生在 `Init()` 方法调用**之前**

8. **元数据上报**：必须在 `Init()` 方法中调用回调上报元数据

9. **注入方式优先级**：SetServices > 特性注入 > 属性/字段注入

10. **线程安全**：模块管理器内部已处理线程安全，可放心在多线程环境使用

11. **完全卸载**：支持 `AssemblyLoadContext.Unload()` + `GC.Collect()` 强制释放

12. **图标 Base64**：可为 null，框架端需处理空值情况

---

## 十、目录结构

```
YourApp/
├── Modules/                          # 模块目录
│   ├── Library/                      # 依赖库目录
│   │   ├── Newtonsoft.Json.dll
│   │   ├── Python.Runtime.dll
│   │   └── Sqlite.dll
│   ├── ModuleA.dll                   # 有效模块
│   ├── ModuleB.dll
│   └── PythonBridgeModule.dll
├── Plugins/                          # 插件资源目录
│   └── icon.png                      # 插件图标
├── YourApp.exe
└── config.json
```

---

## 十一、加载流程图

```
LoadAllModulesAsync()
    │
    ├── 1. LoadLibraries()
    │       └── 加载 Modules/Library/*.dll
    │
    ├── 2. ScanModulesAsync()
    │       ├── 为每个 DLL 创建 AssemblyLoadContext
    │       ├── 扫描包含 Init() 的类
    │       └── 创建 ModuleInfo
    │
    ├── 3. ParseDependenciesAsync()
    │       ├── 调用 GetDependencies()
    │       └── 构建依赖图
    │
    ├── 4. TopologicalSort()
    │       └── 按依赖顺序排序
    │
    └── 5. 对每个模块（按顺序）:
            │
            ├── InjectDependencies()
            │       ├── 注入元数据回调
            │       ├── 注入服务
            │       └── 调用 SetServices()
            │
            ├── 调用 Init()
            │       └── 插件上报元数据（触发 OnPluginMetadata）
            │
            └── 更新状态为 Running
```