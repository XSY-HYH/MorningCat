# MCT 完整任务流

从 `MorningCatLaunch.exe` 被激活，到运行期间所有行为的完整流程。
每个 if 都是一个分流节点。

## 总览

```
用户双击/运行 MorningCatLaunch.exe
        │
        ▼
┌─────────────────────────┐
│  ML  - MorningCatLaunch │  exe入口，引导层
└───────────┬─────────────┘
            │
            ▼
┌─────────────────────────────┐
│ MLC - MorningCatLaunchCore  │  更新层，自更新+核心更新+WS监控
└───────────┬─────────────────┘
            │
            ▼
┌─────────────────────────┐
│ MCT - MorningCat Core   │  核心层，Bot主体
└───────────┬─────────────┘
            │
            ▼
    运行中：处理消息/重连/更新/重启/关闭
```

---

## 约定说明

### 函数返回值含义

| 返回值 | 含义 | 来源 |
|--------|------|------|
| 0 | 正常退出 | MLC.Run()、MCT.Main() |
| 1 | 启动失败（配置缺失/核心dll找不到/连接失败） | MLC.Run()、MCT.Main() |
| 100 | 重启请求（ML/MLC/MCT任一层触发重启） | MLC.Run() |

### 并发安全机制

| 变量 | 同步方式 | 说明 |
|------|---------|------|
| `_restartRequested` | `volatile` 读写 | MLC WS监控线程与MCT主线程共享 |
| `_isRunning` | 普通读写 | 仅MCT主线程修改，其他线程只读 |
| `_isReconnecting` | 普通读写 | 重连定时器回调中检查和修改 |
| `_isAuthenticated` | 普通读写 | 事件回调中修改，定时器中检查 |
| `_wsDisconnectCount` | `Interlocked.Increment` | 事件回调中原子递增 |
| `_serviceContainer` | `lock` | DI服务注册和查找时加锁 |
| `_namedServices` | `lock` | 命名服务注册和查找时加锁 |
| `_moduleInfos` | `lock` | 模块列表增删时加锁 |
| `module.InitLock` | `SemaphoreSlim(1,1)` | 防止模块并发初始化 |

### 术语

- **模块(Module)**：ModuleBase子类的统称，包括内置模块和外部插件
- **插件(Plugin)**：外部加载的模块，位于 Modules/ 目录，以独立dll形式存在
- **内置模块**：编译在MCT内部的模块（HelpModule、PluginModule等）
- **外部模块**：同"插件"

---

## 阶段一：ML (MorningCatLaunch)

```
MorningCatLaunch.exe 启动
│
├─ 读取 start.args 文件
│   ├─ 文件存在且非空 → 用文件内容覆盖命令行参数
│   └─ 文件不存在/为空 → 使用原始命令行参数
│
├─ EnsureLaunchCore()
│   ├─ MorningCatLaunchCore.dll 存在 → 继续
│   └─ MorningCatLaunchCore.dll 不存在
│       ├─ 从嵌入资源提取 mlc.zip → 解压到根目录
│       └─ 提取失败 → 红色报错，等待5秒，退出(code=1)
│
└─ while(true) 循环
    │
    └─ RunLaunchCore(args)
        │
        ├─ 在独立 ALC "LaunchCoreContext" 中加载 MLC
        ├─ 反射调用 LaunchCore.Run(string[])
        ├─ 获取 Run() 函数返回值
        │
        ├─ 返回值 == 100 (重启请求)?
        │   ├─ 是 → 卸载 ALC，强制GC，等待500ms
        │   │       → continue → 重新循环（重新加载MLC）
        │   └─ 否 → 进程以该返回值退出
```

---

## 阶段二：MLC (MorningCatLaunchCore)

```
LaunchCore.Run(args) 被调用
│
├─ 解析命令行参数
│   ├─ --enable-ws-monitor → _enableWsMonitor = true
│   └─ --ws-heartbeat-hours N → _wsHeartbeatIntervalHours = N
│
├─ SelfUpdateCheck() — MLC自更新
│   ├─ 从服务器获取 MorningCat/LaunchCore 目录列表
│   │   ├─ 获取失败 → return (shouldContinue=true, serverReachable=false)
│   │   └─ 获取成功 → 继续比较
│   │
│   ├─ 遍历远程文件，与本地SHA256对比
│   │   ├─ 本地文件不存在 → 加入差异列表
│   │   ├─ SHA256不匹配 → 加入差异列表
│   │   └─ SHA256匹配 → 跳过
│   │
│   ├─ 差异列表为空?
│   │   ├─ 是 → return (shouldContinue=true, serverReachable=true)
│   │   └─ 否 → 下载差异文件覆盖本地
│   │       ├─ 下载成功 → return (shouldContinue=false, serverReachable=true)
│   │       │   → ML收到返回值100 → 重启 → 重新加载MLC
│   │       └─ 下载失败 → return (shouldContinue=true, serverReachable=true)
│   │
│   └─ 异常 → return (shouldContinue=true, serverReachable=false)
│
├─ shouldContinue == false? → return 100 → ML重启循环
│
├─ CheckAndUpdateCore() — MCT核心更新
│   ├─ 从服务器获取 MorningCat/Core 目录列表
│   │   ├─ 获取失败 → return null
│   │   └─ 获取成功 → 继续
│   │
│   ├─ 从目录列表中找最新版本（按版本号降序排列取第一个）
│   │   ├─ 无有效版本 → return null
│   │   └─ 找到最新版本 → 获取该版本文件列表
│   │       ├─ 获取失败 → return null
│   │       └─ 获取成功 → 继续
│   │
│   ├─ 本地 MorningCatCore 目录不存在?
│   │   ├─ 是 → 创建目录，下载所有文件
│   │   │   ├─ 下载失败 → return (Success=false)
│   │   │   └─ 下载成功 → return (Success=true, RepoDllPath=...)
│   │   └─ 否 → 继续
│   │
│   ├─ 计算差异文件(SHA256对比)
│   ├─ 清理本地多余文件（远程不存在的文件）
│   ├─ 差异文件数 == 0?
│   │   ├─ 是 → return (Success=true, "All files are up to date")
│   │   └─ 否 → 差异文件数 > 3?
│   │       ├─ 是 → 批量下载
│   │       │   ├─ 任一失败 → return (Success=false)
│   │       │   └─ 全部成功 → return 更新结果
│   │       └─ 否 → 逐个下载
│   │           └─ 下载完成 → return 更新结果
│   │
│   └─ 异常 → return null
│
├─ 确定 MorningCat.dll 路径
│   ├─ coreUpdateResult.RepoDllPath 非空? → 使用该路径
│   └─ coreUpdateResult.RepoDllPath 为空?
│       ├─ 在根目录查找 MorningCat.dll
│       │   ├─ 找到 → 使用该路径
│       │   └─ 未找到 → 红色报错，等待5秒，退出(code=1)
│
├─ WS监控（条件启动）
│   ├─ _enableWsMonitor == true && updateServerReachable == true?
│   │   ├─ 是 → 启动后台线程 WatchForUpdates
│   │   └─ 否 → 不启动
│   │       注：服务器不可达时 serverReachable=false，WS监控不会启动，
│   │       属于降级行为——MCT仍可正常运行，但无法接收远程更新推送。
│   │       下次重启时若服务器恢复，WS监控将正常启动。
│   └─
│
├─ LoadAndRunCore(repoDllPath, args) — 加载并运行MCT
│   │
│   ├─ 创建 ALC "MorningCatContext"
│   ├─ 设置程序集解析器
│   │   ├─ 先尝试默认ALC加载
│   │   └─ 失败则从本地dll文件流加载
│   ├─ 从流加载 MorningCat.dll 主程序集
│   ├─ 加载目录下所有其他 dll 到 ALC
│   │   ├─ 先尝试默认ALC加载（成功则跳过）
│   │   └─ 失败则从流加载到 MorningCatContext
│   │
│   ├─ 查找 MorningCat.Program 类型
│   │   ├─ 未找到 → 红色报错，等待5秒，return false
│   │   └─ 找到 → 继续
│   │
│   ├─ 为所有程序集设置 NativeLibrary DllImportResolver
│   │   └─ 搜索路径: runtimes/{rid}/native → runtimes/{os}/native → 根目录
│   │       ├─ Windows → .dll
│   │       ├─ macOS → .dylib
│   │       └─ Linux → .so, .so.0
│   │
│   ├─ 查找 MainWithRestartCallback 方法
│   │   ├─ 方法存在 && 参数数量 == 4?
│   │   │   └─ Invoke(args, restartCallback, shutdownCallback, updateCallback)
│   │   │       → MCT同步运行直到退出
│   │   ├─ 方法存在 && 参数数量 == 2?
│   │   │   └─ Invoke(args, restartCallback)
│   │   └─ 方法不存在 或 参数不匹配?
│   │       └─ 调用 Main(string[])
│   │
│   ├─ MCT退出后
│   │   ├─ 清理dll字节缓存
│   │   ├─ 卸载 MorningCatContext ALC
│   │   ├─ 强制GC
│   │   ├─ needRestart || _restartRequested? → return true
│   │   └─ 否则 → return false
│   │
│   └─ 回调定义
│       ├─ restartCallback → _restartRequested=true, needRestart=true
│       ├─ shutdownCallback → _restartRequested=true, needRestart=true
│       └─ updateCallback → _restartRequested=true, needRestart=true
│
├─ needRestart? → return 100 → ML重启循环
└─ 否则 → return 0 → 进程退出
```

---

## 阶段二-A：MLC WS监控线程（运行期间持续运行）

```
WatchForUpdates(ct) — 后台线程
│
└─ while(!ct.IsCancellationRequested && !_restartRequested)
    │
    ├─ 创建 ClientWebSocket
    ├─ 连接 wss://服务器/ws?path=MorningCat/Core
    │   ├─ 连接失败?
    │   │   └─ 是 → 等待30秒 → continue 重试
    │   └─ 连接成功 → 进入消息循环
    │
    ├─ 消息循环 while(ws.State == Open && !ct.IsCancellationRequested && !_restartRequested)
    │   │
    │   ├─ 心跳检查: 距上次心跳 >= _wsHeartbeatIntervalHours 小时?
    │   │   ├─ 是 → 发送 {"type":"ping"}，更新 lastHeartbeat
    │   │   └─ 否 → 跳过
    │   │
    │   ├─ 接收消息（超时1分钟）
    │   │   ├─ 未完成（超时） → continue
    │   │   ├─ MessageType == Close → break 断开
    │   │   └─ MessageType == Text → 解析JSON
    │   │       ├─ type == "update" || type == "file_changed"?
    │   │       │   ├─ 是 → _restartRequested=true → break
    │   │       │   │   → MCT退出后MLC返回100 → ML重启 → 重新更新
    │   │       └─ 否 → 忽略
    │   │
    │   └─ 异常 → break 断开
    │
    ├─ 断开后: !ct.IsCancellationRequested && !_restartRequested?
    │   ├─ 是 → 等待30秒 → continue 重连
    │   └─ 否 → 退出线程
    │
    └─ 异常 → 等待30秒 → continue 重连
```

---

## 阶段三：MCT (MorningCat Core) — Program.Main

```
Program.MainWithRestartCallback() 被调用
│
├─ 包装 updateCallback：调用MLC回调 + 触发 _exitEvent
├─ 调用 Main(args).GetAwaiter().GetResult()
│
└─ Program.Main(args)
    │
    ├─ 初始化 _exitEvent = new TaskCompletionSource<bool>
    ├─ 注册全局异常处理
    │   ├─ AppDomain.UnhandledException → Log.Error
    │   └─ TaskScheduler.UnobservedTaskException → Log.Error
    │
    ├─ 注册程序集解析器 ResolveAssembly
    ├─ VirtualTerminal.Enable()
    │
    ├─ 解析启动参数
    │   ├─ --debug / -d → isDebugMode=true
    │   └─ --testmode / -t → isTestMode=true
    │
    ├─ 设置日志级别
    │   ├─ isDebugMode? → Console=Debug, File=Debug
    │   └─ 非调试 → Console=Info, File=Debug
    │
    ├─ isTestMode? → 禁用插件签名验证
    │
    ├─ 打印 ANSI 启动画面
    │
    ├─ new MorningCatBot(exitCallback, isTestMode)
    │   ├─ new ConfigManager()
    │   │   ├─ config.yml 存在?
    │   │   │   ├─ 是 → 反序列化加载
    │   │   │   │   ├─ 配置完整 → 直接使用
    │   │   │   │   └─ 配置不完整 → 自动补全缺失键并保存
    │   │   └─ 否 → 创建默认配置并保存，IsNewConfig=true
    │   ├─ new PluginConfigManager()
    │   ├─ new CommandRegistry(client, configManager)
    │   ├─ new PluginCommandAPI(commandRegistry)
    │   ├─ new PluginDatabaseAPI(config.Database)
    │   ├─ new PluginSignatureVerifier(configManager, testMode)
    │   └─ 注册 OneBotClient 4个事件
    │       ├─ OnMessage → OnMessageReceived
    │       ├─ OnLifecycle → OnLifecycleEventReceived
    │       ├─ OnHeartbeat → OnHeartbeatReceived
    │       └─ OnConnectionStateChanged → OnConnectionStateChanged
    │
    ├─ bot.IsNewConfig?
    │   ├─ 是 → 提示修改配置，等待5秒，return 1
    │   └─ 否 → 继续
    │
    ├─ await bot.StartAsync()    ← 见阶段三-A
    ├─ await bot.StartWebUIAsync() ← 见阶段三-B
    ├─ bot.StartGui()            ← 见阶段三-C
    │
    ├─ 注册 Ctrl+C 处理
    │   └─ e.Cancel=true → _exitEvent.TrySetResult(true)
    │
    ├─ await _exitEvent.Task → 阻塞等待退出信号
    │
    ├─ await bot.StopAsync()     ← 见退出流程
    └─ return 0
```

---

## 阶段三-A：MorningCatBot.StartAsync()

```
StartAsync()
│
├─ _isRunning? → 是 → 直接返回
│
├─ await FetchPublicKeyAsync() — 拉取插件签名公钥
│   ├─ 从服务器获取公钥
│   │   ├─ 获取成功?
│   │   │   ├─ 是 → 公钥与本地不同? → 更新并保存到配置
│   │   │   └─ 否 → 跳过
│   │   └─ 获取失败 → 使用本地缓存公钥
│   └─
│
├─ await InitializeModuleManagerAsync()
│   │
│   ├─ 读取配置获取模块目录路径
│   ├─ 模块目录不存在? → 创建
│   │
│   ├─ LoadCachedMetadata() — 加载 .plugin_metadata.json 缓存
│   │   ├─ 文件存在? → 反序列化，补全 _pluginMetadata 和 _assemblyNameToModuleName
│   │   └─ 文件不存在 → 跳过
│   │
│   ├─ ProcessDisabledPlugins() — 处理 .dll.disabled 标记
│   │   └─ 遍历 *.dll.disabled 文件
│   │       ├─ 文件大小 == 0（禁用标记）?
│   │       │   ├─ 是 → 对应 .dll 存在?
│   │       │   │   ├─ 是 → 删除标记文件，重命名dll为.disabled
│   │       │   │   └─ 否 → 删除空标记文件
│   │       │   └─ 否 → 跳过（已是禁用状态）
│   │
│   ├─ _moduleManager.Init(modulesDirectory)
│   │
│   ├─ VerifyPluginSignatures() — 插件签名验证
│   │   └─ 遍历 Modules/*.dll（排除 Library/ 子目录）
│   │       ├─ 签名验证通过 → 不处理
│   │       └─ 签名验证失败
│   │           ├─ 记录到 _signatureFailedModules
│   │           └─ 重命名 .dll → .dll.disabled
│   │               ├─ 成功 → Log.Warning
│   │               └─ 失败 → Log.Error
│   │
│   ├─ await LoadBuiltinModulesAsync() — 见阶段三-A-1
│   │
│   └─ await LoadExternalModulesAndReportStatusAsync() — 见阶段三-A-2
│
├─ await ConnectToOneBotAsync() — 见阶段三-A-3
│
└─ _isRunning = true
```

### 阶段三-A-1：LoadBuiltinModulesAsync()

```
LoadBuiltinModulesAsync()
│
├─ 注册DI服务（按类型 + 按名称）
│   类型注册: PluginConfigManager, IPluginConfigManager, MessageDistributionCore,
│             CommandRegistry, ModuleManager, PluginApiService, ConfigManager,
│             PluginCommandAPI, PluginDatabaseAPI, MorningCatBot
│   名称注册: "ConfigManager", "MDC", "CommandRegistry", "ModuleManager",
│             "PluginApiService", "PluginCommandAPI", "PluginDatabaseAPI", "MorningCatBot"
│
├─ HelpModule.Init()
├─ PluginModule.Init()
├─ SystemModule.Init()
├─ SetModule.Init()
└─ MessageRelayModule.Init()
```

### 阶段三-A-2：LoadExternalModulesAndReportStatusAsync()

```
LoadExternalModulesAndReportStatusAsync()
│
├─ SubscribeModuleManagerEvents()
│   ├─ OnProgressUpdated → Log.Debug 进度信息
│   │   ├─ Status == "Initializing" && CurrentModule非空?
│   │   │   └─ 是 → _pluginConfigManager.SetCurrentModule(moduleName)
│   │   └─ Status == "Done" || Status == "Unloaded"?
│   │       └─ 是 → _pluginConfigManager.ClearCurrentModule()
│   ├─ ModuleLoaded → 记录assembly映射，更新_pluginMetadata
│   ├─ ModuleUnloaded → 移除_pluginMetadata
│   └─ ModuleFailed → Log.Warning
│
├─ RegisterDeclarationProvider() — 注册声明提供者
│   └─ 对每个模块类型:
│       ├─ 反射调用 GetDependencies() → pluginDeps
│       ├─ 反射调用 GetLibraryDependencies() → libDeps
│       ├─ 检查 PluginMetadataAttribute
│       └─ 返回 ModuleDeclaration
│
├─ await _moduleManager.LoadAllModulesAsync()
│   │
│   ├─ [LoadingLibs] 加载 Library/ 目录下所有 dll
│   │   └─ 每个 dll → 独立 ALC 加载
│   │       ├─ 成功 → LibraryLoaded事件
│   │       └─ 失败 → 静默跳过
│   │
│   ├─ [Scanning] 扫描 Modules/*.dll
│   │   └─ 每个 dll → 独立 ALC + 反射查找 ModuleBase 子类
│   │       ├─ 找到 Init() 方法且返回 Task?
│   │       │   ├─ 是 → Activator.CreateInstance，加入模块列表
│   │       │   └─ 否 → 报错 NoInitMethod，卸载ALC
│   │       └─ 加载异常 → 报错 LoadFailed
│   │
│   ├─ 扫描结果有错误? → return (Success=false)
│   │
│   ├─ [ParsingDeps] 解析依赖关系
│   │   └─ 对每个模块:
│   │       ├─ _declarationProvider 存在? → 调用获取声明
│   │       │   ├─ 调用失败 → 标记为FailedModules
│   │       │   └─ 成功 → 继续
│   │       ├─ declaration == null? → 反射调用 GetDependencies/GetLibraryDependencies
│   │       │   ├─ 调用失败 → 标记为FailedModules
│   │       │   └─ 成功 → 构建 ModuleDeclaration
│   │       └─ 检查库依赖
│   │           ├─ 依赖的库不在 _loadedLibraries 中?
│   │           │   ├─ 是 → MissingLibraryDependency，标记为FailedModules
│   │           │   └─ 否 → 通过
│   │
│   ├─ 清理FailedModules: 卸载模块，从列表移除
│   │
│   ├─ BuildDependencyGraph() — 构建依赖图和反向依赖图
│   │
│   ├─ TopologicalSort() — 拓扑排序
│   │   ├─ 检测到循环依赖? → return null
│   │   └─ 无循环 → 按依赖顺序排列
│   │
│   ├─ 排序结果为null? → return (Success=false, "CircularDependencyDetected")
│   │
│   ├─ [Initializing] 按顺序初始化每个模块
│   │   └─ 对每个模块:
│   │       ├─ module.Status != Scanned? → 跳过
│   │       ├─ InjectDependencies() — 见附录F
│   │       ├─ 调用 Init() 方法
│   │       │   ├─ 成功 → Status=Running，ModuleLoaded事件
│   │       │   └─ 失败 → Status=Error，ModuleFailed事件
│   │       └─ InitLock 保证并发安全
│   │
│   ├─ [Done] 清理Error状态模块（卸载+移除）
│   │
│   └─ return LoadResult
│
├─ 生成状态报告
│   ├─ loadedModules.Count > 0? → 显示已加载模块名
│   └─ 否 → 显示"无插件"
│   ├─ failedModules.Count > 0? → 显示失败模块名
│   └─
│
├─ 报告错误详情
│   └─ 包含 "InitException:" 或 "InitFailed:"? → Log.Warning "插件加载错误"
│       └─ 否则 → Log.Warning "模块加载错误"
│
├─ 更新 _assemblyNameToModuleName 映射
├─ SaveMetadataCache() — 保存 .plugin_metadata.json
│
└─ LogRegisteredCommands()
    ├─ commands.Count == 0? → Log.Debug "当前没有注册任何命令"
    └─ 否则 → 按模块分组显示所有命令
```

### 阶段三-A-3：ConnectToOneBotAsync()

```
ConnectToOneBotAsync()
│
├─ _isAuthenticated = false
├─ _authCompletionSource = new TaskCompletionSource<bool>
│
├─ _client.ConnectSync(url, token, timeout=10)
│   ├─ 连接失败 → 抛出异常 → StartAsync失败 → MCT退出
│   └─ 连接成功 → 等待认证
│
├─ await Task.WhenAny(authTask, Task.Delay(15000))
│   ├─ authTask先完成（收到Lifecycle事件）
│   │   ├─ _isAuthenticated = await authTask
│   │   │   ├─ true（connect/enable）→ Log.Info "连接成功"
│   │   │   └─ false（disconnect/disable）→ 抛出异常 "登录验证失败"
│   │   └─
│   └─ Delay先完成（15秒超时）
│       └─ 抛出 TimeoutException "登录验证超时"
│
└─ 异常 → Log.Error → throw → MCT退出
```

**注意**：启动时连接失败会导致MCT直接退出。但运行中断开不会退出，而是进入重连流程（见运行时行为）。

---

## 阶段三-B：StartWebUIAsync()

```
StartWebUIAsync()
│
├─ webUIConfig.Enabled?
│   ├─ 否 → Log "WebUI已禁用"，返回
│   └─ 是 → 继续
│
├─ _webUIManager == null?
│   ├─ 是 → 创建 WebUIManager 实例
│   │   ├─ SetStartTime
│   │   ├─ SetRestartCallback(RestartAsync)
│   │   ├─ SetShutdownCallback(RequestExit)
│   │   ├─ SetUpdateCallback → Program.UpdateCallback
│   │   ├─ SetMessageRelayModule
│   │   ├─ SetPluginDatabaseAPI
│   │   └─ SetOneBotClient
│   └─ 否 → 复用已有实例
│
└─ await _webUIManager.StartAsync()
    ├─ _server已在运行? → Log.Warning "WebUI已在运行中"，返回
    ├─ 创建 WebUIServer(username, password)
    ├─ 注册所有Provider
    ├─ 设置 UpdateCallback → Program.UpdateCallback?.Invoke()
    ├─ 设置 OnCredentialsChanged → 保存到config.yml
    ├─ 读取 PluginStoreUrl
    ├─ await _server.StartAsync(port, listenAddress)
    │   ├─ 成功 → Log.Info "WebUI已启动"
    │   └─ 失败 → Log.Error "WebUI启动失败"
    └─ 输出默认账户信息
```

---

## 阶段三-C：StartGui()

```
StartGui()
│
├─ config.EnableGui?
│   ├─ 否 → Log "GUI已禁用"，返回
│   └─ 是 → 继续
│
├─ new GuiManager()
├─ Initialize()
├─ SetWebuiPort
├─ SetRestartCallback
├─ SetShutdownCallback
├─ Show()
│
└─ 异常 → Log警告，_guiManager = null
```

---

## 运行时行为

MCT启动完成后进入 `await _exitEvent.Task` 阻塞。以下所有行为在运行期间并发发生。

### R1. WebSocket连接状态监控

```
OneBotClient WebSocket 连接状态变化
│
├─ OnConnectionStateChanged 触发
│   └─ e.NewState == Disconnected && _isAuthenticated?
│       ├─ 是 → 检测到断开
│       │   ├─ _wsDisconnectCount++
│       │   ├─ _isAuthenticated = false
│       │   ├─ WebUI.ClearBotInfo()
│       │   ├─ WebUI.SetConnectionStatus(false)
│       │   └─ StartReconnectTimer() → 进入重连流程 R2
│       └─ 否 → 忽略（非认证后断开）
│
├─ OnLifecycleEventReceived 触发
│   ├─ SubType == "connect" || "enable"?
│   │   ├─ 是 → 认证成功
│   │   │   ├─ _authCompletionSource.TrySetResult(true)
│   │   │   ├─ _isAuthenticated = true
│   │   │   ├─ _isReconnecting = false
│   │   │   ├─ StopReconnectTimer()
│   │   │   ├─ WebUI.SetConnectionStatus(true)
│   │   │   └─ 延迟2秒后:
│   │   │       ├─ UpdateBotInfoAsync()
│   │   │       │   ├─ GetLoginInfo成功? → WebUI.SetBotInfo
│   │   │       │   └─ 失败 → Log.Warning
│   │   │       └─ RefreshContactListAsync()
│   │   │           ├─ 获取群列表+好友列表 → Log.Debug 数量
│   │   │           └─ 失败 → Log.Warning
│   │   └─
│   └─ SubType == "disconnect" || "disable"?
│       ├─ 是 → 连接丢失
│       │   ├─ _authCompletionSource.TrySetResult(false)
│       │   ├─ _isAuthenticated = false
│       │   ├─ WebUI.ClearBotInfo()
│       │   ├─ WebUI.SetConnectionStatus(false)
│       │   └─ StartReconnectTimer() → 进入重连流程 R2
│       └─ 否 → 忽略
│
├─ OnHeartbeatReceived 触发
│   └─ Log.Debug 心跳间隔信息
```

### R2. 重连流程

```
StartReconnectTimer()
│
├─ _isReconnecting || !_isRunning? → 已在重连或已停止 → 返回
│
├─ 读取 ReconnectDelay 配置（默认5秒）
├─ _isReconnecting = true
└─ 创建定时器，每 ReconnectDelay 秒触发 TryReconnectAsync

TryReconnectAsync() — 每次定时器触发
│
├─ !_isRunning || _isAuthenticated?
│   ├─ 是 → StopReconnectTimer()，返回（已连接或已停止）
│   └─ 否 → 继续重连
│
├─ 取消旧 OneBotClient 所有事件订阅（4个事件 -=）
├─ await _client.CloseAsync() — 关闭旧WebSocket连接，释放资源
├─ 旧 _client 引用丢弃，等待GC回收（无显式Dispose）
├─ 创建新 OneBotClient
├─ 重新注册4个事件
├─ 更新 CommandRegistry 的 Client 引用
├─ UpdateBuiltinModulesClient() — 更新所有内置模块的Client
│   └─ 遍历已加载外部模块
│       ├─ 有 MDC 属性且类型为 MessageDistributionCore 且可写?
│       │   ├─ 是 → SetValue 更新
│       │   └─ 否 → 跳过
├─ 更新 WebUI 的 OneBotClient
│
├─ _client.ConnectSync(url, token, timeout=10)
│   ├─ 连接失败 → Log.Warning "重连失败"，等待下次定时器
│   └─ 连接成功 → 等待认证
│       ├─ await Task.WhenAny(authTask, Task.Delay(15000))
│       │   ├─ authTask先完成?
│       │   │   ├─ success == true → Log.Info "重连成功"，StopReconnectTimer
│       │   │   └─ success == false → Log.Warning "重连失败"，等待下次
│       │   └─ 超时 → Log.Warning "重连失败"，等待下次
│       └─
│
└─ 异常 → Log.Error "重连异常"，等待下次定时器
```

**关键区别**：启动时连接失败 → MCT退出；运行中断开 → 自动重连，不退出。

### R3. 消息处理

```
OnMessageReceived 触发
│
├─ 记录来源日志
├─ 记录详细调试信息（群名/昵称/文本）
│
└─ Task.Run → HandleMessageAsync(message)
    │
    ├─ PlainText 为空? → 返回
    │
    ├─ 用户在 BlockedUsers? → 返回
    ├─ 群在 BlockedGroups? → 返回
    │
    ├─ 清理消息文本
    │   ├─ CleanMessageText → 去除CQ码等
    │   └─ RemoveAtSegments → 去除@机器人段
    │
    ├─ #Mct 状态查询?（且 config.EnableMctStatus）
    │   ├─ 是 → HandleMctStatusQuery
    │   │   ├─ 获取版本号
    │   │   ├─ 计算运行时间（天/时/分/秒格式化）
    │   │   ├─ 统计 ws断开次数、异常数、警告数
    │   │   ├─ 构建回复消息（引用回复）
    │   │   ├─ MessageType == "group"? → SendGroupMsgAsync
    │   │   └─ 否 → SendPrivateMsgAsync
    │   └─ 否 → 继续
    │
    ├─ CommandRegistry.ExecuteCommandAsync(message, text)
    │   │
    │   ├─ 清理文本，按空格分割
    │   ├─ 提取命令名（去/前缀，转小写）
    │   │
    │   ├─ 查找命令 → 未找到? → return false
    │   │
    │   ├─ RequireAt 检查
    │   │   ├─ 命令需要@ + 未@ + 非私聊? → return false
    │   │   └─ 通过 → 继续
    │   │
    │   ├─ RequireSlash 检查
    │   │   ├─ 命令需要/ + 无/前缀? → return false
    │   │   └─ 通过 → 继续
    │   │
    │   ├─ 权限检查 CheckPermission
    │   │   ├─ Everyone → 通过
    │   │   ├─ BotOwner → 仅 ownerQQ
    │   │   ├─ Owner → 群主 或 ownerQQ
    │   │   └─ GroupAdmin → 管理员/群主 或 ownerQQ
    │   │   └─ 不通过 → 回复"你无权使用此命令" → return true
    │   │
    │   ├─ 作用域检查 CheckScope
    │   │   ├─ All → 通过
    │   │   ├─ PrivateOnly → 仅私聊
    │   │   └─ GroupOnly → 仅群聊
    │   │   └─ 不通过 → 回复提示 → return true
    │   │
    │   ├─ 参数验证 ValidateParameters
    │   │   ├─ 缺少必需参数 → 回复错误 → return false
    │   │   ├─ 类型不匹配 → 回复错误 → return false
    │   │   └─ 通过 → 继续
    │   │
    │   ├─ 解析参数 ParseParameters
    │   │   ├─ At类型参数 → 提取@用户列表
    │   │   ├─ Reply类型参数 → 提取回复消息ID
    │   │   └─ 普通参数 → 按位置映射
    │   │
    │   ├─ 构建 CommandContext
    │   └─ 调用 command.Handler(context)
    │       ├─ 成功 → return true
    │       └─ 异常
    │           ├─ 插件命令 → Log.Error "插件命令执行失败"
    │           └─ 内置命令 → Log.Error "执行命令失败"
    │           └─ return false
    │
    ├─ handled == true? → Log.Debug 处理信息
    │
    └─ handled == false → RaiseUnhandledMessage
        └─ OnUnhandledMessage事件 → 外部插件可订阅处理
```

### R4. 消息发送

```
MDC.SendAsync(originalMessage, Action<IMessageBuilder> configure)
│
├─ 获取平台适配器 GetAdapter(target.Platform)
│   ├─ 适配器为null → return SendMessageResult.Fail("无可用适配器")
│   └─ 平台未启用 → return SendMessageResult.Fail("平台未启用")
│
├─ adapter.CreateMessageBuilder() → 创建平台对应的 IMessageBuilder
├─ 执行 configure(builder) → 用户通过链式API构建消息
│   标准API: Text, At, AtAll, Reply, Image, ImageBase64
│   平台特殊API: Face(OneBot), Embed(Discord), Markdown(钉钉)等
│
├─ builder.Build() → 生成 MessageBody
│
├─ adapter.SendAsync(target, body)
│   ├─ OneBot: MessageBody → ToCQCode() → SendGroupMsgAsync/SendPrivateMsgAsync
│   ├─ Discord: MessageBody → DSharpPlusMessageBuilder → Channel.SendMessageAsync
│   ├─ 钉钉: MessageBody → HTTP API/Webhook 发送
│   └─ 其他平台 → 按各自协议转换
│
├─ 异常 → Log.Error
└─ 返回 SendMessageResult (Ok/Fail)

--- 旧版兼容API ---

SendMessageAsync(originalMessage, responseText)
│
├─ await _sendSemaphore.WaitAsync(5秒)
│   ├─ 超时 → Log.Warning "获取发送信号量超时"，返回
│   └─ 获取成功 → 继续
│
├─ MessageType == "private"?
│   ├─ 是 → SendPrivateMsgAsync
│   │   ├─ 成功 → 完成
│   │   └─ 失败 → Log.Warning
│   ├─ MessageType == "group"?
│   │   ├─ 是 → SendGroupMsgAsync
│   │   │   ├─ 成功 → 完成
│   │   │   └─ 失败 → Log.Warning
│   │   └─
│   └─ 其他 → Log.Warning "未知的消息类型"
│
├─ 异常 → Log.Error
└─ finally → _sendSemaphore.Release()
```

### R5. WebUI请求处理（运行期间持续）

```
WebUIServer 接收HTTP请求
│
├─ WebSocket连接请求
│   ├─ 认证成功 → 推送日志/消息/状态更新
│   └─ 认证失败 → 断开
│
├─ REST API 请求
│   ├─ /api/system → 返回系统信息（CPU/内存/运行时间）
│   ├─ /api/bot → 返回Bot信息（QQ号/昵称/连接状态）
│   ├─ /api/plugins → 返回插件列表及状态
│   ├─ /api/logs → 返回最近日志
│   ├─ /api/config → 读取/修改配置
│   │   ├─ GET → 返回当前配置
│   │   └─ POST → 更新配置 → OnCredentialsChanged → 保存到config.yml
│   ├─ /api/database → 返回数据库信息
│   ├─ /api/messages → 返回最近消息
│   ├─ /api/message/send → 发送消息
│   │   └─ 调用 OneBotClient.SendGroupMsgAsync/SendPrivateMsgAsync
│   ├─ /api/restart → 触发重启
│   │   └─ _restartCallback() → RestartAsync()
│   ├─ /api/shutdown → 触发关闭
│   │   └─ _shutdownCallback() → RequestExit()
│   └─ /api/update → 触发更新
│       └─ Program.UpdateCallback() → MLC updateCallback + _exitEvent
│
└─ 插件商店请求
    └─ 代理到 PluginStoreUrl
```

### R6. GUI事件处理（运行期间持续）

```
GuiManager 窗口事件
│
├─ 关闭按钮 → _shutdownCallback → RequestExit → _exitEvent
├─ 重启按钮 → _restartCallback → RestartAsync
├─ 打开WebUI → 打开浏览器 http://localhost:port
└─ 系统托盘
    ├─ 双击 → 显示窗口
    └─ 右键菜单 → 重启/关闭
```

### R7. MLC WS监控（运行期间持续）

见阶段二-A。当服务器推送 `update` 或 `file_changed` 消息时：
- `_restartRequested = true`
- MCT退出后MLC返回100 → ML重启 → 重新更新

---

## 退出流程

```
退出信号触发（以下任一）
├─ Ctrl+C → _exitEvent.TrySetResult(true)
├─ GUI关闭按钮 → RequestExit() → _exitCallback → _exitEvent
├─ WebUI /api/shutdown → _shutdownCallback → RequestExit → _exitEvent
├─ WebUI /api/update → Program.UpdateCallback()
│   ├─ MLC updateCallback() → _restartRequested=true, needRestart=true
│   └─ _exitEvent.TrySetResult(true)
├─ WebUI /api/restart → _restartCallback → RestartAsync()
│   ├─ StopAsync() → 重新初始化 → StartAsync()
│   └─ 不触发退出
└─ MLC WS监控检测到更新 → _restartRequested=true
    └─ MCT自然退出后MLC检测到重启请求
    │
    ▼
_exitEvent.Task 完成（RestartAsync除外）
│
├─ bot.StopAsync()
│   ├─ !_isRunning? → 返回
│   ├─ StopReconnectTimer() → 停止重连定时器
│   ├─ await StopWebUIAsync() → 停止WebUI服务器
│   ├─ StopGui() → 关闭GUI窗口
│   ├─ await _moduleManager.UnloadAllModulesAsync()
│   │   └─ 遍历所有模块
│   │       ├─ 调用 Exit() 方法
│   │       ├─ 卸载 ALC
│   │       └─ ModuleUnloaded 事件
│   ├─ 取消4个OneBotClient事件订阅
│   ├─ await _client.CloseAsync()
│   ├─ _authCompletionSource.TrySetCanceled()
│   └─ _isRunning = false
│
├─ Main() return 0（正常退出，非错误）
├─ MainWithRestartCallback() 返回
├─ LoadAndRunCore() 返回 needRestart
│
├─ needRestart=true? → MLC Run() return 100
│   └─ ML → 重启循环 → 重新加载MLC → 重新加载MCT
│
└─ needRestart=false → MLC Run() return 0
    └─ ML → 进程退出
```

---

## 附录：插件 API 调用流程

插件通过 DI 注入获得以下 API 对象，每个 API 的调用行为如下。

---

### A. PluginConfigManager — 插件配置管理

插件通过 `ConfigManager` 属性注入获得。

#### GetConfigAsync\<T\>(pluginName, configName, defaultValue)

```
GetConfigAsync<T>(pluginName, configName, defaultValue)
│
├─ 计算配置文件路径: Config/{pluginName}-{configName}.yml
├─ 注册配置到 _registeredConfigs
├─ 记录模块名映射 (_currentModuleName → pluginName)
│
├─ 文件不存在?
│   ├─ 是 + defaultValue 非空
│   │   ├─ 用 defaultValue 生成带注释的 YAML
│   │   ├─ 写入文件
│   │   └─ return defaultValue
│   ├─ 是 + defaultValue 为空
│   │   └─ return new T()
│   └─ 否 → 继续
│
├─ 读取文件并反序列化
│   ├─ 成功 → return 反序列化结果
│   └─ 失败(损坏)
│       ├─ defaultValue 非空 → 用默认值覆盖文件 → return defaultValue
│       └─ defaultValue 为空 → return new T()
```

#### SetConfigAsync\<T\>(pluginName, configName, config)

```
SetConfigAsync<T>(pluginName, configName, config)
│
├─ 注册配置到 _registeredConfigs
├─ 序列化 config 为 YAML（带注释头）
└─ 写入 Config/{pluginName}-{configName}.yml
```

#### GetValueAsync\<T\>(pluginName, configName, keyPath, defaultValue)

```
GetValueAsync<T>(pluginName, configName, keyPath, defaultValue)
│
├─ 配置文件不存在?
│   ├─ 是 → 创建默认配置，设置 keyPath = defaultValue → return defaultValue
│   └─ 否 → 继续
│
├─ 读取整个配置为 Dictionary<string, object>
├─ 按 keyPath（用.分隔）逐层查找
│   ├─ 路径存在 → 返回值（JsonElement 反序列化 / Convert.ChangeType）
│   └─ 路径不存在
│       ├─ 自动补全：SetNestedValue 设置默认值
│       ├─ 写回文件
│       └─ return defaultValue
```

#### SetValueAsync\<T\>(pluginName, configName, keyPath, value)

```
SetValueAsync<T>(pluginName, configName, keyPath, value)
│
├─ 读取整个配置为 Dictionary<string, object>
├─ 按 keyPath 逐层定位到父字典
├─ 设置目标键值
└─ 调用 SetConfigAsync 写回文件
```

#### ConfigExistsAsync / DeleteConfigAsync

```
ConfigExistsAsync → File.Exists 检查
DeleteConfigAsync → File.Delete 删除
```

---

### B. PluginCommandAPI — 命令注册与执行

插件通过 `CommandRegistry` 属性注入获得，内部委托给 `CommandRegistry`。

#### RegisterCommand(commandName, description, helpText, parameters, handler, moduleName, ...)

```
RegisterCommand(...)
│
├─ commandName 为空? → return false
├─ 去除前导 /，转小写
├─ 命令已存在? → return false
├─ 创建 CommandInfo 对象
├─ 验证参数名（ValidateParameterNames）
├─ 加入 _commands 字典
└─ return true
```

#### UnregisterCommand(commandName)

```
UnregisterCommand(commandName)
│
├─ 去除前导 /，转小写
├─ _commands.Remove(commandName)
│   ├─ 成功 → return true
│   └─ 不存在 → return false
```

#### UnregisterModuleCommands(moduleName)

```
UnregisterModuleCommands(moduleName)
│
└─ 找到所有 ModuleName 匹配的命令 → 批量移除
```

#### EnumerateCommands(permission)

```
EnumerateCommands(permission)
│
├─ 获取所有已注册命令
├─ 过滤: cmd.Permission <= permission
└─ 转换为 CommandInfoEntry 列表返回
```

#### ExecuteAsNormal / ExecuteAsGroupAdmin / ExecuteAsBotOwner / ExecuteWithPermission

```
ExecuteAs*(message, commandLine 或 commandName+args)
│
└─ 委托给 CommandRegistry.ExecuteCommandAsPlugin(permissionLevel, ...)
    │
    ├─ commandName 为空? → return (Success=false)
    ├─ 去除前导 /，转小写
    ├─ 查找命令
    │   ├─ 不存在 → return (Success=false, "命令不存在")
    │   └─ 存在 → 继续
    │
    ├─ 权限检查: permissionLevel < command.Permission?
    │   └─ 是 → return (Success=false, "权限不足")
    │
    ├─ 作用域检查: CheckScope(message, command.Scope)
    │   ├─ All → 通过
    │   ├─ PrivateOnly → 仅私聊通过
    │   └─ GroupOnly → 仅群聊通过
    │   └─ 不通过 → return (Success=false, "作用域不匹配")
    │
    ├─ 解析参数 → ParseParameters
    ├─ 构建 CommandContext
    ├─ 调用 command.Handler(context)
    │   ├─ 成功 → return (Success=true)
    │   └─ 异常 → return (Success=false, ex.Message)
```

---

### C. PluginDatabaseAPI — 数据库操作

插件通过 `PluginDatabaseAPI` 属性注入获得。

#### GetDatabase(id, pluginClassName)

```
GetDatabase(id, pluginClassName)
│
├─ 计算 key = "{id}-{pluginClassName}"
├─ _databases 缓存命中? → return 缓存实例
│
├─ 缓存未命中，创建新实例
│   ├─ 配置 type == "sql"?
│   │   └─ 是 → new SqlDatabase(connectionString, id, pluginClassName)
│   └─ 否（默认 sqlite）
│       └─ new SqliteDatabase(baseDirectory, id, pluginClassName)
│           ├─ 创建 Database/ 目录（如不存在）
│           ├─ 数据库路径: Database/{id}-{pluginClassName}.db
│           └─ 连接字符串: Data Source={path}
│
├─ 存入 _databases 缓存
└─ return IPluginDatabase 实例
```

#### IPluginDatabase 方法（以 SQLite 为例）

```
ExecuteNonQuery(sql, params)
│
├─ 创建 SqliteConnection
├─ Open
├─ 创建 Command，绑定参数
└─ ExecuteNonQuery → return 影响行数

ExecuteScalar(sql, params)
│
├─ 同上流程
└─ ExecuteScalar → return 单个值

Query(sql, params)
│
├─ 同上流程
├─ ExecuteReader
├─ 逐行读取 → Dictionary<string, object>
│   └─ DBNull → 转为 null
└─ return List<Dictionary<string, object>>

QueryTable(sql, params)
│
├─ 同上流程
├─ ExecuteReader → DataTable.Load
└─ return DataTable

CreateParameter(name, value)
│
└─ return new SqliteParameter(name, value)

GetTableNames()
│
├─ 查询 sqlite_master WHERE type='table'
└─ return 表名列表

GetColumns(tableName)
│
├─ PRAGMA table_info(tableName)
└─ return ColumnInfo 列表
```

#### GetAllDatabases() / ScanDatabaseFiles()

```
GetAllDatabases()
│
└─ 遍历 _databases 缓存 → 生成 DatabaseEntry 列表

ScanDatabaseFiles()
│
├─ 先调用 GetAllDatabases()
├─ sqlite 模式下额外扫描 Database/*.db 文件
│   ├─ 跳过已在缓存中的
│   └─ 读取未缓存文件的表名信息
└─ 合并返回
```

---

### D. PluginApiService — 插件间通信

插件通过 `PluginApiService` 属性注入获得（ModuleManager.PluginApi）。

#### RegisterApi(pluginName, apiName, handler)

```
RegisterApi(pluginName, apiName, handler)
│
└─ _apis[pluginName][apiName] = handler（Delegate）
```

#### CallApi\<TResult\>(pluginName, apiName, args)

```
CallApi<TResult>(pluginName, apiName, args)
│
├─ 插件未注册任何 API? → throw InvalidOperationException
├─ API 不存在? → throw InvalidOperationException
├─ handler.DynamicInvoke(args)
├─ 结果类型匹配 TResult? → return
├─ 不匹配 → Convert.ChangeType 尝试转换 → return
└─ null → return default
```

#### RegisterEvent(pluginName, eventName)

```
RegisterEvent(pluginName, eventName)
│
├─ _registeredEvents[pluginName].Add(eventName)
└─ _eventSubscribers["{pluginName}:{eventName}"] 初始化
```

#### SubscribeEvent\<T\>(pluginName, eventName, handler)

```
SubscribeEvent<T>(pluginName, eventName, handler)
│
└─ _eventSubscribers["{pluginName}:{eventName}"]["_handlers"].Add(handler)
```

#### PublishEvent\<T\>(pluginName, eventName, data)

```
PublishEvent<T>(pluginName, eventName, data)
│
├─ 查找 _eventSubscribers["{pluginName}:{eventName}"]
│   └─ 不存在 → return（无订阅者）
│
├─ 复制 handlers 列表（避免遍历时修改）
└─ 逐个调用 handler.DynamicInvoke(pluginName, data)
    └─ 异常 → 打印错误，继续下一个
```

#### UnregisterAll(pluginName)

```
UnregisterAll(pluginName)
│
├─ 移除所有 API 注册
├─ 移除所有事件注册
└─ 移除所有事件订阅
```

---

### E. 命令执行完整流程（从收到消息到命令处理完成）

当 `OnMessageReceived` 触发后，消息进入 `HandleMessageAsync`：

```
收到消息
│
├─ PlainText 为空? → 丢弃
├─ 用户在 BlockedUsers? → 丢弃
├─ 群在 BlockedGroups? → 丢弃
│
├─ 清理消息文本（去除CQ码、@机器人段）
│
├─ #Mct 状态查询?（且配置启用）
│   └─ 回复运行状态信息 → 结束
│
├─ CommandRegistry.ExecuteCommandAsync(message, text)
│   │
│   ├─ 清理文本，按空格分割
│   ├─ 提取命令名（去/前缀，转小写）
│   │
│   ├─ 查找命令
│   │   └─ 未找到 → return false
│   │
│   ├─ RequireAt 检查
│   │   ├─ 命令需要@ + 未@ + 非私聊 → return false
│   │   └─ 通过 → 继续
│   │
│   ├─ RequireSlash 检查
│   │   ├─ 命令需要/ + 无/前缀 → return false
│   │   └─ 通过 → 继续
│   │
│   ├─ 权限检查 CheckPermission
│   │   ├─ Everyone → 通过
│   │   ├─ BotOwner → 仅 ownerQQ
│   │   ├─ Owner → 群主 或 ownerQQ
│   │   └─ GroupAdmin → 管理员/群主 或 ownerQQ
│   │   └─ 不通过 → 回复"你无权使用此命令" → return true
│   │
│   ├─ 作用域检查 CheckScope
│   │   ├─ All → 通过
│   │   ├─ PrivateOnly → 仅私聊
│   │   └─ GroupOnly → 仅群聊
│   │   └─ 不通过 → 回复提示 → return true
│   │
│   ├─ 参数验证 ValidateParameters
│   │   ├─ 缺少必需参数 → 回复错误信息 → return false
│   │   ├─ 类型不匹配 → 回复错误信息 → return false
│   │   └─ 通过 → 继续
│   │
│   ├─ 解析参数 ParseParameters
│   │   ├─ 提取 @ 用户列表（At 类型参数）
│   │   ├─ 提取回复消息ID（Reply 类型参数）
│   │   └─ 普通参数按位置映射
│   │
│   ├─ 构建 CommandContext
│   │   └─ Message, Parameters, RawCommand, Client
│   │
│   └─ 调用 command.Handler(context)
│       ├─ 成功 → return true
│       └─ 异常
│           ├─ 插件命令 → Log.Error "插件命令执行失败"
│           └─ 内置命令 → Log.Error "执行命令失败"
│           └─ return false
│
└─ 命令未处理 (handled=false)
    └─ RaiseUnhandledMessage → 触发 OnUnhandledMessage 事件
        └─ 外部插件可订阅此事件处理非命令消息
```

---

### F. 插件依赖注入流程

`ModuleManager.InjectDependencies(instance)` 在每个外部模块初始化前被调用：

```
InjectDependencies(instance)
│
├─ 遍历公共可写属性
│   ├─ ResolveByType(prop.PropertyType) 查找服务
│   │   ├─ _serviceContainer 精确类型匹配
│   │   └─ _serviceContainer FullName 匹配（跨ALC兼容）
│   │
│   ├─ 服务存在?
│   │   ├─ 否 → 跳过
│   │   └─ 是 → IsTypeCompatible?
│   │       ├─ 否 → Log跳过原因（类型不兼容）
│   │       └─ 是 → SafeSetValue
│   │           ├─ prop.SetValue 成功 → 注入成功
│   │           └─ ArgumentException（跨ALC类型）
│   │               └─ 尝试 setter.Invoke 反射调用
│   │                   ├─ 成功 → 注入成功
│   │                   └─ 失败 → Log失败原因
│   └─
│
├─ 遍历公共字段（同上逻辑，使用 SafeSetField）
│
├─ 遍历带 [Inject] 特性的属性
│   ├─ 有 Name → 从 _namedServices 查找
│   ├─ 有 Type → 从 _serviceContainer 查找
│   └─ IsTypeCompatible? → SafeSetValue
│
├─ 遍历带 [Inject] 特性的字段（同上）
│
└─ 查找 SetServices() 方法
    ├─ 无参数 → Invoke()
    └─ 有参数 → 逐个按类型解析 → Invoke(args)
```

已注册的 DI 服务：

| 类型 | 名称注册 | 实例 |
|------|---------|------|
| PluginConfigManager | "ConfigManager" | _pluginConfigManager |
| IPluginConfigManager | — | _pluginConfigManager |
| MessageDistributionCore | "MDC" | _mdc |
| CommandRegistry | "CommandRegistry" | _commandRegistry |
| ModuleManager | "ModuleManager" | _moduleManager |
| PluginApiService | "PluginApiService" | _moduleManager.PluginApi |
| ConfigManager | — | _configManager |
| PluginCommandAPI | "PluginCommandAPI" | _pluginCommandAPI |
| PluginDatabaseAPI | "PluginDatabaseAPI" | _pluginDatabaseAPI |
| MorningCatBot | "MorningCatBot" | this |

---

### G. 辅助函数定义

#### IsTypeCompatible(targetType, value)

```
IsTypeCompatible(targetType, value)
│
├─ value == null? → return false
├─ targetType.IsAssignableFrom(value.GetType())? → return true（正常类型兼容）
├─ targetType.FullName == value.GetType().FullName? → return true（跨ALC同名类型兼容）
└─ 否 → return false
```

用途：当插件ALC和宿主ALC加载了同名程序集时，类型对象不同（`IsAssignableFrom`返回false），
但`FullName`相同说明是同一个类型定义，应当允许注入。

#### SafeSetValue(prop, instance, value)

```
SafeSetValue(prop, instance, value)
│
├─ value == null? → 返回
├─ prop.SetValue(instance, value) — 正常路径
│   ├─ 成功 → 返回
│   └─ ArgumentException（跨ALC类型不匹配）
│       ├─ prop.PropertyType.FullName == value.GetType().FullName?
│       │   ├─ 是 → 尝试 setter.Invoke(instance, [value]) — 反射绕过类型检查
│       │   │   ├─ 成功 → 注入成功
│       │   │   └─ 失败 → Log失败原因
│       │   └─ 否 → 放弃（确实不兼容）
│       └─
└─
```

#### SafeSetField(field, instance, value)

```
SafeSetField(field, instance, value)
│
├─ value == null? → 返回
├─ field.SetValue(instance, value) — 正常路径
│   ├─ 成功 → 返回
│   └─ ArgumentException（跨ALC类型不匹配）
│       ├─ field.FieldType.FullName == value.GetType().FullName?
│       │   └─ 是 → field.SetValueDirect(__makeref(instance), value) — 非安全绕过
│       └─ 否 → 放弃
└─
```

#### ResolveByType(type)

```
ResolveByType(type)
│
├─ _serviceContainer 精确类型匹配?
│   ├─ 是 → return 匹配的服务实例
│   └─ 否 → 遍历 _serviceContainer
│       ├─ 找到 Key.FullName == type.FullName? → return 该服务实例（跨ALC兼容）
│       └─ 未找到 → return null
```

#### OnModuleContextResolving(context, assemblyName)

```
OnModuleContextResolving(context, assemblyName) — 插件ALC解析依赖时的回调
│
├─ 优先从默认ALC（主程序）查找同名程序集
│   ├─ 找到 → return 该程序集（确保插件与主程序共享同一份核心类型定义）
│   └─ 未找到 → 继续
├─ 遍历其他ALC（排除自身和默认ALC）
│   ├─ 找到 → return 该程序集
│   └─ 未找到 → 继续
└─ return null（解析失败）
```

**设计意图**：优先从主程序ALC解析，避免插件ALC和主程序ALC各加载一份相同程序集导致
同名类型被 `IsAssignableFrom` 判定为不兼容，从而依赖注入失败。