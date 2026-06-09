# NetworkModule 设计规格

> 状态：规格  
> 日期：2026-06-09  
> 范围：TEngine 新增网络模块

---

## 1. 设计决策汇总

| # | 决策点 | 结论 |
|---|--------|------|
| 1 | 协议层 | 双 Codec 可插拔：MessagePack-C# + protobuf-net |
| 2 | 传输层 | TCP + KCP + WebSocket + Platform（小游戏平台适配） |
| 3 | 多连接 | 并发多长连接，各自独立生命周期 |
| 4 | 业务隔离 | 只暴露 连接名 + MsgId + IMessage，零感知底层 |
| 5 | 热更 | 消息类 + Handler + Interceptor 全在 HotFix 程序集 |
| 6 | 配置方式 | 代码 Builder 模式（ConnectionConfig），不走 Luban |
| 7 | 拦截器 | 洋葱管道，支持全局/连接级注册 |
| 8 | 分帧分发 | 三级优先级队列 + 双 budget（条数 + 时间） |
| 9 | 优先级策略 | 默认 Normal，显式标记 High / Low |
| 10 | RPC 模式 | SendAsync + RpcPolicy（SafeRecover / FailOnDisconnect） |
| 11 | 事件断线 | 未发送消息保留在 SendQueue，重连后自动补发 |
| 12 | RPC 断线 | 未发出→补发；已发出→服务端恢复协议；超时→Fail |
| 13 | 包格式 | IPacketFormatter 可插拔，默认 DefaultPacketFormatter |
| 14 | 诊断 | PingMonitor(独立) + TrafficCounter(内建) + QualityEstimator |
| 15 | 错误处理 | 混合策略：Send 走事件，RPC 走异常 |
| 16 | 线程模型 | 先单线程轮询，预留多线程扩展点 |
| 17 | 内存管理 | BufferPool（分桶池，前期包装 ArrayPool） |
| 18 | 服务端 | 不确定，客户端先做框架，包格式可插拔 |
| 19 | MsgId | 全局唯一，0xFFFC-0xFFFF 系统保留 |
| 20 | 连接生命周期 | 基础静态 + 业务动态 |
| 21 | 时序追踪 | MessageTrace 元数据，客户端内部，不走线路 |

---

## 2. 整体架构

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
│  ConnectionPool / MessageRegistry / GlobalInterceptors              │
│  PacketFormatter / CodecFactory                                     │
│  ModuleSystem.Update 驱动所有连接轮询                                │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  INetworkConnection (每条长连接，各自独立)                            │
│                                                                     │
│  Send: Message → Codec.Serialize → InterceptorPipeline →            │
│        PacketFormatter → SendQueue → Transport                      │
│                                                                     │
│  Receive: Transport → PacketFormatter → InterceptorPipeline →       │
│           Codec.Deserialize → PriorityQueue → Dispatcher            │
│                                                                     │
│  内含: HeartbeatKeeper / PingMonitor / RpcTracker / ReconnectPolicy  │
│        TrafficCounter / QualityEstimator / SendQueue / Dispatcher    │
│                                                                     │
├──────────────┬──────────────────────┬───────────────────────────────┤
│  Transport层 │       Codec层        │     诊断层                    │
│  (可插拔)     │      (可插拔)        │                               │
│  TcpTransport│  MessagePackCodec    │  PingMonitor (独立可选)        │
│  KcpTransport│  ProtobufCodec       │  TrafficCounter (内建)         │
│  WSTransport │  CodecFactory        │  QualityEstimator (计算)       │
│  PlatformTrsp│                      │  DispatchStats (内建)          │
└──────────────┴──────────────────────┴───────────────────────────────┘
```

---

## 3. 包格式

### 3.1 IPacketFormatter

```csharp
public interface IPacketFormatter
{
    /// 将消息打包成字节流
    byte[] BuildPacket(ushort msgId, byte flags, byte[] payload);

    /// 尝试从缓冲区中解析一条完整消息
    /// 返回 null 表示半包（数据不完整）
    PacketParseResult? TryParse(byte[] buffer, int offset, int count, out int consumed);
}

public struct PacketParseResult
{
    public ushort MsgId;
    public byte Flags;
    public byte[] Payload;          // BufferPool 租用，消费后需归还
    public uint RequestId;          // Flags bit0=1 时有效，否则 0
}

public static class PacketFlags
{
    public const byte Rpc        = 0x01;
    public const byte Compressed = 0x02;
    public const byte Encrypted  = 0x04;
    // bit 3-7: 保留
}
```

### 3.2 DefaultPacketFormatter

```
普通事件 (Flags bit0 = 0):
┌──────────┬──────────┬──────────┬──────────────────────────┐
│  MsgId   │  Flags   │  Length  │  Payload                 │
│  2B BE   │  1B      │  4B BE   │  NB                       │
└──────────┴──────────┴──────────┴──────────────────────────┘

RPC 消息 (Flags bit0 = 1):
┌──────────┬──────────┬──────────┬────────────┬────────────┐
│  MsgId   │  Flags   │  Length  │  ReqId     │  Payload   │
│  2B BE   │  1B      │  4B BE   │  4B BE     │  NB         │
└──────────┴──────────┴──────────┴────────────┴────────────┘
BE = Big Endian (网络字节序)
```

契约:
- `BuildPacket` 永不返回 null
- `TryParse` 返回 null 时 `consumed = 0`
- `TryParse` 成功时 `consumed > 0`
- `Payload` 由 BufferPool 租用，调用者负责归还

### 3.3 系统消息 MsgId

```
0x0000  心跳请求
0x0001  心跳回复
0xFFFC  Ping 回复
0xFFFD  Ping 请求
0xFFFE  RPC 恢复请求（客户端→服务端）
0xFFFF  RPC 恢复回复（服务端→客户端）

0x0002 ~ 0xFFFB  业务可用
```

### 3.4 MessageTrace

```csharp
/// 消息追踪元数据 — 每条收发消息自动生成，不走线路
public struct MessageTrace
{
    public uint SequenceId;              // 连接内递增序号
    public long Timestamp;               // Stopwatch.GetTimestamp()
    public MessageDirection Direction;   // Send / Receive
    public int WireSize;                 // 含包头的总字节数
}

public enum MessageDirection
{
    Send,
    Receive,
}
```

贯穿整个生命周期：Interceptor、Dispatcher Hook、Diagnostics 都接收 `in MessageTrace`。

---

## 4. INetworkModule 契约

```csharp
public interface INetworkModule
{
    // ── 连接管理 ──

    /// 创建连接
    /// 前置: config.Name 非空 且 不存在同名连接
    /// 异常: null → ArgumentNullException; 重名 → ArgumentException
    INetworkConnection CreateConnection(ConnectionConfig config);

    /// 获取连接
    /// 异常: 不存在 → KeyNotFoundException
    INetworkConnection GetConnection(string name);

    /// 安全获取（不抛异常）
    INetworkConnection? GetConnectionOrDefault(string name);

    /// 销毁连接
    /// 后置: 连接 Dispose，未发送消息丢弃，等待中 RPC Abort
    void DestroyConnection(string name);

    bool HasConnection(string name);

    // ── 消息注册（全局，MsgId 全局唯一）──

    /// 异常: msgId 已注册 → ArgumentException
    /// 异常: msgId == 0 或 >= 0xFFFC → ArgumentException（系统保留）
    void RegisterMessage<T>(
        ushort msgId,
        MessagePriority priority = MessagePriority.Normal
    ) where T : IMessage;

    void RegisterMessages(
        IDictionary<ushort, (Type messageType, MessagePriority priority)> entries
    );

    MessagePriority GetMessagePriority(ushort msgId);

    // ── Codec 注入 ──
    void RegisterCodec<T>(CodecType type) where T : ICodec, new();

    // ── Platform Transport 注入 ──
    void RegisterPlatformTransport(string platformId, ITransport transport);

    // ── 全局拦截器 ──
    /// 异常: 同名已存在 → ArgumentException
    void AddInterceptor(IMessageInterceptor interceptor);
    void RemoveInterceptor(string name);

    // ── 包格式化器 ──
    /// 前置: 必须在任何 CreateConnection 之前调用
    void SetPacketFormatter(IPacketFormatter formatter);

    // ── 全局事件（主线程回调）──
    event Action<string, ConnectionState> OnConnectionStateChanged;
    event Action<string, Exception> OnError;
}
```

拦截器执行顺序:
- 出站: 全局(升序) → 连接级(升序)
- 入站: 连接级(升序) → 全局(升序)
- 同一排序列中 Order 小的先执行

---

## 5. INetworkConnection 契约

```csharp
public interface INetworkConnection
{
    string Name { get; }
    ConnectionState State { get; }

    // ── 生命周期 ──

    /// 前置: None || Closed
    /// 后置: 成功 → Connected + OnConnected
    /// 异常: 非法状态 → InvalidOperationException
    /// 异常: 失败/超时 → ConnectionException
    UniTask ConnectAsync();

    /// 幂等安全，任何状态可调用
    /// 后置: State == Closed + OnDisconnected
    /// SendQueue 消息丢弃；SafeRecover RPC 保留；FailOnDisconnect RPC Abort
    void Disconnect();

    /// 前置: Disconnected
    /// 后置: 成功 → Connected + SafeRecover RPC 恢复
    /// 异常: 超过重试次数 → ReconnectFailedException
    UniTask ReconnectAsync();

    // ── 发送：事件模式 ──

    /// 前置: Connected 时立即发送；否则入 SendQueue 等待
    /// 错误: 序列化失败 → OnError，不抛异常
    /// 错误: 拦截器拒绝 → 静默丢弃
    void Send<T>(ushort msgId, T message) where T : IMessage;

    // ── 发送：RPC 模式 ──

    /// 异常: RpcTimeoutException — 超时
    /// 异常: RpcAbortException — 断线不可恢复 / FailOnDisconnect
    /// 异常: SerializationException — 序列化失败
    UniTask<TResp> SendAsync<TReq, TResp>(
        ushort msgId,
        TReq message,
        RpcPolicy policy = RpcPolicy.SafeRecover,
        int timeoutMs = 10000
    ) where TReq : IMessage where TResp : IMessage;

    // ── 接收 ──

    /// 同一 msgId 注册多次 → 后者覆盖前者
    void On<T>(ushort msgId, Action<T> handler) where T : IMessage;
    void Off(ushort msgId);
    void OffAll();

    // ── 拦截器 ──
    void AddInterceptor(IMessageInterceptor interceptor);
    void RemoveInterceptor(string name);

    // ── 诊断 ──
    NetworkDiagnostics Diagnostics { get; }

    // ── 事件（主线程回调）──
    event Action OnConnected;                               // 含重连成功
    event Action OnDisconnected;                            // 心跳超时/Transport错误/服务端关闭
    event Action<ConnectionState> OnStateChanged;
    event Action<Exception> OnError;                        // 未被业务处理的错误
}
```

状态转换表:

| 当前状态 | 操作 | 成功后 | 失败后 |
|---------|------|--------|--------|
| None | ConnectAsync | Connected | Failed |
| Connected | Disconnect | Closed | - |
| Connected | (断线检测) | Disconnected | - |
| Disconnected | ReconnectAsync | Connected | Disconnected/Closed |
| Disconnected | Disconnect | Closed | - |
| Closed | ConnectAsync | Connected | Failed |
| Any | Send | Connected(入队列) | Connected(入队列) |

Disconnect 在任何状态都安全（幂等）。

---

## 6. ITransport 契约

```csharp
public interface ITransport : IDisposable
{
    bool Connected { get; }
    TransportType TransportType { get; }

    void Connect(string host, int port);
    void Connect(Uri uri);
    UniTask ConnectAsync(string host, int port);

    /// 前置: Connected == true
    /// 异常: 未连接 → InvalidOperationException
    /// 异常: 发送失败 → TransportException
    void Send(byte[] data);
    void Send(ReadOnlyMemory<byte> data);

    /// 幂等安全
    void Close();

    event Action OnConnected;
    event Action OnDisconnected;
    event Action<ReadOnlyMemory<byte>> OnDataReceived;  // 含包头的原始字节
    event Action<Exception> OnError;
}
```

Transport 职责边界:
- ✅ 连接建立/关闭、原始字节收发、断线检测
- ❌ 不管消息格式、序列化、粘包、心跳、重连、消息去重

---

## 7. ICodec 契约

```csharp
public interface ICodec
{
    CodecType Type { get; }

    void RegisterMessage<T>(ushort msgId) where T : IMessage;

    /// 返回 BufferPool 租用的 byte[]，调用者归还
    /// 异常: null → ArgumentNullException; 失败 → SerializationException
    byte[] Serialize<T>(T message) where T : IMessage;

    /// 异常: msgId 未注册 → KeyNotFoundException; 失败 → SerializationException
    object Deserialize(ushort msgId, byte[] data, int offset, int count);
}
```

每条连接独立一个 Codec 实例（避免锁竞争）。

MessagePack 约束: `[MessagePackObject]` + `[Key(N)]`，热更程序集用 DynamicObjectResolver。  
Protobuf 约束: `[ProtoContract]` + `[ProtoMember(N)]`，RegisterMessage 时自动注册 RuntimeTypeModel。

---

## 8. IMessage / 枚举

```csharp
public interface IMessage { }

public enum MessagePriority
{
    High = 0,    // 不走分帧，当帧立即分发
    Normal = 1,  // 默认
    Low = 2,     // Normal 清空后处理
}

public enum RpcPolicy
{
    SafeRecover,       // 断线尝试恢复
    FailOnDisconnect,  // 断线立即失败
}

public enum ConnectionState
{
    None = 0,
    Connecting = 1,
    Connected = 2,
    Disconnected = 3,
    Reconnecting = 4,
    Failed = 5,
    Closed = 6,
}

public enum CodecType
{
    MessagePack,
    Protobuf,
}

public enum TransportType
{
    Tcp,
    Kcp,
    WebSocket,
    Platform,
}
```

---

## 9. IMessageInterceptor 契约

```csharp
public interface IMessageInterceptor
{
    string Name { get; }
    int Order { get; }

    /// 出站拦截
    /// return true: 继续; return false: 中止
    /// 异常: 捕获 → 等同 return false + OnError
    bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace);

    /// 入站拦截
    /// return true: 继续; return false: 丢弃
    /// 异常: 捕获 → 等同 return false + OnError
    bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace);
}
```

---

## 10. ConnectionConfig

```csharp
public class ConnectionConfig
{
    public string Name { get; }  // 构造函数必填

    // Builder 方法
    public ConnectionConfig UseTcp();
    public ConnectionConfig UseKcp();
    public ConnectionConfig UseWebSocket();
    public ConnectionConfig UsePlatform(string platformId);
    public ConnectionConfig UseMessagePack();
    public ConnectionConfig UseProtobuf();
    public ConnectionConfig SetEndpoint(string host, int port);
    public ConnectionConfig SetUrl(string url);
    public ConnectionConfig SetHeartbeat(int intervalSec, int timeoutSec);
    public ConnectionConfig SetReconnect(bool auto, int intervalMs, int maxAttempts,
                                         float backoff = 1.0f, int maxIntervalMs = 30000);
    public ConnectionConfig SetBuffer(int sendKB, int recvKB);
    public ConnectionConfig SetDispatchBudget(int maxPerFrame, int maxTimeMs = 4);
    public ConnectionConfig SetConnectTimeout(int ms);
}
```

默认值:

| 字段 | 默认值 | 说明 |
|------|--------|------|
| HeartbeatInterval | 10 秒 | |
| HeartbeatTimeout | 30 秒 | |
| AutoReconnect | true | |
| ReconnectInterval | 1000 ms | |
| MaxReconnectAttempts | 5 | |
| BackoffMultiplier | 1.0 | 1.0=固定，2.0=指数退避 |
| MaxReconnectInterval | 30000 ms | |
| SendBufferSize | 64 KB | |
| RecvBufferSize | 64 KB | |
| MaxDispatchPerFrame | 50 条 | |
| MaxDispatchTimeMs | 4 ms | |
| ConnectTimeout | 5000 ms | |

Validate 在 CreateConnection 时调用，缺少必填项抛 ArgumentException。

---

## 11. NetworkDiagnostics 契约

```csharp
public sealed class NetworkDiagnostics
{
    // Ping / 延迟
    public float RTT { get; }              // ms
    public float AvgRTT { get; }           // 滑动平均 (近10次)
    public float Jitter { get; }           // ms
    public float PacketLoss { get; }       // 0-1

    // Ping 开关
    public bool PingEnabled { get; set; }  // 默认 false
    public int PingIntervalMs { get; set; }// 默认 2000, 最小 500

    // 流量
    public long BytesSent { get; }
    public long BytesReceived { get; }
    public int MessagesSent { get; }
    public int MessagesReceived { get; }

    // RPC
    public int RpcsPending { get; }
    public int RpcsCompleted { get; }
    public int RpcsFailed { get; }

    // 质量
    public NetworkQualityLevel Quality { get; }
    public event Action<NetworkQualityLevel> OnQualityChanged;

    // 分发
    public DispatchStats Dispatch { get; }

    public void ResetConnectionMetrics();
}

public enum NetworkQualityLevel
{
    Excellent,  // RTT < 50ms, 丢包 < 1%
    Good,       // RTT < 100ms, 丢包 < 3%
    Fair,       // RTT < 200ms, 丢包 < 10%
    Poor,       // RTT < 500ms, 丢包 < 30%
    Bad,        // RTT >= 500ms 或 丢包 >= 30%
}

public struct DispatchStats
{
    public int HighQueueCount;
    public int NormalQueueCount;
    public int LowQueueCount;
    public int DispatchedThisFrame;
    public int TotalDispatched;
    public int TotalDropped;
    public float LastFrameTimeMs;
}
```

---

## 12. 内部组件

### 12.1 RpcTracker + PendingRpc

```csharp
internal struct PendingRpc
{
    public uint RequestId;
    public ushort MsgId;
    public byte[] OriginalPayload;      // BufferPool 租用
    public RpcPolicy Policy;
    public UniTaskCompletionSource Tcs;
    public Type ResponseType;
    public long StartTimestamp;
    public int TimeoutMs;
    public bool Sent;
}
```

断线处理:
- FailOnDisconnect → 立即 `Tcs.TrySetException(RpcAbortException)`
- SafeRecover + !Sent → SendQueue 保留，重连后自动补发
- SafeRecover + Sent → 重连后发起 RpcRecovery

RPC 收包匹配: `flags & Rpc != 0` → 查 `_rpcTracker[reqId]` → 匹配则 Complete，不匹配则丢弃。

### 12.2 RpcRecovery

重连后发送系统消息 0xFFFE `{ pendingIds: [...] }`。  
服务端回复 0xFFFF: `{ supported: bool, results: [{reqId, found, msgId?, payload?}] }`。  
不支持或超时 5s → 所有 SafeRecover RPC 全部 Abort。

### 12.3 MessageDispatcher

```
Update 执行顺序:
  1. High 队列: 全部立即执行
  2. Normal 队列: 按 budget (条数 + 时间) 分发
  3. Low 队列: 仅 Normal 清空后，按 budget 分发
```

异常安全: handler 抛异常 → OnAfterDispatch 仍被调用 → 记录 DispatchStats → 不影响后续消息。

回调:
```csharp
event Action<ushort, object, MessageTrace> OnBeforeDispatch;
event Action<ushort, object, MessageTrace> OnAfterDispatch;
```

### 12.4 SendQueue

```csharp
internal class SendQueue
{
    void Enqueue(byte[] packet);
    List<byte[]>.Enumerator DequeueAll();
    int Count { get; }
    void Pause();   // 断线时调用，消息保留
    void Resume();  // 重连时调用，可继续发送
    void Clear();   // 关闭/销毁时调用
}
```

### 12.5 InterceptorPipeline

```csharp
internal class InterceptorPipeline
{
    void Add(IMessageInterceptor interceptor);
    void Remove(string name);
    void SetGlobalInterceptors(IReadOnlyList<IMessageInterceptor> globals);
    bool ProcessSending(ushort msgId, ref byte[] payload, in MessageTrace trace);
    bool ProcessReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace);
}
```

### 12.6 BufferPool

```csharp
internal static class BufferPool
{
    byte[] Rent(int size);           // 长度 >= size
    byte[] RentCleared(int size);    // 清零
    void Return(byte[] buffer);      // 多次 Return 安全
    int ActiveCount { get; }
    long ActiveBytes { get; }
    void Clear();
}
```

前期实现: 包装 `System.Buffers.ArrayPool<byte>.Shared`。  
分桶策略: 64, 128, 256, 512, 1K, 2K, 4K, 8K, 16K，每桶上限 64 个。

### 12.7 NetworkConnection.Update 时序

```
void Update(float elapseSeconds)
{
    // 1. Transport 轮询收包
    _transport.Poll();
    // 2. 心跳维护
    _heartbeat.Update(elapseSeconds);
    // 3. Ping 监测（可选）
    if (_diagnostics.PingEnabled) _pingMonitor.Update(elapseSeconds);
    // 4. RPC 超时检查
    _rpcTracker.CheckTimeouts();
    // 5. 发送队列刷新
    if (State == Connected) { foreach (p in _sendQueue.DequeueAll()) _transport.Send(p); }
    // 6. 消息分发
    _dispatcher.Dispatch(elapseSeconds);
    // 7. 质量评估（每 30 帧）
    _qualityEstimator.MaybeUpdate();
}
```

---

## 13. 异常体系

```
NetworkException (基类, 带 ConnectionName)
├── ConnectionException              连接建立失败
├── ReconnectFailedException         重连耗尽 (AttemptCount)
├── TransportException               Transport IO 错误
├── SerializationException           序列化失败 (MsgId, Direction)
├── RpcTimeoutException              RPC 超时 (MsgId, RequestId, TimeoutMs)
└── RpcAbortException                RPC 中止 (MsgId, RequestId, Reason)
```

| 场景 | 异常/事件 |
|------|----------|
| ConnectAsync 失败 | ConnectionException |
| ReconnectAsync 失败 | ReconnectFailedException |
| Transport IO 错误 | TransportException → OnError |
| Send 序列化失败 | SerializationException → OnError |
| SendAsync 超时 | RpcTimeoutException |
| SendAsync 断线(FOD) | RpcAbortException |
| SendAsync 恢复失败 | RpcAbortException |
| Dispatch handler 异常 | 不抛，记录 DispatchStats |
| Interceptor 异常 | 不抛，中止传递 → OnError |

---

## 14. 小游戏平台适配

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

// 条件编译 + 运行时检测
#if WECHAT_MINI_GAME
public class WeChatNetworkAdapter : IPlatformNetworkAdapter { ... }
#elif BYTEDANCE_MINI_GAME
public class ByteDanceNetworkAdapter : IPlatformNetworkAdapter { ... }
#else
public class WebGLWebSocketAdapter : IPlatformNetworkAdapter { ... }
#endif
```

使用:
```csharp
GameModule.Network.RegisterPlatformTransport("wechat", new WeChatNetworkAdapter());
var wx = new ConnectionConfig("wxgame").UsePlatform("wechat").UseMessagePack().SetUrl("wss://...");
```

---

## 15. 文件结构

```
Assets/TEngine/Runtime/Module/NetworkModule/
├── INetworkModule.cs
├── NetworkModule.cs
├── Connection/
│   ├── INetworkConnection.cs
│   ├── NetworkConnection.cs
│   ├── ConnectionState.cs
│   └── ConnectionConfig.cs
├── Transport/
│   ├── ITransport.cs
│   ├── TransportType.cs
│   ├── TcpTransport.cs
│   ├── KcpTransport.cs
│   ├── WebSocketTransport.cs
│   ├── PlatformTransport.cs
│   └── PlatformAdapters/
│       ├── IPlatformNetworkAdapter.cs
│       ├── WeChatNetworkAdapter.cs
│       ├── ByteDanceNetworkAdapter.cs
│       └── WebGLWebSocketAdapter.cs
├── Codec/
│   ├── ICodec.cs
│   ├── CodecType.cs
│   ├── MessagePackCodec.cs
│   ├── ProtobufCodec.cs
│   └── CodecFactory.cs
├── Protocol/
│   ├── IMessage.cs
│   ├── MessagePriority.cs
│   ├── MessageDirection.cs
│   ├── MessageTrace.cs
│   ├── PacketFlags.cs
│   ├── PacketParseResult.cs
│   ├── IPacketFormatter.cs
│   ├── DefaultPacketFormatter.cs
│   └── SystemMessageIds.cs
├── Rpc/
│   ├── RpcPolicy.cs
│   ├── PendingRpc.cs
│   ├── RpcTracker.cs
│   ├── RpcRecovery.cs
│   └── RpcExceptions.cs
├── Dispatch/
│   ├── MessageDispatcher.cs
│   ├── DispatchStats.cs
│   └── PriorityQueue.cs
├── Interceptor/
│   ├── IMessageInterceptor.cs
│   └── InterceptorPipeline.cs
├── Diagnostics/
│   ├── NetworkDiagnostics.cs
│   ├── PingMonitor.cs
│   ├── TrafficCounter.cs
│   ├── NetworkQualityLevel.cs
│   └── QualityEstimator.cs
├── Infrastructure/
│   ├── HeartbeatKeeper.cs
│   ├── ReconnectPolicy.cs
│   ├── BufferPool.cs
│   ├── ConnectionStateMachine.cs
│   └── SendQueue.cs
└── Exceptions/
    ├── NetworkException.cs
    ├── ConnectionException.cs
    ├── ReconnectFailedException.cs
    ├── TransportException.cs
    ├── SerializationException.cs
    ├── RpcTimeoutException.cs
    └── RpcAbortException.cs

Assets/GameScripts/HotFix/GameLogic/Network/
├── Messages/
├── Handlers/
├── Interceptors/
├── MessageRegistry.cs
└── NetworkInitializer.cs
```

---

## 16. 依赖库

| 库 | 用途 | 协议 |
|----|------|------|
| kcp2k | KCP 传输 | MIT |
| NativeWebSocket | WebSocket 客户端 | MIT |
| MessagePack-C# | MessagePack 序列化 | MIT |
| protobuf-net | Protobuf 序列化 | Apache-2.0 |

已有: UniTask（异步）、System.Net.Sockets（TCP）。

---

## 17. 实施阶段

| Phase | 内容 | 文件数 |
|-------|------|--------|
| 1 | 核心骨架: Module 注册、接口定义、ConnectionConfig、ConnectionStateMachine | ~12 |
| 2 | 传输层: TcpTransport、KcpTransport、WebSocketTransport、PlatformTransport | ~8 |
| 3 | 协议+分发: ICodec 实现、PacketFormatter、MessageDispatcher、SendQueue | ~10 |
| 4 | RPC: RpcTracker、SendAsync、RpcRecovery、异常类 | ~6 |
| 5 | 辅助: HeartbeatKeeper、ReconnectPolicy、InterceptorPipeline、BufferPool | ~5 |
| 6 | 诊断: NetworkDiagnostics、PingMonitor、TrafficCounter、QualityEstimator | ~5 |
| 7 | HotFix 集成: MessageRegistry、NetworkInitializer、示例消息和 Handler | ~5 |

---

## 18. 风险

1. **线程安全**: 前期单线程，Transport.Poll() 在主线程 Update 驱动。预留接口不变，将来切换多线程只需改 Transport 收包侧加 ConcurrentQueue。
2. **IL2CPP/AOT**: MessagePack 用 SourceGenerator；protobuf-net v3 同理。热更程序集类型在 HybridCLR 下运行时反射可用。
3. **热更边界**: NetworkModule 在 Runtime（不热更），消息类和 Handler 在 HotFix。
4. **服务端恢复**: 需服务端配合 RPC 结果缓存，不支持时降级为 Fail。
5. **KCP UDP 穿透**: 部分网络环境封禁 UDP，需 TCP fallback。
6. **小游戏平台差异**: 各平台 WebSocket API 细节不同，需逐平台测试。
