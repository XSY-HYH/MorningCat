using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

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

    // ===================== 线程安全的集合包装器 =====================

    internal class ThreadSafeModuleCollection
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly List<InternalModuleInfo> _modules = new List<InternalModuleInfo>();
        private readonly Dictionary<string, InternalModuleInfo> _nameMap = new Dictionary<string, InternalModuleInfo>();

        public void Add(InternalModuleInfo module)
        {
            _lock.EnterWriteLock();
            try
            {
                _modules.Add(module);
                _nameMap[module.ModuleName] = module;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Remove(string moduleName)
        {
            _lock.EnterWriteLock();
            try
            {
                if (_nameMap.TryGetValue(moduleName, out var module))
                {
                    _modules.Remove(module);
                    _nameMap.Remove(moduleName);
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool TryGet(string moduleName, out InternalModuleInfo module)
        {
            _lock.EnterReadLock();
            try
            {
                return _nameMap.TryGetValue(moduleName, out module);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<InternalModuleInfo> GetAll()
        {
            _lock.EnterReadLock();
            try
            {
                return _modules.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<string> GetAllNames()
        {
            _lock.EnterReadLock();
            try
            {
                return _nameMap.Keys.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<InternalModuleInfo> GetByStatus(ModuleStatus status)
        {
            _lock.EnterReadLock();
            try
            {
                return _modules.Where(m => m.Status == status).ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _modules.Clear();
                _nameMap.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _modules.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }
    }

    internal class ThreadSafeGraph
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly Dictionary<string, List<string>> _graph = new Dictionary<string, List<string>>();

        public void Set(string key, List<string> values)
        {
            _lock.EnterWriteLock();
            try
            {
                _graph[key] = values.ToList();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool TryGet(string key, out List<string> values)
        {
            _lock.EnterReadLock();
            try
            {
                if (_graph.TryGetValue(key, out var v))
                {
                    values = v.ToList();
                    return true;
                }
                values = null;
                return false;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Remove(string key)
        {
            _lock.EnterWriteLock();
            try
            {
                _graph.Remove(key);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public Dictionary<string, List<string>> GetSnapshot()
        {
            _lock.EnterReadLock();
            try
            {
                return _graph.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _graph.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public List<string> GetKeys()
        {
            _lock.EnterReadLock();
            try
            {
                return _graph.Keys.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    internal class ThreadSafeLibraryCollection
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private readonly List<string> _names = new List<string>();
        private readonly List<string> _paths = new List<string>();

        public void Add(string name, string path)
        {
            _lock.EnterWriteLock();
            try
            {
                _names.Add(name);
                _paths.Add(path);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Remove(string name)
        {
            _lock.EnterWriteLock();
            try
            {
                int idx = _names.IndexOf(name);
                if (idx != -1)
                {
                    _names.RemoveAt(idx);
                    _paths.RemoveAt(idx);
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool Contains(string name)
        {
            _lock.EnterReadLock();
            try
            {
                return _names.Contains(name);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<string> GetNames()
        {
            _lock.EnterReadLock();
            try
            {
                return _names.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public List<string> GetPaths()
        {
            _lock.EnterReadLock();
            try
            {
                return _paths.ToList();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public (string name, string path)? GetByName(string name)
        {
            _lock.EnterReadLock();
            try
            {
                int idx = _names.IndexOf(name);
                if (idx != -1)
                    return (_names[idx], _paths[idx]);
                return null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                _names.Clear();
                _paths.Clear();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public int Count
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _names.Count;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }
    }

    // ===================== 主模块管理器 =====================

    public class ModuleManager : IDisposable
    {
        private string _modulesFolderPath;
        private string _libraryFolderPath;
        
        // 线程安全的集合
        private readonly ThreadSafeModuleCollection _modules = new ThreadSafeModuleCollection();
        private readonly ThreadSafeGraph _dependencyGraph = new ThreadSafeGraph();
        private readonly ThreadSafeGraph _reverseDependencyGraph = new ThreadSafeGraph();
        private readonly ThreadSafeGraph _libraryDependencyGraph = new ThreadSafeGraph();
        private readonly ThreadSafeGraph _libraryReverseDependencyGraph = new ThreadSafeGraph();
        private readonly ThreadSafeLibraryCollection _loadedLibraries = new ThreadSafeLibraryCollection();
        
        // 并发字典用于 ALC 管理（已经线程安全）
        private readonly ConcurrentDictionary<string, AssemblyLoadContext> _moduleContexts = new ConcurrentDictionary<string, AssemblyLoadContext>();
        private readonly ConcurrentDictionary<string, AssemblyLoadContext> _libraryContexts = new ConcurrentDictionary<string, AssemblyLoadContext>();
        
        // 服务容器
        private readonly ReaderWriterLockSlim _serviceLock = new ReaderWriterLockSlim();
        private readonly Dictionary<Type, object> _serviceContainer = new Dictionary<Type, object>();
        private readonly Dictionary<string, object> _namedServices = new Dictionary<string, object>();
        
        private readonly PluginApiService _pluginApiService = new PluginApiService();
        private ProgressInfo _currentProgress = new ProgressInfo();
        private readonly object _progressLock = new object();
        
        private Func<Type, object, Task<ModuleDeclaration>> _declarationProvider;
        private bool _isLoaded = false;
        private bool _disposed = false;
        
        // 日志回调（可选）
        public Action<string, Exception> OnError { get; set; }

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
            _serviceLock.EnterWriteLock();
            try
            {
                _serviceContainer[typeof(T)] = service;
            }
            finally
            {
                _serviceLock.ExitWriteLock();
            }
        }

        public void RegisterService(string name, object service)
        {
            _serviceLock.EnterWriteLock();
            try
            {
                _namedServices[name] = service;
            }
            finally
            {
                _serviceLock.ExitWriteLock();
            }
        }

        public void RegisterServices(Dictionary<Type, object> services)
        {
            _serviceLock.EnterWriteLock();
            try
            {
                foreach (var kvp in services)
                    _serviceContainer[kvp.Key] = kvp.Value;
            }
            finally
            {
                _serviceLock.ExitWriteLock();
            }
        }

        public T GetService<T>() where T : class
        {
            _serviceLock.EnterReadLock();
            try
            {
                return _serviceContainer.TryGetValue(typeof(T), out var s) ? s as T : null;
            }
            finally
            {
                _serviceLock.ExitReadLock();
            }
        }

        public object GetService(string name)
        {
            _serviceLock.EnterReadLock();
            try
            {
                return _namedServices.TryGetValue(name, out var s) ? s : null;
            }
            finally
            {
                _serviceLock.ExitReadLock();
            }
        }
        
        public List<Type> GetRegisteredServiceTypes()
        {
            _serviceLock.EnterReadLock();
            try
            {
                return _serviceContainer.Keys.ToList();
            }
            finally
            {
                _serviceLock.ExitReadLock();
            }
        }
        
        public List<string> GetRegisteredServiceNames()
        {
            _serviceLock.EnterReadLock();
            try
            {
                return _namedServices.Keys.ToList();
            }
            finally
            {
                _serviceLock.ExitReadLock();
            }
        }

        private object? ResolveByType(Type type)
        {
            _serviceLock.EnterReadLock();
            try
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
            finally
            {
                _serviceLock.ExitReadLock();
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
                    catch { LogError($"Failed to set property {prop.Name}", null); }
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
                    catch { LogError($"Failed to set field {field.Name}", null); }
                }
            }
        }

        private void InjectDependencies(object instance)
        {
            if (instance == null) return;
            var type = instance.GetType();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                var service = ResolveByType(prop.PropertyType);
                if (service != null && IsTypeCompatible(prop.PropertyType, service))
                {
                    SafeSetValue(prop, instance, service);
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var service = ResolveByType(field.FieldType);
                if (service != null && IsTypeCompatible(field.FieldType, service))
                    SafeSetField(field, instance, service);
            }

            // Inject attribute injection
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
                    service = GetService(attrName);
                }
                else if (attrType != null)
                {
                    service = ResolveByType(attrType);
                }
                if (service != null && IsTypeCompatible(prop.PropertyType, service))
                {
                    SafeSetValue(prop, instance, service);
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
                    service = GetService(attrName);
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
                            if (service != null && IsTypeCompatible(paramType, service))
                                args[i] = service;
                        }
                        setMethod.Invoke(instance, args);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to invoke SetServices on {type.Name}", ex);
                }
            }
        }

        #endregion

        #region ALC 依赖解析

        private Assembly? OnModuleContextResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            var defaultAlc = AssemblyLoadContext.Default;
            var defaultAssembly = defaultAlc.Assemblies
                .FirstOrDefault(a =>
                {
                    var name = a.GetName();
                    return string.Equals(name.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase);
                });
            if (defaultAssembly != null)
                return defaultAssembly;

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

        #region 内存优化的流加载

        /// <summary>
        /// 内存优化的程序集加载方法
        /// 避免重复内存分配，正确释放资源
        /// </summary>
        private async Task<Assembly> LoadAssemblyFromStreamAsync(string path, AssemblyLoadContext context)
        {
            // 使用 FileStream 直接读取，避免额外内存拷贝
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var assembly = context.LoadFromStream(fs);
            return await Task.FromResult(assembly);
        }

        /// <summary>
        /// 安全加载程序集，带内存保护
        /// </summary>
        private async Task<Assembly> LoadAssemblySafelyAsync(string path, AssemblyLoadContext context)
        {
            try
            {
                return await LoadAssemblyFromStreamAsync(path, context);
            }
            catch (Exception ex)
            {
                LogError($"Failed to load assembly from {path}", ex);
                throw;
            }
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
                    ctx.Resolving += OnModuleContextResolving;
                    await LoadAssemblySafelyAsync(path, ctx);
                    _loadedLibraries.Add(name, path);
                    _libraryContexts.TryAdd(name, ctx);
                    LibraryLoaded?.Invoke(name);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to load library {Path.GetFileName(path)}", ex);
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
                    AssemblyLoadContext ctx = null;
                    try
                    {
                        ctx = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(path), true);
                        ctx.Resolving += OnModuleContextResolving;
                        var asm = await LoadAssemblySafelyAsync(path, ctx);

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
                                _moduleContexts.TryAdd(type.Name, ctx);
                            }
                            else
                            {
                                result.Errors.Add($"NoInitMethod:{type.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"LoadFailed:{Path.GetFileName(path)}|{ex.Message}");
                        LogError($"Failed to scan module {Path.GetFileName(path)}", ex);
                        try { ctx?.Unload(); } catch { }
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
                        LogError($"Declaration failed for {module.ModuleName}", ex);
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
                            LogError($"GetDependencies failed for {module.ModuleName}", ex);
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
                            LogError($"GetLibraryDependencies failed for {module.ModuleName}", ex);
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
            foreach (var module in modules)
            {
                _dependencyGraph.Set(module.ModuleName, module.Declaration.PluginDependencies.ToList());
                _libraryDependencyGraph.Set(module.ModuleName, module.Declaration.LibraryDependencies
                    .Select(l => l.EndsWith(".dll") ? l : l + ".dll").ToList());
            }

            RebuildReverseGraph();
            RebuildLibraryReverseGraph();
        }

        private void RebuildReverseGraph()
        {
            var newReverseGraph = new Dictionary<string, List<string>>();
            var snapshot = _dependencyGraph.GetSnapshot();
            
            foreach (var kvp in snapshot)
            {
                foreach (var dep in kvp.Value)
                {
                    if (!newReverseGraph.ContainsKey(dep))
                        newReverseGraph[dep] = new List<string>();
                    newReverseGraph[dep].Add(kvp.Key);
                }
            }
            
            _reverseDependencyGraph.Clear();
            foreach (var kvp in newReverseGraph)
            {
                _reverseDependencyGraph.Set(kvp.Key, kvp.Value);
            }
        }

        private void RebuildLibraryReverseGraph()
        {
            var newReverseGraph = new Dictionary<string, List<string>>();
            var snapshot = _libraryDependencyGraph.GetSnapshot();
            
            foreach (var kvp in snapshot)
            {
                foreach (var lib in kvp.Value)
                {
                    if (!newReverseGraph.ContainsKey(lib))
                        newReverseGraph[lib] = new List<string>();
                    newReverseGraph[lib].Add(kvp.Key);
                }
            }
            
            _libraryReverseDependencyGraph.Clear();
            foreach (var kvp in newReverseGraph)
            {
                _libraryReverseDependencyGraph.Set(kvp.Key, kvp.Value);
            }
        }

        #endregion

        #region 拓扑排序

        private List<InternalModuleInfo> TopologicalSort(IEnumerable<InternalModuleInfo> modules, ThreadSafeGraph graph)
        {
            var moduleMap = modules.ToDictionary(m => m.ModuleName, m => m);
            var sorted = new List<InternalModuleInfo>();
            var temp = new HashSet<string>();
            var perm = new HashSet<string>();
            var graphSnapshot = graph.GetSnapshot();

            bool Visit(string name)
            {
                if (perm.Contains(name)) return true;
                if (temp.Contains(name)) return false;
                temp.Add(name);
                if (graphSnapshot.TryGetValue(name, out var deps))
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
                LogError($"Failed to initialize module {module.ModuleName}", ex);
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

            foreach (var m in scan.Modules)
            {
                _modules.Add(m);
            }

            UpdateProgress("ParsingDeps", 0, _modules.Count, "");
            var allModules = _modules.GetAll();
            var parse = await ParseDependenciesAsync(allModules);
            if (parse.Errors.Count > 0)
            {
                result.Success = false;
                result.Errors.AddRange(parse.Errors);
                foreach (var fail in parse.FailedModules)
                {
                    var mod = allModules.FirstOrDefault(m => m.ModuleName == fail);
                    if (mod != null)
                    {
                        await UnloadModuleInternalAsync(mod);
                        _modules.Remove(mod.ModuleName);
                        result.Errors.Add($"ModuleDependencyFailed:{mod.ModuleName}");
                        ModuleFailed?.Invoke(mod.ToPublic(), new Exception($"DependencyFailed:{string.Join(",", mod.Declaration?.PluginDependencies ?? new List<string>())}"));
                    }
                }
            }

            BuildDependencyGraph(allModules);

            var sorted = TopologicalSort(allModules, _dependencyGraph);
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

            var errors = _modules.GetByStatus(ModuleStatus.Error);
            foreach (var mod in errors)
            {
                await UnloadModuleInternalAsync(mod);
                _modules.Remove(mod.ModuleName);
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
                asm = await LoadAssemblySafelyAsync(dllPath, ctx);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"LoadFailed:{Path.GetFileName(dllPath)}|{ex.Message}");
                try { ctx.Unload(); } catch { }
                return result;
            }

            var types = asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.GetMethod("Init") != null).ToList();
            if (types.Count == 0)
            {
                result.Errors.Add($"NoModuleFound:{Path.GetFileName(dllPath)}");
                try { ctx.Unload(); } catch { }
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
                try { ctx.Unload(); } catch { }
                return result;
            }

            foreach (var m in newModules)
            {
                _modules.Add(m);
                _moduleContexts.TryAdd(m.ModuleName, ctx);
            }

            var parse = await ParseDependenciesAsync(newModules);
            if (parse.Errors.Count > 0)
            {
                foreach (var m in newModules)
                {
                    _modules.Remove(m.ModuleName);
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
                    _modules.Remove(m.ModuleName);
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
                _modules.Remove(m.ModuleName);
                await UnloadModuleInternalAsync(m);
            }

            result.Success = result.Errors.Count == 0;
            return result;
        }

        #endregion

        #region 动态卸载

        public async Task<bool> DynamicUnloadModuleAsync(string moduleName)
        {
            if (!_modules.TryGet(moduleName, out var target))
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
                if (_modules.TryGet(name, out var mod))
                    toUnload.Add(mod);
                if (_reverseDependencyGraph.TryGet(name, out var dependents))
                {
                    foreach (var dep in dependents)
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
                _modules.Remove(mod.ModuleName);
                _dependencyGraph.Remove(mod.ModuleName);
                _libraryDependencyGraph.Remove(mod.ModuleName);
                _moduleContexts.TryRemove(mod.ModuleName, out _);
                ModuleUnloaded?.Invoke(publicInfo);
            }

            RebuildReverseGraph();
            RebuildLibraryReverseGraph();

            ForceGarbageCollection();

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
                
                // 尝试卸载 ALC，可能不会立即生效
                try { module.AssemblyContext?.Unload(); } catch { }
                module.Status = ModuleStatus.Unloaded;
            }
            catch (Exception ex)
            {
                LogError($"Failed to unload module {module.ModuleName}", ex);
                ModuleFailed?.Invoke(module.ToPublic(), ex);
            }
        }

        #endregion

        #region 公共卸载 API

        public async Task UnloadAllModulesAsync()
        {
            var allModules = _modules.GetAll();
            foreach (var module in allModules)
            {
                await UnloadModuleInternalAsync(module);
            }

            _modules.Clear();
            _dependencyGraph.Clear();
            _reverseDependencyGraph.Clear();
            _libraryDependencyGraph.Clear();
            _libraryReverseDependencyGraph.Clear();
            _moduleContexts.Clear();

            UpdateProgress("Unloaded", 0, 0, "");
            _isLoaded = false;
            
            ForceGarbageCollection();
        }

        public async Task<bool> UnloadModuleAsync(string moduleName)
        {
            if (!_modules.TryGet(moduleName, out var module))
                return false;
            if (module.Status != ModuleStatus.Running)
                return false;

            var publicInfo = module.ToPublic();
            await UnloadModuleInternalAsync(module);
            _modules.Remove(moduleName);
            _dependencyGraph.Remove(moduleName);
            _libraryDependencyGraph.Remove(moduleName);
            RebuildReverseGraph();
            RebuildLibraryReverseGraph();
            ModuleUnloaded?.Invoke(publicInfo);
            return true;
        }

        #endregion

        #region 查询 API

        public ModuleStatus GetModuleStatus(string name)
            => _modules.TryGet(name, out var m) ? m.Status : ModuleStatus.NotFound;

        public List<string> GetLoadedModuleNames()
            => _modules.GetByStatus(ModuleStatus.Running).Select(m => m.ModuleName).ToList();

        public List<ModuleInfo> GetLoadedModules()
            => _modules.GetByStatus(ModuleStatus.Running).Select(m => m.ToPublic()).ToList();

        public List<string> GetAllModuleNames()
            => _modules.GetAllNames();

        public List<ModuleInfo> GetAllModules()
            => _modules.GetAll().Select(m => m.ToPublic()).ToList();

        public List<string> GetModuleNamesByStatus(ModuleStatus status)
            => _modules.GetByStatus(status).Select(m => m.ModuleName).ToList();

        public ModuleInfo GetModuleInfo(string name)
            => _modules.TryGet(name, out var m) ? m.ToPublic() : null;

        public bool IsModuleLoaded(string name)
            => _modules.TryGet(name, out var m) && m.Status == ModuleStatus.Running;

        public List<string> GetModuleDependencies(string name)
            => _dependencyGraph.TryGet(name, out var deps) ? deps : new List<string>();

        public List<string> GetModuleLibraryDependencies(string name)
            => _libraryDependencyGraph.TryGet(name, out var libs) ? libs : new List<string>();

        public List<string> GetModulesDependentOn(string name)
            => _reverseDependencyGraph.TryGet(name, out var deps) ? deps : new List<string>();

        public List<string> GetLibrariesDependentOn(string libraryName)
        {
            string lib = libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll";
            return _libraryReverseDependencyGraph.TryGet(lib, out var deps) ? deps : new List<string>();
        }

        public List<string> GetLoadingOrder()
        {
            var sorted = TopologicalSort(_modules.GetAll(), _dependencyGraph);
            return sorted?.Select(m => m.ModuleName).ToList() ?? new List<string>();
        }

        #endregion

        #region Library API

        public List<string> GetLoadedLibraries() => _loadedLibraries.GetNames();
        public List<string> GetLoadedLibraryPaths() => _loadedLibraries.GetPaths();
        public int GetLoadedLibraryCount() => _loadedLibraries.Count;
        public bool IsLibraryLoaded(string name)
        {
            string lib = name.EndsWith(".dll") ? name : name + ".dll";
            return _loadedLibraries.Contains(lib);
        }

        public bool UnloadLibrary(string libraryName)
        {
            string lib = libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll";
            if (_libraryReverseDependencyGraph.TryGet(lib, out var deps) && deps.Count > 0)
                return false;
            if (_libraryContexts.TryRemove(lib, out var ctx))
            {
                try { ctx.Unload(); } catch { }
            }
            _loadedLibraries.Remove(lib);
            LibraryUnloaded?.Invoke(lib);
            ForceGarbageCollection();
            return true;
        }

        public bool UnloadLibraryByPath(string path) => UnloadLibrary(Path.GetFileName(path));

        public void UnloadAllLibraries()
        {
            var libs = _loadedLibraries.GetNames();
            foreach (var ctx in _libraryContexts.Values)
            {
                try { ctx.Unload(); } catch { }
            }
            _libraryContexts.Clear();
            _loadedLibraries.Clear();
            foreach (var lib in libs)
            {
                LibraryUnloaded?.Invoke(lib);
            }
            ForceGarbageCollection();
        }

        public bool ReloadLibrary(string libraryName)
        {
            string lib = libraryName.EndsWith(".dll") ? libraryName : libraryName + ".dll";
            var libInfo = _loadedLibraries.GetByName(lib);
            string path = libInfo?.path;
            
            if (path == null)
            {
                path = Path.Combine(_libraryFolderPath, lib);
                if (!File.Exists(path)) return false;
            }
            
            UnloadLibrary(lib);
            
            try
            {
                var ctx = new AssemblyLoadContext(lib, true);
                ctx.Resolving += OnModuleContextResolving;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, false);
                ctx.LoadFromStream(fs);
                _libraryContexts.TryAdd(lib, ctx);
                _loadedLibraries.Add(lib, path);
                LibraryLoaded?.Invoke(lib);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to reload library {lib}", ex);
                return false;
            }
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

        #region 辅助方法

        private static void LogError(string message, Exception ex)
        {
            // 可以在此添加日志记录逻辑
            System.Diagnostics.Debug.WriteLine($"[ERROR] {message}: {ex?.Message}");
        }

        private static void ForceGarbageCollection()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            UnloadAllModulesAsync().GetAwaiter().GetResult();
            UnloadAllLibraries();
            
            _serviceLock.EnterWriteLock();
            try
            {
                _serviceContainer.Clear();
                _namedServices.Clear();
            }
            finally
            {
                _serviceLock.ExitWriteLock();
            }
            
            ForceGarbageCollection();
        }

        #endregion
    }
}