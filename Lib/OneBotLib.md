# OneBotLib API 文档

OneBotLib 是一个完整实现 OneBot 11 协议的 C# 类库，支持通过 WebSocket 与 OneBot 实现进行通信。

## 目录

1. [快速开始](#快速开始)
2. [核心类](#核心类)
3. [事件系统](#事件系统)
4. [消息段构建器](#消息段构建器)
5. [API 方法](#api-方法)
   - [用户相关 API](#用户相关-api)
   - [群组相关 API](#群组相关-api)
   - [消息相关 API](#消息相关-api)
   - [文件相关 API](#文件相关-api)
   - [系统相关 API](#系统相关-api)
6. [数据模型](#数据模型)

---

## 快速开始

### 安装

将 OneBotLib 项目添加到您的解决方案中，或编译为 DLL 后引用。

### 基本用法

```csharp
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;
using OneBotLib.MessageSegment;

// 创建客户端实例
var client = new OneBotClient();

// 订阅事件
client.OnMessage += (sender, e) =>
{
    Console.WriteLine($"收到消息: {e.Message.PlainText}");
};

client.OnGroupMessage += (sender, e) =>
{
    var msg = e.Message;
    Console.WriteLine($"群 {msg.GroupId} 的 {msg.Sender.Nickname} 说: {msg.PlainText}");
};

client.OnPrivateMessage += (sender, e) =>
{
    var msg = e.Message;
    Console.WriteLine($"私聊 {msg.UserId} 说: {msg.PlainText}");
};

// 连接到 OneBot 服务
await client.ConnectAsync("ws://127.0.0.1:3001", "your_access_token");

// 或同步连接
bool connected = client.ConnectSync("ws://127.0.0.1:3001", "your_access_token", 5);

// 发送消息（使用 ApiResult 检查结果）
var result = await client.SendPrivateMsgAsync(123456789, "Hello, World!");
if (result.Success)
{
    Console.WriteLine($"消息发送成功，消息ID: {result.Data}");
}
else
{
    Console.WriteLine($"发送失败: {result.ErrorMessage}");
    Console.WriteLine($"堆栈: {result.StackTrace}");
}

// 发送复杂消息
var segments = new List<MessageSegment.MessageSegment>
{
    MessageSegment.MessageSegment.At(123456789),
    MessageSegment.MessageSegment.Text(" 你好！"),
    MessageSegment.MessageSegment.Image("https://example.com/image.png")
};
var groupResult = await client.SendGroupMsgAsync(987654321, segments);

// 关闭连接
await client.CloseAsync();
```

---

## 核心类

### OneBotClient

主要的客户端类，负责 WebSocket 连接、事件处理和 API 调用。

#### 属性

| 属性 | 类型 | 描述 |
|------|------|------|
| `ConnectionState` | `ConnectionState` | 当前连接状态 |
| `IsConnected` | `bool` | 是否已连接到 OneBot 服务 |
| `IsExternalConnection` | `bool` | 是否使用外部连接 |
| `CurrentAccountInfo` | `AccountInfo?` | 当前登录账号信息 |
| `GroupList` | `List<GroupInfo>?` | 群组列表缓存 |
| `FriendList` | `List<FriendInfo>?` | 好友列表缓存 |

#### 方法

| 方法 | 描述 |
|------|------|
| `ConnectAsync(string wsUrl, string token)` | 异步连接到 OneBot 服务 |
| `ConnectSync(string wsUrl, string token, int timeoutSeconds)` | 同步连接（阻塞） |
| `AttachToExternalConnection(ExternalSendMessageDelegate sendMessage)` | 共享外部连接上下文 |
| `OnExternalMessageReceived(string message)` | 处理外部连接接收的消息 |
| `OnExternalMessageReceivedAsync(string message)` | 异步处理外部连接接收的消息 |
| `DetachFromExternalConnection()` | 断开与外部连接的关联 |
| `CloseAsync()` | 关闭连接 |
| `DisposeAsync()` | 释放资源 |

---

### ConnectionState 枚举

连接状态枚举，表示当前 WebSocket 连接的状态。

| 值 | 描述 |
|------|------|
| `Connecting` | 正在连接 |
| `Connected` | 已连接 |
| `Disconnected` | 连接断开 |

### ConnectionStateChangedEventArgs

连接状态变更事件参数。

| 属性 | 类型 | 描述 |
|------|------|------|
| `OldState` | `ConnectionState` | 旧状态 |
| `NewState` | `ConnectionState` | 新状态 |
| `Message` | `string?` | 状态变更消息 |

---

### 共享上下文

OneBotLib 支持与外部程序共享 WebSocket 连接上下文。如果您的程序已经建立了 WebSocket 连接，可以通过这个功能让 OneBotLib 使用已有的连接。

#### 使用方式

```csharp
// 假设您已经有一个 WebSocket 连接
// externalWebSocket 是您已有的 WebSocket 实例

var client = new OneBotClient();

// 定义发送消息的委托
async Task SendMessageAsync(string message)
{
    var bytes = Encoding.UTF8.GetBytes(message);
    await externalWebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
}

// 共享上下文
var sharedContext = client.AttachToExternalConnection(SendMessageAsync);

// 现在您需要在接收到消息时调用 OneBotLib 的处理方法
// 在您的 WebSocket 接收循环中：
async Task OnWebSocketMessageReceived(string message)
{
    // 将消息传递给 OneBotLib 处理
    await client.OnExternalMessageReceivedAsync(message);
}

// 或者使用同步版本
void OnWebSocketMessageReceivedSync(string message)
{
    client.OnExternalMessageReceived(message);
}

// 现在可以正常使用 OneBotLib 的所有 API
var result = await client.GetLoginInfoAsync();
if (result.Success)
{
    Console.WriteLine($"账号: {result.Data.UserId}");
}

// 断开共享（不会关闭您的 WebSocket 连接）
client.DetachFromExternalConnection();
```

#### SharedContext 类

`AttachToExternalConnection` 方法返回一个 `SharedContext` 对象：

| 属性 | 类型 | 描述 |
|------|------|------|
| `SendMessageAsync` | `ExternalSendMessageDelegate?` | 发送消息的委托 |
| `OnMessageReceived` | `Action<string>?` | 接收消息的回调 |
| `IsExternalConnection` | `bool` | 是否为外部连接 |

#### ExternalSendMessageDelegate 委托

```csharp
public delegate Task ExternalSendMessageDelegate(string message);
```

这是用于发送消息的委托类型，您需要实现这个委托来将消息发送到外部连接。

#### 注意事项

1. 使用共享上下文时，OneBotLib 不会管理 WebSocket 连接的生命周期
2. 调用 `CloseAsync()` 或 `DetachFromExternalConnection()` 只会断开关联，不会关闭您的 WebSocket
3. 您需要负责接收消息并调用 `OnExternalMessageReceived` 或 `OnExternalMessageReceivedAsync` 来处理消息
4. `IsExternalConnection` 属性可以用来判断当前是否使用外部连接

---

### ApiResult

所有 API 方法返回的结果类型，包含成功状态、错误信息和堆栈跟踪。

**命名空间**: `OneBotLib`

```csharp
using OneBotLib;

// 使用示例
var result = await client.GetLoginInfoAsync();
if (result.Success)
{
    Console.WriteLine($"账号: {result.Data.UserId}");
}
else
{
    Console.WriteLine($"错误: {result.ErrorMessage}");
}
```

#### ApiResult（无返回数据）

| 属性 | 类型 | 描述 |
|------|------|------|
| `Success` | `bool` | 是否成功 |
| `ErrorMessage` | `string?` | 错误信息 |
| `StackTrace` | `string?` | 堆栈跟踪 |

**静态方法**:

| 方法 | 描述 |
|------|------|
| `ApiResult.Ok()` | 创建成功结果 |
| `ApiResult.Fail(string message, string? stackTrace = null)` | 创建失败结果 |
| `ApiResult.Fail(Exception ex)` | 从异常创建失败结果 |

#### ApiResult\<T\>（有返回数据）

| 属性 | 类型 | 描述 |
|------|------|------|
| `Success` | `bool` | 是否成功 |
| `Data` | `T?` | 返回数据 |
| `ErrorMessage` | `string?` | 错误信息 |
| `StackTrace` | `string?` | 堆栈跟踪 |

**静态方法**:

| 方法 | 描述 |
|------|------|
| `ApiResult<T>.Ok(T data)` | 创建成功结果（带数据） |
| `ApiResult<T>.Fail(string message, string? stackTrace = null)` | 创建失败结果 |
| `ApiResult<T>.Fail(Exception ex)` | 从异常创建失败结果 |

---

## 事件系统

所有事件参数类都在 `OneBotLib.Events` 命名空间中，使用时需要：

```csharp
using OneBotLib.Events;
```

### 消息事件

```csharp
// 所有消息
client.OnMessage += (sender, e) => { };

// 私聊消息
client.OnPrivateMessage += (sender, e) => { };

// 群消息
client.OnGroupMessage += (sender, e) => { };
```

### 消息发送事件

机器人自己发送的消息（包括从其他设备发送的消息）：

```csharp
// 所有自己发送的消息
client.OnMessageSent += (sender, e) =>
{
    Console.WriteLine($"机器人发送消息: {e.MessageId}, 类型: {e.MessageType}");
};

// 私聊发送的消息
client.OnPrivateMessageSent += (sender, e) =>
{
    Console.WriteLine($"机器人发送私聊消息给 {e.UserId}: {e.MessageId}");
};

// 群发送的消息
client.OnGroupMessageSent += (sender, e) =>
{
    Console.WriteLine($"机器人在群 {e.GroupId} 发送消息: {e.MessageId}");
};
```

**MessageSentEventArgs 属性**:

| 属性 | 类型 | 描述 |
|------|------|------|
| `MessageId` | `long` | 消息 ID |
| `MessageType` | `string` | 消息类型 (private/group) |
| `UserId` | `long?` | 用户 ID（私聊） |
| `GroupId` | `long?` | 群 ID（群聊） |
| `Message` | `object` | 消息内容 |
| `Time` | `long` | 发送时间戳 |
| `IsGroupMessage` | `bool` | 是否群消息 |
| `IsPrivateMessage` | `bool` | 是否私聊消息 |

### 元事件

```csharp
// 生命周期事件
client.OnLifecycle += (sender, e) =>
{
    Console.WriteLine($"生命周期事件: {e.MetaEventType}, 子类型: {e.SubType}");
};

// 心跳事件
client.OnHeartbeat += (sender, e) =>
{
    Console.WriteLine($"心跳间隔: {e.Interval}ms");
};
```

### 连接状态事件

```csharp
// 连接状态变更
client.OnConnectionStateChanged += (sender, e) =>
{
    Console.WriteLine($"连接状态: {e.OldState} -> {e.NewState}");
    if (e.Message != null)
    {
        Console.WriteLine($"消息: {e.Message}");
    }
};

// 状态值：
// ConnectionState.Connecting - 正在连接
// ConnectionState.Connected - 已连接
// ConnectionState.Disconnected - 连接断开
```

### 群通知事件

```csharp
// 群成员变动
client.OnGroupMemberChange += (sender, e) =>
{
    Console.WriteLine($"群 {e.GroupId} 成员 {e.UserId} 变动: {e.NoticeType}");
};

// 群管理员变动
client.OnGroupAdmin += (sender, e) => { };

// 群禁言
client.OnGroupBan += (sender, e) => { };

// 群文件上传
client.OnGroupUpload += (sender, e) => { };

// 群消息撤回
client.OnGroupRecall += (sender, e) => { };

// 群戳一戳
client.OnGroupPoke += (sender, e) => { };

// 群红包运气王
client.OnGroupLuckyKing += (sender, e) => { };

// 群荣誉变更
client.OnGroupHonor += (sender, e) => { };
```

### 好友通知事件

```csharp
// 好友添加
client.OnFriendAdd += (sender, e) => { };

// 好友消息撤回
client.OnFriendRecall += (sender, e) => { };

// 好友戳一戳
client.OnFriendPoke += (sender, e) => { };

// 客户端状态变更
client.OnClientStatus += (sender, e) => { };
```

### 请求事件

```csharp
// 好友请求
client.OnFriendRequest += async (sender, e) =>
{
    Console.WriteLine($"好友请求来自 {e.UserId}: {e.Comment}");
    // 处理请求
    var result = await client.SetFriendAddRequestAsync(e.Flag, true);
    if (!result.Success)
    {
        Console.WriteLine($"处理失败: {result.ErrorMessage}");
    }
};

// 群请求
client.OnGroupRequest += async (sender, e) =>
{
    Console.WriteLine($"群 {e.GroupId} 请求来自 {e.UserId}: {e.Comment}");
    // 处理请求
    var result = await client.SetGroupAddRequestAsync(e.Flag, true);
    if (!result.Success)
    {
        Console.WriteLine($"处理失败: {result.ErrorMessage}");
    }
};
```

---

## 消息段构建器

`MessageSegment` 类提供了构建各种消息段的静态方法：

### 文本消息

```csharp
MessageSegment.Text("普通文本消息")
```

### @某人

```csharp
// @特定用户
MessageSegment.At(123456789)

// @全体成员
MessageSegment.AtAll()
```

### 表情

```csharp
MessageSegment.Face(123)  // QQ 表情 ID
```

### 图片

```csharp
// 发送图片
MessageSegment.Image("https://example.com/image.png")
MessageSegment.Image("file:///path/to/image.png")
MessageSegment.Image("base64://...")

// 带参数
MessageSegment.Image("url", cache: true, proxy: false, timeout: 5000)
```

### 语音

```csharp
MessageSegment.Record("https://example.com/audio.mp3")
MessageSegment.Record("file:///path/to/audio.mp3", magic: true)
```

### 视频

```csharp
MessageSegment.Video("https://example.com/video.mp4")
```

### 回复

```csharp
MessageSegment.Reply(messageId)  // 回复指定消息
```

### 合并转发

```csharp
// 发送合并转发节点
MessageSegment.Node(userId, "昵称", messageSegments)

// 引用合并转发
MessageSegment.Forward("forward_id")
```

### XML/JSON 消息

```csharp
MessageSegment.Xml("<xml>...</xml>")
MessageSegment.Json("{\"content\": \"...\"}")
```

### 位置分享

```csharp
MessageSegment.Location(39.9042, 116.4074, "北京", "中国首都")
```

### 链接分享

```csharp
MessageSegment.Share("https://example.com", "标题", "描述", "https://example.com/image.png")
```

### 名片分享

```csharp
// 好友名片
MessageSegment.Contact(123456789)

// 群名片
MessageSegment.ContactGroup(987654321)
```

### 其他

```csharp
// 骰子
MessageSegment.Dice()

// 石头剪刀布
MessageSegment.Rps()

// 窗口抖动
MessageSegment.Shake()

// 戳一戳
MessageSegment.Poke(1, 123456789)

// 匿名发消息
MessageSegment.Anonymous(ignore: true)

// 音乐分享
MessageSegment.Music(123456, "qq")  // QQ 音乐
MessageSegment.Music(123456, "163") // 网易云音乐

// 自定义音乐分享
MessageSegment.MusicCustom("https://...", "https://...mp3", "标题", "描述", "封面URL")

// 文件
MessageSegment.File("file:///path/to/file.pdf", "文件名.pdf")

// 表情包
MessageSegment.Mface(1, "emoji_id", "key", "[表情]")

// Markdown
MessageSegment.Markdown("# 标题\n内容")
```

### 小程序/卡片消息

```csharp
// 小程序消息
MessageSegment.MiniApp("小程序数据")

// Ark 卡片消息
MessageSegment.Ark("卡片数据")

// 卡片分享
MessageSegment.Card("卡片数据")

// 气泡消息
MessageSegment.Bubble("气泡数据")
```

### 键盘/按钮消息

```csharp
// 键盘消息
MessageSegment.Keyboard("键盘数据")

// 单个按钮
MessageSegment.KeyboardButton("button_id", "按钮文字")

// 内联键盘
var rows = new List<List<KeyboardButtonData>>
{
    new List<KeyboardButtonData>
    {
        new KeyboardButtonData { Id = "1", Label = "按钮1" },
        new KeyboardButtonData { Id = "2", Label = "按钮2" }
    }
};
MessageSegment.InlineKeyboard(rows)
```

### 富文本消息

```csharp
// 富文本
MessageSegment.RichText("富文本内容")

// 多图文消息
MessageSegment.MultiMsg("消息数据")

// 长消息
MessageSegment.LongMsg("消息ID")

// 链接
MessageSegment.Link("https://example.com", "链接文字")
```

### 其他扩展

```csharp
// 文字转语音
MessageSegment.Tts("要转换的文字")

// 礼物
MessageSegment.Gift(userId, giftId)

// 群通知
MessageSegment.Enotify("标题", "内容")

// @带昵称
MessageSegment.AtWithNick(123456789, "昵称")

// 提及用户
MessageSegment.Mention("user_id")

// Emoji
MessageSegment.Emoji("emoji_id")
```

---

## API 方法

所有 API 方法都返回 `ApiResult` 或 `ApiResult<T>`，通过检查 `Success` 属性判断是否成功。

### 用户相关 API

#### GetLoginInfoAsync
获取当前登录账号信息。

```csharp
var result = await client.GetLoginInfoAsync();
if (result.Success)
{
    Console.WriteLine($"账号: {result.Data.UserId}, 昵称: {result.Data.Nickname}");
}
else
{
    Console.WriteLine($"错误: {result.ErrorMessage}");
    Console.WriteLine($"堆栈: {result.StackTrace}");
}
```

#### SendLikeAsync
发送点赞。

```csharp
var result = await client.SendLikeAsync(userId, 10);  // 点赞 10 次
if (!result.Success)
{
    Console.WriteLine($"点赞失败: {result.ErrorMessage}");
}
```

#### GetFriendListAsync
获取好友列表。

```csharp
var result = await client.GetFriendListAsync(noCache: false);
if (result.Success)
{
    foreach (var friend in result.Data)
    {
        Console.WriteLine($"{friend.UserId}: {friend.Nickname}");
    }
}
else
{
    Console.WriteLine($"获取好友列表失败: {result.ErrorMessage}");
}
```

#### GetFriendsWithCategoryAsync
获取分类好友列表。

```csharp
var result = await client.GetFriendsWithCategoryAsync();
if (result.Success)
{
    // 处理分类好友列表
}
```

#### DeleteFriendAsync
删除好友。

```csharp
var result = await client.DeleteFriendAsync(userId);
if (!result.Success)
{
    Console.WriteLine($"删除好友失败: {result.ErrorMessage}");
}
```

#### SetFriendAddRequestAsync
处理好友请求。

```csharp
var result = await client.SetFriendAddRequestAsync(flag, approve: true, remark: "备注名");
if (!result.Success)
{
    Console.WriteLine($"处理请求失败: {result.ErrorMessage}");
}
```

#### SetFriendRemarkAsync
设置好友备注。

```csharp
var result = await client.SetFriendRemarkAsync(userId, "新备注");
```

#### GetStrangerInfoAsync
获取陌生人信息。

```csharp
var result = await client.GetStrangerInfoAsync(userId);
if (result.Success)
{
    Console.WriteLine($"昵称: {result.Data.Nickname}");
}
```

#### SetQQAvatarAsync
设置 QQ 头像。

```csharp
var result = await client.SetQQAvatarAsync("file:///path/to/avatar.png");
```

#### FriendPokeAsync
好友戳一戳。

```csharp
var result = await client.FriendPokeAsync(userId);
```

#### GetProfileLikeAsync / GetProfileLikeMeAsync
获取点赞列表。

```csharp
var result = await client.GetProfileLikeAsync(start: 0, count: 20);
if (result.Success)
{
    // 处理点赞列表
}
```

---

### 群组相关 API

#### GetGroupListAsync
获取群列表。

```csharp
var result = await client.GetGroupListAsync(noCache: false);
if (result.Success)
{
    foreach (var group in result.Data)
    {
        Console.WriteLine($"群: {group.GroupName}");
    }
}
```

#### GetGroupInfoAsync
获取群信息。

```csharp
var result = await client.GetGroupInfoAsync(groupId);
if (result.Success)
{
    Console.WriteLine($"群名: {result.Data.GroupName}, 成员数: {result.Data.MemberCount}");
}
```

#### GetGroupMemberListAsync
获取群成员列表。

```csharp
var result = await client.GetGroupMemberListAsync(groupId);
if (result.Success)
{
    foreach (var member in result.Data)
    {
        Console.WriteLine($"{member.UserId}: {member.Nickname}");
    }
}
```

#### GetGroupMemberInfoAsync
获取群成员信息。

```csharp
var result = await client.GetGroupMemberInfoAsync(groupId, userId);
```

#### GroupPokeAsync
群戳一戳。

```csharp
var result = await client.GroupPokeAsync(groupId, userId);
```

#### GetGroupSystemMsgAsync
获取群系统消息。

```csharp
var result = await client.GetGroupSystemMsgAsync();
```

#### SetGroupAddRequestAsync
处理群请求。

```csharp
var result = await client.SetGroupAddRequestAsync(flag, approve: true, reason: "欢迎加入");
```

#### SetGroupWholeBanAsync
设置全员禁言。

```csharp
var result = await client.SetGroupWholeBanAsync(groupId, enable: true);  // 开启
var result = await client.SetGroupWholeBanAsync(groupId, enable: false); // 关闭
```

#### GetGroupShutListAsync
获取禁言列表。

```csharp
var result = await client.GetGroupShutListAsync(groupId);
```

#### SetGroupNameAsync
设置群名。

```csharp
var result = await client.SetGroupNameAsync(groupId, "新群名");
```

#### SetGroupKickAsync
踢出群成员。

```csharp
var result = await client.SetGroupKickAsync(groupId, userId, rejectAddRequest: true);
```

#### SetGroupBanAsync
禁言群成员。

```csharp
var result = await client.SetGroupBanAsync(groupId, userId, duration: 600);  // 禁言 10 分钟
var result = await client.SetGroupBanAsync(groupId, userId, duration: 0);    // 解除禁言
```

#### SetGroupAdminAsync
设置群管理员。

```csharp
var result = await client.SetGroupAdminAsync(groupId, userId, enable: true);  // 设置
var result = await client.SetGroupAdminAsync(groupId, userId, enable: false); // 取消
```

#### SetGroupCardAsync
设置群名片。

```csharp
var result = await client.SetGroupCardAsync(groupId, userId, "新名片");
```

#### SetGroupLeaveAsync
退出群。

```csharp
var result = await client.SetGroupLeaveAsync(groupId, isDismiss: false);
```

#### SetGroupSpecialTitleAsync
设置群头衔。

```csharp
var result = await client.SetGroupSpecialTitleAsync(groupId, userId, "专属头衔");
```

#### GetGroupHonorInfoAsync
获取群荣誉信息。

```csharp
var result = await client.GetGroupHonorInfoAsync(groupId, "all");
```

#### SetEssenceMsgAsync / DeleteEssenceMsgAsync
设置/删除精华消息。

```csharp
var result = await client.SetEssenceMsgAsync(messageId);
var result = await client.DeleteEssenceMsgAsync(messageId);
```

#### GetGroupAtAllRemainAsync
获取 @全体成员 剩余次数。

```csharp
var result = await client.GetGroupAtAllRemainAsync(groupId);
```

#### SendGroupNoticeAsync
发送群公告。

```csharp
var result = await client.SendGroupNoticeAsync(groupId, "公告内容", image: "url", pinned: true);
```

#### GetGroupNoticeAsync
获取群公告。

```csharp
var result = await client.GetGroupNoticeAsync(groupId);
```

#### DeleteGroupNoticeAsync
删除群公告。

```csharp
var result = await client.DeleteGroupNoticeAsync(groupId, noticeId);
```

#### SendGroupSignAsync
群签到。

```csharp
var result = await client.SendGroupSignAsync(groupId);
```

#### SetGroupRemarkAsync
设置群备注。

```csharp
var result = await client.SetGroupRemarkAsync(groupId, "群备注");
```

---

### 消息相关 API

#### SendPrivateMsgAsync
发送私聊消息。

```csharp
// 发送文本
var result = await client.SendPrivateMsgAsync(userId, "Hello!");
if (result.Success)
{
    Console.WriteLine($"消息ID: {result.Data}");
}

// 发送消息段
var segments = new List<MessageSegment.MessageSegment>
{
    MessageSegment.MessageSegment.Text("Hello "),
    MessageSegment.MessageSegment.Face(1)
};
var result = await client.SendPrivateMsgAsync(userId, segments);
```

#### SendGroupMsgAsync
发送群消息。

```csharp
var result = await client.SendGroupMsgAsync(groupId, "Hello, Group!");
if (result.Success)
{
    Console.WriteLine($"消息ID: {result.Data}");
}
```

#### SendMsgAsync
通用发送消息。

```csharp
var result = await client.SendMsgAsync("private", userId, "Hello!");
var result = await client.SendMsgAsync("group", groupId, "Hello!");
```

#### DeleteMsgAsync
撤回消息。

```csharp
var result = await client.DeleteMsgAsync(messageId);
```

#### GetMsgAsync
获取消息。

```csharp
var result = await client.GetMsgAsync(messageId);
if (result.Success)
{
    Console.WriteLine($"消息内容: {result.Data.PlainText}");
}
```

#### GetForwardMsgAsync
获取转发消息。

```csharp
var result = await client.GetForwardMsgAsync(forwardId);
```

#### MarkMsgAsReadAsync
标记消息已读。

```csharp
var result = await client.MarkMsgAsReadAsync(messageId);
```

#### SetMsgEmojiLikeAsync
设置消息表情回应。

```csharp
// 添加表情回应（默认）
var result = await client.SetMsgEmojiLikeAsync(messageId, "emoji_id");

// 移除表情回应
var result = await client.SetMsgEmojiLikeAsync(messageId, "emoji_id", set: false);
```

| 参数 | 类型 | 描述 |
|------|------|------|
| `messageId` | `long` | 消息 ID |
| `emojiId` | `string` | 表情 ID |
| `set` | `bool` | true: 添加表情, false: 移除表情 (默认: true) |

#### SendPrivateForwardMsgAsync
发送私聊合并转发。

```csharp
var nodes = new List<ForwardNode>
{
    new ForwardNode { UserId = 123, Nickname = "用户A", Content = "消息1" },
    new ForwardNode { UserId = 456, Nickname = "用户B", Content = "消息2" }
};
var result = await client.SendPrivateForwardMsgAsync(userId, nodes);
```

#### SendGroupForwardMsgAsync
发送群合并转发。

```csharp
var result = await client.SendGroupForwardMsgAsync(groupId, nodes);
```

#### GetGroupMsgHistoryAsync
获取群消息历史。

```csharp
var result = await client.GetGroupMsgHistoryAsync(groupId, count: 50);
```

#### GetPrivateMsgHistoryAsync
获取私聊消息历史。

```csharp
var result = await client.GetPrivateMsgHistoryAsync(userId, count: 50);
```

#### SendGroupAiRecordAsync
发送群 AI 语音。

```csharp
var result = await client.SendGroupAiRecordAsync(groupId, "要转换的文本", "character_id");
```

#### GetAiCharactersAsync
获取 AI 语音角色列表。

```csharp
var result = await client.GetAiCharactersAsync(groupId);
```

---

### 文件相关 API

#### GetGroupFilesAsync
获取群文件列表。

```csharp
var result = await client.GetGroupFilesAsync(groupId);
if (result.Success)
{
    foreach (var file in result.Data.Files)
    {
        Console.WriteLine($"文件: {file.FileName}");
    }
}
```

#### GetGroupFileUrlAsync
获取群文件下载链接。

```csharp
var result = await client.GetGroupFileUrlAsync(groupId, fileId, busid);
if (result.Success)
{
    Console.WriteLine($"下载链接: {result.Data.Url}");
}
```

#### UploadGroupFileAsync
上传群文件。

```csharp
var result = await client.UploadGroupFileAsync(groupId, "file:///path/to/file.pdf", "文件名.pdf", "folder_id");
```

#### DeleteGroupFileAsync
删除群文件。

```csharp
var result = await client.DeleteGroupFileAsync(groupId, fileId, busid);
```

#### CreateGroupFileFolderAsync
创建群文件文件夹。

```csharp
var result = await client.CreateGroupFileFolderAsync(groupId, "文件夹名", parentId);
```

#### DeleteGroupFolderAsync
删除群文件文件夹。

```csharp
var result = await client.DeleteGroupFolderAsync(groupId, folderId);
```

#### GetGroupSpaceInfoAsync
获取群空间信息。

```csharp
var result = await client.GetGroupSpaceInfoAsync(groupId);
if (result.Success)
{
    Console.WriteLine($"已用: {result.Data.UsedSize}, 总量: {result.Data.TotalSize}");
}
```

#### UploadPrivateFileAsync
上传私聊文件。

```csharp
var result = await client.UploadPrivateFileAsync(userId, "file:///path/to/file.pdf", "文件名.pdf");
```

---

### 系统相关 API

#### GetVersionInfoAsync
获取版本信息。

```csharp
var result = await client.GetVersionInfoAsync();
if (result.Success)
{
    Console.WriteLine($"版本: {result.Data.Version}, 实现: {result.Data.Implementation}");
}
```

#### GetStatusAsync
获取运行状态。

```csharp
var result = await client.GetStatusAsync();
if (result.Success)
{
    Console.WriteLine($"在线: {result.Data.Online}");
}
```

#### CanSendImageAsync
检查是否可以发送图片。

```csharp
var result = await client.CanSendImageAsync();
if (result.Success)
{
    Console.WriteLine($"可以发送图片: {result.Data}");
}
```

#### CanSendRecordAsync
检查是否可以发送语音。

```csharp
var result = await client.CanSendRecordAsync();
if (result.Success)
{
    Console.WriteLine($"可以发送语音: {result.Data}");
}
```

#### SetRestartAsync
重启 OneBot 实现。

```csharp
var result = await client.SetRestartAsync(delay: 5000);  // 5 秒后重启
```

#### CleanCacheAsync
清理缓存。

```csharp
var result = await client.CleanCacheAsync();
```

#### ReloadEventFilterAsync
重载事件过滤器。

```csharp
var result = await client.ReloadEventFilterAsync();
```

#### DownloadFileAsync
下载文件。

```csharp
var result = await client.DownloadFileAsync("https://example.com/file.pdf");
```

#### CheckUrlSafelyAsync
检查 URL 安全性。

```csharp
var result = await client.CheckUrlSafelyAsync("https://example.com");
```

#### GetOnlineClientsAsync
获取在线客户端。

```csharp
var result = await client.GetOnlineClientsAsync();
if (result.Success)
{
    foreach (var client in result.Data)
    {
        Console.WriteLine($"客户端: {client.ClientId}");
    }
}
```

#### OcrImageAsync
OCR 图片识别。

```csharp
var result = await client.OcrImageAsync("https://example.com/image.png");
if (result.Success)
{
    foreach (var text in result.Data.Texts)
    {
        Console.WriteLine($"识别文本: {text.Text}, 置信度: {text.Confidence}");
    }
}
```

#### GetWordSlicesAsync
分词。

```csharp
var result = await client.GetWordSlicesAsync("要分词的文本");
if (result.Success)
{
    foreach (var slice in result.Data.Slices)
    {
        Console.WriteLine(slice);
    }
}
```

#### SetAccountProfileAsync
设置个人资料。

```csharp
var result = await client.SetAccountProfileAsync(nickname: "新昵称", personalNote: "个性签名");
```

#### GetUnidirectionalFriendListAsync
获取单向好友列表。

```csharp
var result = await client.GetUnidirectionalFriendListAsync();
```

#### DeleteUnidirectionalFriendAsync
删除单向好友。

```csharp
var result = await client.DeleteUnidirectionalFriendAsync(userId);
```

---

## 数据模型

所有数据模型都在 `OneBotLib.Models` 命名空间中，使用时需要：

```csharp
using OneBotLib.Models;
```

### AccountInfo
账号信息模型。

| 属性 | 类型 | 描述 |
|------|------|------|
| `UserId` | `long` | 用户 ID |
| `Nickname` | `string` | 昵称 |
| `Sign` | `string` | 个性签名 |
| `Sex` | `string` | 性别 |
| `Age` | `int` | 年龄 |
| `Level` | `int` | 等级 |
| `LoginDays` | `int` | 登录天数 |

### FriendInfo
好友信息模型。

| 属性 | 类型 | 描述 |
|------|------|------|
| `UserId` | `long` | 用户 ID |
| `Nickname` | `string` | 昵称 |
| `Remark` | `string` | 备注 |
| `Sex` | `string` | 性别 |
| `Age` | `int` | 年龄 |

### GroupInfo
群组信息模型。

| 属性 | 类型 | 描述 |
|------|------|------|
| `GroupId` | `long` | 群号 |
| `GroupName` | `string` | 群名 |
| `GroupMemo` | `string` | 群备注 |
| `MemberCount` | `int` | 成员数 |
| `MaxMemberCount` | `int` | 最大成员数 |
| `GroupCreateTime` | `long` | 创建时间 |
| `GroupLevel` | `int` | 群等级 |
| `OwnerId` | `long` | 群主 ID |

### GroupMemberInfo
群成员信息模型。

| 属性 | 类型 | 描述 |
|------|------|------|
| `GroupId` | `long` | 群号 |
| `UserId` | `long` | 用户 ID |
| `Nickname` | `string` | 昵称 |
| `Card` | `string` | 群名片 |
| `Role` | `string` | 角色 (owner/admin/member) |
| `JoinTime` | `long` | 入群时间 |
| `LastSentTime` | `long` | 最后发言时间 |
| `Level` | `string` | 群等级 |
| `Title` | `string` | 群头衔 |

### MessageObject
消息对象模型。

| 属性 | 类型 | 描述 |
|------|------|------|
| `MessageId` | `long` | 消息 ID |
| `UserId` | `long?` | 发送者 ID |
| `GroupId` | `long?` | 群号（群消息时） |
| `MessageType` | `string` | 消息类型 |
| `PlainText` | `string` | 纯文本内容 |
| `RawMessage` | `string` | 原始消息 |
| `Sender` | `SenderInfo` | 发送者信息 |
| `MessageSegments` | `List<MessageSegmentData>` | 消息段列表 |
| `Time` | `long` | 时间戳 |

### SenderInfo
发送者信息模型。

| 属性 | 类型 | 描述 |
|------|------|------|
| `UserId` | `long` | 用户 ID |
| `Nickname` | `string` | 昵称 |
| `Card` | `string` | 群名片 |
| `Sex` | `string` | 性别 |
| `Age` | `int` | 年龄 |
| `Area` | `string` | 地区 |
| `Level` | `string` | 等级 |
| `Role` | `string` | 角色 |
| `Title` | `string` | 头衔 |

---

## 完整示例

### 机器人示例

```csharp
using OneBotLib;
using OneBotLib.Events;
using OneBotLib.Models;
using OneBotLib.MessageSegment;

public class MyBot
{
    private OneBotClient _client = new();

    public async Task StartAsync()
    {
        _client.OnGroupMessage += OnGroupMessage;
        _client.OnPrivateMessage += OnPrivateMessage;
        _client.OnFriendRequest += OnFriendRequest;
        _client.OnGroupRequest += OnGroupRequest;

        await _client.ConnectAsync("ws://127.0.0.1:3001", "your_token");
        Console.WriteLine("Bot started!");
    }

    private async Task OnGroupMessage(object? sender, GroupMessageEventArgs e)
    {
        var msg = e.Message;

        if (msg.PlainText.StartsWith("/hello"))
        {
            var result = await _client.SendGroupMsgAsync(msg.GroupId!.Value, $"你好，{msg.Sender.Nickname}！");
            if (!result.Success)
            {
                Console.WriteLine($"发送失败: {result.ErrorMessage}");
            }
        }
        else if (msg.PlainText.StartsWith("/image"))
        {
            var segments = new List<MessageSegment.MessageSegment>
            {
                MessageSegment.MessageSegment.At(msg.UserId!.Value),
                MessageSegment.MessageSegment.Text(" 这是一张图片："),
                MessageSegment.MessageSegment.Image("https://example.com/image.png")
            };
            var result = await _client.SendGroupMsgAsync(msg.GroupId!.Value, segments);
            if (!result.Success)
            {
                Console.WriteLine($"发送失败: {result.ErrorMessage}");
            }
        }
        else if (msg.PlainText.StartsWith("/ban "))
        {
            if (msg.Sender.Role == "owner" || msg.Sender.Role == "admin")
            {
                if (long.TryParse(msg.PlainText[5..], out var targetId))
                {
                    var result = await _client.SetGroupBanAsync(msg.GroupId!.Value, targetId, 600);
                    if (result.Success)
                    {
                        await _client.SendGroupMsgAsync(msg.GroupId!.Value, "已禁言 10 分钟");
                    }
                    else
                    {
                        Console.WriteLine($"禁言失败: {result.ErrorMessage}");
                    }
                }
            }
        }
    }

    private async Task OnPrivateMessage(object? sender, PrivateMessageEventArgs e)
    {
        var msg = e.Message;
        var result = await _client.SendPrivateMsgAsync(msg.UserId!.Value, $"收到你的消息：{msg.PlainText}");
        if (!result.Success)
        {
            Console.WriteLine($"回复失败: {result.ErrorMessage}");
        }
    }

    private async Task OnFriendRequest(object? sender, FriendRequestEventArgs e)
    {
        Console.WriteLine($"好友请求: {e.UserId} - {e.Comment}");
        var result = await _client.SetFriendAddRequestAsync(e.Flag, true);
        if (!result.Success)
        {
            Console.WriteLine($"处理好友请求失败: {result.ErrorMessage}");
        }
    }

    private async Task OnGroupRequest(object? sender, GroupRequestEventArgs e)
    {
        Console.WriteLine($"群请求: 群{e.GroupId} - 用户{e.UserId}");
        var result = await _client.SetGroupAddRequestAsync(e.Flag, true);
        if (!result.Success)
        {
            Console.WriteLine($"处理群请求失败: {result.ErrorMessage}");
        }
    }

    public async Task StopAsync()
    {
        await _client.CloseAsync();
    }
}
```

---

## 错误处理

所有 API 方法返回 `ApiResult` 或 `ApiResult<T>`，通过检查 `Success` 属性判断是否成功：

```csharp
var result = await client.SendGroupMsgAsync(groupId, "Hello!");

if (result.Success)
{
    Console.WriteLine($"消息发送成功，消息ID: {result.Data}");
}
else
{
    Console.WriteLine($"发送失败: {result.ErrorMessage}");
    Console.WriteLine($"堆栈跟踪: {result.StackTrace}");
}
```

对于无返回数据的 API：

```csharp
var result = await client.SetGroupBanAsync(groupId, userId, 600);

if (result.Success)
{
    Console.WriteLine("操作成功");
}
else
{
    Console.WriteLine($"操作失败: {result.ErrorMessage}");
    Console.WriteLine($"堆栈跟踪: {result.StackTrace}");
}
```

---

## 注意事项

1. 确保在调用 API 前已成功连接到 OneBot 服务
2. 消息段列表需要转换为 `List<MessageSegment.MessageSegment>` 类型
3. 所有异步方法都支持 `await` 等待
4. 事件处理函数应该是异步的以避免阻塞
5. 使用 `using` 或手动调用 `DisposeAsync()` 释放资源
6. 所有 API 调用都返回 `ApiResult`，请检查 `Success` 属性以确认操作是否成功
