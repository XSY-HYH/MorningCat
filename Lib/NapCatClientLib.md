# NapCatClient 类库 API 文档

## 概述

NapCatClient 是一个用于连接 NapCat WebSocket 服务的 C# 客户端库，支持消息收发、群成员管理、好友列表、账号信息等功能。消息文本**不处理 CQ 码**，直接返回原始内容。

## 命名空间

```csharp
using NapCatClientLib;
```

## 快速开始

```csharp
using NapCatClientLib;

var client = new NapCatClient();

// 设置回调
client.OnMessage = async (msg) =>
{
    Console.WriteLine($"收到: {msg.PlainText}");
};

// 连接
await client.ConnectAsync("ws://127.0.0.1:8080", "your_token");

// 获取账号信息
var account = await client.GetAccountInfoAsync();
Console.WriteLine($"当前账号: {account}");

// 获取群聊列表
var groups = await client.GetGroupListAsync();
foreach (var group in groups)
{
    Console.WriteLine($"群: {group.GroupName}");
}
```

---

## 数据模型

### AccountInfo - 账号信息

| 属性 | 类型 | 说明 |
|------|------|------|
| UserId | long | QQ号 |
| Nickname | string | 昵称 |
| Sex | string | 性别 |
| Age | int | 年龄 |
| Qid | string | QID |
| Level | int | 等级 |
| LoginDays | int | 登录天数 |

### GroupInfo - 群聊信息

| 属性 | 类型 | 说明 |
|------|------|------|
| GroupId | long | 群号 |
| GroupName | string | 群名称 |
| MemberCount | int | 成员数量 |
| MaxMemberCount | int | 最大成员数 |
| GroupUin | long | 群UIN |
| GroupCreateTime | long | 创建时间 |
| GroupLevel | int | 群等级 |

### FriendInfo - 好友信息

| 属性 | 类型 | 说明 |
|------|------|------|
| UserId | long | QQ号 |
| Nickname | string | 昵称 |
| Remark | string | 备注名 |
| DisplayName | string | 显示名称（优先备注） |

### GroupMemberInfo - 群成员信息

| 属性 | 类型 | 说明 |
|------|------|------|
| UserId | long | QQ号 |
| Nickname | string | 昵称 |
| Card | string | 群名片 |
| Level | string | 等级 |
| LastSentTime | long | 最后发言时间 |
| JoinTime | long | 入群时间 |
| Role | string | 角色：`owner` / `admin` / `member` |
| UserRoleDesc | string | 角色描述（只读） |
| DisplayName | string | 显示名称（优先群名片） |

### MessageObject - 消息对象

| 属性 | 类型 | 说明 |
|------|------|------|
| SelfId | long | 机器人自身QQ号 |
| UserId | long? | 发送者QQ号 |
| GroupId | long? | 群号（群消息时有值） |
| MessageId | long | 消息ID |
| Time | long | 消息时间戳 |
| MessageType | string | 消息类型：`private` / `group` |
| SubType | string | 消息子类型 |
| RawMessage | string | 原始消息文本 |
| Font | string | 字体 |
| Sender | SenderInfo | 发送者详细信息 |
| Anonymous | AnonymousInfo | 匿名信息 |
| MessageSegments | List\<MessageSegmentObject\> | 消息段列表 |
| **PlainText** | string | **原始消息文本（包含CQ码，不处理）** |
| GroupName | string | 群名称 |
| SenderName | string | 发送者显示名称 |
| SenderNickname | string | 发送者昵称 |
| SenderCard | string | 发送者群名片 |
| SenderRole | string | 发送者角色 |
| IsGroupMessage | bool | 是否是群消息 |
| IsPrivateMessage | bool | 是否是私聊消息 |
| IsAnonymous | bool | 是否是匿名消息 |

### SenderInfo - 发送者信息

| 属性 | 类型 | 说明 |
|------|------|------|
| UserId | long | QQ号 |
| Nickname | string | 昵称 |
| Card | string | 群名片 |
| Sex | string | 性别 |
| Age | int | 年龄 |
| Area | string | 地区 |
| Level | string | 等级 |
| Role | string | 角色 |
| Title | string | 头衔 |
| DisplayName | string | 显示名称（优先群名片） |

### MessageSegment - 消息段构建器

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `Text(string text)` | MessageSegment | 创建文本消息段 |
| `Image(string file)` | MessageSegment | 创建图片消息段 |
| `At(long userId)` | MessageSegment | 创建@消息段 |
| `Face(int faceId)` | MessageSegment | 创建表情消息段 |
| `Reply(long msgId)` | MessageSegment | 创建回复消息段 |

---

## 事件回调

| 事件 | 类型 | 说明 |
|------|------|------|
| `OnMessage` | Func\<MessageObject, Task\>? | 消息接收回调 |
| `OnGroupMemberChange` | Func\<GroupMemberChangeObject, Task\>? | 群成员变动回调 |
| `OnLifecycleEvent` | Func\<LifecycleEventObject, Task\>? | 生命周期事件回调 |
| `OnHeartbeat` | Func\<HeartbeatEventObject, Task\>? | 心跳事件回调 |
| `OnFriendStatusChange` | Func\<FriendStatusChangeObject, Task\>? | 好友状态变更回调 |
| `OnGroupStatusChange` | Func\<GroupStatusChangeObject, Task\>? | 群聊状态变更回调 |
| `OnAccountInfoChange` | Func\<AccountInfoChangeObject, Task\>? | 账号信息变更回调 |

### 回调数据类

#### FriendStatusChangeObject - 好友状态变更

| 属性 | 类型 | 说明 |
|------|------|------|
| Time | long | 时间戳 |
| SelfId | long | 机器人QQ号 |
| PostType | string | 类型：`notice` |
| NoticeType | string | 通知类型 |
| UserId | long | 好友QQ号 |
| Status | string | 状态：`online` / `offline` / `added` / `recalled` |
| Client | string | 客户端类型 |

#### GroupStatusChangeObject - 群聊状态变更

| 属性 | 类型 | 说明 |
|------|------|------|
| Time | long | 时间戳 |
| SelfId | long | 机器人QQ号 |
| PostType | string | 类型：`notice` |
| NoticeType | string | 通知类型 |
| GroupId | long | 群号 |
| SubType | string | 子类型：`create` / `update` / `dissolve` / `recall` |
| GroupName | string | 群名称 |

#### AccountInfoChangeObject - 账号信息变更

| 属性 | 类型 | 说明 |
|------|------|------|
| Time | long | 时间戳 |
| SelfId | long | 机器人QQ号 |
| PostType | string | 类型：`notice` |
| NoticeType | string | 通知类型 |
| Nickname | string | 新昵称 |
| OldNickname | string | 旧昵称 |
| Signature | string | 新签名 |
| OldSignature | string | 旧签名 |

#### GroupMemberChangeObject - 群成员变动

| 属性 | 类型 | 说明 |
|------|------|------|
| Time | long | 时间戳 |
| SelfId | long | 机器人QQ号 |
| PostType | string | 类型：`notice` |
| NoticeType | string | 通知类型 |
| GroupId | long | 群号 |
| UserId | long | 变动用户QQ号 |
| OperatorId | long | 操作者QQ号 |
| SubType | string | 子类型：`join` / `leave` / `kick` |

#### LifecycleEventObject - 生命周期事件

| 属性 | 类型 | 说明 |
|------|------|------|
| Time | long | 时间戳 |
| SelfId | long | 机器人QQ号 |
| PostType | string | 类型：`meta_event` |
| MetaEventType | string | 元事件类型：`lifecycle` |
| SubType | string | 子类型：`connect` / `disconnect` |

#### HeartbeatEventObject - 心跳事件

| 属性 | 类型 | 说明 |
|------|------|------|
| Time | long | 时间戳 |
| SelfId | long | 机器人QQ号 |
| PostType | string | 类型：`meta_event` |
| MetaEventType | string | 元事件类型：`heartbeat` |
| Interval | int | 心跳间隔（毫秒） |
| Online | bool | 是否在线 |

---

## API 方法

### 连接相关

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `ConnectAsync(string wsUrl, string token)` | Task | 异步连接WebSocket |
| `ConnectSync(string wsUrl, string token, int timeoutSeconds = 5)` | bool | 同步连接WebSocket |
| `CloseAsync()` | Task | 关闭连接 |
| `DisconnectAsync()` | Task | 断开连接 |
| `DisposeAsync()` | ValueTask | 释放资源 |

### 信息获取 API

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `GetAccountInfoAsync()` | Task\<AccountInfo\> | 获取当前登录账号基本信息 |
| `GetGroupListAsync()` | Task\<List\<GroupInfo\>\> | 获取群聊列表 |
| `GetFriendListAsync()` | Task\<List\<FriendInfo\>\> | 获取好友列表 |
| `GetGroupBasicInfoAsync(long groupId)` | Task\<GroupInfo\> | 获取群基本信息 |
| `GetAllMembersAsync(long groupId)` | Task\<List\<GroupMemberInfo\>\> | 获取群成员列表 |

### 消息发送 API

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `SendPrivateMessageAsync(long userId, string message)` | Task\<SendMessageResult\> | 发送私聊文本消息 |
| `SendPrivateMessageAsync(long userId, object message)` | Task\<SendMessageResult\> | 发送私聊消息（支持消息段） |
| `SendGroupMessageAsync(long groupId, string message)` | Task\<SendMessageResult\> | 发送群聊文本消息 |
| `SendGroupMessageAsync(long groupId, object message)` | Task\<SendMessageResult\> | 发送群聊消息（支持消息段） |

### 属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `IsConnected` | bool | 是否已连接 |
| `CurrentAccountInfo` | AccountInfo? | 当前账号信息（缓存） |

---

## 使用示例

### 1. 基础连接和获取信息

```csharp
var client = new NapCatClient();

await client.ConnectAsync("ws://127.0.0.1:8080", "token");

// 获取账号信息
var account = await client.GetAccountInfoAsync();
Console.WriteLine($"当前账号: {account.Nickname} ({account.UserId})");

// 获取群聊列表
var groups = await client.GetGroupListAsync();
foreach (var group in groups)
{
    Console.WriteLine($"群: {group.GroupName} - {group.MemberCount}人");
}

// 获取好友列表
var friends = await client.GetFriendListAsync();
foreach (var friend in friends)
{
    Console.WriteLine($"好友: {friend.DisplayName}");
}
```

### 2. 设置事件回调

```csharp
client.OnMessage = async (msg) =>
{
    Console.WriteLine($"[{msg.SenderName}] {msg.PlainText}");
};

client.OnFriendStatusChange = async (e) =>
{
    Console.WriteLine($"好友 {e.UserId} 状态变更: {e.Status}");
};

client.OnGroupStatusChange = async (e) =>
{
    Console.WriteLine($"群 {e.GroupId} 状态变更: {e.SubType}");
};

client.OnGroupMemberChange = async (e) =>
{
    string action = e.NoticeType == "group_increase" ? "加入" : "退出";
    Console.WriteLine($"[群成员] {action}: {e.UserId} -> {e.GroupId}");
};

client.OnLifecycleEvent = async (e) =>
{
    Console.WriteLine($"[生命周期] {e.SubType}");
};

client.OnHeartbeat = async (e) =>
{
    Console.WriteLine($"[心跳] {e.Interval}ms, 在线: {e.Online}");
};
```

### 3. 发送消息

```csharp
// 发送私聊
await client.SendPrivateMessageAsync(123456789, "你好");

// 发送群聊
await client.SendGroupMessageAsync(987654321, "大家好");

// 发送富文本
var segments = new List<MessageSegment>
{
    MessageSegment.At(123456789),
    MessageSegment.Text(" 你好 "),
    MessageSegment.Face(14)
};
await client.SendGroupMessageAsync(987654321, segments);
```

### 4. 获取群成员

```csharp
var members = await client.GetAllMembersAsync(987654321);
foreach (var member in members)
{
    Console.WriteLine($"{member.DisplayName} - {member.UserRoleDesc}");
}
```

### 5. 完整示例

```csharp
using NapCatClientLib;

var client = new NapCatClient();

// 设置回调
client.OnMessage = async (msg) =>
{
    if (msg.PlainText.Contains("你好"))
    {
        await client.SendGroupMessageAsync(msg.GroupId.Value, "你好！");
    }
};

client.OnGroupMemberChange = async (change) =>
{
    var group = await client.GetGroupBasicInfoAsync(change.GroupId);
    Console.WriteLine($"[{group.GroupName}] 成员变动: {change.UserId}");
};

// 连接
await client.ConnectAsync("ws://127.0.0.1:8080", "token");

// 获取账号信息
var account = await client.GetAccountInfoAsync();
Console.WriteLine($"机器人 {account.Nickname} 已启动");

// 获取群聊列表
var groups = await client.GetGroupListAsync();
Console.WriteLine($"共 {groups.Count} 个群聊");

Console.WriteLine("按任意键退出...");
Console.ReadKey();

await client.DisconnectAsync();
```

---

## PlainText 说明

`PlainText` 直接返回 `RawMessage`，**不处理任何 CQ 码**：

| 原始消息 | PlainText 内容 |
|----------|----------------|
| 普通文本 | `你好` |
| @某人 | `[CQ:at,qq=123456]` |
| 图片 | `[CQ:image,file=xxx.jpg]` |
| 表情 | `[CQ:face,id=14]` |

---

## 消息段类型

| Type | 说明 | Data字段 |
|------|------|----------|
| text | 文本 | `text` |
| at | @某人 | `qq` |
| face | QQ表情 | `id` |
| image | 图片 | `file`, `url` |
| reply | 回复 | `id` |
| record | 语音 | `file` |
| video | 视频 | `file` |
| file | 文件 | `file` |

---

## 错误处理

```csharp
try
{
    await client.ConnectAsync(wsUrl, token);
    var account = await client.GetAccountInfoAsync();
}
catch (WebSocketException ex)
{
    Console.WriteLine($"WebSocket错误: {ex.Message}");
}
catch (TimeoutException ex)
{
    Console.WriteLine($"超时: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
}
```

---

## 注意事项

1. **PlainText 不处理 CQ 码**：需要自行解析或使用 `MessageSegments`
2. **异步方法**：大多数方法都是异步的，需要使用 `await`
3. **回调异常**：回调中的异常会被内部捕获，不会导致客户端崩溃
4. **线程安全**：客户端内部已处理线程安全
5. **资源释放**：使用完毕后调用 `DisconnectAsync()` 或 `DisposeAsync()`
6. **群名称缓存**：群名称会自动缓存，避免重复请求

---

## 版本信息

| 项目 | 内容 |
|------|------|
| 版本 | 1.3.0 |
| 命名空间 | NapCatClientLib |
| 依赖 | .NET Standard 2.1 / .NET 6.0+ |

---

**更新日期：** 2026-04-16