# MorningCat.PlatformAbstraction API 文档

跨平台消息抽象层，封装 Discord、钉钉、推特等平台的协议抽象，为 MCT 的消息分发核心（MDC）提供统一接口。

## 核心模型

### PlatformId 枚举

| 值 | 描述 |
|------|------|
| `OneBot` | QQ (NapCat/LLOneBot等) |
| `Discord` | Discord |
| `DingTalk` | 钉钉 |
| `Twitter` | 推特/X |
| `Telegram` | Telegram (预留) |

### PlatformMessage

统一消息模型，屏蔽平台差异。

| 属性 | 类型 | 描述 |
|------|------|------|
| `Platform` | `PlatformId` | 消息来源平台 |
| `MessageType` | `UnifiedMessageType` | 消息类型（Private/Group/Channel） |
| `MessageId` | `string` | 消息ID |
| `SenderId` | `string` | 发送者ID |
| `SenderName` | `string` | 发送者昵称 |
| `SenderCard` | `string?` | 群名片 |
| `GroupId` | `string?` | 群/频道ID |
| `GroupName` | `string?` | 群/频道名 |
| `SenderRole` | `string?` | 角色（owner/admin/member） |
| `PlainText` | `string` | 纯文本内容 |
| `SelfId` | `string` | 机器人自身ID |
| `IsAtBot` | `bool` | 是否@了机器人 |
| `Segments` | `List<MessageSegment>` | 消息段列表 |
| `RawMessage` | `object?` | 平台原始消息对象 |
| `SenderDisplayName` | `string` | 显示名称（优先群名片） |

## 消息体构造抽象（IMessageBuilder）

链式 API 构建消息，支持标准 API 和平台特殊 API。

### IMessageBuilder 接口（标准 API，所有平台通用）

| 方法 | 描述 |
|------|------|
| `Text(text)` | 添加纯文本 |
| `At(userId)` | @某人 |
| `AtAll()` | @全体 |
| `Reply(messageId)` | 回复消息 |
| `Image(url)` | 添加图片（URL） |
| `ImageBase64(base64)` | 添加图片（Base64） |
| `Segment(segment)` | 添加原始消息段 |
| `Build()` | 构建消息体（MessageBody） |
| `Clear()` | 清空当前构建状态 |

### MessageBody

构建完成的消息载体。

| 属性/方法 | 描述 |
|------|------|
| `Segments` | 消息段列表 |
| `IsEmpty` | 是否为空消息 |
| `GetPlainText()` | 获取纯文本内容 |

### 平台特殊 API 接口

| 接口 | 平台 | 额外方法 |
|------|------|------|
| `IOneBotMessageBuilder` | OneBot(QQ) | `Face(faceId)`, `Poke(userId)`, `ForwardNode(userId, nickname, content)` |
| `IDiscordMessageBuilder` | Discord | `Embed(title, description, color)`, `Button(label, customId, style)` |
| `IDingTalkMessageBuilder` | 钉钉 | `Markdown(title, text)`, `OaMessage(title, content)` |

### 使用示例

```csharp
// 标准API - 回复+文本
await mdc.SendAsync(message, builder => builder
    .Reply(message.MessageId)
    .Text("你好！"));

// 标准API - 回复+AT+图片
await mdc.SendAsync(message, builder => builder
    .Reply(message.MessageId)
    .At(message.SenderId)
    .ImageBase64(base64Gif));

// 平台特殊API - OneBot表情
var builder = mdc.CreateMessageBuilder(message) as IOneBotMessageBuilder;
if (builder != null)
{
    builder.Face(178).Text("开心");
    await mdc.SendAsync(message, builder.Build());
}

// 平台特殊API - Discord Embed
var discordBuilder = mdc.CreateMessageBuilder(message) as IDiscordMessageBuilder;
if (discordBuilder != null)
{
    discordBuilder.Embed("标题", "描述", 0xFF0000).Text("查看详情");
    await mdc.SendAsync(message, discordBuilder.Build());
}
```

### IPlatformAdapter 接口

每个平台实现此接口。

| 方法/属性 | 描述 |
|------|------|
| `Platform` | 平台标识 |
| `PlatformName` | 平台显示名称 |
| `ConnectionState` | 连接状态 |
| `IsAuthenticated` | 是否已认证 |
| `ConnectAsync()` | 连接到平台 |
| `CloseAsync()` | 断开连接 |
| `ReconnectAsync()` | 尝试重连 |
| `CreateMessageBuilder()` | 创建消息构造器 |
| `SendAsync(target, body)` | 发送消息体（推荐） |
| `SendMessageAsync(target, text)` | 发送纯文本（兼容旧API） |
| `SendPrivateMessageAsync(userId, text)` | 发送私聊消息 |
| `SendGroupMessageAsync(groupId, text)` | 发送群聊消息 |
| `OnMessageReceived` | 收到消息事件 |
| `OnConnectionStateChanged` | 连接状态变化事件 |
| `OnAuthenticated` | 认证成功事件 |
| `OnDisconnected` | 断开连接事件 |

## 平台配置

### DiscordConfig

| 属性 | 类型 | 描述 |
|------|------|------|
| `Enabled` | `bool` | 是否启用 |
| `Token` | `string` | Bot Token |
| `GuildId` | `ulong?` | 目标服务器ID（为空监听所有） |
| `CommandPrefix` | `string` | 命令前缀，默认 `/` |

### DingTalkConfig

| 属性 | 类型 | 描述 |
|------|------|------|
| `Enabled` | `bool` | 是否启用 |
| `AppKey` | `string` | 机器人AppKey |
| `AppSecret` | `string` | 机器人AppSecret |
| `ClientId` | `string` | Stream模式ClientID |
| `ClientSecret` | `string` | Stream模式ClientSecret |
| `WebhookUrl` | `string` | Webhook地址（群消息发送） |
| `WebhookSecret` | `string` | Webhook签名密钥 |
| `UseStreamMode` | `bool` | 是否使用Stream模式，默认 true |

### TwitterConfig

| 属性 | 类型 | 描述 |
|------|------|------|
| `Enabled` | `bool` | 是否启用 |
| `ApiKey` | `string` | API Key |
| `ApiSecret` | `string` | API Secret |
| `AccessToken` | `string` | Access Token |
| `AccessTokenSecret` | `string` | Access Token Secret |
| `BearerToken` | `string` | Bearer Token |

## 使用示例

```csharp
using MorningCat.PlatformAbstraction;
using MorningCat.PlatformAbstraction.Discord;
using MorningCat.PlatformAbstraction.DingTalk;

// 创建Discord适配器
var discordConfig = new DiscordConfig { Token = "your-bot-token", Enabled = true };
var discord = new DiscordPlatformAdapter(discordConfig);

// 创建钉钉适配器
var dingtalkConfig = new DingTalkConfig
{
    AppKey = "your-app-key",
    AppSecret = "your-app-secret",
    ClientId = "your-client-id",
    ClientSecret = "your-client-secret",
    UseStreamMode = true,
    Enabled = true
};
var dingtalk = new DingTalkPlatformAdapter(dingtalkConfig);

// 注册到MDC
var mdc = new MessageDistributionCore();
mdc.RegisterAdapter(discord);
mdc.RegisterAdapter(dingtalk);
mdc.EnablePlatform(PlatformId.Discord);
mdc.EnablePlatform(PlatformId.DingTalk);

// 监听消息
mdc.OnMessageReceived += (sender, msg) =>
{
    Console.WriteLine($"[{msg.Platform}] {msg.SenderDisplayName}: {msg.PlainText}");
};

// 连接所有平台
await mdc.ConnectAllAsync();

// 使用IMessageBuilder发送消息
await mdc.SendAsync(targetMessage, builder => builder
    .Reply(targetMessage.MessageId)
    .Text("收到！"));
```

## 平台实现状态

| 平台 | 状态 | 说明 |
|------|------|------|
| Discord | 已实现 | 基于DSharpPlus 4.5.0，支持收发消息、DM、频道消息、Embed |
| 钉钉 | 已实现 | Stream API接收消息 + HTTP API/Webhook发送消息，支持Markdown卡片 |
| 推特/X | 桩实现 | 接口预留，方法抛出NotImplementedException |
| Telegram | 预留 | 未实现 |
