using System.Diagnostics;

namespace TEngine
{
    /// <summary>
    /// 单条消息的追踪信息。随消息在拦截器管道、分发器、诊断系统中流转。
    /// </summary>
    public struct MessageTrace
    {
        /// <summary>消息序列号（连接级递增）。</summary>
        public uint SequenceId;

        /// <summary>时间戳（<see cref="Stopwatch.GetTimestamp"/>，用于延迟计算）。</summary>
        public long Timestamp;

        /// <summary>消息方向。</summary>
        public MessageDirection Direction;

        /// <summary>线缆字节数（含包头）。</summary>
        public int WireSize;

        public static MessageTrace Create(uint seq, MessageDirection dir, int wireSize)
        {
            return new MessageTrace
            {
                SequenceId = seq,
                Timestamp = Stopwatch.GetTimestamp(),
                Direction = dir,
                WireSize = wireSize,
            };
        }
    }
}
