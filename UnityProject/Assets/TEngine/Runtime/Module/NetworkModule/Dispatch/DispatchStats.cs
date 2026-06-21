namespace TEngine
{
    /// <summary>
    /// 分发器统计数据快照（每帧 Dispatch 结束后更新）。
    /// </summary>
    public struct DispatchStats
    {
        /// <summary>剩余高优先级消息数。</summary>
        public int HighQueueCount;

        /// <summary>剩余普通优先级消息数。</summary>
        public int NormalQueueCount;

        /// <summary>剩余低优先级消息数。</summary>
        public int LowQueueCount;

        /// <summary>本帧分发的消息数。</summary>
        public int DispatchedThisFrame;

        /// <summary>累计分发消息数（跨帧）。</summary>
        public int TotalDispatched;

        /// <summary>累计丢弃消息数（handler 异常等）。</summary>
        public int TotalDropped;

        /// <summary>本帧分发耗时（毫秒）。</summary>
        public float LastFrameTimeMs;
    }
}
