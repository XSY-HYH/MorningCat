using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlusMessageBuilder = DSharpPlus.Entities.DiscordMessageBuilder;

namespace MorningCat.PlatformAbstraction.Discord
{
    /// <summary>
    /// Discord平台适配器 - 基于DSharpPlus实现
    /// </summary>
    public class DiscordPlatformAdapter : IPlatformAdapter
    {
        private readonly DiscordConfig _config;
        private DiscordClient? _client;
        private bool _isAuthenticated;
        private PlatformConnectionState _connectionState = PlatformConnectionState.Disconnected;
        private bool _disposed;

        public PlatformId Platform => PlatformId.Discord;
        public string PlatformName => "Discord";
        public PlatformConnectionState ConnectionState => _connectionState;
        public bool IsAuthenticated => _isAuthenticated;

        public event EventHandler<PlatformMessage>? OnMessageReceived;
        public event EventHandler<GroupJoinRequest>? OnGroupJoinRequest;
        public event EventHandler<PlatformConnectionState>? OnConnectionStateChanged;
        public event EventHandler? OnAuthenticated;
        public event EventHandler<string>? OnDisconnected;

        public DiscordPlatformAdapter(DiscordConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public async Task<bool> ConnectAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.Token))
                throw new InvalidOperationException("Discord Token未配置");

            SetConnectionState(PlatformConnectionState.Connecting);

            try
            {
                _client = new DiscordClient(new DiscordConfiguration
                {
                    Token = _config.Token,
                    TokenType = TokenType.Bot,
                    Intents = DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents
                });

                RegisterEventHandlers();

                await _client.ConnectAsync();
                SetConnectionState(PlatformConnectionState.Connected);

                // DSharpPlus在Ready事件触发后才算认证成功
                return true;
            }
            catch (Exception ex)
            {
                SetConnectionState(PlatformConnectionState.Disconnected);
                throw new InvalidOperationException($"Discord连接失败: {ex.Message}", ex);
            }
        }

        public async Task CloseAsync()
        {
            if (_client != null)
            {
                try
                {
                    _isAuthenticated = false;
                    SetConnectionState(PlatformConnectionState.Disconnected);
                    await _client.DisconnectAsync();
                    _client.Dispose();
                }
                catch { }
                _client = null;
            }
        }

        public async Task<bool> ReconnectAsync()
        {
            await CloseAsync();
            return await ConnectAsync();
        }

        public async Task<SendMessageResult> SendMessageAsync(PlatformMessage target, string text)
        {
            if (!_isAuthenticated || _client == null)
                return SendMessageResult.Fail("Discord未认证");

            try
            {
                DiscordMessage message;

                if (target.MessageType == UnifiedMessageType.Private)
                {
                    var channel = await _client.GetChannelAsync(ulong.Parse(target.GroupId ?? target.SenderId));
                    message = await channel.SendMessageAsync(text);
                }
                else if (target.MessageType == UnifiedMessageType.Group || target.MessageType == UnifiedMessageType.Channel)
                {
                    if (target.GroupId == null)
                        return SendMessageResult.Fail("群消息缺少GroupId");

                    var channel = await _client.GetChannelAsync(ulong.Parse(target.GroupId));
                    message = await channel.SendMessageAsync(text);
                }
                else
                {
                    return SendMessageResult.Fail($"不支持的消息类型: {target.MessageType}");
                }

                return SendMessageResult.Ok(message.Id.ToString());
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(PlatformMessage target, List<MessageSegment> segments)
        {
            if (!_isAuthenticated || _client == null)
                return SendMessageResult.Fail("Discord未认证");

            try
            {
                var dsharpBuilder = new DSharpPlusMessageBuilder();
                foreach (var seg in segments)
                {
                    switch (seg.Type)
                    {
                        case "text":
                            dsharpBuilder.WithContent(seg.Data.TryGetValue("text", out var t) ? t.ToString() ?? "" : "");
                            break;
                        case "image":
                            if (seg.Data.TryGetValue("url", out var url))
                                dsharpBuilder.WithEmbed(new DiscordEmbedBuilder().WithImageUrl(url.ToString()));
                            break;
                        case "reply":
                            // Discord回复通过消息构建器处理
                            break;
                    }
                }

                DiscordChannel channel;
                if (target.MessageType == UnifiedMessageType.Private)
                    channel = await _client.GetChannelAsync(ulong.Parse(target.GroupId ?? target.SenderId));
                else if (target.GroupId != null)
                    channel = await _client.GetChannelAsync(ulong.Parse(target.GroupId));
                else
                    return SendMessageResult.Fail("无法确定目标频道");

                var message = await channel.SendMessageAsync(dsharpBuilder);
                return SendMessageResult.Ok(message.Id.ToString());
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        public async Task<SendMessageResult> SendPrivateMessageAsync(string userId, string text)
        {
            if (!_isAuthenticated || _client == null)
                return SendMessageResult.Fail("Discord未认证");

            try
            {
                var uid = ulong.Parse(userId);
                // 尝试从所有服务器中找到该用户作为Member
                DiscordDmChannel? dmChannel = null;
                foreach (var guild in _client.Guilds.Values)
                {
                    var members = await guild.GetAllMembersAsync();
                    var member = members.FirstOrDefault(m => m.Id == uid);
                    if (member != null)
                    {
                        dmChannel = await member.CreateDmChannelAsync();
                        break;
                    }
                }

                if (dmChannel == null)
                    return SendMessageResult.Fail("无法找到用户创建DM频道");

                var message = await dmChannel.SendMessageAsync(text);
                return SendMessageResult.Ok(message.Id.ToString());
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        public async Task<SendMessageResult> SendGroupMessageAsync(string groupId, string text)
        {
            if (!_isAuthenticated || _client == null)
                return SendMessageResult.Fail("Discord未认证");

            try
            {
                var channel = await _client.GetChannelAsync(ulong.Parse(groupId));
                var message = await channel.SendMessageAsync(text);
                return SendMessageResult.Ok(message.Id.ToString());
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        public IMessageBuilder CreateMessageBuilder()
        {
            return new DiscordMessageBuilder();
        }

        public async Task<SendMessageResult> SendAsync(PlatformMessage target, MessageBody body)
        {
            if (!_isAuthenticated || _client == null)
                return SendMessageResult.Fail("Discord未认证");

            try
            {
                DiscordChannel channel;
                if (target.MessageType == UnifiedMessageType.Private)
                    channel = await _client.GetChannelAsync(ulong.Parse(target.GroupId ?? target.SenderId));
                else if (target.GroupId != null)
                    channel = await _client.GetChannelAsync(ulong.Parse(target.GroupId));
                else
                    return SendMessageResult.Fail("无法确定目标频道");

                var dsharpBuilder = new DSharpPlusMessageBuilder();
                string? replyId = null;

                foreach (var seg in body.Segments)
                {
                    switch (seg.Type)
                    {
                        case "text":
                            var text = seg.Data.TryGetValue("text", out var t) ? t.ToString() ?? "" : "";
                            dsharpBuilder.WithContent(text);
                            break;
                        case "at":
                            // Discord @通过内容中的 <@userId> 实现
                            var uid = seg.Data.TryGetValue("user_id", out var uidVal) ? uidVal?.ToString() : "";
                            if (uid == "all")
                                dsharpBuilder.WithContent($"@everyone");
                            else if (!string.IsNullOrEmpty(uid))
                                dsharpBuilder.WithContent($"<@{uid}>");
                            break;
                        case "reply":
                            replyId = seg.Data.TryGetValue("message_id", out var mid) ? mid?.ToString() : null;
                            break;
                        case "image":
                            var url = seg.Data.TryGetValue("url", out var imgUrl) ? imgUrl?.ToString() ?? "" : "";
                            if (!string.IsNullOrEmpty(url))
                                dsharpBuilder.WithEmbed(new DiscordEmbedBuilder().WithImageUrl(url).Build());
                            break;
                        case "discord_embed":
                            var title = seg.Data.TryGetValue("title", out var et) ? et?.ToString() ?? "" : "";
                            var desc = seg.Data.TryGetValue("description", out var ed) ? ed?.ToString() ?? "" : "";
                            var color = seg.Data.TryGetValue("color", out var ec) ? Convert.ToInt32(ec) : 0;
                            dsharpBuilder.WithEmbed(new DiscordEmbedBuilder().WithTitle(title).WithDescription(desc).WithColor(new DiscordColor(color)).Build());
                            break;
                    }
                }

                if (replyId != null && ulong.TryParse(replyId, out var replyMsgId))
                {
                    dsharpBuilder.WithReply(replyMsgId);
                }

                var message = await channel.SendMessageAsync(dsharpBuilder);
                return SendMessageResult.Ok(message.Id.ToString());
            }
            catch (Exception ex)
            {
                return SendMessageResult.Fail(ex.Message);
            }
        }

        /// <summary>获取Discord客户端（供需要Discord特有API的场景使用）</summary>
        public DiscordClient? GetClient() => _client;

        private void RegisterEventHandlers()
        {
            if (_client == null) return;

            _client.Ready += OnReady;
            _client.MessageCreated += OnMessageCreated;
            _client.ClientErrored += OnClientError;
            _client.SocketClosed += OnSocketClosed;
        }

        private Task OnReady(DiscordClient sender, ReadyEventArgs e)
        {
            _isAuthenticated = true;
            SetConnectionState(PlatformConnectionState.Authenticated);
            OnAuthenticated?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private Task OnMessageCreated(DiscordClient sender, MessageCreateEventArgs e)
        {
            // 忽略自己发送的消息
            if (e.Author.IsBot || e.Author.Id == _client?.CurrentUser.Id)
                return Task.CompletedTask;

            var message = ConvertToPlatformMessage(e.Message);
            OnMessageReceived?.Invoke(this, message);
            return Task.CompletedTask;
        }

        private Task OnClientError(DiscordClient sender, ClientErrorEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[Discord] 客户端错误: {e.Exception?.Message}");
            return Task.CompletedTask;
        }

        private Task OnSocketClosed(DiscordClient sender, SocketCloseEventArgs e)
        {
            if (_isAuthenticated)
            {
                _isAuthenticated = false;
                SetConnectionState(PlatformConnectionState.Disconnected);
                OnDisconnected?.Invoke(this, $"WebSocket关闭: {e.CloseCode} - {e.CloseMessage}");
            }
            return Task.CompletedTask;
        }

        private PlatformMessage ConvertToPlatformMessage(DiscordMessage msg)
        {
            var platformMsg = new PlatformMessage
            {
                Platform = PlatformId.Discord,
                MessageId = msg.Id.ToString(),
                SenderId = msg.Author.Id.ToString(),
                SenderName = msg.Author.Username,
                SelfId = _client?.CurrentUser.Id.ToString() ?? "",
                PlainText = msg.Content ?? "",
                RawMessage = msg
            };

            // 判断消息类型
            if (msg.Channel.IsPrivate)
            {
                platformMsg.MessageType = UnifiedMessageType.Private;
            }
            else
            {
                // Discord频道统一为Channel类型
                platformMsg.MessageType = UnifiedMessageType.Channel;
                platformMsg.GroupId = msg.ChannelId.ToString();
                platformMsg.GroupName = msg.Channel.Name;

                // 获取频道角色
                if (msg.Channel.Guild != null)
                {
                    var member = msg.Channel.Guild.Members.GetValueOrDefault(msg.Author.Id);
                    if (member != null)
                    {
                        if (member.IsOwner)
                            platformMsg.SenderRole = "owner";
                        else if (member.Permissions.HasPermission(Permissions.Administrator))
                            platformMsg.SenderRole = "admin";
                        else
                            platformMsg.SenderRole = "member";
                    }
                }
            }

            // 检查是否@了机器人
            platformMsg.IsAtBot = msg.MentionedUsers?.Any(u => u.Id == _client?.CurrentUser.Id) ?? false;

            // 转换消息段
            if (!string.IsNullOrEmpty(msg.Content))
                platformMsg.Segments.Add(MessageSegment.Text(msg.Content));

            foreach (var attachment in msg.Attachments)
            {
                if (attachment.MediaType?.StartsWith("image/") == true)
                    platformMsg.Segments.Add(MessageSegment.Image(attachment.Url));
            }

            return platformMsg;
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

            _client?.Dispose();
            _client = null;
        }
    }
}
