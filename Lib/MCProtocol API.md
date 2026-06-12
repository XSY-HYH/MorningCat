# MCProtocol API 文档

## 概述

MCProtocol 是一个用于 Minecraft 1.21.1 协议封装的 C# 类库，提供模块化、可扩展的网络通信功能。

## 安装

```bash
dotnet add package MCProtocol
```

## 快速开始

### 1. 查询服务器信息

```csharp
using MCProtocol.Game;
using MCProtocol.Events;

using var client = new MinecraftClient();

// 查询服务器信息
var serverInfo = await client.QueryServerAsync("localhost", 25565);
Console.WriteLine($"服务器版本: {serverInfo.Version.Name}");
Console.WriteLine($"在线玩家: {serverInfo.Players.Online}/{serverInfo.Players.Max}");
Console.WriteLine($"服务器描述: {serverInfo.Description}");
```

### 2. 离线登录

```csharp
using var client = new MinecraftClient();

// 订阅事件
client.Events.Subscribe<LoginSuccessEvent>(e =>
{
    Console.WriteLine($"登录成功! UUID: {e.Uuid}, 用户名: {e.Username}");
});

client.Events.Subscribe<ConnectionStateChangedEvent>(e =>
{
    Console.WriteLine($"状态变更: {e.OldState} -> {e.NewState}");
});

client.Events.Subscribe<ErrorEvent>(e =>
{
    Console.WriteLine($"错误: {e.Message}");
});

// 连接并登录
await client.ConnectAsync("localhost", 25565);
client.Login("YourUsername");
```

### 3. 发送聊天消息

```csharp
// 发送聊天消息
client.SendChat("Hello, World!");

// 发送命令
client.SendCommand("help");
```

### 4. 移动控制

```csharp
// 设置移动状态
client.SetMovement(
    forward: 1.0f,    // 前进
    sideways: 0.0f,   // 横向
    jumping: false,   // 跳跃
    sneaking: false   // 潜行
);

// 设置视角
client.SetRotation(yaw: 90.0f, pitch: 0.0f);
```

## 核心 API

### MinecraftClient

主客户端类，管理与服务器的连接和通信。

#### 属性

| 属性 | 类型 | 描述 |
|------|------|------|
| `ProtocolVersion` | `int` | 协议版本号 (默认: 767 for 1.21.1) |
| `GameVersion` | `string` | 游戏版本字符串 |
| `State` | `ConnectionState` | 当前连接状态 |
| `Events` | `EventBus` | 事件总线 |
| `IsConnected` | `bool` | 是否已连接 |
| `CompressionThreshold` | `int` | 压缩阈值 |
| `Player` | `PlayerInfo` | 玩家信息 |

#### 方法

| 方法 | 描述 |
|------|------|
| `QueryServerAsync(host, port)` | 异步查询服务器信息 |
| `ConnectAsync(host, port)` | 异步连接服务器 |
| `Login(username)` | 发送登录请求 |
| `SendChat(message)` | 发送聊天消息 |
| `SendCommand(command)` | 发送命令 |
| `SetMovement(forward, sideways, jumping, sneaking)` | 设置移动状态 |
| `SetRotation(yaw, pitch)` | 设置视角 |
| `Disconnect()` | 断开连接 |
| `Dispose()` | 释放资源 |

### ConnectionState 枚举

连接状态枚举值：

| 值 | 描述 |
|------|------|
| `Handshaking` | 握手阶段 |
| `Status` | 状态查询阶段 |
| `Login` | 登录阶段 |
| `Configuration` | 配置阶段 |
| `Play` | 游戏阶段 |

### PlayerInfo

玩家信息类。

#### 属性

| 属性 | 类型 | 描述 |
|------|------|------|
| `Username` | `string` | 玩家用户名 |
| `Uuid` | `Guid` | 玩家 UUID |
| `EntityId` | `int` | 实体 ID |
| `X`, `Y`, `Z` | `double` | 玩家坐标 |
| `Yaw`, `Pitch` | `float` | 玩家视角 |
| `ViewDistance` | `int` | 视距 |
| `OnGround` | `bool` | 是否在地面 |
| `IsSprinting` | `bool` | 是否正在疾跑 |
| `IsSneaking` | `bool` | 是否正在潜行 |

## 事件系统

### 订阅事件

```csharp
client.Events.Subscribe<EventType>(e =>
{
    // 处理事件
});
```

### 可用事件

#### LoginSuccessEvent

登录成功事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `Uuid` | `Guid` | 玩家 UUID |
| `Username` | `string` | 玩家用户名 |

#### ConnectionStateChangedEvent

连接状态变更事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `OldState` | `ConnectionState` | 旧状态 |
| `NewState` | `ConnectionState` | 新状态 |

#### ChatReceivedEvent

聊天消息接收事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `Sender` | `string` | 发送者名称 |
| `SenderUuid` | `Guid` | 发送者 UUID |
| `Message` | `string` | 消息内容 |
| `IsSystem` | `bool` | 是否为系统消息 |

#### PlayerPositionUpdateEvent

玩家位置更新事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `X`, `Y`, `Z` | `double` | 新坐标 |
| `Yaw`, `Pitch` | `float` | 新视角 |
| `TeleportId` | `int` | 传送 ID |

#### ChunkLoadedEvent

区块加载事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `ChunkX`, `ChunkZ` | `int` | 区块坐标 |
| `ChunkData` | `byte[]` | 区块数据 |

#### ChunkUnloadedEvent

区块卸载事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `ChunkX`, `ChunkZ` | `int` | 区块坐标 |

#### BlockUpdateEvent

方块更新事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `X`, `Y`, `Z` | `int` | 方块坐标 |
| `BlockId` | `int` | 方块 ID |

#### DisconnectEvent

断开连接事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `Reason` | `string` | 断开原因 |

#### ErrorEvent

错误事件。

| 属性 | 类型 | 描述 |
|------|------|------|
| `Message` | `string` | 错误消息 |
| `Exception` | `Exception` | 异常对象 |

## ServerInfo

服务器信息类。

### 属性

| 属性 | 类型 | 描述 |
|------|------|------|
| `Version` | `VersionInfo` | 版本信息 |
| `Players` | `PlayersInfo` | 玩家信息 |
| `Description` | `string` | 服务器描述 (MOTD) |
| `Favicon` | `string?` | 服务器图标 (Base64) |
| `EnforcesSecureChat` | `bool` | 是否强制安全聊天 |

### VersionInfo

| 属性 | 类型 | 描述 |
|------|------|------|
| `Name` | `string` | 版本名称 |
| `Protocol` | `int` | 协议版本号 |

### PlayersInfo

| 属性 | 类型 | 描述 |
|------|------|------|
| `Max` | `int` | 最大玩家数 |
| `Online` | `int` | 在线玩家数 |
| `Sample` | `PlayerSample[]` | 玩家示例列表 |

## 协议抽象层

MCProtocol 支持多版本协议，通过抽象层实现版本无关的接口。

### IProtocolHandler

协议处理器接口，提供版本特定的功能。

```csharp
public interface IProtocolHandler
{
    int ProtocolVersion { get; }
    string GameVersion { get; }
    ILoginHandler Login { get; }
    IStatusHandler Status { get; }
    IChatHandler Chat { get; }
    IMovementHandler Movement { get; }
}
```

### 创建自定义协议处理器

```csharp
using MCProtocol.Protocol.Abstract;

public class CustomProtocolHandler : IProtocolHandler
{
    public int ProtocolVersion => 768; // 自定义版本
    public string GameVersion => "1.21.2";
    
    public ILoginHandler Login { get; }
    public IStatusHandler Status { get; }
    public IChatHandler Chat { get; }
    public IMovementHandler Movement { get; }
    
    // ... 实现细节
}

// 注册协议处理器
ProtocolFactory.Register(768, () => new CustomProtocolHandler());

// 使用自定义协议
var client = new MinecraftClient(768);
```

## 数据类型

### VarInt / VarLong

Minecraft 协议使用的变长整数类型。

```csharp
// 读取 VarInt
int value = VarInt.Read(stream);

// 写入 VarInt
VarInt.Write(stream, value);

// 获取 VarInt 字节大小
int size = VarInt.GetSize(value);
```

### Position

方块位置类型，使用单个 long 值存储。

```csharp
var pos = new Position(x, y, z);
long encoded = pos.ToLong();
var decoded = Position.FromLong(encoded);
```

### Angle

角度类型，使用单个字节存储 (0-255)。

```csharp
var angle = new Angle(180.0f); // 转换为字节表示
float degrees = angle.Degrees; // 转换回度数
```

## 网络层

### TcpConnection

底层 TCP 连接类。

```csharp
var connection = new TcpConnection();
connection.PacketReceived += (sender, packet) => { /* 处理数据包 */ };
connection.ErrorOccurred += (sender, ex) => { /* 处理错误 */ };
connection.Disconnected += (sender, e) => { /* 处理断开 */ };

await connection.ConnectAsync("localhost", 25565);
connection.SendPacket(packet);
connection.Disconnect();
```

### Packet

数据包基类。

```csharp
public class CustomPacket : BasePacket
{
    public override int Id => 0x00;
    public override ConnectionState State => ConnectionState.Play;
    public override PacketDirection Direction => PacketDirection.Serverbound;

    public override void Read(PacketReader reader)
    {
        // 读取数据包数据
    }

    public override void Write(PacketWriter writer)
    {
        // 写入数据包数据
    }
}
```

## 注意事项

1. **离线模式**: 当前版本仅支持离线模式服务器 (`online-mode=false`)
2. **压缩**: 自动处理数据包压缩
3. **线程安全**: 事件处理器在接收线程中调用，请注意线程安全
4. **资源释放**: 使用 `using` 语句或手动调用 `Dispose()` 释放资源

## 示例：完整的机器人客户端

```csharp
using MCProtocol.Game;
using MCProtocol.Events;

public class MinecraftBot : IDisposable
{
    private readonly MinecraftClient _client;

    public MinecraftBot()
    {
        _client = new MinecraftClient();
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        _client.Events.Subscribe<LoginSuccessEvent>(OnLoginSuccess);
        _client.Events.Subscribe<ChatReceivedEvent>(OnChatReceived);
        _client.Events.Subscribe<PlayerPositionUpdateEvent>(OnPositionUpdate);
        _client.Events.Subscribe<DisconnectEvent>(OnDisconnect);
        _client.Events.Subscribe<ErrorEvent>(OnError);
    }

    public async Task StartAsync(string host, int port, string username)
    {
        await _client.ConnectAsync(host, port);
        _client.Login(username);
    }

    private void OnLoginSuccess(LoginSuccessEvent e)
    {
        Console.WriteLine($"[{e.Timestamp}] 登录成功: {e.Username}");
    }

    private void OnChatReceived(ChatReceivedEvent e)
    {
        Console.WriteLine($"[聊天] {e.Sender}: {e.Message}");
        
        // 自动回复示例
        if (e.Message.Contains("hello"))
        {
            _client.SendChat("Hello!");
        }
    }

    private void OnPositionUpdate(PlayerPositionUpdateEvent e)
    {
        Console.WriteLine($"[位置] X={e.X:F2}, Y={e.Y:F2}, Z={e.Z:F2}");
    }

    private void OnDisconnect(DisconnectEvent e)
    {
        Console.WriteLine($"[断开] {e.Reason}");
    }

    private void OnError(ErrorEvent e)
    {
        Console.WriteLine($"[错误] {e.Message}");
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

// 使用示例
using var bot = new MinecraftBot();
await bot.StartAsync("localhost", 25565, "Bot");
await Task.Delay(Timeout.Infinite);
```

## 版本支持

| 游戏版本 | 协议版本 | 支持状态 |
|---------|---------|---------|
| 1.21.1 | 767 | ✅ 完全支持 |

## 许可证

MIT License
