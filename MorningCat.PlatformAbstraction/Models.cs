using System;
using System.Collections.Generic;
using MorningCat.I18n;
using YamlDotNet.Serialization;

namespace MorningCat.PlatformAbstraction
{
    /// <summary>
    /// 平台标识
    /// </summary>
    public enum PlatformId
    {
        OneBot,    // QQ (NapCat/LLOneBot等)
        Discord,   // Discord
        DingTalk,  // 钉钉
        Twitter,   // 推特/X
        Telegram   // Telegram (预留)
    }

    /// <summary>
    /// 统一消息类型
    /// </summary>
    public enum UnifiedMessageType
    {
        Private,  // 私聊/DM
        Group,    // 群聊/频道
        Channel   // 频道（Discord特有，区别于群聊）
    }

    /// <summary>
    /// 统一消息模型 - 屏蔽平台差异
    /// </summary>
    public class PlatformMessage
    {
        /// <summary>消息来源平台</summary>
        public PlatformId Platform { get; set; }

        /// <summary>消息类型（私聊/群聊/频道）</summary>
        public UnifiedMessageType MessageType { get; set; }

        /// <summary>消息ID（平台原始ID）</summary>
        public string MessageId { get; set; } = "";

        /// <summary>发送者ID</summary>
        public string SenderId { get; set; } = "";

        /// <summary>发送者昵称</summary>
        public string SenderName { get; set; } = "";

        /// <summary>发送者在群内的名片（如有）</summary>
        public string? SenderCard { get; set; }

        /// <summary>会话ID（群ID/频道ID，私聊时为空）</summary>
        public string? GroupId { get; set; }

        /// <summary>群名/频道名</summary>
        public string? GroupName { get; set; }

        /// <summary>发送者在群内的角色（owner/admin/member）</summary>
        public string? SenderRole { get; set; }

        /// <summary>纯文本内容</summary>
        public string PlainText { get; set; } = "";

        /// <summary>机器人自身ID</summary>
        public string SelfId { get; set; } = "";

        /// <summary>是否@了机器人</summary>
        public bool IsAtBot { get; set; }

        /// <summary>消息段列表（富文本/图片/AT等）</summary>
        public List<MessageSegment> Segments { get; set; } = new();

        /// <summary>平台原始消息对象（供需要平台特有功能的场景使用）</summary>
        public object? RawMessage { get; set; }

        /// <summary>获取发送者显示名称（优先群名片，其次昵称）</summary>
        public string SenderDisplayName => SenderCard ?? SenderName ?? SenderId;

        /// <summary>是否包含非文本消息段（图片、AT等）</summary>
        public bool HasNonTextSegments => Segments != null && Segments.Exists(s => s.Type != "text");

        /// <summary>获取消息来源描述（用于日志）</summary>
        public string GetSourceInfo()
        {
            if (MessageType == UnifiedMessageType.Group && GroupId != null)
                return $"{SenderDisplayName}（{SenderId}）, {I18nManager.S("log.group")}: {GroupId} [{Platform}]";
            return $"{SenderDisplayName}（{SenderId}） [{Platform}]";
        }

        /// <summary>获取消息简要描述（用于日志）</summary>
        public string GetBriefInfo()
        {
            var text = PlainText ?? "";
            if (MessageType == UnifiedMessageType.Group && GroupId != null)
            {
                var gName = GroupName ?? GroupId;
                return $"[{gName}]{SenderDisplayName}: {text}";
            }
            return $"[{I18nManager.S("log.private")}]{SenderDisplayName}: {text}";
        }
    }

    /// <summary>
    /// 统一消息段 - 表示消息中的富文本元素
    /// </summary>
    public class MessageSegment
    {
        /// <summary>段类型：text/at/image/reply/face 等</summary>
        public string Type { get; set; } = "text";

        /// <summary>段数据</summary>
        public Dictionary<string, object> Data { get; set; } = new();

        /// <summary>创建文本段</summary>
        public static MessageSegment Text(string text)
            => new() { Type = "text", Data = { ["text"] = text } };

        /// <summary>创建@段</summary>
        public static MessageSegment At(string userId)
            => new() { Type = "at", Data = { ["user_id"] = userId } };

        /// <summary>创建回复段</summary>
        public static MessageSegment Reply(string messageId)
            => new() { Type = "reply", Data = { ["message_id"] = messageId } };

        /// <summary>创建图片段</summary>
        public static MessageSegment Image(string url)
            => new() { Type = "image", Data = { ["url"] = url } };
    }

    /// <summary>
    /// 消息发送结果
    /// </summary>
    public class SendMessageResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? MessageId { get; set; }

        public static SendMessageResult Ok(string? messageId = null)
            => new() { Success = true, MessageId = messageId };

        public static SendMessageResult Fail(string error)
            => new() { Success = false, ErrorMessage = error };
    }

    /// <summary>
    /// 平台连接状态
    /// </summary>
    public enum PlatformConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Authenticated
    }

    /// <summary>
    /// 平台适配器接口 - 每个平台实现此接口
    /// </summary>
    public interface IPlatformAdapter : IDisposable
    {
        /// <summary>平台标识</summary>
        PlatformId Platform { get; }

        /// <summary>平台显示名称</summary>
        string PlatformName { get; }

        /// <summary>当前连接状态</summary>
        PlatformConnectionState ConnectionState { get; }

        /// <summary>是否已认证（登录成功）</summary>
        bool IsAuthenticated { get; }

        /// <summary>连接到平台</summary>
        Task<bool> ConnectAsync();

        /// <summary>断开连接</summary>
        Task CloseAsync();

        /// <summary>尝试重连</summary>
        Task<bool> ReconnectAsync();

        /// <summary>创建消息构造器</summary>
        IMessageBuilder CreateMessageBuilder();

        /// <summary>发送消息体到指定会话</summary>
        Task<SendMessageResult> SendAsync(PlatformMessage target, MessageBody body);

        /// <summary>发送消息到指定会话（纯文本，兼容旧API）</summary>
        Task<SendMessageResult> SendMessageAsync(PlatformMessage target, string text);

        /// <summary>发送消息到指定会话（消息段）</summary>
        Task<SendMessageResult> SendMessageAsync(PlatformMessage target, List<MessageSegment> segments);

        /// <summary>发送私聊消息</summary>
        Task<SendMessageResult> SendPrivateMessageAsync(string userId, string text);

        /// <summary>发送群聊消息</summary>
        Task<SendMessageResult> SendGroupMessageAsync(string groupId, string text);

        /// <summary>收到消息事件</summary>
        event EventHandler<PlatformMessage>? OnMessageReceived;

        /// <summary>入群请求事件</summary>
        event EventHandler<GroupJoinRequest>? OnGroupJoinRequest;

        /// <summary>连接状态变化事件</summary>
        event EventHandler<PlatformConnectionState>? OnConnectionStateChanged;

        /// <summary>认证成功事件</summary>
        event EventHandler? OnAuthenticated;

        /// <summary>断开连接事件</summary>
        event EventHandler<string>? OnDisconnected;
    }

    /// <summary>
    /// 入群请求
    /// </summary>
    public class GroupJoinRequest : EventArgs
    {
        /// <summary>平台标识</summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>群ID</summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>请求用户ID</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>请求附言</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>请求标识（用于审批操作）</summary>
        public string Flag { get; set; } = string.Empty;

        /// <summary>请求子类型（add/invite等）</summary>
        public string SubType { get; set; } = string.Empty;
    }

    /// <summary>
    /// 平台连接配置基类
    /// </summary>
    public abstract class PlatformConfig
    {
        /// <summary>是否启用此平台</summary>
        [YamlMember(Alias = "enabled")]
        public bool Enabled { get; set; } = false;
    }

    /// <summary>
    /// Discord平台配置
    /// </summary>
    public class DiscordConfig : PlatformConfig
    {
        /// <summary>Bot Token</summary>
        [YamlMember(Alias = "token")]
        public string Token { get; set; } = "";

        /// <summary>目标服务器ID（为空则监听所有服务器）</summary>
        [YamlMember(Alias = "guild_id")]
        public ulong? GuildId { get; set; }

        /// <summary>命令前缀</summary>
        [YamlMember(Alias = "command_prefix")]
        public string CommandPrefix { get; set; } = "/";
    }

    /// <summary>
    /// 钉钉平台配置
    /// </summary>
    public class DingTalkConfig : PlatformConfig
    {
        /// <summary>机器人AppKey</summary>
        [YamlMember(Alias = "app_key")]
        public string AppKey { get; set; } = "";

        /// <summary>机器人AppSecret</summary>
        [YamlMember(Alias = "app_secret")]
        public string AppSecret { get; set; } = "";

        /// <summary>Stream连接模式使用的ClientID</summary>
        [YamlMember(Alias = "client_id")]
        public string ClientId { get; set; } = "";

        /// <summary>Stream连接模式使用的ClientSecret</summary>
        [YamlMember(Alias = "client_secret")]
        public string ClientSecret { get; set; } = "";

        /// <summary>Webhook地址（用于发送消息）</summary>
        [YamlMember(Alias = "webhook_url")]
        public string WebhookUrl { get; set; } = "";

        /// <summary>Webhook签名密钥</summary>
        [YamlMember(Alias = "webhook_secret")]
        public string WebhookSecret { get; set; } = "";

        /// <summary>是否使用Stream模式（长连接接收消息）</summary>
        [YamlMember(Alias = "use_stream_mode")]
        public bool UseStreamMode { get; set; } = true;
    }

    /// <summary>
    /// 推特平台配置
    /// </summary>
    public class TwitterConfig : PlatformConfig
    {
        /// <summary>API Key</summary>
        [YamlMember(Alias = "api_key")]
        public string ApiKey { get; set; } = "";

        /// <summary>API Secret</summary>
        [YamlMember(Alias = "api_secret")]
        public string ApiSecret { get; set; } = "";

        /// <summary>Access Token</summary>
        [YamlMember(Alias = "access_token")]
        public string AccessToken { get; set; } = "";

        /// <summary>Access Token Secret</summary>
        [YamlMember(Alias = "access_token_secret")]
        public string AccessTokenSecret { get; set; } = "";

        /// <summary>Bearer Token</summary>
        [YamlMember(Alias = "bearer_token")]
        public string BearerToken { get; set; } = "";
    }

    /// <summary>
    /// 消息体 - 构建完成的消息载体，平台无关
    /// </summary>
    public class MessageBody
    {
        /// <summary>消息段列表</summary>
        public List<MessageSegment> Segments { get; } = new();

        /// <summary>是否为空消息</summary>
        public bool IsEmpty => Segments.Count == 0;

        /// <summary>获取纯文本内容（仅文本段）</summary>
        public string GetPlainText()
        {
            return string.Join("", Segments
                .Where(s => s.Type == "text")
                .Select(s => s.Data.TryGetValue("text", out var t) ? t?.ToString() ?? "" : ""));
        }
    }

    /// <summary>
    /// 消息体构造抽象 - 链式API构建消息
    /// </summary>
    public interface IMessageBuilder
    {
        /// <summary>添加纯文本</summary>
        IMessageBuilder Text(string text);

        /// <summary>@某人</summary>
        IMessageBuilder At(string userId);

        /// <summary>@全体</summary>
        IMessageBuilder AtAll();

        /// <summary>回复消息</summary>
        IMessageBuilder Reply(string messageId);

        /// <summary>添加图片（URL）</summary>
        IMessageBuilder Image(string url);

        /// <summary>添加图片（Base64）</summary>
        IMessageBuilder ImageBase64(string base64Data);

        /// <summary>添加原始消息段</summary>
        IMessageBuilder Segment(MessageSegment segment);

        /// <summary>构建消息体</summary>
        MessageBody Build();

        /// <summary>清空当前构建状态</summary>
        IMessageBuilder Clear();
    }

    /// <summary>
    /// 消息体构造抽象基类 - 提供标准API的默认实现
    /// </summary>
    public abstract class MessageBuilderBase : IMessageBuilder
    {
        protected readonly List<MessageSegment> _segments = new();

        public virtual IMessageBuilder Text(string text)
        {
            _segments.Add(MessageSegment.Text(text));
            return this;
        }

        public virtual IMessageBuilder At(string userId)
        {
            _segments.Add(MessageSegment.At(userId));
            return this;
        }

        public virtual IMessageBuilder AtAll()
        {
            _segments.Add(MessageSegment.At("all"));
            return this;
        }

        public virtual IMessageBuilder Reply(string messageId)
        {
            _segments.Add(MessageSegment.Reply(messageId));
            return this;
        }

        public virtual IMessageBuilder Image(string url)
        {
            _segments.Add(MessageSegment.Image(url));
            return this;
        }

        public virtual IMessageBuilder ImageBase64(string base64Data)
        {
            _segments.Add(new MessageSegment { Type = "image", Data = { ["url"] = $"base64://{base64Data}" } });
            return this;
        }

        public virtual IMessageBuilder Segment(MessageSegment segment)
        {
            _segments.Add(segment);
            return this;
        }

        public virtual MessageBody Build()
        {
            var body = new MessageBody();
            foreach (var seg in _segments)
            {
                body.Segments.Add(seg);
            }
            return body;
        }

        public virtual IMessageBuilder Clear()
        {
            _segments.Clear();
            return this;
        }
    }

    /// <summary>
    /// OneBot平台特殊消息构造API
    /// </summary>
    public interface IOneBotMessageBuilder : IMessageBuilder
    {
        /// <summary>QQ表情</summary>
        IOneBotMessageBuilder Face(int faceId);

        /// <summary>戳一戳</summary>
        IOneBotMessageBuilder Poke(string userId);

        /// <summary>转发消息节点</summary>
        IOneBotMessageBuilder ForwardNode(string userId, string nickname, string content);
    }

    /// <summary>
    /// Discord平台特殊消息构造API
    /// </summary>
    public interface IDiscordMessageBuilder : IMessageBuilder
    {
        /// <summary>添加Embed</summary>
        IDiscordMessageBuilder Embed(string title, string description, int color = 0);

        /// <summary>添加按钮组件</summary>
        IDiscordMessageBuilder Button(string label, string customId, string style = "Primary");
    }

    /// <summary>
    /// 钉钉平台特殊消息构造API
    /// </summary>
    public interface IDingTalkMessageBuilder : IMessageBuilder
    {
        /// <summary>Markdown卡片</summary>
        IDingTalkMessageBuilder Markdown(string title, string markdownText);

        /// <summary>OA消息</summary>
        IDingTalkMessageBuilder OaMessage(string title, string content);
    }

    /// <summary>
    /// 简单消息构造器 - 无平台特殊API的默认实现
    /// </summary>
    public class SimpleMessageBuilder : MessageBuilderBase
    {
    }
}
