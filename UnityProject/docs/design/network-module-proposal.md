# NetworkModule 设计提案

> 状态：提案  
> 日期：2026-06-09  
> 范围：TEngine 新增网络模块，支持多传输、双协议、多连接、RPC、分帧分发

---

## 1. 背景与目标

### 1.1 现状

TEngine 目前没有网络模块。现有模块体系（Audio / Debugger / FSM / Localization / ObjectPool / Procedure / Resource / Scene / Timer）通过 `ModuleSystem` 统一管理，网络模块需要遵循相同的 Module 模式。

### 1.2 需求

- 支持三种传输方式：TCP、KCP、WebSocket，以及小游戏平台专属网络接口
- 支持两种序列化协议：MessagePack、Protobuf，可插拔
- 支持多长连接并发（如大厅 TCP + 战斗 KCP + 聊天 WebSocket 同时在线）
- 对业务层隐藏传输和协议细节
- 消息类型、Handler、拦截器全部支持热更（HybridCLR HotFix 程序集）
- 连接参数通过代码 Builder 配置，非配置表驱动
- RPC 请求-响应模式，支持断线恢复策略
- 消息分帧分发，平滑处理洪峰
- 拦截器管道（加密/压缩/日志/限流等中间件）
- 网络诊断（Ping/RTT、流量统计、网络质量评估）

---

## 2. 设计决策汇总

| # | 决策点 | 结论 |
|---|--------|------|
| 1 | 协议层 | 双 Codec 可插拔：MessagePack-C# + protobuf-net |
| 2 | 传输层 | TCP + KCP + WebSocket + Platform（小游戏平台适配） |
| 3 | 多连接 | 并发多长连接，各自独立生命周期 |
| 4 | 业务隔离 | 只暴露 连接名 + MsgId + IMessage，零感知底层 |
| 5 | 热更 | 消息类 + Handler + Interceptor 全在 HotFix 程序集 |
| 6 | 配置方式 | 代码 Builder 模式（`ConnectionConfig`），不走 Luban |
| 7 | 拦截器 | 洋葱管道，支持全局/连接级注册，改造型+观察型 |
| 8 | 分帧分发 | 三级优先级队列 + 双 budget（条数 + 时间） |
| 9 | 优先级策略 | 默认 Normal，显式标记 High / Low |
| 10 | RPC 模式 | `SendAsync` + `RpcPolicy`（SafeRecover / FailOnDisconnect） |
| 11 | 事件消息断线 | 未发送消息保留在发送队列，重连后自动补发 |
| 12 | RPC 断线恢复 | 未发出 → 补发；已发出 → 服务端恢复协议；超时 → Fail |
| 13 | 包格式 | Flags 区分事件/RPC，事件消息零额外开销 |
| 14 | 网络诊断 | PingMonitor（独立组件）+ TrafficCounter（内建）+ QualityEstimator |
| 15 | 日志 | 二进制层 → 拦截器观察；对象层 → Dispatcher Hook |

---

## 3. 整体架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                        HotFix 业务层                                │
│                                                                     │
│  GameModule.Network.GetConnection("lobby").Send(1001, loginReq);    │
│  GameModule.Network.GetConnection("battle").On<FrameSync>(3002, cb);│
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│                     INetworkModule (TEngine Module)                  │
│                                                                     │
│  ConnectionPool: Dict<string, INetworkConnection>                   │
│  MessageRegistry: Dict<ushort, Type>                                │
│  GlobalInterceptors: List<IMessageInterceptor>                      │
│  ModuleSystem.Update 驱动所有连接轮询                                │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  INetworkConnection (每条长连接，各自独立)                            │
│                                                                     │
│  Send 流程:                                                         │
│  Message → Codec.Serialize → InterceptorPipeline(Send)              │
│          → PacketBuilder → Transport.Send                           │
│                                                                     │
│  Receive 流程:                                                      │
│  Transport.Recv → PacketParser → InterceptorPipeline(Recv)          │
│          → Codec.Deserialize → PriorityQueue → Dispatcher.Update    │
│                                                                     │
│  内含子系统: HeartbeatKeeper / PingMonitor / RpcTracker              │
│            ReconnectPolicy / TrafficCounter / QualityEstimator       │
│                                                                     │
├──────────────┬──────────────────────────┬───────────────────────────┤
│  Transport层 │       Codec层            │     诊断层                │
│  (可插拔)     │      (可插拔)            │                          │
│              │                          │  PingMonitor (独立)       │
│  TcpTransport│  MessagePackCodec        │  TrafficCounter (内建)    │
│  KcpTransport│  ProtobufCodec           │  QualityEstimator (计算)  │
│  WSTransport │  CodecFactory            │  DispatchStats (内建)     │
│  PlatformTrsp│                          │                          │
└──────────────┴──────────────────────────┴───────────────────────────┘
```

---

## 4. 核心接口定义

### 4.1 INetworkModule

```csharp
public interface INetworkModule
{
    // ── 连接管理 ──
    INetworkConnection CreateConnection(ConnectionConfig config);
    INetworkConnection GetConnection(string name);
    void DestroyConnection(string name);
    bool HasConnection(string name);

    // ── 消息注册（HotFix 层调用）──
    void RegisterMessage<T>(ushort msgId, MessagePriority priority = MessagePriority.Normal)
        where T : IMessage;
    void RegisterMessages(IDictionary<ushort, (Type, MessagePriority)> messageTypes);

    // ── Codec 注入 ──
    void RegisterCodec<T>(CodecType type) where T : ICodec, new();

    // ── Platform Transport 注入 ──
    void RegisterPlatformTransport(string platformId, ITransport transport);

    // ── 全局拦截器 ──
    void AddInterceptor(IMessageInterceptor interceptor);
    void RemoveInterceptor(string name);

    // ── 全局事件 ──
    event Action<string, ConnectionState> OnConnectionStateChanged;
}
```

### 4.2 INetworkConnection

```csharp
public interface INetworkConnection
{
    string Name { get; }
    ConnectionState State { get; }

    // ── 生命周期 ──
    UniTask ConnectAsync();
    void Disconnect();
    UniTask ReconnectAsync();

    // ── 发送 ──

    /// 事件驱动：发完不管，支持断线队列补发
    void Send<T>(ushort msgId, T message) where T : IMessage;

    /// RPC：等待回复
    UniTask<TResp> SendAsync<TReq, TResp>(
        ushort msgId,
        TReq message,
        RpcPolicy policy = RpcPolicy.SafeRecover,
        int timeoutMs = 10000
    ) where TReq : IMessage where TResp : IMessage;

    // ── 接收（HotFix 注册 Handler）──
    void On<T>(ushort msgId, Action<T> handler) where T : IMessage;
    void Off(ushort msgId);
    void OffAll();

    // ── 拦截器（连接级）──
    void AddInterceptor(IMessageInterceptor interceptor);
    void RemoveInterceptor(string name);

    // ── 诊断 ──
    NetworkDiagnostics Diagnostics { get; }

    // ── 事件 ──
    event Action OnConnected;
    event Action OnDisconnected;
    event Action<ConnectionState> OnStateChanged;
    event Action<Exception> OnError;
}
```

### 4.3 ConnectionConfig（Builder 模式）

```csharp
public class ConnectionConfig
{
    // ── 必填 ──
    public string Name { get; }

    // ── Transport ──
    public TransportType Transport { get; private set; }
    public string PlatformId { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    public string Url { get; private set; }

    // ── Codec ──
    public CodecType Codec { get; private set; }

    // ── 心跳 ──
    public int HeartbeatInterval { get; private set; }
    public int HeartbeatTimeout { get; private set; }

    // ── 重连 ──
    public bool AutoReconnect { get; private set; }
    public int ReconnectInterval { get; private set; }
    public int MaxReconnectAttempts { get; private set; }
    public float BackoffMultiplier { get; private set; }
    public int MaxReconnectInterval { get; private set; }

    // ── 缓冲 ──
    public int SendBufferSize { get; private set; }
    public int RecvBufferSize { get; private set; }

    // ── 分帧分发 ──
    public int MaxDispatchPerFrame { get; private set; }
    public int MaxDispatchTimeMs { get; private set; }

    // ── 超时 ──
    public int ConnectTimeout { get; private set; }

    // ── Builder 方法 ──
    public ConnectionConfig(string name);
    public ConnectionConfig UseTcp();
    public ConnectionConfig UseKcp();
    public ConnectionConfig UseWebSocket();
    public ConnectionConfig UsePlatform(string platformId);
    public ConnectionConfig UseMessagePack();
    public ConnectionConfig UseProtobuf();
    public ConnectionConfig SetEndpoint(string host, int port);
    public ConnectionConfig SetUrl(string url);
    public ConnectionConfig SetHeartbeat(int intervalSec, int timeoutSec);
    public ConnectionConfig SetReconnect(
        bool auto, int intervalMs, int maxAttempts,
        float backoff = 1.0f, int maxIntervalMs = 30000);
    public ConnectionConfig SetBuffer(int sendKB, int recvKB);
    public ConnectionConfig SetDispatchBudget(int maxPerFrame, int maxTimeMs = 4);
    public ConnectionConfig SetConnectTimeout(int ms);
}
```

### 4.4 IMessageInterceptor

```csharp
public interface IMessageInterceptor
{
    string Name { get; }
    int Order { get; }

    /// 出站拦截（发送前）
    /// return true: 继续传递; return false: 中止发送
    bool OnSending(ushort msgId, ref byte[] payload);

    /// 入站拦截（接收后、反序列化前）
    /// return true: 继续传递; return false: 丢弃消息
    bool OnReceiving(ushort msgId, ref byte[] payload);
}
```

### 4.5 IMessage / IMessageHandler

```csharp
/// 消息标记接口 — HotFix 中所有消息类实现此接口
public interface IMessage { }

/// 消息优先级
public enum MessagePriority
{
    High = 0,    // 不走分帧，当帧立即分发
    Normal = 1,  // 默认，每帧按 budget 分发
    Low = 2,     // Normal 队列清空后才处理
}

/// RPC 策略
public enum RpcPolicy
{
    /// 断线后自动恢复（未发出 → 补发；已发出 → 服务端恢复协议）
    SafeRecover,
    /// 断线后立即失败，抛出 RpcAbortException
    FailOnDisconnect,
}
```

### 4.6 NetworkDiagnostics

```csharp
public class NetworkDiagnostics
{
    // ── Ping / 延迟 ──
    public float RTT { get; }
    public float AvgRTT { get; }
    public float Jitter { get; }
    public float PacketLoss { get; }

    // ── 流量 ──
    public long BytesSent { get; }
    public long BytesReceived { get; }
    public int MessagesSent { get; }
    public int MessagesReceived { get; }

    // ── RPC ──
    public int RpcsPending { get; }
    public int RpcsCompleted { get; }
    public int RpcsFailed { get; }

    // ── 网络质量 ──
    public NetworkQualityLevel Quality { get; }
    public event Action<NetworkQualityLevel> OnQualityChanged;

    // ── 分发统计 ──
    public DispatchStats Dispatch { get; }

    // ── Ping 开关 ──
    public bool PingEnabled { get; set; }
    public int PingIntervalMs { get; set; }
}

public enum NetworkQualityLevel
{
    Excellent,  // RTT < 50ms, 丢包 < 1%
    Good,       // RTT < 100ms, 丢包 < 3%
    Fair,       // RTT < 200ms, 丢包 < 10%
    Poor,       // RTT < 500ms, 丢包 < 30%
    Bad,        // RTT > 500ms 或 丢包 > 30%
}
```

### 4.7 ITransport

```csharp
public interface ITransport : IDisposable
{
    bool Connected { get; }

    void Connect(string host, int port);
    void Connect(Uri uri);
    UniTask ConnectAsync(string host, int port);

    void Send(byte[] data);
    void Send(ReadOnlyMemory<byte> data);

    void Close();

    event Action OnConnected;
    event Action OnDisconnected;
    event Action<ReadOnlyMemory<byte>> OnDataReceived;
    event Action<Exception> OnError;
}
```

### 4.8 ICodec

```csharp
public interface ICodec
{
    CodecType Type { get; }
    void RegisterMessage<T>(ushort msgId) where T : IMessage;
    byte[] Serialize<T>(T message) where T : IMessage;
    object Deserialize(ushort msgId, byte[] data, int offset, int count);
}
```

---

## 5. 包格式设计

### 5.1 统一包头

```
┌──────────┬──────────┬──────────┬──────────────────────────┐
│  MsgId   │  Flags   │  Length  │  Payload                 │
│  2 bytes │ 1 byte   │ 4 bytes  │  N bytes                 │
└──────────┴──────────┴──────────┴──────────────────────────┘

Flags 位定义:
  bit 0: 是否 RPC (1=是, 0=普通事件)
  bit 1: payload 是否压缩
  bit 2: payload 是否加密
  bit 3-7: 保留
```

### 5.2 普通事件消息（Flags bit0 = 0）

```
┌──────────┬──────────┬──────────┬──────────────────────────┐
│  MsgId   │  0x00    │  Length  │  Payload                 │
└──────────┴──────────┴──────────┴──────────────────────────┘
无额外字段，零开销。
```

### 5.3 RPC 消息（Flags bit0 = 1）

```
┌──────────┬──────────┬──────────┬────────────┬────────────┐
│  MsgId   │  0x01    │  Length  │  ReqId     │  Payload   │
│  2B      │  1B      │  4B     │  4B        │  NB         │
└──────────┴──────────┴──────────┴────────────┴────────────┘
RPC 消息多 4 字节 ReqId，用于匹配请求和回复。
```

### 5.4 系统消息 MsgId 约定

```
0x0000  心跳请求 (Heartbeat Request)
0x0001  心跳回复 (Heartbeat Response)
0xFFFC  Ping 回复
0xFFFD  Ping 请求
0xFFFE  RPC 恢复请求（客户端→服务端）
0xFFFF  RPC 恢复回复（服务端→客户端）
```

---

## 6. 关键机制详解

### 6.1 分帧分发

**问题**：大量消息同时到达时，逐条处理会导致主线程卡顿。

**方案**：三级优先级队列 + 双 budget 控制。

```
每帧 Update 执行:
  1. High 队列: 全部立即执行（心跳、认证、关键同步帧）
  2. Normal 队列: 按 budget 分发
     - 条数 budget: MaxDispatchPerFrame (默认 50)
     - 时间 budget: MaxDispatchTimeMs (默认 4ms)
     - 任一条件满足即停止
  3. Low 队列: 仅 Normal 清空后处理
     - 同样受 budget 约束
```

消息注册时指定优先级，默认 Normal：

```csharp
Network.RegisterMessage<LoginResp>(1002, MessagePriority.High);
Network.RegisterMessage<ChatMsg>(4001);                           // 默认 Normal
Network.RegisterMessage<MailList>(2010, MessagePriority.Low);
```

### 6.2 RPC 断线恢复

**三种场景**：

| 场景 | 条件 | 行为 | 业务感知 |
|------|------|------|---------|
| 未发出 | 消息在发送队列中断线 | 重连后自动补发，TCS 继续等待 | 无感 |
| 已发出无回复 | 消息已到 Transport 但无回复 | 重连后发送 `RpcRecoverRequest(pendingIds)`，服务端回复缓存结果或告知未收到 | 无感 |
| 超时 | 重连耗时过长 | TCS 抛出 `RpcTimeoutException` | catch 处理 |

**RPC 策略**：

```csharp
// SafeRecover (默认): 断线尝试恢复，适用于幂等查询
var info = await conn.SendAsync<PlayerInfoReq, PlayerInfoResp>(6001, req);

// FailOnDisconnect: 断线立即失败，适用于非幂等操作
try {
    var result = await conn.SendAsync<BuyReq, BuyResp>(5001, buyReq,
        RpcPolicy.FailOnDisconnect);
} catch (RpcAbortException) {
    // 断线了，业务决定: 查订单? 提示用户?
}
```

**服务端恢复协议**（可选，不支持则降级 Fail）：

```
重连后客户端发送:
{ "pendingIds": [1, 5, 12] }

服务端回复:
{
  "supported": true,
  "results": [
    { "reqId": 1,  "found": true,  "msgId": 5002, "payload": "<bytes>" },
    { "reqId": 5,  "found": false },
    { "reqId": 12, "found": true,  "msgId": 6002, "payload": "<bytes>" }
  ]
}

服务端不支持:
{ "supported": false }
→ 所有 SafeRecover 的 pending RPC 全部 Fail
```

### 6.3 事件消息断线补发

```
Send(msgId, message)
  → 消息进入 SendQueue
  → 如果连接正常: Transport.Send 发出
  → 如果断线: 消息留在 SendQueue
  → 重连成功: SendQueue 中的消息按顺序自动发出
  → 业务代码无感知
```

### 6.4 拦截器管道

**洋葱模型**：出站顺序与入站顺序互逆。

```
出站: Msg → Codec.Serialize → [加密 Order:100] → [压缩 Order:10] → Transport
入站: Transport → PacketParser → [解压 Order:10] → [解密 Order:100] → Codec.Deserialize → Queue
```

**改造型示例**：

```csharp
public class EncryptionInterceptor : IMessageInterceptor
{
    public string Name => "encryption";
    public int Order => 100;

    public bool OnSending(ushort msgId, ref byte[] payload)
    {
        payload = AES.Encrypt(payload, _key);
        return true;
    }

    public bool OnReceiving(ushort msgId, ref byte[] payload)
    {
        payload = AES.Decrypt(payload, _key);
        return true;
    }
}
```

**观察型示例**：

```csharp
public class MessageLogInterceptor : IMessageInterceptor
{
    public string Name => "log";
    public int Order => 0;

    public bool OnSending(ushort msgId, ref byte[] payload)
    {
        Debug.Log($"[NET] SEND msgId={msgId} size={payload.Length}");
        return true;
    }

    public bool OnReceiving(ushort msgId, ref byte[] payload)
    {
        Debug.Log($"[NET] RECV msgId={msgId} size={payload.Length}");
        return true;
    }
}
```

### 6.5 小游戏平台适配

```csharp
public interface IPlatformNetworkAdapter
{
    void Connect(string url);
    void Send(byte[] data);
    void Close();

    event Action OnConnected;
    event Action OnDisconnected;
    event Action<byte[]> OnDataReceived;
}

// 条件编译 + 运行时检测自动选择
#if WECHAT_MINI_GAME
public class WeChatNetworkAdapter : IPlatformNetworkAdapter { ... }
#elif BYTEDANCE_MINI_GAME
public class ByteDanceNetworkAdapter : IPlatformNetworkAdapter { ... }
#else
public class WebGLWebSocketAdapter : IPlatformNetworkAdapter { ... }
#endif
```

配置使用：

```csharp
var wxgame = new ConnectionConfig("wxgame")
    .UsePlatform("wechat")
    .UseMessagePack()
    .SetUrl("wss://api.wxgame.example.com/ws")
    .SetHeartbeat(10, 30);
```

### 6.6 粘包处理

```
PacketParser 维护接收缓冲区:

1. 缓冲区 < 7 bytes → 等待更多数据（半包: MsgId 2B + Flags 1B + Length 4B）
2. 读取 Header → 得到 MsgId + Flags + Length
3. 缓冲区 < 7 + Length → 等待更多数据（半包: payload 不完整）
4. 提取完整 payload → 交给 Interceptor Pipeline
5. 移动缓冲区指针 → 继续解析（粘包: 下一条消息紧跟着）
```

### 6.7 网络诊断体系

| 组件 | 类型 | 职责 |
|------|------|------|
| HeartbeatKeeper | 必要组件 | 保活 + 粗略 RTT，超时触发断线 |
| PingMonitor | 可选独立组件 | 主动测量延迟/抖动/丢包率，可选开启 |
| TrafficCounter | 内建计数器 | 统计发送/接收字节数和消息条数 |
| QualityEstimator | 计算属性 | 综合评估网络质量等级 |
| DispatchStats | 内建统计 | 分发队列积压数、每帧分发数、处理时间 |

PingMonitor 与 HeartbeatKeeper 的区别：
- Heartbeat：保活机制，频率低（5-15s），超时触发断线
- PingMonitor：诊断工具，频率高（1-2s），只记录不断线，可选开启

访问方式：

```csharp
var conn = GameModule.Network.GetConnection("battle");
Debug.Log($"RTT: {conn.Diagnostics.RTT}ms, Quality: {conn.Diagnostics.Quality}");
Debug.Log($"Sent: {conn.Diagnostics.BytesSent} bytes, {conn.Diagnostics.MessagesSent} msgs");
```

对象层日志（看到反序列化后的 IMessage）：

```csharp
// Dispatcher 分发回调（可选注册）
conn.OnBeforeDispatch += (msgId, msg) => {
    Debug.Log($"← {msgId}: {msg}");
};
```

---

## 7. 连接状态机

```
┌─────────┐   Connect()   ┌───────────┐   成功   ┌───────────┐
│  None   │──────────────▶│Connecting │────────▶│ Connected │
└─────────┘               └─────┬─────┘          └─────┬─────┘
                              │ 失败                     │
                              ▼                          │ 断线
                         ┌──────────┐                    ▼
                         │  Failed  │             ┌────────────┐
                         └──────────┘             │Disconnecti │
                                                   │    ng      │
                              ┌───────────────────└─────┬──────┘
                              │                         │
                              │  AutoReconnect=true     │ 手动 Close
                              ▼                         ▼
                         ┌────────────┐           ┌──────────┐
                         │Reconnecting│           │  Closed  │
                         └─────┬──────┘           └──────────┘
                               │
                     成功 ─────┼──── 失败(超过次数)
                       │              │
                       ▼              ▼
                  ┌──────────┐   ┌──────────┐
                  │Connected │   │  Closed  │
                  └──────────┘   └──────────┘
```

---

## 8. 文件结构

```
Assets/TEngine/Runtime/Module/NetworkModule/
│
├── INetworkModule.cs
├── NetworkModule.cs
│
├── Connection/
│   ├── INetworkConnection.cs
│   ├── NetworkConnection.cs
│   ├── ConnectionState.cs
│   └── ConnectionConfig.cs
│
├── Transport/
│   ├── ITransport.cs
│   ├── TcpTransport.cs
│   ├── KcpTransport.cs
│   ├── WebSocketTransport.cs
│   ├── PlatformTransport.cs
│   └── PlatformAdapters/
│       ├── IPlatformNetworkAdapter.cs
│       ├── WeChatNetworkAdapter.cs
│       ├── ByteDanceNetworkAdapter.cs
│       └── WebGLWebSocketAdapter.cs
│
├── Codec/
│   ├── ICodec.cs
│   ├── CodecType.cs
│   ├── MessagePackCodec.cs
│   ├── ProtobufCodec.cs
│   └── CodecFactory.cs
│
├── Protocol/
│   ├── IMessage.cs
│   ├── PacketHeader.cs
│   ├── PacketParser.cs
│   ├── MessagePriority.cs
│   └── SystemMessageIds.cs
│
├── Rpc/
│   ├── RpcPolicy.cs
│   ├── PendingRpc.cs
│   ├── RpcTracker.cs
│   ├── RpcRecovery.cs
│   └── RpcExceptions.cs
│
├── Dispatch/
│   ├── MessageDispatcher.cs
│   ├── DispatchStats.cs
│   └── PriorityQueue.cs
│
├── Interceptor/
│   ├── IMessageInterceptor.cs
│   └── InterceptorPipeline.cs
│
├── Diagnostics/
│   ├── NetworkDiagnostics.cs
│   ├── PingMonitor.cs
│   ├── TrafficCounter.cs
│   ├── NetworkQualityLevel.cs
│   └── QualityEstimator.cs
│
├── Infrastructure/
│   ├── HeartbeatKeeper.cs
│   ├── ReconnectPolicy.cs
│   ├── BufferPool.cs
│   ├── ConnectionStateMachine.cs
│   └── SendQueue.cs
│
└── TransportType.cs

Assets/GameScripts/HotFix/GameLogic/Network/
│
├── Messages/
│   ├── LobbyMessages.cs
│   ├── BattleMessages.cs
│   └── ChatMessages.cs
│
├── Handlers/
│   ├── LobbyHandler.cs
│   ├── BattleHandler.cs
│   └── ChatHandler.cs
│
├── Interceptors/
│   ├── EncryptionInterceptor.cs
│   └── CompressionInterceptor.cs
│
├── MessageRegistry.cs
└── NetworkInitializer.cs
```

---

## 9. 依赖库

| 库 | 用途 | GitHub | 协议 |
|----|------|--------|------|
| kcp2k | KCP 传输 | kcp2k/kcp2k ★800+ | MIT |
| NativeWebSocket | WebSocket 客户端 | endel/NativeWebSocket ★600+ | MIT |
| MessagePack-C# | MessagePack 序列化 | neuecc/MessagePack-C# ★5.2k | MIT |
| protobuf-net | Protobuf 序列化 | protobuf-net/protobuf-net ★4.6k | Apache-2.0 |

已有依赖（无需新增）：
- **UniTask** — 所有异步操作
- **System.Net.Sockets** — TCP 传输（内置）

---

## 10. 使用示例

### 10.1 初始化连接（HotFix 代码）

```csharp
// NetworkInitializer.cs — 在 GameApp.OnStart() 中调用
public static class NetworkInitializer
{
    public static void Initialize(INetworkModule network)
    {
        // 创建连接
        var lobby = new ConnectionConfig("lobby")
            .UseTcp()
            .UseProtobuf()
            .SetEndpoint("10.0.0.1", 9000)
            .SetHeartbeat(10, 30)
            .SetReconnect(true, 1000, 5, 1.5f)
            .SetDispatchBudget(50, 4)
            .SetConnectTimeout(5000);

        var battle = new ConnectionConfig("battle")
            .UseKcp()
            .UseMessagePack()
            .SetEndpoint("10.0.0.1", 9001)
            .SetHeartbeat(3, 10)
            .SetReconnect(true, 500, 10, 1.5f)
            .SetBuffer(256, 256)
            .SetDispatchBudget(100, 4);

        network.CreateConnection(lobby);
        network.CreateConnection(battle);

        // 注册消息
        MessageRegistry.Register(network);

        // 注册拦截器
        network.AddInterceptor(new EncryptionInterceptor(key));
        network.GetConnection("battle")
            .AddInterceptor(new CompressionInterceptor());
    }
}
```

### 10.2 注册消息类型

```csharp
// MessageRegistry.cs
public static class MessageRegistry
{
    public static void Register(INetworkModule network)
    {
        // High priority: 关键消息立即分发
        network.RegisterMessage<LoginResp>(1002, MessagePriority.High);
        network.RegisterMessage<FrameSync>(3002, MessagePriority.High);

        // Normal priority: 默认
        network.RegisterMessage<EnterLobbyResp>(2002);
        network.RegisterMessage<ChatMsg>(4001);

        // Low priority: 闲时处理
        network.RegisterMessage<MailList>(2010, MessagePriority.Low);
    }
}
```

### 10.3 业务代码

```csharp
// 连接大厅
await GameModule.Network.GetConnection("lobby").ConnectAsync();

// RPC 登录
var loginResp = await GameModule.Network.GetConnection("lobby")
    .SendAsync<LoginReq, LoginResp>(1001, new LoginReq { Account = "xxx" });

// 监听推送
GameModule.Network.GetConnection("battle")
    .On<FrameSync>(3002, sync => ApplyFrame(sync));

// 发送事件
GameModule.Network.GetConnection("battle")
    .Send(3001, new FrameInput { Action = "jump" });

// 非幂等 RPC
try {
    var result = await GameModule.Network.GetConnection("lobby")
        .SendAsync<BuyReq, BuyResp>(5001, buyReq, RpcPolicy.FailOnDisconnect);
} catch (RpcAbortException) {
    ShowDialog("网络波动，请检查购买结果");
}
```

---

## 11. 实施阶段

### Phase 1: 核心骨架
- Module 注册 + ModuleSystem 集成
- INetworkModule / INetworkConnection 接口
- ConnectionConfig Builder
- ConnectionStateMachine
- ITransport 接口 + TcpTransport 实现

### Phase 2: 传输层
- KcpTransport (集成 kcp2k)
- WebSocketTransport (集成 NativeWebSocket)
- PlatformTransport + IPlatformNetworkAdapter

### Phase 3: 协议层 + 消息分发
- ICodec + MessagePackCodec + ProtobufCodec
- PacketParser (粘包处理)
- MessageDispatcher (三级优先队列 + 双 budget)
- SendQueue (断线保留)

### Phase 4: RPC + 断线恢复
- RpcTracker (RequestId + PendingRpc 追踪)
- SendAsync 实现
- RpcRecovery (服务端恢复协议)
- RpcExceptions

### Phase 5: 辅助机制
- HeartbeatKeeper
- ReconnectPolicy
- InterceptorPipeline
- BufferPool

### Phase 6: 诊断 + 调试
- NetworkDiagnostics
- PingMonitor
- TrafficCounter
- QualityEstimator
- DispatchStats
- DebuggerModule 集成（网络调试面板）

---

## 12. 风险与注意事项

1. **线程安全**：Transport 层回调可能在工作线程，需通过 ConcurrentQueue 转移到主线程，Handler 回调保证在主线程执行
2. **IL2CPP / AOT**：MessagePack 使用 SourceGenerator 模式；protobuf-net 需要预生成序列化代码
3. **热更边界**：NetworkModule 本身在 Runtime（不热更），消息类和 Handler 在 HotFix（热更）
4. **服务端恢复协议**：需要服务端配合实现 RPC 结果缓存，不支持时降级为 Fail
5. **KCP UDP 穿透**：部分网络环境可能封禁 UDP，需要 TCP fallback 机制
6. **小游戏平台差异**：各平台 WebSocket API 存在细微差异，需要逐平台测试适配
