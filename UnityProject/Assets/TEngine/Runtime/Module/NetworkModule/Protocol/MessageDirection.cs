namespace TEngine
{
    /// <summary>
    /// 消息方向。用于 <see cref="MessageTrace"/> 与拦截器判定。
    /// </summary>
    public enum MessageDirection
    {
        /// <summary>发送方向。</summary>
        Send,

        /// <summary>接收方向。</summary>
        Receive,
    }
}
