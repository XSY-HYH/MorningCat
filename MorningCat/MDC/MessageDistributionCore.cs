using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MorningCat.I18n;
using MorningCat.PlatformAbstraction;

namespace MorningCat.MDC
{
    /// <summary>
    /// 消息分发核心 - 管理所有平台适配器，路由消息
    /// </summary>
    public class MessageDistributionCore : IDisposable
    {
        private readonly Dictionary<PlatformId, IPlatformAdapter> _adapters = new();
        private readonly List<PlatformId> _enabledPlatforms = new();
        private readonly object _lock = new();
        private bool _disposed = false;

        /// <summary>收到消息事件（统一后的消息）</summary>
        public event EventHandler<PlatformMessage>? OnMessageReceived;

        /// <summary>入群请求事件</summary>
        public event EventHandler<GroupJoinRequest>? OnGroupJoinRequest;

        /// <summary>平台连接状态变化</summary>
        public event EventHandler<(PlatformId Platform, PlatformConnectionState State)>? OnPlatformConnectionChanged;

        /// <summary>所有平台已认证</summary>
        public event EventHandler? OnAllPlatformsAuthenticated;

        /// <summary>任一平台断开</summary>
        public event EventHandler<(PlatformId Platform, string Reason)>? OnPlatformDisconnected;

        /// <summary>获取已注册的所有平台</summary>
        public IReadOnlyList<PlatformId> RegisteredPlatforms => _adapters.Keys.ToList().AsReadOnly();

        /// <summary>获取已启用的平台</summary>
        public IReadOnlyList<PlatformId> EnabledPlatforms => _enabledPlatforms.AsReadOnly();

        /// <summary>注册平台适配器</summary>
        public void RegisterAdapter(IPlatformAdapter adapter)
        {
            lock (_lock)
            {
                _adapters[adapter.Platform] = adapter;
                adapter.OnMessageReceived += Adapter_OnMessageReceived;
                adapter.OnGroupJoinRequest += Adapter_OnGroupJoinRequest;
                adapter.OnConnectionStateChanged += Adapter_OnConnectionStateChanged;
                adapter.OnAuthenticated += Adapter_OnAuthenticated;
                adapter.OnDisconnected += Adapter_OnDisconnected;
            }
        }

        /// <summary>启用平台</summary>
        public void EnablePlatform(PlatformId platform)
        {
            lock (_lock)
            {
                if (!_enabledPlatforms.Contains(platform))
                {
                    _enabledPlatforms.Add(platform);
                }
            }
        }

        /// <summary>禁用平台（不断开连接，只是不接收/发送该平台消息）</summary>
        public void DisablePlatform(PlatformId platform)
        {
            lock (_lock)
            {
                _enabledPlatforms.Remove(platform);
            }
        }

        /// <summary>平台是否已启用</summary>
        public bool IsPlatformEnabled(PlatformId platform)
        {
            lock (_lock)
            {
                return _enabledPlatforms.Contains(platform);
            }
        }

        /// <summary>获取平台适配器</summary>
        public IPlatformAdapter? GetAdapter(PlatformId platform)
        {
            lock (_lock)
            {
                return _adapters.TryGetValue(platform, out var adapter) ? adapter : null;
            }
        }

        /// <summary>获取指定平台的特定类型适配器</summary>
        public T? GetAdapter<T>(PlatformId platform) where T : class, IPlatformAdapter
        {
            return GetAdapter(platform) as T;
        }

        /// <summary>
        /// 连接所有已启用的平台
        /// </summary>
        public async Task<Dictionary<PlatformId, bool>> ConnectAllAsync()
        {
            var results = new Dictionary<PlatformId, bool>();
            var platforms = new List<PlatformId>();

            lock (_lock)
            {
                platforms.AddRange(_enabledPlatforms);
            }

            foreach (var platform in platforms)
            {
                var adapter = GetAdapter(platform);
                if (adapter != null)
                {
                    try
                    {
                        var success = await adapter.ConnectAsync();
                        results[platform] = success;
                    }
                    catch (Exception ex)
                    {
                        results[platform] = false;
                        Logging.Log.Error(I18nManager.S("mdc.platform_connect_failed", platform, ex.Message));
                    }
                }
                else
                {
                    results[platform] = false;
                    Logging.Log.Warning(I18nManager.S("mdc.platform_not_registered", platform));
                }
            }

            return results;
        }

        /// <summary>
        /// 断开所有平台
        /// </summary>
        public async Task CloseAllAsync()
        {
            List<IPlatformAdapter> adapters;
            lock (_lock)
            {
                adapters = _adapters.Values.ToList();
            }

            foreach (var adapter in adapters)
            {
                try
                {
                    await adapter.CloseAsync();
                }
                catch (Exception ex)
                {
                    Logging.Log.Error(I18nManager.S("mdc.platform_disconnect_failed", adapter.Platform, ex.Message));
                }
            }
        }

        /// <summary>
        /// 发送消息 - 路由到来源平台
        /// </summary>
        public async Task<SendMessageResult> SendMessageAsync(PlatformMessage target, string text)
        {
            var adapter = GetAdapter(target.Platform);
            if (adapter == null)
                return SendMessageResult.Fail($"平台 {target.Platform} 无可用适配器");

            if (!IsPlatformEnabled(target.Platform))
                return SendMessageResult.Fail($"平台 {target.Platform} 未启用");

            return await adapter.SendMessageAsync(target, text);
        }

        /// <summary>
        /// 发送消息体 - 路由到来源平台（推荐使用）
        /// </summary>
        public async Task<SendMessageResult> SendAsync(PlatformMessage target, MessageBody body)
        {
            var adapter = GetAdapter(target.Platform);
            if (adapter == null)
                return SendMessageResult.Fail($"平台 {target.Platform} 无可用适配器");

            if (!IsPlatformEnabled(target.Platform))
                return SendMessageResult.Fail($"平台 {target.Platform} 未启用");

            return await adapter.SendAsync(target, body);
        }

        /// <summary>
        /// 便捷发送 - 使用构造器回调构建消息体并发送
        /// </summary>
        public async Task<SendMessageResult> SendAsync(PlatformMessage target, Action<IMessageBuilder> configure)
        {
            var adapter = GetAdapter(target.Platform);
            if (adapter == null)
                return SendMessageResult.Fail($"平台 {target.Platform} 无可用适配器");

            if (!IsPlatformEnabled(target.Platform))
                return SendMessageResult.Fail($"平台 {target.Platform} 未启用");

            var builder = adapter.CreateMessageBuilder();
            configure(builder);
            var body = builder.Build();
            return await adapter.SendAsync(target, body);
        }

        /// <summary>
        /// 创建消息构造器（根据目标平台自动选择对应实现）
        /// </summary>
        public IMessageBuilder CreateMessageBuilder(PlatformId platform)
        {
            var adapter = GetAdapter(platform);
            if (adapter != null)
                return adapter.CreateMessageBuilder();
            return new SimpleMessageBuilder();
        }

        /// <summary>
        /// 创建消息构造器（根据目标消息的平台自动选择）
        /// </summary>
        public IMessageBuilder CreateMessageBuilder(PlatformMessage target)
        {
            return CreateMessageBuilder(target.Platform);
        }

        /// <summary>
        /// 发送消息到指定平台 - 跨平台发送
        /// </summary>
        public async Task<SendMessageResult> SendMessageAsync(PlatformId platform, string userId, string? groupId, string text)
        {
            var adapter = GetAdapter(platform);
            if (adapter == null)
                return SendMessageResult.Fail($"平台 {platform} 无可用适配器");

            if (!IsPlatformEnabled(platform))
                return SendMessageResult.Fail($"平台 {platform} 未启用");

            if (groupId != null)
            {
                return await adapter.SendGroupMessageAsync(groupId, text);
            }
            else
            {
                return await adapter.SendPrivateMessageAsync(userId, text);
            }
        }

        /// <summary>
        /// 广播消息到所有已启用的平台
        /// </summary>
        public async Task<Dictionary<PlatformId, SendMessageResult>> BroadcastAsync(string text, string? userId = null, string? groupId = null)
        {
            var results = new Dictionary<PlatformId, SendMessageResult>();
            List<PlatformId> platforms;

            lock (_lock)
            {
                platforms = _enabledPlatforms.ToList();
            }

            foreach (var platform in platforms)
            {
                try
                {
                    var result = await SendMessageAsync(platform, userId ?? "", groupId, text);
                    results[platform] = result;
                }
                catch (Exception ex)
                {
                    results[platform] = SendMessageResult.Fail(ex.Message);
                }
            }

            return results;
        }

        /// <summary>
        /// 获取指定平台的连接状态
        /// </summary>
        public PlatformConnectionState GetConnectionState(PlatformId platform)
        {
            var adapter = GetAdapter(platform);
            return adapter?.ConnectionState ?? PlatformConnectionState.Disconnected;
        }

        /// <summary>
        /// 是否所有已启用平台都已认证
        /// </summary>
        public bool AllPlatformsAuthenticated
        {
            get
            {
                lock (_lock)
                {
                    foreach (var platform in _enabledPlatforms)
                    {
                        var adapter = GetAdapter(platform);
                        if (adapter == null || !adapter.IsAuthenticated)
                            return false;
                    }
                    return _enabledPlatforms.Count > 0;
                }
            }
        }

        private void Adapter_OnMessageReceived(object? sender, PlatformMessage message)
        {
            if (!IsPlatformEnabled(message.Platform)) return;
            OnMessageReceived?.Invoke(this, message);
        }

        private void Adapter_OnGroupJoinRequest(object? sender, GroupJoinRequest request)
        {
            if (!IsPlatformEnabled(Enum.Parse<PlatformId>(request.Platform))) return;
            OnGroupJoinRequest?.Invoke(this, request);
        }

        private void Adapter_OnConnectionStateChanged(object? sender, PlatformConnectionState state)
        {
            if (sender is IPlatformAdapter adapter)
            {
                OnPlatformConnectionChanged?.Invoke(this, (adapter.Platform, state));
            }
        }

        private void Adapter_OnAuthenticated(object? sender, EventArgs e)
        {
            if (sender is IPlatformAdapter adapter)
            {
                Logging.Log.Name("MDC");
                Logging.Log.Info(I18nManager.S("mdc.platform_authenticated", adapter.PlatformName));
                if (AllPlatformsAuthenticated)
                {
                    OnAllPlatformsAuthenticated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void Adapter_OnDisconnected(object? sender, string reason)
        {
            if (sender is IPlatformAdapter adapter)
            {
                OnPlatformDisconnected?.Invoke(this, (adapter.Platform, reason));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                foreach (var adapter in _adapters.Values)
                {
                    adapter.OnMessageReceived -= Adapter_OnMessageReceived;
                    adapter.OnGroupJoinRequest -= Adapter_OnGroupJoinRequest;
                    adapter.OnConnectionStateChanged -= Adapter_OnConnectionStateChanged;
                    adapter.OnAuthenticated -= Adapter_OnAuthenticated;
                    adapter.OnDisconnected -= Adapter_OnDisconnected;
                    adapter.Dispose();
                }
                _adapters.Clear();
            }
        }
    }
}
