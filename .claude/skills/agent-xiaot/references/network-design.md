# 网络层设计参考

> 小T的技术预研笔记 — 网络层架构方案

## 一、目标

为 TEngine 设计一个通用的网络模块抽象层，支持多种通信协议，并与服务端框架（GameNetty/Fantasy）深度适配。

## 二、支持的协议

| 协议 | 场景 | 优先级 |
|------|------|--------|
| TCP | 游戏长连接，可靠传输 | P0 |
| KCP | 游戏 UDP 可靠传输，比 TCP 更低的延迟 | P0 |
| WebSocket | WebGL / 微信小游戏 | P0 |
| HTTP/HTTPS | RESTful API，资源下载 | P1 |
| UDP | 语音/位置同步等非可靠场景 | P2 |

## 三、接口设计草案

```csharp
public interface INetworkModule
{
    // 连接管理
    UniTask<NetResult> ConnectAsync(string host, int port, NetProtocol protocol);
    void Disconnect();
    bool IsConnected { get; }
    
    // 发送
    UniTask SendAsync(INetPacket packet);
    void Send(INetPacket packet); // 不等待，fire-and-forget
    
    // 接收（事件模式）
    event Action<INetPacket> OnPacketReceived;
    event Action<NetDisconnectReason> OnDisconnected;
    
    // 心跳
    void SetHeartbeat(int intervalMs, INetPacket heartbeatPacket);
    
    // 序列化
    INetSerializer Serializer { get; set; } // 可插拔：ProtoBuf / MessagePack / JSON
}

public enum NetProtocol
{
    TCP,
    KCP,
    WebSocket,
}

public interface INetPacket
{
    int MsgId { get; }
    object Body { get; }
}

public interface INetSerializer
{
    byte[] Serialize<T>(T obj);
    T Deserialize<T>(byte[] data);
}
```