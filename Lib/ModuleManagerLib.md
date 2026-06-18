# ModuleManagerLib API 文档

## 命名空间
```
ModuleManagerLib
```

---

## 目录

1. [模块开发要求](#一模块开发要求)
2. [核心类型](#二核心类型)
3. [初始化与配置](#三初始化与配置)
4. [依赖注入 API](#四依赖注入-api)
5. [模块加载 API](#五模块加载-api)
6. [模块卸载 API](#六模块卸载-api)
7. [动态加载/卸载 API](#七动态加载卸载-api)
8. [模块查询 API](#八模块查询-api)
9. [Library API](#九library-api)
10. [API 系统](#十api-系统)
11. [加载控制机制](#十一加载控制机制)
12. [自定义扫描流程](#十二自定义扫描流程)
13. [事件回调](#十三事件回调)
14. [进度反馈](#十四进度反馈)
15. [使用示例](#十五使用示例)
16. [错误码](#十六错误码)

---

## 一、模块开发要求

### 1.1 必需方法

| 方法名 | 返回类型 | 说明 |
|--------|----------|------|
| Init | Task | 模块初始化入口 |

### 1.2 可选方法

| 方法名 | 返回类型 | 说明 |
|--------|----------|------|
| GetDependencies | IEnumerable\<string\> | 返回依赖的插件类名列表 |
| GetLibraryDependencies | IEnumerable\<string\> | 返回依赖的库文件名列表 |
| Exit | Task | 模块卸载时的清理方法 |
| SetServices | void | 依赖注入回调（参数自动注入） |

### 1.3 模块示例

```csharp
public class MyPlugin
{
    private ILogger _logger;
    
    // 依赖注入（属性注入）
    [Inject]
    public ILogger Logger { set => _logger = value; }
    
    // 或通过 SetServices 方法注入
    public void SetServices(ILogger logger)
    {
        _logger = logger;
    }
    
    public async Task Init() 
    { 
        _logger?.Info("初始化完成");
        await Task.CompletedTask;
    }
    
    public IEnumerable<string> GetDependencies() => new[] { "LoggerPlugin" };
    public IEnumerable<string> GetLibraryDependencies() => new[] { "Newtonsoft.Json.dll" };
    public async Task Exit() { }
}
```

---

## 二、核心类型

### 2.1 ModuleDeclaration

模板化声明，由主程序通过注册器提供。

| 属性 | 类型 | 说明 | 必需 |
|------|------|------|------|
| PluginDependencies | List\<string\> | 插件依赖类名列表 | ✓ |
| LibraryDependencies | List\<string\> | 库文件名列表 | ✓ |
| AllowDynamicLoad | bool | 是否允许动态加载/卸载（默认 true） | - |

### 2.2 ModuleInfo

模块公共信息。

| 属性 | 类型 | 说明 |
|------|------|------|
| ModuleName | string | 模块类名 |
| ModuleType | Type | 模块类型 |
| ModuleInstance | object | 模块实例 |
| AssemblyPath | string | 程序集路径 |
| Status | ModuleStatus | 当前状态 |
| Declaration | ModuleDeclaration | 模块声明 |
| IsDynamicLoaded | bool | 是否动态加载 |

### 2.3 ModuleStatus

| 值 | 说明 |
|----|------|
| NotFound | 模块不存在 |
| Scanned | 已扫描 |
| Initializing | 初始化中 |
| Running | 运行中 |
| Error | 错误 |
| Unloaded | 已卸载 |
| Skipped | 被跳过（未加载） |
| Interrupted | 被中断 |

### 2.4 ModuleLoadAction

加载动作枚举，用于 `OnBeforeModuleLoad` 事件返回值。

| 值 | 说明 |
|----|------|
| Continue | 继续加载（默认） |
| Interrupt | 中断加载，执行自定义逻辑 |
| Skip | 跳过此模块，不加载 |

### 2.5 ModuleLoadContext

模块加载上下文，用于加载控制事件。

| 属性 | 类型 | 说明 |
|------|------|------|
| ModuleName | string | 模块名称 |
| ModuleType | Type | 模块类型 |
| ModuleInstance | object | 模块实例 |
| Dependencies | List\<string\> | 插件依赖列表 |
| LibraryDependencies | List\<string\> | 库依赖列表 |
| Completed | int | 当前进度（已完成数） |
| Total | int | 总数 |
| Declaration | ModuleDeclaration | 模块声明 |
| AssemblyPath | string | 程序集路径 |
| CustomData | Dictionary\<string, object\> | 自定义数据 |

### 2.6 InterruptResult

中断处理结果。

| 属性 | 类型 | 说明 |
|------|------|------|
| ContinueOriginalLogic | bool | 是否继续执行原本的加载逻辑（默认 false） |
| MarkAsLoaded | bool | 是否标记模块为已加载（默认 false） |
| Error | string | 错误信息（非空则标记为失败） |
| CustomData | Dictionary\<string, object\> | 自定义数据 |

### 2.7 ScanContext

扫描上下文，用于自定义扫描流程。

| 属性 | 类型 | 说明 |
|------|------|------|
| ModulesFolderPath | string | 模块文件夹路径 |
| LibraryFolderPath | string | 库文件夹路径 |
| FoundDllFiles | List\<string\> | 扫描到的 DLL 文件列表 |
| ScannedModuleNames | List\<string\> | 已扫描模块名称列表 |
| ScannedModules | Dictionary\<string, ScanModuleInfo\> | 自定义扫描结果 |
| CustomData | Dictionary\<string, object\> | 自定义数据 |
| CancelScan | bool | 是否取消扫描 |

### 2.8 ScanModuleInfo

扫描到的模块信息。

| 属性 | 类型 | 说明 |
|------|------|------|
| ModuleName | string | 模块名称 |
| ModuleType | Type | 模块类型 |
| ModuleInstance | object | 模块实例 |
| AssemblyPath | string | 程序集路径 |
| Assembly | Assembly | 程序集 |
| AssemblyContext | AssemblyLoadContext | ALC 上下文 |
| InitMethod | MethodInfo | Init 方法 |
| ExitMethod | MethodInfo | Exit 方法 |
| GetDependenciesMethod | MethodInfo | GetDependencies 方法 |
| GetLibraryDependenciesMethod | MethodInfo | GetLibraryDependencies 方法 |
| Dependencies | List\<string\> | 插件依赖 |
| LibraryDependencies | List\<string\> | 库依赖 |
| ShouldSkip | bool | 是否跳过此模块 |
| SkipReason | string | 跳过原因 |
| CustomData | Dictionary\<string, object\> | 自定义数据 |

### 2.9 LoadResult

| 属性 | 类型 | 说明 |
|------|------|------|
| Success | bool | 是否成功 |
| Errors | List\<string\> | 错误列表 |
| SkippedModules | List\<string\> | 被跳过的模块列表 |
| InterruptedModules | List\<string\> | 被中断的模块列表 |
| LoadedModules | List\<string\> | 成功加载的模块列表 |

### 2.10 ProgressInfo

| 属性 | 类型 | 说明 |
|------|------|------|
| Status | string | LoadingLibs, Scanning, ParsingDeps, Initializing, Done, Unloaded |
| Completed | int | 已完成数 |
| Total | int | 总数 |
| CurrentModule | string | 当前模块名 |
| Percentage | int | 百分比 |

---

## 三、初始化与配置

| 方法 | 说明 |
|------|------|
| `Init(string folderPath)` | 设置模块文件夹路径（自动创建 Modules 和 Modules/Library） |
| `RegisterDeclarationProvider(Func<Type, object, Task<ModuleDeclaration>>)` | 注册模板化声明提供者 |

---

## 四、依赖注入 API

### 4.1 注册服务

| 方法 | 说明 |
|------|------|
| `RegisterService<T>(T)` | 按类型注册服务 |
| `RegisterService(string, object)` | 按名称注册服务 |
| `RegisterServices(Dictionary<Type, object>)` | 批量注册 |

### 4.2 获取服务

| 方法 | 说明 |
|------|------|
| `GetService<T>()` | 获取服务（按类型） |
| `GetService(string)` | 获取服务（按名称） |
| `GetRegisteredServiceTypes()` | 获取所有已注册类型 |
| `GetRegisteredServiceNames()` | 获取所有已注册名称 |

### 4.3 注入方式

- 属性注入（按类型）
- 字段注入（按类型）
- `[Inject("name")]` 特性（按名称）
- `[Inject(typeof(Type))]` 特性（按类型）
- `SetServices(object[] args)` 方法（严格检查参数可用性）

---

## 五、模块加载 API

| 方法 | 说明 |
|------|------|
| `LoadAllModulesAsync()` | 扫描并加载所有模块 |

**加载流程：**
1. 加载 `Modules/Library/*.dll`（独立 AssemblyLoadContext）
2. 触发 `OnScanning` 事件（自定义扫描）
3. 扫描 `Modules/*.dll`（流加载，排除 Library 目录）
4. 通过声明提供者获取模块依赖
5. 构建依赖图，拓扑排序
6. 对每个模块触发 `OnBeforeModuleLoad`（加载控制）
7. 按顺序初始化模块

---

## 六、模块卸载 API

| 方法 | 说明 |
|------|------|
| `UnloadAllModulesAsync()` | 卸载所有模块 |
| `UnloadModuleAsync(string moduleName)` | 卸载指定模块 |

**卸载特性：**
- 调用模块 `Exit()` 方法
- 释放 `IDisposable` 资源
- 清理 API 注册
- 卸载 `AssemblyLoadContext`
- 强制 GC 回收文件句柄

---

## 七、动态加载/卸载 API

### 7.1 动态加载

| 方法 | 说明 |
|------|------|
| `DynamicLoadModuleAsync(string dllPath)` | 动态加载模块 DLL |

**特性：**
- 从流加载，加载后释放字节数组
- 自动构建子依赖树
- **检测全局循环依赖**（包含已有模块）
- 仅初始化新模块及其缺失依赖
- 跳过已初始化的模块
- 支持 `OnBeforeModuleLoad` 加载控制

### 7.2 动态卸载

| 方法 | 说明 |
|------|------|
| `DynamicUnloadModuleAsync(string moduleName)` | 动态卸载模块及所有依赖它的模块 |

**特性：**
- 递归收集依赖目标模块的所有模块
- 按拓扑逆序卸载（先卸载依赖者）
- **检查所有待卸载模块的 `AllowDynamicLoad` 标志**（任一为 false 则整体失败）
- 卸载后重建依赖图

---

## 八、模块查询 API

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetModuleStatus(string)` | ModuleStatus | 查询状态 |
| `GetLoadedModuleNames()` | List\<string\> | 已加载模块名称 |
| `GetLoadedModules()` | List\<ModuleInfo\> | 已加载模块信息 |
| `GetAllModuleNames()` | List\<string\> | 所有模块名称 |
| `GetAllModules()` | List\<ModuleInfo\> | 所有模块信息 |
| `GetModuleNamesByStatus(ModuleStatus)` | List\<string\> | 按状态筛选 |
| `GetModuleInfo(string)` | ModuleInfo | 模块详情 |
| `IsModuleLoaded(string)` | bool | 是否已加载 |
| `GetModuleDependencies(string)` | List\<string\> | 插件依赖列表 |
| `GetModuleLibraryDependencies(string)` | List\<string\> | 库依赖列表 |
| `GetModulesDependentOn(string)` | List\<string\> | 依赖该模块的模块 |
| `GetLibrariesDependentOn(string)` | List\<string\> | 依赖该库的模块 |
| `GetLoadingOrder()` | List\<string\> | 拓扑排序顺序 |

---

## 九、Library API

| 方法 | 返回类型 | 说明 |
|------|----------|------|
| `GetLoadedLibraries()` | List\<string\> | 已加载库名称 |
| `GetLoadedLibraryPaths()` | List\<string\> | 已加载库路径 |
| `GetLoadedLibraryCount()` | int | 已加载库数量 |
| `IsLibraryLoaded(string)` | bool | 检查是否已加载 |
| `UnloadLibrary(string)` | bool | 按名称卸载（检查依赖） |
| `UnloadLibraryByPath(string)` | bool | 按路径卸载 |
| `UnloadAllLibraries()` | void | 卸载所有库 |
| `ReloadLibrary(string)` | bool | 重新加载库 |

**注意：** 卸载库时会检查是否有模块依赖，有则返回 false。

---

## 十、API 系统

模块间 API 调用和事件通信系统。

### 10.1 API 注册与调用

| 方法 | 说明 |
|------|------|
| `RegisterApi(string pluginName, string apiName, Delegate handler)` | 注册 API |
| `UnregisterApi(string pluginName, string apiName)` | 注销 API |
| `UnregisterAllApis(string pluginName)` | 注销模块所有 API |
| `CallApi<T>(string pluginName, string apiName, params object[] args)` | 调用 API（泛型返回） |
| `CallApi(string pluginName, string apiName, params object[] args)` | 调用 API（object 返回） |
| `HasApi(string pluginName, string apiName)` | 检查 API 是否存在 |
| `GetRegisteredApiNames(string pluginName)` | 获取模块所有 API 名称 |
| `GetPluginsWithApis()` | 获取所有注册了 API 的插件 |

### 10.2 事件注册与发布

| 方法 | 说明 |
|------|------|
| `RegisterEvent(string pluginName, string eventName)` | 注册事件 |
| `UnregisterEvent(string pluginName, string eventName)` | 注销事件 |
| `UnregisterAllEvents(string pluginName)` | 注销模块所有事件 |
| `SubscribeEvent<T>(string pluginName, string eventName, Action<string, T>)` | 订阅事件（类型安全） |
| `SubscribeEvent(string pluginName, string eventName, Action<string, object>)` | 订阅事件（object） |
| `UnsubscribeEvent<T>(string pluginName, string eventName, Action<string, T>)` | 取消订阅 |
| `PublishEvent<T>(string pluginName, string eventName, T data)` | 发布事件（类型安全） |
| `PublishEvent(string pluginName, string eventName, object data)` | 发布事件（object） |
| `GetRegisteredEventNames(string pluginName)` | 获取模块所有事件名称 |
| `GetPluginsWithEvents()` | 获取所有注册了事件的插件 |
| `UnregisterAll(string pluginName)` | 注销模块所有 API 和事件 |

**示例：**
```csharp
// 模块 A 注册事件
manager.RegisterEvent("ModuleA", "DataUpdated");
manager.PublishEvent<string>("ModuleA", "DataUpdated", "Hello World");

// 模块 B 订阅事件
manager.SubscribeEvent<string>("ModuleA", "DataUpdated", (sender, data) => {
    Console.WriteLine($"收到来自 {sender} 的数据: {data}");
});
```

---

## 十一、加载控制机制

### 11.1 概述

加载控制机制允许宿主在模块加载前进行干预，实现细粒度的加载控制。

### 11.2 控制流程

```
加载开始
    │
    ▼
OnBeforeModuleLoad（返回 ModuleLoadAction）
    │
    ├── Continue ──────────────► 正常加载
    │
    ├── Interrupt ─────────────► OnModuleInterrupted
    │                                   │
    │                                   ├── ContinueOriginalLogic = true ──► 正常加载
    │                                   ├── MarkAsLoaded = true ──────────► 标记已加载，跳过 Init
    │                                   ├── Error 非空 ──────────────────► 标记失败
    │                                   └── 其他 ────────────────────────► 标记 Interrupted
    │
    └── Skip ───────────────────► 标记 Skipped，跳过加载
```

### 11.3 事件说明

| 事件 | 参数 | 说明 |
|------|------|------|
| `OnBeforeModuleLoad` | `ModuleLoadContext` → `Task<ModuleLoadAction>` | 加载前决策，返回 Continue/Interrupt/Skip |
| `OnModuleInterrupted` | `ModuleLoadContext` → `Task<InterruptResult>` | 中断处理，返回 InterruptResult |
| `OnModuleLoadComplete` | `ModuleLoadContext, ModuleStatus` | 加载完成回调 |

### 11.4 InterruptResult 行为

| 属性值 | 行为 |
|--------|------|
| `ContinueOriginalLogic = true` | 执行中断逻辑后，继续执行原本的 Init 加载 |
| `MarkAsLoaded = true` | 标记模块为已加载状态（Running），跳过 Init 执行 |
| `Error = "xxx"` | 标记模块为 Error 状态，加载失败 |
| 两者均为 false | 标记模块为 Interrupted 状态，不加载 |

---

## 十二、自定义扫描流程

### 12.1 概述

`OnScanning` 事件允许宿主在扫描阶段自定义模块发现逻辑。

### 12.2 使用方式

```csharp
manager.OnScanning += async (context) =>
{
    // 自定义扫描逻辑
    foreach (var dll in context.FoundDllFiles)
    {
        // 解析 DLL，构建 ScanModuleInfo
        var info = new ScanModuleInfo
        {
            ModuleName = "CustomModule",
            ModuleType = typeof(MyPlugin),
            ModuleInstance = new MyPlugin(),
            AssemblyPath = dll,
            // ... 设置其他属性
        };
        context.ScannedModules["CustomModule"] = info;
    }
    
    // 或跳过某些模块
    // context.ScannedModules["ModuleA"].ShouldSkip = true;
    
    // 或取消整个扫描
    // context.CancelScan = true;
};
```

### 12.3 行为说明

1. **如果 `ScannedModules` 有内容**：使用自定义扫描结果，跳过默认扫描
2. **如果 `CancelScan = true`**：停止扫描，返回空结果
3. **如果 `ScannedModules` 为空且未取消**：执行默认扫描逻辑

---

## 十三、事件回调

| 事件 | 参数 | 触发时机 |
|------|------|----------|
| `ModuleLoaded` | `ModuleInfo` | 模块加载完成后 |
| `ModuleUnloaded` | `ModuleInfo` | 模块卸载完成后 |
| `ModuleFailed` | `ModuleInfo, Exception` | 模块加载或卸载失败时 |
| `LibraryLoaded` | `string` | 库加载完成后 |
| `LibraryUnloaded` | `string` | 库卸载完成后 |
| `DependencyResolutionFailed` | `string, List<string>` | 依赖解析失败时 |
| `OnProgressUpdated` | `ProgressInfo` | 进度更新时 |
| `OnError` | `string, Exception` | 错误发生时 |
| `OnScanning` | `ScanContext` | 扫描阶段（自定义扫描） |
| `OnBeforeModuleLoad` | `ModuleLoadContext` | 模块加载前（加载控制） |
| `OnModuleInterrupted` | `ModuleLoadContext` | 模块加载中断时 |
| `OnModuleLoadComplete` | `ModuleLoadContext, ModuleStatus` | 模块加载完成时 |

---

## 十四、进度反馈

### 14.1 状态词

| Status | 阶段 |
|--------|------|
| LoadingLibs | 加载依赖库 |
| Scanning | 扫描模块 |
| ParsingDeps | 解析依赖 |
| Initializing | 初始化模块 |
| Done | 完成 |
| Unloaded | 已卸载 |

---

## 十五、使用示例

### 15.1 框架端初始化

```csharp
var manager = new ModuleManager();
manager.Init(@"./Modules");

// 注册事件
manager.ModuleLoaded += (info) => Console.WriteLine($"加载: {info.ModuleName}");
manager.ModuleFailed += (info, ex) => Console.WriteLine($"失败: {info.ModuleName} - {ex.Message}");
manager.OnProgressUpdated += (p) => Console.WriteLine($"[{p.Status}] {p.Percentage}%");
manager.OnError += (msg, ex) => Console.WriteLine($"错误: {msg}");

// 注册模板化声明
manager.RegisterDeclarationProvider(async (type, instance) =>
{
    return new ModuleDeclaration
    {
        PluginDependencies = new List<string> { "LoggerPlugin" },
        LibraryDependencies = new List<string> { "Newtonsoft.Json.dll" },
        AllowDynamicLoad = true
    };
});

// 注册服务
manager.RegisterService<ILogger>(logger);

// 加载模块
var result = await manager.LoadAllModulesAsync();
```

### 15.2 加载控制示例

```csharp
// 在加载前检查模块
manager.OnBeforeModuleLoad += async (context) =>
{
    if (context.ModuleName == "UnstablePlugin")
    {
        return ModuleLoadAction.Skip;  // 跳过不稳定的插件
    }
    
    if (context.ModuleName == "SpecialPlugin")
    {
        return ModuleLoadAction.Interrupt;  // 需要特殊处理
    }
    
    return ModuleLoadAction.Continue;
};

// 中断处理
manager.OnModuleInterrupted += async (context) =>
{
    // 执行自定义初始化
    var specialService = manager.GetService<ISpecialService>();
    await specialService.InitializeSpecialModule(context.ModuleInstance);
    
    return new InterruptResult
    {
        ContinueOriginalLogic = false,  // 不执行原本的 Init
        MarkAsLoaded = true              // 标记为已加载
    };
};
```

### 15.3 自定义扫描

```csharp
manager.OnScanning += async (context) =>
{
    foreach (var dll in context.FoundDllFiles)
    {
        // 只加载名称包含 "Core" 的模块
        if (dll.Contains("Core"))
        {
            // 手动解析并添加到 ScannedModules
            var asm = Assembly.LoadFrom(dll);
            var type = asm.GetType("CorePlugin");
            context.ScannedModules["CorePlugin"] = new ScanModuleInfo
            {
                ModuleName = "CorePlugin",
                ModuleType = type,
                ModuleInstance = Activator.CreateInstance(type),
                AssemblyPath = dll
                // ... 设置其他必要属性
            };
        }
    }
};
```

### 15.4 动态加载

```csharp
var result = await manager.DynamicLoadModuleAsync("./Plugins/NewModule.dll");
if (result.Success)
    Console.WriteLine($"动态加载成功: {string.Join(",", result.LoadedModules)}");
```

### 15.5 动态卸载

```csharp
bool ok = await manager.DynamicUnloadModuleAsync("TargetPlugin");
if (ok)
    Console.WriteLine("模块及其依赖者已安全卸载");
else
    Console.WriteLine("卸载失败（可能依赖者不允许动态卸载）");
```

### 15.6 模块间 API 调用

```csharp
// 模块 A 注册 API
manager.RegisterApi("ModuleA", "GetData", (string key) => {
    return $"Data for {key}";
});

// 模块 B 调用 API
var data = manager.CallApi<string>("ModuleA", "GetData", "user123");
```

### 15.7 模块间事件通信

```csharp
// 模块 A 注册并发布事件
manager.RegisterEvent("ModuleA", "UserLoggedIn");
manager.PublishEvent<string>("ModuleA", "UserLoggedIn", "user123");

// 模块 B 订阅事件
manager.SubscribeEvent<string>("ModuleA", "UserLoggedIn", (sender, userId) => {
    Console.WriteLine($"用户 {userId} 已登录（来自 {sender}）");
});
```

### 15.8 插件端代码

```csharp
public class MyPlugin
{
    private ILogger _logger;
    
    [Inject]
    public ILogger Logger { set => _logger = value; }
    
    public async Task Init()
    {
        _logger?.Info("初始化完成");
        await Task.CompletedTask;
    }
    
    public IEnumerable<string> GetDependencies() => new[] { "LoggerPlugin" };
    public IEnumerable<string> GetLibraryDependencies() => new[] { "Newtonsoft.Json.dll" };
    public async Task Exit() { }
    
    // 注册 API
    public void SetServices(ModuleManager manager)
    {
        manager.RegisterApi("MyPlugin", "GetVersion", (Func<string>)(() => "1.0.0"));
    }
}
```

---

## 十六、错误码

| 错误信息 | 说明 |
|----------|------|
| `NoInitMethod:{ClassName}` | 缺少 Init 方法 |
| `LoadFailed:{DllName}|{Message}` | DLL 加载失败 |
| `FileNotFound:{Path}` | 文件不存在 |
| `NoModuleFound:{DllName}` | 未找到有效模块 |
| `InvalidInit:{ClassName}` | Init 方法无效 |
| `DeclarationFailed:{ModuleName}|{Message}` | 获取声明失败 |
| `MissingPluginDependency:{ModuleName}->{Dep}` | 插件依赖不存在 |
| `MissingLibraryDependency:{ModuleName}->{Lib}` | 库依赖不存在 |
| `CircularDependencyDetected` | 循环依赖 |
| `CircularDependencyInDynamicLoad` | 动态加载时循环依赖 |
| `InitFailed:{ModuleName}|{Message}` | 初始化失败 |
| `InterruptFailed:{ModuleName}|{Message}` | 中断处理失败 |

---

## 十七、目录结构

```
YourApp/
├── Modules/
│   ├── Library/
│   │   └── Newtonsoft.Json.dll
│   ├── LoggerPlugin.dll
│   └── CorePlugin.dll
├── Plugins/
│   └── NewPlugin.dll
└── YourApp.exe
```