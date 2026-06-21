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
        /// 取出当前所有待发送消息的快照。<b>非暂停状态</b>下会清空队列并返回快照；
        /// 暂停状态下返回空列表、保留消息。返回值仅供遍历。
        /// <para>注意：不能返回 <c>Queue&lt;Tgt;.Enumerator</c>——该 struct 自身没有
        /// <c>GetEnumerator()</c>，无法被 <c>foreach</c> 消费。</para>
        /// </summary>
        public List<byte[]> DequeueAll()
        {
            if (_paused || _queue.Count == 0)
                return EmptySnapshot;

            var result = new List<byte[]>(_queue.Count);
            while (_queue.Count > 0)
                result.Add(_queue.Dequeue());
            return result;
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
