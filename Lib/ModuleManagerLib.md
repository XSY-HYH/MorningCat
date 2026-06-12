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
10. [事件回调](#十事件回调)
11. [进度反馈](#十一进度反馈)
12. [插件间依赖与通信](#十二插件间依赖与通信)
13. [使用示例](#十三使用示例)
14. [错误码](#十四错误码)
15. [目录结构](#十五目录结构)

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
| SetServices | void | 依赖注入回调 |

### 1.3 模块示例

```csharp
public class MyPlugin
{
    public async Task Init() { }
    public IEnumerable<string> GetDependencies() => new[] { "Logger" };
    public IEnumerable<string> GetLibraryDependencies() => new[] { "Newtonsoft.Json.dll" };
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

### 2.4 ProgressInfo

| 属性 | 类型 | 说明 |
|------|------|------|
| Status | string | LoadingLibs, Scanning, ParsingDeps, Initializing, Done, Unloaded |
| Completed | int | 已完成数 |
| Total | int | 总数 |
| CurrentModule | string | 当前模块名 |
| Percentage | int | 百分比 |

### 2.5 LoadResult

| 属性 | 类型 | 说明 |
|------|------|------|
| Success | bool | 是否成功 |
| Errors | List\<string\> | 错误列表 |

---

## 三、初始化与配置

| 方法 | 说明 |
|------|------|
| `Init(string folderPath)` | 设置模块文件夹路径（自动创建 Modules 和 Modules/Library） |
| `RegisterDeclarationProvider(Func<Type, object, Task<ModuleDeclaration>>)` | 注册模板化声明提供者 |

---

## 四、依赖注入 API

| 方法 | 说明 |
|------|------|
| `RegisterService<T>(T)` | 按类型注册服务 |
| `RegisterService(string, object)` | 按名称注册服务 |
| `RegisterServices(Dictionary<Type, object>)` | 批量注册 |
| `GetService<T>()` | 获取服务（按类型） |
| `GetService(string)` | 获取服务（按名称） |
| `GetRegisteredServiceTypes()` | 获取所有已注册类型 |
| `GetRegisteredServiceNames()` | 获取所有已注册名称 |

**注入方式：**
- 属性注入（按类型）
- 字段注入（按类型）
- `[Inject("name")]` 特性（按名称）
- `[Inject(typeof(Type))]` 特性（按类型）
- `SetServices(object[] args)` 方法

---

## 五、模块加载 API

| 方法 | 说明 |
|------|------|
| `LoadAllModulesAsync()` | 扫描并加载所有模块 |

**加载流程：**
1. 加载 `Modules/Library/*.dll`（独立 AssemblyLoadContext）
2. 扫描 `Modules/*.dll`（流加载，排除 Library 目录）
3. 通过声明提供者获取模块依赖
4. 构建依赖图，拓扑排序
5. 按顺序初始化模块

---

## 六、模块卸载 API

| 方法 | 说明 |
|------|------|
| `UnloadAllModulesAsync()` | 卸载所有模块 |
| `UnloadModuleAsync(string moduleName)` | 卸载指定模块 |

**卸载特性：**
- 调用模块 `Exit()` 方法
- 释放 `IDisposable` 资源
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
- 仅初始化新模块及其缺失依赖
- 跳过已初始化的模块

### 7.2 动态卸载

| 方法 | 说明 |
|------|------|
| `DynamicUnloadModuleAsync(string moduleName)` | 动态卸载模块及所有依赖它的模块 |

**特性：**
- 递归收集依赖目标模块的所有模块
- 按拓扑逆序卸载（先卸载依赖者）
- 检查 `AllowDynamicLoad` 标志
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

## 十、事件回调

| 事件 | 参数 | 触发时机 |
|------|------|----------|
| `ModuleLoaded` | `ModuleInfo` | 模块加载完成后 |
| `ModuleUnloaded` | `ModuleInfo` | 模块卸载完成后 |
| `ModuleFailed` | `ModuleInfo, Exception` | 模块加载或卸载失败时 |
| `LibraryLoaded` | `string` | 库加载完成后 |
| `LibraryUnloaded` | `string` | 库卸载完成后 |
| `DependencyResolutionFailed` | `string, List<string>` | 依赖解析失败时 |
| `OnProgressUpdated` | `ProgressInfo` | 进度更新时 |

---

## 十一、进度反馈

### 状态词

| Status | 阶段 |
|--------|------|
| LoadingLibs | 加载依赖库 |
| Scanning | 扫描模块 |
| ParsingDeps | 解析依赖 |
| Initializing | 初始化模块 |
| Done | 完成 |
| Unloaded | 已卸载 |

---

## 十二、插件间依赖与通信

### 12.1 依赖声明

插件通过 `GetDependencies()` 声明对其他插件的依赖。模块管理器会自动解析依赖图，确保被依赖的插件先加载。

```csharp
public override IEnumerable<string> GetDependencies()
{
    return new[] { "LoggerPlugin", "DatabasePlugin" };
}
```

### 12.2 依赖解析规则

| 规则 | 说明 |
|------|------|
| 拓扑排序 | 按依赖关系排序，被依赖的插件先加载 |
| 循环检测 | 检测到循环依赖时，所有相关模块加载失败 |
| 缺失依赖 | 依赖的插件不存在时，当前模块加载失败 |
| 级联卸载 | 卸载某插件时，依赖它的插件也会被卸载 |

### 12.3 插件间通信方式

#### 方式一：通过服务注册与注入（推荐）

被依赖的插件或框架端通过 `RegisterService<T>()` 注册服务，依赖方通过 `[Inject]` 特性或 `SetServices()` 方法获取服务实例。

```csharp
// 框架端注册服务
manager.RegisterService<ILogger>(logger);

// 插件端通过注入获取
[Inject]
public ILogger Logger { get; set; }

// 或通过 SetServices 回调
public void SetServices(ILogger logger, IConfigManager configManager)
{
    _logger = logger;
    _configManager = configManager;
}
```

#### 方式二：通过 ModuleInfo 获取插件实例

通过模块管理器的查询 API 获取目标插件的 `ModuleInstance`，使用反射调用方法。

```csharp
var targetInfo = moduleManager.GetModuleInfo("TargetPlugin");
if (targetInfo != null)
{
    var method = targetInfo.ModuleInstance.GetType().GetMethod("SomePublicMethod");
    var result = method?.Invoke(targetInfo.ModuleInstance, new object[] { "arg" });
}
```

#### 方式三：通过命令系统间接调用

通过 `PluginCommandAPI` 执行目标插件注册的命令，完全解耦，无需直接引用目标插件类型。

```csharp
// 枚举可用命令
var commands = pluginCommandAPI.EnumerateCommands(CommandPermission.BotOwner);

// 执行命令
var result = await pluginCommandAPI.ExecuteAsBotOwner(message, "somecommand arg1 arg2");
```

### 12.4 跨 AssemblyLoadContext 注意事项

插件在独立的 `AssemblyLoadContext` 中加载，因此：

- **类型兼容性**：同一类型在不同 ALC 中可能被视为不同类型，框架已通过类型全名匹配自动处理
- **ALC 优先解析**：插件 ALC 解析依赖时，优先从默认 ALC（主程序）解析程序集，确保插件与主程序共享同一份核心类型定义，避免跨 ALC 类型不兼容
- **接口共享**：如需强类型交互，建议将共享接口定义放在 `Modules/Library/` 目录的 DLL 中
- **反射安全**：通过反射调用方法不受 ALC 隔离限制，是最通用的跨插件通信方式
- **避免直接引用平台特定类型**：插件应通过 `MessageDistributionCore.SendMessageAsync` 发送消息，而非直接使用 `OneBotClient` 等平台特定 API，以避免跨 ALC 类型不兼容和平台耦合

---

## 十三、使用示例

### 13.1 框架端初始化

```csharp
var manager = new ModuleManager();
manager.Init(@"./Modules");

// 注册事件
manager.ModuleLoaded += (info) => Console.WriteLine($"加载: {info.ModuleName}");
manager.ModuleFailed += (info, ex) => Console.WriteLine($"失败: {info.ModuleName} - {ex.Message}");
manager.OnProgressUpdated += (p) => Console.WriteLine($"[{p.Status}] {p.Percentage}%");

// 注册模板化声明
manager.RegisterDeclarationProvider(async (type, instance) =>
{
    return new ModuleDeclaration
    {
        PluginDependencies = new List<string> { "Logger" },
        LibraryDependencies = new List<string> { "Newtonsoft.Json.dll" },
        AllowDynamicLoad = true
    };
});

// 注册服务
manager.RegisterService<ILogger>(logger);

// 加载模块
var result = await manager.LoadAllModulesAsync();
```

### 13.2 动态加载

```csharp
var result = await manager.DynamicLoadModuleAsync("./Plugins/NewModule.dll");
if (result.Success)
    Console.WriteLine("动态加载成功");
```

### 13.3 动态卸载

```csharp
bool ok = await manager.DynamicUnloadModuleAsync("TargetPlugin");
if (ok)
    Console.WriteLine("模块及其依赖者已安全卸载");
```

### 13.4 查询模块

```csharp
var modules = manager.GetLoadedModuleNames();
foreach (var name in modules)
{
    var deps = manager.GetModuleDependencies(name);
    var libs = manager.GetModuleLibraryDependencies(name);
    Console.WriteLine($"{name} -> 插件依赖:{string.Join(",", deps)} 库依赖:{string.Join(",", libs)}");
}
```

### 13.5 插件端代码

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
}
```

---

## 十四、错误码

| 错误信息 | 说明 |
|----------|------|
| `NoInitMethod:{ClassName}` | 缺少 Init 方法 |
| `LoadFailed:{DllName}\|{Message}` | DLL 加载失败 |
| `FileNotFound:{Path}` | 文件不存在 |
| `NoModuleFound:{DllName}` | 未找到有效模块 |
| `InvalidInit:{ClassName}` | Init 方法无效 |
| `DeclarationFailed:{ModuleName}\|{Message}` | 获取声明失败 |
| `MissingPluginDependency:{ModuleName}->{Dep}` | 插件依赖不存在 |
| `MissingLibraryDependency:{ModuleName}->{Lib}` | 库依赖不存在 |
| `CircularDependencyDetected` | 循环依赖 |
| `CircularDependencyInDynamicLoad` | 动态加载时循环依赖 |
| `InitFailed:{ModuleName}\|{Message}` | 初始化失败 |

---

## 十五、目录结构

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