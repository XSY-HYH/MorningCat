using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using MorningCat.MDC;
using MorningCat.PlatformAbstraction;

namespace MorningCat.Modules
{
    public class FormattedMessage
    {
        public string GroupName { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string Content { get; set; } = "";
        public string MessageType { get; set; } = "";
        public string UserId { get; set; } = "";
        public string? GroupId { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
        public bool HasUnsupportedContent { get; set; }
    }

    public class MessageRelayModule
    {
        private MessageDistributionCore _mdc;
        private readonly Dictionary<string, string> _groupNameCache = new();
        private readonly List<Action<FormattedMessage>> _subscribers = new();
        private readonly object _subLock = new();
        private readonly List<FormattedMessage> _recentMessages = new();
        private const int MaxRecentMessages = 50;

        public event Action<FormattedMessage> OnFormattedMessage
        {
            add
            {
                lock (_subLock) { _subscribers.Add(value); }
            }
            remove
            {
                lock (_subLock) { _subscribers.Remove(value); }
            }
        }

        public void SetMDC(MessageDistributionCore mdc)
        {
            if (_mdc != null)
            {
                _mdc.OnMessageReceived -= OnMessage;
            }
            _mdc = mdc;
            _mdc.OnMessageReceived += OnMessage;
        }

        public void UpdateMDC(MessageDistributionCore mdc)
        {
            SetMDC(mdc);
        }

        public async Task Init()
        {
            Log.Info("消息转发模块初始化完成");
            await Task.CompletedTask;
        }

        private void OnMessage(object? sender, PlatformMessage message)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var formatted = await FormatMessageAsync(message);

                    lock (_subLock)
                    {
                        _recentMessages.Add(formatted);
                        if (_recentMessages.Count > MaxRecentMessages)
                        {
                            _recentMessages.RemoveAt(0);
                        }

                        foreach (var subscriber in _subscribers)
                        {
                            try
                            {
                                subscriber(formatted);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"消息转发模块处理消息失败: {ex.Message}");
                }
            });
        }

        private async Task<FormattedMessage> FormatMessageAsync(PlatformMessage message)
        {
            var senderName = message.SenderDisplayName;
            var messageType = message.MessageType;
            var userId = message.SenderId ?? "";
            var groupId = message.GroupId;

            string groupName = "";
            bool hasUnsupported = false;

            if (messageType == UnifiedMessageType.Group && groupId != null)
            {
                groupName = await GetGroupNameAsync(groupId);
            }

            var content = ExtractDisplayContent(message, ref hasUnsupported);

            return new FormattedMessage
            {
                GroupName = groupName,
                SenderName = senderName,
                Content = content,
                MessageType = messageType.ToString().ToLower(),
                UserId = userId,
                GroupId = groupId,
                Time = DateTime.Now,
                HasUnsupportedContent = hasUnsupported
            };
        }

        private string ExtractDisplayContent(PlatformMessage message, ref bool hasUnsupported)
        {
            var plainText = message.PlainText ?? "";

            if (message.HasNonTextSegments)
            {
                hasUnsupported = true;
                if (string.IsNullOrWhiteSpace(plainText))
                {
                    return "[不支持的消息]";
                }
                return plainText + " [不支持的消息]";
            }

            return plainText;
        }

        private async Task<string> GetGroupNameAsync(string groupId)
        {
            if (_groupNameCache.TryGetValue(groupId, out var name))
            {
                return name;
            }

            try
            {
                var adapter = _mdc?.GetAdapter(PlatformId.OneBot);
                if (adapter is OneBotPlatformAdapter oneBotAdapter)
                {
                    if (long.TryParse(groupId, out var gid))
                    {
                        var result = await oneBotAdapter.GetGroupInfoAsync(gid);
                        if (result?.Success == true && result.Data != null)
                        {
                            var groupName = result.Data.GroupName ?? groupId;
                            _groupNameCache[groupId] = groupName;
                            return groupName;
                        }
                    }
                }
            }
            catch { }

            _groupNameCache[groupId] = groupId;
            return groupId;
        }

        public List<FormattedMessage> GetRecentMessages(int count = 50)
        {
            lock (_subLock)
            {
                var take = Math.Min(count, _recentMessages.Count);
                return new List<FormattedMessage>(_recentMessages.Skip(Math.Max(0, _recentMessages.Count - take)));
            }
        }

        public void Subscribe(Action<FormattedMessage> callback)
        {
            lock (_subLock) { _subscribers.Add(callback); }
        }

        public void Unsubscribe(Action<FormattedMessage> callback)
        {
            lock (_subLock) { _subscribers.Remove(callback); }
        }
    }
}
