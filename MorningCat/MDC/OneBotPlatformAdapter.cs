using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Logging;
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;
using MorningCat.Config;
using MorningCat.I18n;
using MorningCat.PlatformAbstraction;
using PlatformSendMessageResult = MorningCat.PlatformAbstraction.SendMessageResult;

namespace MorningCat.MDC
{
    /// <summary>
    /// OneBot平台适配器 - 封装OneBotClient，对接QQ(NapCat/LLOneBot/Lagrange等)
    /// </summary>
    public class OneBotPlatformAdapter : IPlatformAdapter
    {
        private OneBotClient _client;
        private readonly ConfigManager _configManager;
        private PlatformConnectionState _connectionState = PlatformConnectionState.Disconnected;
        private bool _isAuthenticated = false;
        private bool _disposed = false;

        public PlatformId Platform => PlatformId.OneBot;
        public string PlatformName => "OneBot(QQ)";
        public PlatformConnectionState ConnectionState => _connectionState;
        public bool IsAuthenticated => _isAuthenticated;

        /// <summary>获取底层OneBotClient（供需要OneBot特有API的场景使用）</summary>
        public OneBotClient Client => _client;

        public event EventHandler<PlatformMessage>? OnMessageReceived;
        public event EventHandler<MorningCat.PlatformAbstraction.GroupJoinRequest>? OnGroupJoinRequest;
        public event EventHandler<PlatformConnectionState>? OnConnectionStateChanged;
        public event EventHandler? OnAuthenticated;
        public event EventHandler<string>? OnDisconnected;

        public OneBotPlatformAdapter(ConfigManager configManager)
        {
            _configManager = configManager;
            _client = new OneBotClient();
            RegisterClientEvents();
        }

        private void RegisterClientEvents()
        {
            _client.OnMessage += OnClientMessage;
            _client.OnMessageSent += OnClientMessageSent;
            _client.OnLifecycle += OnClientLifecycle;
            _client.OnHeartbeat += OnClientHeartbeat;
            _client.OnConnectionStateChanged += OnClientConnectionStateChanged;
            _client.OnGroupRequest += OnClientGroupRequest;
        }

        private void UnregisterClientEvents()
        {
            _client.OnMessage -= OnClientMessage;
            _client.OnMessageSent -= OnClientMessageSent;
            _client.OnLifecycle -= OnClientLifecycle;
            _client.OnHeartbeat -= OnClientHeartbeat;
            _client.OnConnectionStateChanged -= OnClientConnectionStateChanged;
            _client.OnGroupRequest -= OnClientGroupRequest;
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                SetConnectionState(PlatformConnectionState.Connecting);
                var config = _configManager.GetConfig();

                bool connectSuccess = _client.ConnectSync(config.OneBotServerUrl, config.OneBotToken, 10);
                if (connectSuccess)
                {
                    SetConnectionState(PlatformConnectionState.Connected);
                    Log.Info(I18nManager.S("onebot.ws_connected"));
                    return true;
                }
                else
                {
                    SetConnectionState(PlatformConnectionState.Disconnected);
                    Log.Error(I18nManager.S("onebot.connect_failed"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                SetConnectionState(PlatformConnectionState.Disconnected);
                Log.Error(I18nManager.S("onebot.connect_error", ex.Message));
                return false;
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                UnregisterClientEvents();
                await _client.CloseAsync();
                _isAuthenticated = false;
                SetConnectionState(PlatformConnectionState.Disconnected);
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("onebot.close_error", ex.Message));
            }
        }

        public async Task<bool> ReconnectAsync()
        {
            try
            {
                UnregisterClientEvents();
                await _client.CloseAsync();

                _client = new OneBotClient();
                RegisterClientEvents();

                return await ConnectAsync();
            }
            catch (Exception ex)
            {
                Log.Error(I18nManager.S("onebot.reconnect_error", ex.Message));
                return false;
            }
        }

        public async Task<PlatformSendMessageResult> SendMessageAsync(PlatformMessage target, string text)
        {
            if (!_isAuthenticated)
                return PlatformSendMessageResult.Fail("OneBot未认证");

            try
            {
                if (target.MessageType == UnifiedMessageType.Private)
                {
                    var result = await _client.SendPrivateMsgAsync(long.Parse(target.SenderId), text);
                    return result.Success
                        ? PlatformSendMessageResult.Ok(result.Data.ToString())
                        : PlatformSendMessageResult.Fail(result.ErrorMessage ?? "发送失败");
                }
                else if (target.MessageType == UnifiedMessageType.Group && target.GroupId != null)
                {
                    var result = await _client.SendGroupMsgAsync(long.Parse(target.GroupId), text);
                    return result.Success
                        ? PlatformSendMessageResult.Ok(result.Data.ToString())
                        : PlatformSendMessageResult.Fail(result.ErrorMessage ?? "发送失败");
                }
                else
                {
                    return PlatformSendMessageResult.Fail($"未知的消息类型: {target.MessageType}");
                }
            }
            catch (Exception ex)
            {
                return PlatformSendMessageResult.Fail(ex.Message);
            }
        }

        public async Task<PlatformSendMessageResult> SendMessageAsync(PlatformMessage target, List<MessageSegment> segments)
        {
            // OneBot使用CQ码拼接
            var text = SegmentsToCQCode(segments);
            return await SendMessageAsync(target, text);
        }

        public async Task<PlatformSendMessageResult> SendPrivateMessageAsync(string userId, string text)
        {
            if (!_isAuthenticated)
                return PlatformSendMessageResult.Fail("OneBot未认证");

            try
            {
                var result = await _client.SendPrivateMsgAsync(long.Parse(userId), text);
                return result.Success
                    ? PlatformSendMessageResult.Ok(result.Data.ToString())
                    : PlatformSendMessageResult.Fail(result.ErrorMessage ?? "发送失败");
            }
            catch (Exception ex)
            {
                return PlatformSendMessageResult.Fail(ex.Message);
            }
        }

        public async Task<PlatformSendMessageResult> SendGroupMessageAsync(string groupId, string text)
        {
            if (!_isAuthenticated)
                return PlatformSendMessageResult.Fail("OneBot未认证");

            try
            {
                var result = await _client.SendGroupMsgAsync(long.Parse(groupId), text);
                return result.Success
                    ? PlatformSendMessageResult.Ok(result.Data.ToString())
                    : PlatformSendMessageResult.Fail(result.ErrorMessage ?? "发送失败");
            }
            catch (Exception ex)
            {
                return PlatformSendMessageResult.Fail(ex.Message);
            }
        }

        public IMessageBuilder CreateMessageBuilder()
        {
            return new OneBotMessageBuilder();
        }

        public async Task<PlatformSendMessageResult> SendAsync(PlatformMessage target, MessageBody body)
        {
            if (!_isAuthenticated)
                return PlatformSendMessageResult.Fail("OneBot未认证");

            try
            {
                var cqCode = OneBotMessageBuilder.ToCQCode(body);
                return await SendMessageAsync(target, cqCode);
            }
            catch (Exception ex)
            {
                return PlatformSendMessageResult.Fail(ex.Message);
            }
        }

        /// <summary>获取登录信息（OneBot特有API）</summary>
        public async Task<ApiResult<AccountInfo>?> GetLoginInfoAsync()
        {
            return await _client.GetLoginInfoAsync();
        }

        /// <summary>获取群信息（OneBot特有API）</summary>
        public async Task<ApiResult<GroupInfo>?> GetGroupInfoAsync(long groupId)
        {
            return await _client.GetGroupInfoAsync(groupId);
        }

        #region OneBotClient事件转发

        private void OnClientMessage(object? sender, MessageEventArgs e)
        {
            var message = ConvertToPlatformMessage(e.Message);
            OnMessageReceived?.Invoke(this, message);
        }

        private void OnClientMessageSent(object? sender, MessageSentEventArgs e)
        {
            // 机器人自己发送的消息也通过OnMessageReceived回调
            // 构造PlatformMessage并标记为自身发送
            var platformMsg = new PlatformMessage
            {
                Platform = PlatformId.OneBot,
                MessageId = e.MessageId.ToString(),
                SenderId = _client.CurrentAccountInfo?.UserId.ToString() ?? "",
                SenderName = _client.CurrentAccountInfo?.Nickname ?? "Bot",
                SelfId = _client.CurrentAccountInfo?.UserId.ToString() ?? "",
                PlainText = (e.Message as MessageObject)?.PlainText ?? "",
                MessageType = e.IsGroupMessage ? UnifiedMessageType.Group : UnifiedMessageType.Private,
                GroupId = e.GroupId?.ToString(),
                RawMessage = e.Message
            };
            OnMessageReceived?.Invoke(this, platformMsg);
        }

        private void OnClientGroupRequest(object? sender, GroupRequestEventArgs e)
        {
            var request = new MorningCat.PlatformAbstraction.GroupJoinRequest
            {
                Platform = PlatformId.OneBot.ToString(),
                GroupId = e.GroupId.ToString(),
                UserId = e.UserId.ToString(),
                Comment = e.Comment ?? "",
                Flag = e.Flag ?? "",
                SubType = ""
            };
            OnGroupJoinRequest?.Invoke(this, request);
        }

        private void OnClientLifecycle(object? sender, LifecycleEventArgs e)
        {
            if (e.SubType == "connect" || e.SubType == "enable")
            {
                _isAuthenticated = true;
                SetConnectionState(PlatformConnectionState.Authenticated);
                OnAuthenticated?.Invoke(this, EventArgs.Empty);
            }
            else if (e.SubType == "disconnect" || e.SubType == "disable")
            {
                _isAuthenticated = false;
                SetConnectionState(PlatformConnectionState.Disconnected);
                OnDisconnected?.Invoke(this, e.SubType);
            }
        }

        private void OnClientHeartbeat(object? sender, HeartbeatEventArgs e)
        {
            Log.Debug(I18nManager.S("onebot.heartbeat", e.Interval));
        }

        private void OnClientConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            if (e.NewState == OneBotLib.ConnectionState.Disconnected && _isAuthenticated)
            {
                _isAuthenticated = false;
                SetConnectionState(PlatformConnectionState.Disconnected);
                OnDisconnected?.Invoke(this, "WebSocket断开");
            }
        }

        #endregion

        #region 消息转换

        /// <summary>
        /// 将OneBot的MessageObject转换为统一PlatformMessage
        /// </summary>
        public static PlatformMessage ConvertToPlatformMessage(MessageObject msg)
        {
            var platformMsg = new PlatformMessage
            {
                Platform = PlatformId.OneBot,
                MessageId = msg.MessageId.ToString(),
                SenderId = msg.UserId?.ToString() ?? "",
                SenderName = msg.Sender?.Nickname ?? "",
                SenderCard = msg.Sender?.Card,
                SelfId = msg.SelfId.ToString(),
                PlainText = msg.PlainText ?? "",
                IsAtBot = CheckIsAtBot(msg),
                RawMessage = msg
            };

            // 消息类型映射
            platformMsg.MessageType = msg.MessageType switch
            {
                "private" => UnifiedMessageType.Private,
                "group" => UnifiedMessageType.Group,
                _ => UnifiedMessageType.Private
            };

            // 群信息
            if (msg.GroupId.HasValue)
            {
                platformMsg.GroupId = msg.GroupId.Value.ToString();
            }

            // 群内角色
            if (msg.Sender?.Role != null)
            {
                platformMsg.SenderRole = msg.Sender.Role;
            }

            // 消息段转换
            if (msg.MessageSegments != null)
            {
                foreach (var seg in msg.MessageSegments)
                {
                    var segment = new MessageSegment { Type = seg.Type };
                    if (seg.Data != null)
                    {
                        foreach (var kv in seg.Data)
                        {
                            segment.Data[kv.Key] = kv.Value;
                        }
                    }
                    platformMsg.Segments.Add(segment);
                }
            }

            return platformMsg;
        }

        /// <summary>
        /// 将统一PlatformMessage转换回OneBot的MessageObject（用于需要原始对象的场景）
        /// </summary>
        public static MessageObject? ConvertToMessageObject(PlatformMessage msg)
        {
            return msg.RawMessage as MessageObject;
        }

        private static bool CheckIsAtBot(MessageObject message)
        {
            if (message.MessageSegments != null)
            {
                var selfId = message.SelfId.ToString();
                foreach (var segment in message.MessageSegments)
                {
                    if (segment.Type == "at" && segment.Data != null)
                    {
                        if (segment.Data.TryGetValue("qq", out var qqValue))
                        {
                            var qqStr = qqValue?.ToString();
                            if (qqStr == selfId || qqStr == "all")
                                return true;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(message.PlainText))
            {
                var selfId = message.SelfId.ToString();
                if (message.PlainText.Contains($"[CQ:at,qq={selfId}]") ||
                    message.PlainText.Contains("[CQ:at,qq=all]"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 将统一消息段转换为CQ码
        /// </summary>
        public static string SegmentsToCQCode(List<MessageSegment> segments)
        {
            var parts = new List<string>();
            foreach (var seg in segments)
            {
                switch (seg.Type)
                {
                    case "text":
                        var text = seg.Data.TryGetValue("text", out var t) ? t?.ToString() ?? "" : "";
                        parts.Add(text);
                        break;
                    case "at":
                        var userId = seg.Data.TryGetValue("user_id", out var uid) ? uid?.ToString() ?? "" : "";
                        if (userId == "all")
                            parts.Add("[CQ:at,qq=all]");
                        else
                            parts.Add($"[CQ:at,qq={userId}]");
                        break;
                    case "reply":
                        var msgId = seg.Data.TryGetValue("message_id", out var mid) ? mid?.ToString() ?? "" : "";
                        parts.Add($"[CQ:reply,id={msgId}]");
                        break;
                    case "image":
                        var url = seg.Data.TryGetValue("url", out var u) ? u?.ToString() ?? "" : "";
                        parts.Add($"[CQ:image,file={url}]");
                        break;
                    default:
                        // 未知类型尝试原样拼接
                        if (seg.Data.Count > 0)
                        {
                            var dataStr = string.Join(",", seg.Data.Select(kv => $"{kv.Key}={kv.Value}"));
                            parts.Add($"[CQ:{seg.Type},{dataStr}]");
                        }
                        else
                        {
                            parts.Add($"[CQ:{seg.Type}]");
                        }
                        break;
                }
            }
            return string.Join("", parts);
        }

        #endregion

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

            UnregisterClientEvents();
            _client = null!;
        }
    }
}
