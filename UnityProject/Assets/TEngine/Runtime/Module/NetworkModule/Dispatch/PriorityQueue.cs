using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 分发队列条目（内部类型）。
    /// </summary>
    internal struct QueueEntry
    {
        public ushort MsgId;
        public object Message;
        public MessagePriority Priority;
        public MessageTrace Trace;
    }

    /// <summary>
    /// 三级优先级队列（High / Normal / Low）。
    /// <para>出队规则：High 单独批量出队；Normal/Low 联合出队，Normal 优先，
    /// 且 <see cref="TryDequeueNormalOrLow(bool, out QueueEntry)"/> 仅在
    /// <c>allowLow=true</c> 时才会在 Normal 空后取 Low——用于保证
    /// "Normal 未清空时 Low 永远排在后面"的优先级语义。</para>
    /// </summary>
    internal class PriorityQueue
    {
        private readonly Queue<QueueEntry> _high = new Queue<QueueEntry>();
        private readonly Queue<QueueEntry> _normal = new Queue<QueueEntry>();
        private readonly Queue<QueueEntry> _low = new Queue<QueueEntry>();

        public int HighCount => _high.Count;
        public int NormalCount => _normal.Count;
        public int LowCount => _low.Count;

        public void Enqueue(in QueueEntry entry)
        {
            switch (entry.Priority)
            {
                case MessagePriority.High: _high.Enqueue(entry); break;
                case MessagePriority.Normal: _normal.Enqueue(entry); break;
                case MessagePriority.Low: _low.Enqueue(entry); break;
            }
        }

        /// <summary>
        /// 取出一条 Normal 或 Low 消息（Normal 优先）。
        /// </summary>
        /// <param name="allowLow">是否允许在 Normal 空时退而取 Low。</param>
        public bool TryDequeueNormalOrLow(bool allowLow, out QueueEntry entry)
        {
            if (_normal.Count > 0)
            {
                entry = _normal.Dequeue();
                return true;
            }
            if (allowLow && _low.Count > 0)
            {
                entry = _low.Dequeue();
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>允许 Low 的便捷重载。</summary>
        public bool TryDequeueNormalOrLow(out QueueEntry entry)
            => TryDequeueNormalOrLow(true, out entry);

        /// <summary>
        /// 取出全部 High 消息并清空 High 队列。
        /// 返回 <see cref="Queue{T}.Enumerator"/>，调用方用 MoveNext/Current 遍历。
        /// </summary>
        public Queue<QueueEntry>.Enumerator DequeueAllHigh()
        {
            var result = new Queue<QueueEntry>(_high);
            _high.Clear();
            return result.GetEnumerator();
        }

        public void Clear()
        {
            _high.Clear();
            _normal.Clear();
            _low.Clear();
        }
    }
}
