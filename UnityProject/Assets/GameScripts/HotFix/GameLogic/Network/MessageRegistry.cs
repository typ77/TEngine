using TEngine;

/// <summary>
/// 网络消息注册入口。集中登记业务消息的 msgId / 类型 / 优先级。
/// </summary>
/// <remarks>
/// 须在创建连接【之前】调用：<see cref="INetworkModule"/> 的全局注册仅注入之后新建的连接
/// （构造时注入 Codec 类型映射与 Dispatcher 优先级）。
/// </remarks>
public static class MessageRegistry
{
    public static void Register(INetworkModule network)
    {
        network.RegisterMessage<LoginReq>(1001);
        network.RegisterMessage<LoginResp>(1002, MessagePriority.High);
        // 后续按需追加业务消息注册
    }
}
