using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MorningCat.PlatformAbstraction.Twitter
{
    /// <summary>
    /// 推特/X平台适配器 - 桩实现，接口预留
    /// </summary>
    public class TwitterPlatformAdapter : IPlatformAdapter
    {
        private readonly TwitterConfig _config;
        private bool _isAuthenticated;
        private PlatformConnectionState _connectionState = PlatformConnectionState.Disconnected;
        private bool _disposed;

        public PlatformId Platform => PlatformId.Twitter;
        public string PlatformName => "推特/X";
        public PlatformConnectionState ConnectionState => _connectionState;
        public bool IsAuthenticated => _isAuthenticated;

        public event EventHandler<PlatformMessage>? OnMessageReceived;
        public event EventHandler<GroupJoinRequest>? OnGroupJoinRequest;
        public event EventHandler<PlatformConnectionState>? OnConnectionStateChanged;
        public event EventHandler? OnAuthenticated;
        public event EventHandler<string>? OnDisconnected;

        public TwitterPlatformAdapter(TwitterConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public Task<bool> ConnectAsync()
        {
            SetConnectionState(PlatformConnectionState.Connecting);

            // TODO: 实现推特API连接
            // 需要使用Twitter API v2的Streaming API接收DM和提及消息
            SetConnectionState(PlatformConnectionState.Disconnected);
            throw new NotImplementedException("推特适配器尚未实现");
        }

        public Task CloseAsync()
        {
            _isAuthenticated = false;
            SetConnectionState(PlatformConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public Task<bool> ReconnectAsync()
        {
            throw new NotImplementedException("推特适配器尚未实现");
        }

        public Task<SendMessageResult> SendMessageAsync(PlatformMessage target, string text)
        {
            throw new NotImplementedException("推特适配器尚未实现");
        }

        public Task<SendMessageResult> SendMessageAsync(PlatformMessage target, List<MessageSegment> segments)
        {
            throw new NotImplementedException("推特适配器尚未实现");
        }

        public Task<SendMessageResult> SendPrivateMessageAsync(string userId, string text)
        {
            throw new NotImplementedException("推特适配器尚未实现");
        }

        public Task<SendMessageResult> SendGroupMessageAsync(string groupId, string text)
        {
            throw new NotImplementedException("推特适配器尚未实现");
        }

        public IMessageBuilder CreateMessageBuilder()
        {
            // 推特暂未实现，返回简单构造器
            return new SimpleMessageBuilder();
        }

        public Task<SendMessageResult> SendAsync(PlatformMessage target, MessageBody body)
        {
            throw new NotImplementedException("推特适配器尚未实现");
        }

        private void SetConnectionState(PlatformConnectionState state)
        {
            if (_connectionState != state)
            {
                _connectionState = state;
                OnConnectionStateChanged?.Invoke(this, state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
