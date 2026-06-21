namespace TEngine
{
    /// <summary>
    /// 网络连接状态机的状态枚举。
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>尚未开始连接。</summary>
        None = 0,

        /// <summary>正在建立连接。</summary>
        Connecting = 1,

        /// <summary>已连接，可正常收发。</summary>
        Connected = 2,

        /// <summary>已断开（主动或被动），等待重连决策。</summary>
        Disconnected = 3,

        /// <summary>正在重连。</summary>
        Reconnecting = 4,

        /// <summary>重连彻底失败，连接已不可用。</summary>
        Failed = 5,

        /// <summary>已关闭并释放资源，不可再使用。</summary>
        Closed = 6,
    }
}
