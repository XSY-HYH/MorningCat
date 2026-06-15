using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ModuleManagerLib
{
    public class PluginApiService
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Delegate>> _apis = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<Delegate>>> _eventSubscribers = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _registeredEvents = new();

        public void RegisterApi(string pluginName, string apiName, Delegate handler)
        {
            var pluginApis = _apis.GetOrAdd(pluginName, _ => new ConcurrentDictionary<string, Delegate>());
            pluginApis[apiName] = handler;
        }

        public void UnregisterApi(string pluginName, string apiName)
        {
            if (_apis.TryGetValue(pluginName, out var pluginApis))
            {
                pluginApis.TryRemove(apiName, out _);
            }
        }

        public void UnregisterAllApis(string pluginName)
        {
            _apis.TryRemove(pluginName, out _);
        }

        public TResult? CallApi<TResult>(string pluginName, string apiName, params object[] args)
        {
            if (!_apis.TryGetValue(pluginName, out var pluginApis))
                throw new InvalidOperationException($"插件'{pluginName}' 未注册任何 API");

            if (!pluginApis.TryGetValue(apiName, out var handler))
                throw new InvalidOperationException($"插件'{pluginName}' 未注册 API: {apiName}");

            var result = handler.DynamicInvoke(args);
            if (result is TResult typed)
                return typed;

            if (result != null)
                return (TResult)Convert.ChangeType(result, typeof(TResult));

            return default;
        }

        public object? CallApi(string pluginName, string apiName, params object[] args)
        {
            if (!_apis.TryGetValue(pluginName, out var pluginApis))
                throw new InvalidOperationException($"插件 '{pluginName}' 未注册任何 API");

            if (!pluginApis.TryGetValue(apiName, out var handler))
                throw new InvalidOperationException($"插件 '{pluginName}' 未注册 API: {apiName}");

            return handler.DynamicInvoke(args);
        }

        public bool HasApi(string pluginName, string apiName)
        {
            return _apis.TryGetValue(pluginName, out var pluginApis) && pluginApis.ContainsKey(apiName);
        }

        public List<string> GetRegisteredApiNames(string pluginName)
        {
            if (!_apis.TryGetValue(pluginName, out var pluginApis))
                return new List<string>();
            return pluginApis.Keys.ToList();
        }

        public List<string> GetPluginsWithApis()
        {
            return _apis.Keys.ToList();
        }

        public void RegisterEvent(string pluginName, string eventName)
        {
            var events = _registeredEvents.GetOrAdd(pluginName, _ => new HashSet<string>());
            lock (events)
            {
                events.Add(eventName);
            }
            _eventSubscribers.GetOrAdd($"{pluginName}:{eventName}", _ => new ConcurrentDictionary<string, List<Delegate>>());
        }

        public void UnregisterEvent(string pluginName, string eventName)
        {
            if (_registeredEvents.TryGetValue(pluginName, out var events))
            {
                lock (events)
                {
                    events.Remove(eventName);
                }
            }
            _eventSubscribers.TryRemove($"{pluginName}:{eventName}", out _);
        }

        public void UnregisterAllEvents(string pluginName)
        {
            _registeredEvents.TryRemove(pluginName, out _);
            var keysToRemove = _eventSubscribers.Keys.Where(k => k.StartsWith(pluginName + ":")).ToList();
            foreach (var key in keysToRemove)
            {
                _eventSubscribers.TryRemove(key, out _);
            }
        }

        public void SubscribeEvent<T>(string pluginName, string eventName, Action<string, T?> handler)
        {
            var key = $"{pluginName}:{eventName}";
            var subscribers = _eventSubscribers.GetOrAdd(key, _ => new ConcurrentDictionary<string, List<Delegate>>());

            lock (subscribers)
            {
                if (!subscribers.TryGetValue("_handlers", out var list))
                {
                    list = new List<Delegate>();
                    subscribers["_handlers"] = list;
                }
                list.Add(handler);
            }
        }

        public void SubscribeEvent(string pluginName, string eventName, Action<string, object?> handler)
        {
            SubscribeEvent<object?>(pluginName, eventName, handler);
        }

        public void UnsubscribeEvent<T>(string pluginName, string eventName, Action<string, T?> handler)
        {
            var key = $"{pluginName}:{eventName}";
            if (_eventSubscribers.TryGetValue(key, out var subscribers))
            {
                lock (subscribers)
                {
                    if (subscribers.TryGetValue("_handlers", out var list))
                    {
                        list.Remove(handler);
                    }
                }
            }
        }

        public void UnsubscribeEvent(string pluginName, string eventName, Action<string, object?> handler)
        {
            UnsubscribeEvent<object?>(pluginName, eventName, handler);
        }

        public void PublishEvent<T>(string pluginName, string eventName, T? data)
        {
            var key = $"{pluginName}:{eventName}";
            if (!_eventSubscribers.TryGetValue(key, out var subscribers))
                return;

            List<Delegate> handlersCopy;
            lock (subscribers)
            {
                if (!subscribers.TryGetValue("_handlers", out var list))
                    return;
                handlersCopy = list.ToList();
            }

            foreach (var handler in handlersCopy)
            {
                try
                {
                    handler.DynamicInvoke(pluginName, data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PluginApiService]事件处理器异常({pluginName}:{eventName}): {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }

        public void PublishEvent(string pluginName, string eventName, object? data = null)
        {
            PublishEvent<object?>(pluginName, eventName, data);
        }

        public List<string> GetRegisteredEventNames(string pluginName)
        {
            if (!_registeredEvents.TryGetValue(pluginName, out var events))
                return new List<string>();
            lock (events)
            {
                return events.ToList();
            }
        }

        public List<string> GetPluginsWithEvents()
        {
            return _registeredEvents.Keys.ToList();
        }

        public void UnregisterAll(string pluginName)
        {
            UnregisterAllApis(pluginName);
            UnregisterAllEvents(pluginName);
        }
    }
}
