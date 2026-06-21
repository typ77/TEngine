namespace TEngine
{
    /// <summary>
    /// 消息拦截器。在发送前/接收后对负载做加工（加密、压缩、鉴权、埋点等）。
    /// 多个拦截器按 <see cref="Order"/> 串成管道。
    /// </summary>
    public interface IMessageInterceptor
    {
        /// <summary>拦截器名称（用于按名移除）。</summary>
        string Name { get; }

        /// <summary>执行顺序（升序）。</summary>
        int Order { get; }

        /// <summary>
        /// 发送前拦截。返回 false 表示丢弃该消息。
        /// </summary>
        bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace);

        /// <summary>
        /// 接收后拦截。返回 false 表示丢弃该消息（不再进入分发）。
        /// </summary>
        bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace);
    }
}
