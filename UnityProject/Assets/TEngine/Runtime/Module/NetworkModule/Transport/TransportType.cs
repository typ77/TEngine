namespace TEngine
{
    /// <summary>
    /// 传输层类型。决定底层使用何种通道实现。
    /// </summary>
    public enum TransportType
    {
        /// <summary>TCP 长连接。</summary>
        Tcp,

        /// <summary>KCP（可靠 UDP，低延迟）。</summary>
        Kcp,

        /// <summary>WebSocket（常见于 WebGL / 小游戏降级）。</summary>
        WebSocket,

        /// <summary>平台原生通道（微信/字节等小游戏平台 SDK）。</summary>
        Platform,
    }
}
