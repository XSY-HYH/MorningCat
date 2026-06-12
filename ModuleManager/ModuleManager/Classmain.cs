using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;

namespace ModuleManagerLib
{
    // ===================== 公共类型定义 =====================

    public class ModuleDeclaration
    {
        public List<string> PluginDependencies { get; set; } = new List<string>();
        public List<string> LibraryDependencies { get; set; } = new List<string>();
        public bool AllowDynamicLoad { get; set; } = true;
    }

    public class ProgressInfo
    {
        public string Status { get; set; } = "";
        public int Completed { get; set; }
        public int Total { get; set; }
        public string CurrentModule { get; set; } = "";
        public int Percentage { get; set; }
    }

    public class LoadResult
    {
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public enum ModuleStatus
    {
        NotFound,
        Scanned,
        Initializing,
        Running,
        Error,
        Unloaded
    }

    public abstract class ModuleBase
    {
        public abstract Task Init();
        public virtual Task Exit() => Task.CompletedTask;
        public virtual IEnumerable<string> GetDependencies() => Array.Empty<string>();
        public virtual IEnumerable<string> GetLibraryDependencies() => Array.Empty<string>();
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class InjectAttribute : Attribute
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public InjectAttribute() { }
        public InjectAttribute(string name) => Name = name;
        public InjectAttribute(Type type) => Type = type;
    }

    public class ModuleInfo
    {
        public string ModuleName { get; set; }
        public Type ModuleType { get; set; }
        public object ModuleInstance { get; set; }
        public string AssemblyPath { get; set; }
        public ModuleStatus Status { get; set; }
        public ModuleDeclaration Declaration { get; set; }
        public bool IsDynamicLoaded { get; set; }

        public override string ToString() => $"{ModuleName} [{Status}]";
    }

    // ===================== 内部类型 =====================

    internal class InternalModuleInfo
    {
        public string ModuleName { get; set; }
        public Type ModuleType { get; set; }
        public object ModuleInstance { get; set; }
        public string AssemblyPath { get; set; }
        public AssemblyLoadContext AssemblyContext { get; set; }
        public MethodInfo InitMethod { get; set; }
        public MethodInfo ExitMethod { get; set; }
        public MethodInfo GetDependenciesMethod { get; set; }
        public MethodInfo GetLibraryDependenciesMethod { get; set; }
        public ModuleStatus Status { get; set; }
        public ModuleDeclaration Declaration { get; set; }
        public bool IsDynamicLoaded { get; set; }
        public SemaphoreSlim InitLock { get; } = new SemaphoreSlim(1, 1);

        public ModuleInfo ToPublic()
        {
            return new ModuleInfo
            {
                ModuleName = this.ModuleName,
                ModuleType = this.ModuleType,
                ModuleInstance = this.ModuleInstance,
                AssemblyPath = this.AssemblyPath,
                Status = this.Status,
                Declaration = this.Declaration,
                IsDynamicLoaded = this.IsDynamicLoaded
            };
        }
    }

    internal class ScanResult
    {
        public List<InternalModuleInfo> Modules { get; } = new List<InternalModuleInfo>();
        public List<string> Errors { get; } = new List<string>();
    }

    internal class DependencyParseResult
    {
        public List<string> Errors { get; } = new List<string>();
        public HashSet<string> FailedModules { get; } = new HashSet<string>();
    }

    // ===================== 主模块管理器 =====================

    public class ModuleManager
    {
        private string _modulesFolderPath;
        private string _libraryFolderPath;
        private readonly List<InternalModuleInfo> _moduleInfos = new List<InternalModuleInfo>();
        private readonly Dictionary<string, InternalModuleInfo> _moduleNameMap = new Dictionary<string, InternalModuleInfo>();
        private readonly Dictionary<string, List<string>> _dependencyGraph = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _reverseDependencyGraph = new Dictionary<string, List<string>>();
        private ProgressInfo _currentProgress = new ProgressInfo();
        private readonly object _progressLock = new object();
        private readonly List<string> _loadedLibraries = new List<string>();
        private readonly List<string> _loadedLibraryPaths = new List<string>();

        private readonly Dictionary<Type, object> _serviceContainer = new Dictionary<Type, object>();
        private readonly Dictionary<string, object> _namedServices = new Dictionary<string, object>();
        private readonly PluginApiService _pluginApiService = new PluginApiService();

        private readonly Dictionary<string, AssemblyLoadContext> _moduleContexts = new Dictionary<string, AssemblyLoadContext>();
        private readonly Dictionary<string, AssemblyLoadContext> _libraryContexts = new Dictionary<string, AssemblyLoadContext>();

        private readonly Dictionary<string, List<string>> _libraryDependencyGraph = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _libraryReverseDependencyGraph = new Dictionary<string, List<string>>();

        private Func<Type, object, Task<ModuleDeclaration>> _declarationProvider;
        private bool _isLoaded = false;

        public ProgressInfo CurrentProgress => _currentProgress;
        public PluginApiService PluginApi => _pluginApiService;

        // ========== 事件 ==========
        public event Action<ModuleInfo> ModuleLoaded;
        public event Action<ModuleInfo> ModuleUnloaded;
        public event Action<ModuleInfo, Exception> ModuleFailed;
        public event Action<string> LibraryLoaded;
        public event Action<string> LibraryUnloaded;
        public event Action<string, List<string>> DependencyResolutionFailed;
        public event Action<ProgressInfo> OnProgressUpdated;

        public void Init(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("FolderPathRequired");

            _modulesFolderPath = Path.GetFullPath(folderPath);
            _libraryFolderPath = Path.Combine(_modulesFolderPath, "Library");

            if (!Directory.Exists(_modulesFolderPath))
                Directory.CreateDirectory(_modulesFolderPath);
            if (!Directory.Exists(_libraryFolderPath))
                Directory.CreateDirectory(_libraryFolderPath);
        }

        public void RegisterDeclarationProvider(Func<Type, object, Task<ModuleDeclaration>> provider)
        {
            _declarationProvider = provider;
        }

        #region 依赖注入

        public void RegisterService<T>(T service) where T : class
        {
            lock (_serviceContainer)
                _serviceContainer[typeof(T)] = service;
        }

        public void RegisterService(string name, object service)
        {
            lock (_namedServices)
                _namedServices[name] = service;
        }

        public void RegisterServices(Dictionary<Type, object> services)
        {
            lock (_serviceContainer)
                foreach (var kvp in services)
                    _serviceContainer[kvp.Key] = kvp.Value;
        }

        public T GetService<T>() where T : class
        {
            lock (_serviceContainer)
                return _serviceContainer.TryGetValue(typeof(T), out var s) ? s as T : null;
        }

        public object GetService(string name)
        {
            lock (_namedServices)
                return _namedServices.TryGetValue(name, out var s) ? s : null;
        }
        public List<Type> GetRegisteredServiceTypes()
        {
            lock (_serviceContainer)
                return _serviceContainer.Keys.ToList();
        }
        public List<string> GetRegisteredServiceNames()
        {
            lock (_namedServices)
                return _namedServices.Keys.ToList();
        }

        private object? ResolveByType(Type type)
        {
            lock (_serviceContainer)
            {
                if (_serviceContainer.TryGetValue(type, out var service))
                    return service;

                var typeName = type.FullName;
                if (typeName != null)
                {
                    foreach (var kvp in _serviceContainer)
                    {
                        if (kvp.Key.FullName == typeName)
                            return kvp.Value;
                    }
                }

                return null;
            }
        }

        private static bool IsTypeCompatible(Type targetType, object? value)
        {
            if (value == null) return false;
            var valueType = value.GetType();
            if (targetType.IsAssignableFrom(valueType)) return true;
            return targetType.FullName == valueType.FullName;
        }

        private static void SafeSetValue(PropertyInfo prop, object instance, object? value)
        {
            if (value == null) return;
            try
            {
                prop.SetValue(instance, value);
            }
            catch (ArgumentException)
            {
                if (prop.PropertyType.FullName == value.GetType().FullName)
                {
                    try
                    {
                        var setter = prop.GetSetMethod(true);
                        if (setter != null)
                            setter.Invoke(instance, new object[] { value });
                    }
                    catch { }
                }
            }
        }

        private static void SafeSetField(FieldInfo field, object instance, object? value)
        {
            if (value == null) return;
            try
            {
                field.SetValue(instance, value);
            }
            catch (ArgumentException)
            {
                if (field.FieldType.FullName == value.GetType().FullName)
                {
                    try
                    {
                        field.SetValueDirect(__makeref(instance), value);
                    }
                    catch { }
                }
            }
        }

        private void InjectDependencies(object instance)
        {
            if (instance == null) return;
            var type = instance.GetType();
            var injectedProps = new List<string>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                var service = ResolveByType(prop.PropertyType);
                if (service != null)
                {
                    var compatible = IsTypeCompatible(prop.PropertyType, service);
                    if (compatible)
                    {
                        try
                        {
                            SafeSetValue(prop, instance, service);
                            injectedProps.Add(prop.Name);
                        }
                        catch { }
                    }
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var service = ResolveByType(field.FieldType);
                if (service != null && IsTypeCompatible(field.FieldType, service))
                    SafeSetField(field, instance, service);
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                var attr = prop.GetCustomAttributes<Attribute>().FirstOrDefault(a => a.GetType().FullName == typeof(InjectAttribute).FullName);
                if (attr == null) continue;

                var nameProp = attr.GetType().GetProperty("Name");
                var typeProp = attr.GetType().GetProperty("Type");
                var attrName = nameProp?.GetValue(attr) as string;
                var attrType = typeProp?.GetValue(attr) as Type;

                object? service = null;
                if (!string.IsNullOrEmpty(attrName))
                {
                    lock (_namedServices)
                        _namedServices.TryGetValue(attrName, out service);
                }
                else if (attrType != null)
                {
                    service = ResolveByType(attrType);
                }
                if (service != null)
                {
                    if (IsTypeCompatible(prop.PropertyType, service))
                    {
                        try
                        {
                            SafeSetValue(prop, instance, service);
                        }
                        catch { }
                    }
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = field.GetCustomAttributes<Attribute>().FirstOrDefault(a => a.GetType().FullName == typeof(InjectAttribute).FullName);
                if (attr == null) continue;

                var nameProp = attr.GetType().GetProperty("Name");
                var typeProp = attr.GetType().GetProperty("Type");
                var attrName = nameProp?.GetValue(attr) as string;
                var attrType = typeProp?.GetValue(attr) as Type;

                object? service = null;
                if (!string.IsNullOrEmpty(attrName))
                {
                    lock (_namedServices)
                        _namedServices.TryGetValue(attrName, out service);
                }
                else if (attrType != null)
                {
                    service = ResolveByType(attrType);
                }
                if (service != null && IsTypeCompatible(field.FieldType, service))
                    SafeSetField(field, instance, service);
            }

            var setMethod = type.GetMethod("SetServices");
            if (setMethod != null)
            {
                try
                {
                    var parameters = setMethod.GetParameters();
                    if (parameters.Length == 0)
                    {
                        setMethod.Invoke(instance, null);
                    }
                    else
                    {
                        var args = new object?[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var paramType = parameters[i].ParameterType;
                            var service = ResolveByType(paramType);
                            if (service != null)
                            {
                                if (IsTypeCompatible(paramType, service))
                                    args[i] = service;
                            }
                        }
                        try
                        {
                            setMethod.Invoke(instance, args);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        #endregion

        #region ALC 依赖解析

        private Assembly? OnModuleContextResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            // 优先从默认ALC（主程序）解析，确保跨ALC类型兼容
            var defaultAlc = AssemblyLoadContext.Default;
            var defaultAssembly = defaultAlc.Assemblies
                .FirstOrDefault(a =>
                {
                    var name = a.GetName();
                    return string.Equals(name.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase);
                });
            if (defaultAssembly != null)
                return defaultAssembly;

            // 再搜索其他ALC
            foreach (var alc in AssemblyLoadContext.All)
            {
                if (alc == context || alc == defaultAlc) continue;
                var assembly = alc.Assemblies
                    .FirstOrDefault(a =>
                    {
                        var name = a.GetName();
                        return string.Equals(name.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase);
                    });
                if (assembly != null)
                    return assembly;
            }

            return null;
        }

        #endregion

        #region 流加载

        private async Task<Assembly> LoadAssemblyFromStreamAsync(string path, AssemblyLoadContext context)
        {
            byte[] data = await File.ReadAllBytesAsync(path);
            using var ms = new MemoryStream(data);
            var assembly = context.LoadFromStream(ms);
            Array.Clear(data, 0, data.Length);
            return assembly;
        }

        #endregion

        #region Library 加载

        private async Task LoadLibrariesAsync()
        {
            if (!Directory.Exists(_libraryFolderPath))
            {
                Directory.CreateDirectory(_libraryFolderPath);
                return;
            }

            var dllFiles = Directory.GetFiles(_libraryFolderPath, "*.dll", SearchOption.TopDirectoryOnly);
            foreach (var path in dllFiles)
            {
                try
                {
                    var name = Path.GetFileName(path);
                    var ctx = new AssemblyLoadContext(name, true);
                    await LoadAssemblyFromStreamAsync(path, ctx);
                    lock (_loadedLibraries)
                    {
                        _loadedLibraries.Add(name);
                        _loadedLibraryPaths.Add(path);
                    }
                    _libraryContexts[name] = ctx;
                    LibraryLoaded?.Invoke(name);
                }
                catch (Exception ex)
                {
                }
            }
        }

        #endregion

        #region 模块扫描

        private async Task<ScanResult> ScanModulesAsync()
        {
            return await Task.Run(async () =>
            {
                var result = new ScanResult();
                var dllFiles = Directory.GetFiles(_modulesFolderPath, "*.dll", SearchOption.TopDirectoryOnly)
                    .Where(dll => !dll.Contains($"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}"))
                    .ToList();

                foreach (var path in dllFiles)
                {
                    try
                    {
                        var ctx = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(path), true);
                        ctx.Resolving += OnModuleContextResolving;
                        var asm = await LoadAssemblyFromStreamAsync(path, ctx);

                        var types = asm.GetTypes()
                            .Where(t => t.IsClass && !t.IsAbstract && t.GetMethod("Init") != null)
                            .ToList();

                        foreach (var type in types)
                        {
                            var init = type.GetMethod("Init");
                            var exit = type.GetMethod("Exit");
                            var getDeps = type.GetMethod("GetDependencies");
                            var getLibDeps = type.GetMethod("GetLibraryDependencies");

                            if (init != null && init.ReturnType == typeof(Task))
                            {
                                var instance = Activator.CreateInstance(type);
                                var info = new InternalModuleInfo
                                {
                                    ModuleName = type.Name,
                                    ModuleType = type,
                                    ModuleInstance = instance,
                                    AssemblyPath = path,
                                    AssemblyContext = ctx,
                                    InitMethod = init,
                                    ExitMethod = exit,
                                    GetDependenciesMethod = getDeps,
                                    GetLibraryDependenciesMethod = getLibDeps,
                                    Status = ModuleStatus.Scanned,
                                    IsDynamicLoaded = false
                                };
                                result.Modules.Add(info);
                                lock (_moduleContexts)
                                    _moduleContexts[type.Name] = ctx;
                            }
                            else
                            {
                                result.Errors.Add($"NoInitMethod:{type.Name}");
                                ctx.Unload();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"LoadFailed:{Path.GetFileName(path)}|{ex.Message}");
                    }
                }
                return result;
            });
        }

        #endregion

        #region 依赖解析

        private async Task<DependencyParseResult> ParseDependenciesAsync(IEnumerable<InternalModuleInfo> modules)
        {
            var result = new DependencyParseResult();

            foreach (var module in modules)
            {
                ModuleDeclaration declaration = null;
                if (_declarationProvider != null)
                {
                    try
                    {
                        declaration = await _declarationProvider(module.ModuleType, module.ModuleInstance);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"DeclarationFailed:{module.ModuleName}|{ex.Message}");
                        result.FailedModules.Add(module.ModuleName);
                        continue;
                    }
                }

                if (declaration == null)
                {
                    var deps = new List<string>();
                    var libDeps = new List<string>();

                    if (module.GetDependenciesMethod != null)
                    {
                        try
                        {
                            var depObj = module.GetDependenciesMethod.Invoke(module.ModuleInstance, null);
                            if (depObj is IEnumerable<string> depEnum)
                                deps.AddRange(depEnum);
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"GetDepsFailed:{module.ModuleName}|{ex.Message}");
                            result.FailedModules.Add(module.ModuleName);
                            continue;
                        }
                    }

                    if (module.GetLibraryDependenciesMethod != null)
                    {
                        try
                        {
                            var libObj = module.GetLibraryDependenciesMethod.Invoke(module.ModuleInstance, null);
                            if (libObj is IEnumerable<string> libEnum)
                                libDeps.AddRange(libEnum);
                        }
                        catch (Exception ex)
                        {
                            result.Errors.Add($"GetLibraryDepsFailed:{module.ModuleName}|{ex.Message}");
                            result.FailedModules.Add(module.ModuleName);
                            continue;
                        }
                    }

                    declaration = new ModuleDeclaration
                    {
                        PluginDependencies = deps,
                        LibraryDependencies = libDeps,
                        AllowDynamicLoad = true
                    };
                }

                module.Declaration = declaration;

                // 检查库依赖
                var missingLibs = new List<string>();
                foreach (var lib in declaration.LibraryDependencies)
                {
                    string libName = lib.EndsWith(".dll") ? lib : lib + ".dll";
                    if (!_loadedLibraries.Contains(libName))
                    {
                        missingLibs.Add(lib);
                        result.Errors.Add($"MissingLibraryDependency:{module.ModuleName}->{lib}");
                        result.FailedModules.Add(module.ModuleName);
                    }
                }

                if (missingLibs.Count > 0)
                {
                    DependencyResolutionFailed?.Invoke(module.ModuleName, missingLibs);
                }
            }

            return result;
        }

        private void BuildDependencyGraph(IEnumerable<InternalModuleInfo> modules)
        {
            lock (_dependencyGraph)
            {
                foreach (var module in modules)
                {
                    _dependencyGraph[module.ModuleName] = module.Declaration.PluginDependencies.ToList();
                    _libraryDependencyGraph[module.ModuleName] = module.Declaration.LibraryDependencies.Select(l => l.EndsWith(".dll") ? l : l + ".dll").ToList();
                }
            }

            RebuildReverseGraph();
            RebuildLibraryReverseGraph();
        }

        private void RebuildReverseGraph()
        {
            lock (_reverseDependencyGraph)
            {
                _reverseDependencyGraph.Clear();
                foreach (var kvp in _dependencyGraph)
                {
                    foreach (var dep in kvp.Value)
                    {
                        if (!_moduleNameMap.ContainsKey(dep)) continue;
                        if (!_reverseDependencyGraph.ContainsKey(dep))
                            _reverseDependencyGraph[dep] = new List<string>();
                        _reverseDependencyGraph[dep].Add(kvp.Key);
                    }
                }
            }
        }

        private void RebuildLibraryReverseGraph()
        {
            lock (_libraryReverseDependencyGraph)
            {
                _libraryReverseDependencyGraph.Clear();
                foreach (var kvp in _libraryDependencyGraph)
                {
                    foreach (var lib in kvp.Value)
                    {
                        if (!_libraryReverseDependencyGraph.ContainsKey(lib))
                            _libraryReverseDependencyGraph[lib] = new List<string>();
                        _libraryReverseDependencyGraph[lib].Add(kvp.Key);
                    }
                }
            }
        }

        #endregion

        #region 拓扑排序

        private List<InternalModuleInfo> TopologicalSort(IEnumerable<InternalModuleInfo> modules, Dictionary<string, List<string>> graph)
        {
            var moduleMap = modules.ToDictionary(m => m.ModuleName, m => m);
            var sorted = new List<InternalModuleInfo>();
            var temp = new HashSet<string>();
            var perm = new HashSet<string>();

            bool Visit(string name)
            {
                if (perm.Contains(name)) return true;
                if (temp.Contains(name)) return false;
                temp.Add(name);
                if (graph.TryGetValue(name, out var deps))
                {
                    foreach (var dep in deps)
                        if (moduleMap.ContainsKey(dep) && !Visit(dep))
                            return false;
                }
                temp.Remove(name);
                perm.Add(name);
                sorted.Add(moduleMap[name]);
                return true;
            }

            foreach (var m in modules)
                if (!perm.Contains(m.ModuleName))
                    if (!Visit(m.ModuleName))
                        return null;
            return sorted;
        }

        #endregion

        #region 模块初始化

        private async Task<(bool, Exception)> InitializeModuleAsync(InternalModuleInfo module)
        {
            await module.InitLock.WaitAsync();
            try
            {
                if (module.Status != ModuleStatus.Scanned)
                    return (false, null);

                module.Status = ModuleStatus.Initializing;
                InjectDependencies(module.ModuleInstance);
                await (Task)module.InitMethod.Invoke(module.ModuleInstance, null);
                module.Status = ModuleStatus.Running;
                return (true, null);
            }
            catch (Exception ex)
            {
                module.Status = ModuleStatus.Error;
                return (false, ex);
            }
            finally
            {
                module.InitLock.Release();
            }
        }

        #endregion

        #region 主加载入口

        public async Task<LoadResult> LoadAllModulesAsync()
        {
            if (string.IsNullOrEmpty(_modulesFolderPath))
                throw new InvalidOperationException("InitFirst");

            var result = new LoadResult();

            UpdateProgress("LoadingLibs", 0, 0, "");
            await LoadLibrariesAsync();

            UpdateProgress("Scanning", 0, 0, "");
            var scan = await ScanModulesAsync();
            if (scan.Errors.Count > 0)
            {
                result.Success = false;
                result.Errors.AddRange(scan.Errors);
                return result;
            }

            lock (_moduleInfos)
            {
                _moduleInfos.Clear();
                _moduleNameMap.Clear();
                foreach (var m in scan.Modules)
                {
                    _moduleInfos.Add(m);
                    _moduleNameMap[m.ModuleName] = m;
                }
            }

            UpdateProgress("ParsingDeps", 0, _moduleInfos.Count, "");
            var parse = await ParseDependenciesAsync(_moduleInfos);
            if (parse.Errors.Count > 0)
            {
                result.Success = false;
                result.Errors.AddRange(parse.Errors);
                foreach (var fail in parse.FailedModules)
                {
                    var mod = _moduleInfos.FirstOrDefault(m => m.ModuleName == fail);
                    if (mod != null)
                    {
                        await UnloadModuleInternalAsync(mod);
                        lock (_moduleInfos)
                        {
                            _moduleInfos.Remove(mod);
                            _moduleNameMap.Remove(mod.ModuleName);
                        }
                        result.Errors.Add($"ModuleDependencyFailed:{mod.ModuleName}");
                        ModuleFailed?.Invoke(mod.ToPublic(), new Exception($"DependencyFailed:{string.Join(",", mod.Declaration?.PluginDependencies ?? new List<string>())}"));
                    }
                }
            }

            BuildDependencyGraph(_moduleInfos);

            var sorted = TopologicalSort(_moduleInfos, _dependencyGraph);
            if (sorted == null)
            {
                result.Success = false;
                result.Errors.Add("CircularDependencyDetected");
                return result;
            }

            UpdateProgress("Initializing", 0, sorted.Count, "");
            int completed = 0;
            foreach (var module in sorted)
            {
                UpdateProgress("Initializing", completed, sorted.Count, module.ModuleName);
                var (ok, ex) = await InitializeModuleAsync(module);
                if (ok)
                {
                    ModuleLoaded?.Invoke(module.ToPublic());
                }
                else
                {
                    result.Errors.Add($"InitFailed:{module.ModuleName}|{ex?.Message}");
                    ModuleFailed?.Invoke(module.ToPublic(), ex);
                }
                completed++;
                UpdateProgress("Initializing", completed, sorted.Count, "");
            }

            result.Success = result.Errors.Count == 0;
            UpdateProgress("Done", sorted.Count, sorted.Count, result.Success ? "OK" : "PartialFail");

            var errors = _moduleInfos.Where(m => m.Status == ModuleStatus.Error).ToList();
            foreach (var mod in errors)
            {
                await UnloadModuleInternalAsync(mod);
                lock (_moduleInfos)
                {
                    _moduleInfos.Remove(mod);
                    _moduleNameMap.Remove(mod.ModuleName);
                }
            }

            _isLoaded = true;
            return result;
        }

        #endregion

        #region 动态加载

        public async Task<LoadResult> DynamicLoadModuleAsync(string dllPath)
        {
            var result = new LoadResult();
            if (!File.Exists(dllPath))
            {
                result.Errors.Add($"FileNotFound:{dllPath}");
                return result;
            }

            var ctx = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(dllPath), true);
            ctx.Resolving += OnModuleContextResolving;
            Assembly asm;
            try
            {
                asm = await LoadAssemblyFromStreamAsync(dllPath, ctx);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"LoadFailed:{Path.GetFileName(dllPath)}|{ex.Message}");
                return result;
            }

            var types = asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetMethod("Init") != null).ToList();
            if (types.Count == 0)
            {
                result.Errors.Add($"NoModuleFound:{Path.GetFileName(dllPath)}");
                ctx.Unload();
                return result;
            }

            var newModules = new List<InternalModuleInfo>();
            foreach (var type in types)
            {
                var init = type.GetMethod("Init");
                var exit = type.GetMethod("Exit");
                var getDeps = type.GetMethod("GetDependencies");
                var getLibDeps = type.GetMethod("GetLibraryDependencies");

                if (init == null || init.ReturnType != typeof(Task))
                {
                    result.Errors.Add($"InvalidInit:{type.Name}");
                    continue;
                }

                var instance = Activator.CreateInstance(type);
                newModules.Add(new InternalModuleInfo
                {
                    ModuleName = type.Name,
                    ModuleType = type,
                    ModuleInstance = instance,
                    AssemblyPath = dllPath,
                    AssemblyContext = ctx,
                    InitMethod = init,
                    ExitMethod = exit,
                    GetDependenciesMethod = getDeps,
                    GetLibraryDependenciesMethod = getLibDeps,
                    Status = ModuleStatus.Scanned,
                    IsDynamicLoaded = true
                });
            }

            if (newModules.Count == 0)
            {
                ctx.Unload();
                return result;
            }

            lock (_moduleInfos)
            {
                foreach (var m in newModules)
                {
                    _moduleInfos.Add(m);
                    _moduleNameMap[m.ModuleName] = m;
                }
            }

            var parse = await ParseDependenciesAsync(newModules);
            if (parse.Errors.Count > 0)
            {
                foreach (var m in newModules)
                {
                    lock (_moduleInfos)
                    {
                        _moduleInfos.Remove(m);
                        _moduleNameMap.Remove(m.ModuleName);
                    }
                    await UnloadModuleInternalAsync(m);
                }
                result.Errors.AddRange(parse.Errors);
                return result;
            }

            BuildDependencyGraph(newModules);

            var toInit = newModules.Where(m => m.Status == ModuleStatus.Scanned).ToList();
            var sorted = TopologicalSort(toInit, _dependencyGraph);
            if (sorted == null)
            {
                result.Errors.Add("CircularDependencyInDynamicLoad");
                foreach (var m in newModules)
                {
                    lock (_moduleInfos)
                    {
                        _moduleInfos.Remove(m);
                        _moduleNameMap.Remove(m.ModuleName);
                    }
                    await UnloadModuleInternalAsync(m);
                }
                return result;
            }

            foreach (var module in sorted)
            {
                var (ok, ex) = await InitializeModuleAsync(module);
                if (ok)
                {
                    ModuleLoaded?.Invoke(module.ToPublic());
                }
                else
                {
                    module.Status = ModuleStatus.Error;
                    result.Errors.Add($"InitFailed:{module.ModuleName}|{ex?.Message}");
                    ModuleFailed?.Invoke(module.ToPublic(), ex);
                }
            }

            var failed = newModules.Where(m => m.Status == ModuleStatus.Error).ToList();
            foreach (var m in failed)
            {
                lock (_moduleInfos)
                {
                    _moduleInfos.Remove(m);
                    _moduleNameMap.Remove(m.ModuleName);
                }
                await UnloadModuleInternalAsync(m);
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        #endregion

        #region 动态卸载

        public async Task<bool> DynamicUnloadModuleAsync(string moduleName)
        {
            if (!_moduleNameMap.TryGetValue(moduleName, out var target))
                return false;
            if (target.Status != ModuleStatus.Running)
                return false;
            if (!target.Declaration.AllowDynamicLoad)
                return false;

            var toUnload = new List<InternalModuleInfo>();
            var visited = new HashSet<string>();

            void Collect(string name)
            {
                if (visited.Contains(name)) return;
                visited.Add(name);
                toUnload.Add(_moduleNameMap[name]);
                if (_reverseDependencyGraph.TryGetValue(name, out var dependents))
                {
                    foreach (var dep in dependents)
                        if (_moduleNameMap.ContainsKey(dep))
                            Collect(dep);
                }
            }
            Collect(moduleName);

            var sorted = TopologicalSort(toUnload, _dependencyGraph);
            if (sorted != null)
                sorted.Reverse();
            else
                sorted = toUnload;

            foreach (var mod in sorted)
            {
                var publicInfo = mod.ToPublic();
                await UnloadModuleInternalAsync(mod);
                lock (_moduleInfos)
                {
                    _moduleInfos.Remove(mod);
                    _moduleNameMap.Remove(mod.ModuleName);
                }
                lock (_dependencyGraph)
                {
                    _dependencyGraph.Remove(mod.ModuleName);
                    _libraryDependencyGraph.Remove(mod.ModuleName);
                }
                lock (_moduleContexts)
                    _moduleContexts.Remove(mod.ModuleName);
                ModuleUnloaded?.Invoke(publicInfo);
            }

            RebuildReverseGraph();
            RebuildLibraryReverseGraph();

            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return true;
        }

        #endregion

        #region 内部卸载

        private async Task UnloadModuleInternalAsync(InternalModuleInfo module)
        {
            try
            {
                _pluginApiService.UnregisterAll(module.ModuleName);
                
                if (module.ExitMethod != null && module.ModuleInstance != null)
                {
                    var exitTask = module.ExitMethod.Invoke(module.ModuleInstance, null);
                    if (exitTask is Task task)
                        await task;
                }
                if (module.ModuleInstance is IDisposable disp)
                    disp.Dispose();
                module.ModuleInstance = null;
                module.AssemblyContext?.Unload();
                module.Status = ModuleStatus.Unloaded;
            }
            catch (Exception ex)
            {
                ModuleFailed?.Invoke(module.ToPublic(), ex);
            }
        }

        #endregion

        #region 公共卸载 API

        public async Task UnloadAllModulesAsync()
        {
            var tasks = _moduleInfos.Select(UnloadModuleInternalAsync);
            await Task.WhenAll(tasks);

            var unloadedModules = _moduleInfos.Select(m => m.ToPublic()).ToList();
            lock (_moduleInfos)
            {
                _moduleInfos.Clear();
                _moduleNameMap.Clear();
            }
            lock (_dependencyGraph)
            {
                _dependencyGraph.Clear();
                _reverseDependencyGraph.Clear();
                _libraryDependencyGraph.Clear();
                _libraryReverseDependencyGraph.Clear();
            }
            lock (_moduleContexts)
                _moduleContexts.Clear();

            foreach (var module in unloadedModules)
            {
                ModuleUnloaded?.Invoke(module);
            }

            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            UpdateProgress("Unloaded", 0, 0, "");
            _isLoaded = false;
        }

        public async Task<bool> UnloadModuleAsync(string moduleName)
        {
            if (!_moduleNameMap.TryGetValue(moduleName, out var module))
                return false;
            if (module.Status != ModuleStatus.Running)
                return false;

            var publicInfo = module.ToPublic();
            await UnloadModuleInternalAsync(module);
            lock (_moduleInfos)
            {
                _moduleInfos.Remove(module);
                _moduleNameMap.Remove(moduleName);
            }
            lock (_dependencyGraph)
            {
                _dependencyGraph.Remove(moduleName);
                _libraryDependencyGraph.Remove(moduleName);
            }
            RebuildReverseGraph();
            RebuildLibraryReverseGraph();
            ModuleUnloaded?.Invoke(publicInfo);
            return true;
        }

        #endregion

        #region 查询 API

        public ModuleStatus GetModuleStatus(string name)
            => _moduleNameMap.TryGetValue(name, out var m) ? m.Status : ModuleStatus.NotFound;

        public List<string> GetLoadedModuleNames()
            => _moduleInfos.Where(m => m.Status == ModuleStatus.Running).Select(m => m.ModuleName).ToList();

        public List<ModuleInfo> GetLoadedModules()
            => _moduleInfos.Where(m => m.Status == ModuleStatus.Running).Select(m => m.ToPublic()).ToList();

        public List<string> GetAllModuleNames()
            => _moduleInfos.Select(m => m.ModuleName).ToList();

        public List<ModuleInfo> GetAllModules()
            => _moduleInfos.Select(m => m.ToPublic()).ToList();

        public List<string> GetModuleNamesByStatus(ModuleStatus status)
            => _moduleInfos.Where(m => m.Status == status).Select(m => m.ModuleName).ToList();

        public ModuleInfo GetModuleInfo(string name)
            => _moduleNameMap.TryGetValue(name, out var m) ? m.ToPublic() : null;

        public bool IsModuleLoaded(string name)
            => _moduleNameMap.TryGetValue(name, out var m) && m.Status == ModuleStatus.Running;

        public List<string> GetModuleDependencies(string name)
            => _dependencyGraph.TryGetValue(name, out var deps) ? deps.ToList() : new List<string>();

        public List<string> GetModuleLibraryDependencies(string name)
            => _libraryDependencyGraph.TryGetValue(name, out var libs) ? libs.ToList() : new List<string>();

        public List<string> GetModulesDependentOn(string name)
            => _reverseDependencyGraph.TryGetValue(name, out var deps) ? deps.ToList() : new List<string>();

        public List<string> GetLibrariesDependentOn(string libraryName)
        {
            string lib = libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll";
            return _libraryReverseDependencyGraph.TryGetValue(lib, out var deps) ? deps.ToList() : new List<string>();
        }

        public List<string> GetLoadingOrder()
        {
            var sorted = TopologicalSort(_moduleInfos, _dependencyGraph);
            return sorted?.Select(m => m.ModuleName).ToList() ?? new List<string>();
        }

        #endregion

        #region Library API

        public List<string> GetLoadedLibraries() { lock (_loadedLibraries) return _loadedLibraries.ToList(); }
        public List<string> GetLoadedLibraryPaths() { lock (_loadedLibraries) return _loadedLibraryPaths.ToList(); }
        public int GetLoadedLibraryCount() { lock (_loadedLibraries) return _loadedLibraries.Count; }
        public bool IsLibraryLoaded(string name)
        {
            string lib = name.EndsWith(".dll") ? name : name + ".dll";
            lock (_loadedLibraries) return _loadedLibraries.Contains(lib);
        }

        public bool UnloadLibrary(string libraryName)
        {
            string lib = libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll";
            if (_libraryReverseDependencyGraph.TryGetValue(lib, out var deps) && deps.Count > 0)
                return false;
            if (_libraryContexts.TryGetValue(lib, out var ctx))
                ctx.Unload();
            lock (_loadedLibraries)
            {
                int idx = _loadedLibraries.IndexOf(lib);
                if (idx != -1)
                {
                    _loadedLibraries.RemoveAt(idx);
                    _loadedLibraryPaths.RemoveAt(idx);
                }
            }
            _libraryContexts.Remove(lib);
            LibraryUnloaded?.Invoke(lib);
            for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            return true;
        }

        public bool UnloadLibraryByPath(string path) => UnloadLibrary(Path.GetFileName(path));

        public void UnloadAllLibraries()
        {
            var libs = _loadedLibraries.ToList();
            foreach (var ctx in _libraryContexts.Values) ctx.Unload();
            _libraryContexts.Clear();
            lock (_loadedLibraries)
            {
                _loadedLibraries.Clear();
                _loadedLibraryPaths.Clear();
            }
            foreach (var lib in libs)
            {
                LibraryUnloaded?.Invoke(lib);
            }
            for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        }

        public bool ReloadLibrary(string libraryName)
        {
            string lib = libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll";
            string path = null;
            lock (_loadedLibraries)
            {
                int idx = _loadedLibraries.IndexOf(lib);
                if (idx != -1)
                    path = _loadedLibraryPaths[idx];
            }
            if (path == null)
            {
                path = Path.Combine(_libraryFolderPath, lib);
                if (!File.Exists(path)) return false;
            }
            UnloadLibrary(lib);
            try
            {
                var ctx = new AssemblyLoadContext(lib, true);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                byte[] data = new byte[fs.Length];
                fs.Read(data, 0, data.Length);
                using var ms = new MemoryStream(data);
                ctx.LoadFromStream(ms);
                Array.Clear(data, 0, data.Length);
                _libraryContexts[lib] = ctx;
                lock (_loadedLibraries)
                {
                    _loadedLibraries.Add(lib);
                    _loadedLibraryPaths.Add(path);
                }
                LibraryLoaded?.Invoke(lib);
                return true;
            }
            catch { return false; }
        }

        #endregion

        #region 进度更新

        private void UpdateProgress(string status, int completed, int total, string currentModule)
        {
            lock (_progressLock)
            {
                _currentProgress.Status = status;
                _currentProgress.Completed = completed;
                _currentProgress.Total = total;
                _currentProgress.CurrentModule = currentModule;
                _currentProgress.Percentage = total == 0 ? 0 : (int)((double)completed / total * 100);
            }
            OnProgressUpdated?.Invoke(_currentProgress);
        }

        #endregion
    }
}