using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 发送队列。连接在断线重连期间 <c>Pause</c> 暂停发送、缓存消息，恢复后批量送出。
    /// <para>非线程安全——调用方需保证在 Unity 主线程（Update）内驱动。</para>
    /// </summary>
    public class SendQueue
    {
        private readonly Queue<byte[]> _queue = new Queue<byte[]>();
        private bool _paused;

        // 复用快照列表：DequeueAll 每次填入此列表返回，避免每帧 new List（调用方仅即时遍历，勿跨帧持有）。
        private readonly List<byte[]> _snapshot = new List<byte[]>();

        /// <summary>当前队列中待发送的消息数（含暂停期间缓存的）。</summary>
        public int Count => _queue.Count;

        /// <summary>入队一个完整网络包。</summary>
        public void Enqueue(byte[] packet)
        {
            _queue.Enqueue(packet);
        }

        // 暂停/空队列时复用的空快照，避免无谓分配。调用方仅遍历，请勿修改。
        private static readonly List<byte[]> EmptySnapshot = new List<byte[]>();

        /// <summary>
        /// 取出当前所有待发送消息的快照（复用内部列表，避免每次分配）。<b>非暂停状态</b>下会清空队列并返回快照；
        /// 暂停状态下返回空列表、保留消息。
        /// <para><b>复用语义</b>：返回值仅供调用方即时遍历，<b>勿跨帧持有引用</b>——
        /// 下次调用会 <see cref="List{T}.Clear"/> 并复用同一内部列表，跨帧持有会被覆盖。</para>
        /// <para>注意：不能返回 <c>Queue&lt;T&gt;.Enumerator</c>——该 struct 自身没有
        /// <c>GetEnumerator()</c>，无法被 <c>foreach</c> 消费。</para>
        /// </summary>
        public List<byte[]> DequeueAll()
        {
            if (_paused || _queue.Count == 0)
                return EmptySnapshot;

            _snapshot.Clear();
            while (_queue.Count > 0)
                _snapshot.Add(_queue.Dequeue());
            return _snapshot;
        }

        /// <summary>暂停发送（断线期间调用，消息继续入队缓存）。</summary>
        public void Pause()
        {
            _paused = true;
        }

        /// <summary>恢复发送。</summary>
        public void Resume()
        {
            _paused = false;
        }

        /// <summary>清空队列并取消暂停。</summary>
        public void Clear()
        {
            _queue.Clear();
            _paused = false;
        }
    }
}
