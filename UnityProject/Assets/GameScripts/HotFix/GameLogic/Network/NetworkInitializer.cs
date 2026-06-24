using TEngine;

/// <summary>
/// 网络初始化框架。注册消息并创建默认连接。
/// </summary>
/// <remarks>
/// <b>依赖前置</b>：<see cref="CreateConnection"/> 内部 <c>CodecRegistry.Get(MessagePack)</c> 需要
/// MessagePack codec 已注册。在 T6b 提供 MessagePack codec 并 <see cref="INetworkModule.RegisterCodec"/>
/// 之前，调用本方法会因 codec 未注册抛 <see cref="System.InvalidOperationException"/>。
/// T6b 完成后由业务层在合适时机（如登录流程）调用。<br/>
/// <b>顺序</b>：先 <see cref="MessageRegistry.Register"/>（全局注册仅注入【之后】创建的连接），
/// 再 <c>CreateConnection</c>，确保新连接携带已注册的消息映射。<br/>
/// <b>连接</b>：<see cref="CreateConnection"/> 仅创建未连接的连接，不立即发起网络连接（需显式 ConnectAsync）。
/// </remarks>
public static class NetworkInitializer
{
    public static void Initialize(INetworkModule network)
    {
        // 1. 先注册消息（全局注册仅注入之后创建的连接）
        MessageRegistry.Register(network);

        // 2. 创建大厅基础连接（Tcp + MessagePack）
        var lobby = new ConnectionConfig("lobby")
            .UseTcp()
            .UseMessagePack()
            .SetEndpoint("127.0.0.1", 9000)
            .SetHeartbeat(10, 30)
            .SetReconnect(true, 1000, 5, 1.5f);

        network.CreateConnection(lobby);
    }
}
