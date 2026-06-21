namespace TEngine
{
    /// <summary>
    /// 消息优先级。用于 Dispatcher 的优先级队列调度。
    /// </summary>
    public enum MessagePriority
    {
        /// <summary>高优先级（如战斗指令、RPC），优先分发。</summary>
        High = 0,

        /// <summary>普通优先级（默认）。</summary>
        Normal = 1,

        /// <summary>低优先级（如心跳、统计上报）。</summary>
        Low = 2,
    }
}
