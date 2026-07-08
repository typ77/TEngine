using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TEngine
{
    /// <summary>
    /// 消息分发器。接收方在主线程 Update 中调用 <see cref="Dispatch"/>，
    /// 将收到的消息按优先级、受预算约束地分发给已注册的 handler。
    /// <para>优先级策略：</para>
    /// <list type="bullet">
    /// <item><b>High</b>：本帧全部立即执行，不受 budget 限制。</item>
    /// <item><b>Normal</b>：受 <c>maxPerFrame</c> / <c>maxTimeMs</c> budget 约束。</item>
    /// <item><b>Low</b>：仅当本次 Dispatch 开始时 Normal 队列为空，才在本帧处理；
    /// 否则等到 Normal 清空后的下一帧。保证 Low 永远不抢占 Normal。</item>
    /// </list>
    /// </summary>
    public class MessageDispatcher
    {
        private readonly int _maxPerFrame;
        private readonly int _maxTimeMs;
        private readonly PriorityQueue _queue = new PriorityQueue();
        private readonly Dictionary<ushort, Delegate> _handlers = new Dictionary<ushort, Delegate>();

        /// <summary>消息即将分发前触发（可用于埋点 / 拦截）。</summary>
        public event Action<ushort, object, MessageTrace> OnBeforeDispatch;

        /// <summary>消息分发完成后触发。</summary>
        public event Action<ushort, object, MessageTrace> OnAfterDispatch;

        /// <summary>消息 handler 抛异常时触发（围栏1 可观测：上报 msgId/异常/trace，不静默吞）。</summary>
        public event Action<ushort, Exception, MessageTrace> OnDispatchError;

        /// <summary>最近一次 Dispatch 的统计快照。</summary>
        public DispatchStats Stats { get; private set; }

        public MessageDispatcher(int maxPerFrame = 50, int maxTimeMs = 4)
        {
            _maxPerFrame = maxPerFrame;
            _maxTimeMs = maxTimeMs;
        }

        /// <summary>
        /// 注册 / 覆盖某 msgId 的 handler。
        /// 不约束 T : <see cref="IMessage"/>——分发器只做 msgId→Delegate 路由 + DynamicInvoke，
        /// 消息类型合法性由 Codec / 业务层保证；放宽约束便于测试与跨层复用。
        /// </summary>
        public void RegisterHandler<T>(ushort msgId, Action<T> handler)
        {
            _handlers[msgId] = handler;
        }

        /// <summary>取消某 msgId 的 handler。</summary>
        public void UnregisterHandler(ushort msgId)
        {
            _handlers.Remove(msgId);
        }

        /// <summary>清空所有 handler。</summary>
        public void UnregisterAll()
        {
            _handlers.Clear();
        }

        /// <summary>入队一条待分发消息。</summary>
        public void Enqueue(ushort msgId, object message, MessagePriority priority, in MessageTrace trace)
        {
            _queue.Enqueue(new QueueEntry
            {
                MsgId = msgId,
                Message = message,
                Priority = priority,
                Trace = trace,
            });
        }

        /// <summary>
        /// 执行一帧分发。
        /// </summary>
        /// <param name="elapseSeconds">本帧时长（保留参数，时间预算实际由 <see cref="Stopwatch"/> 精确测量）。</param>
        public void Dispatch(float elapseSeconds)
        {
            var sw = Stopwatch.StartNew();
            int dispatched = 0;
            int dropped = 0;

            // 1. High: 全部立即执行，不受 budget 限制
            var highEnum = _queue.DequeueAllHigh();
            while (highEnum.MoveNext())
            {
                var entry = highEnum.Current;
                DispatchOne(in entry, ref dropped);
                dispatched++;
            }

            // 2. Normal + Low: 按 budget。
            //    Low 仅在本次 Dispatch 开始时 Normal 队列为空才允许处理，保证
            //    Normal 未清空时 Low 永远排在后面（优先级语义）。
            bool lowAllowed = (_queue.NormalCount == 0);
            int budgetCount = 0;
            while (budgetCount < _maxPerFrame && sw.ElapsedMilliseconds < _maxTimeMs)
            {
                if (!_queue.TryDequeueNormalOrLow(lowAllowed, out var entry))
                    break;
                DispatchOne(in entry, ref dropped);
                dispatched++;
                budgetCount++;
            }

            sw.Stop();

            Stats = new DispatchStats
            {
                HighQueueCount = _queue.HighCount,
                NormalQueueCount = _queue.NormalCount,
                LowQueueCount = _queue.LowCount,
                DispatchedThisFrame = dispatched,
                TotalDispatched = Stats.TotalDispatched + dispatched,
                TotalDropped = Stats.TotalDropped + dropped,
                LastFrameTimeMs = (float)sw.Elapsed.TotalMilliseconds,
            };
        }

        private void DispatchOne(in QueueEntry entry, ref int dropped)
        {
            try
            {
                OnBeforeDispatch?.Invoke(entry.MsgId, entry.Message, entry.Trace);

                if (_handlers.TryGetValue(entry.MsgId, out var del))
                {
                    del.DynamicInvoke(entry.Message);
                }

                OnAfterDispatch?.Invoke(entry.MsgId, entry.Message, entry.Trace);
            }
            catch (Exception ex)
            {
                // 围栏1：单条 handler 异常上报（含 msgId 上下文），不击垮分发循环
                dropped++;
                OnDispatchError?.Invoke(entry.MsgId, ex, entry.Trace);
            }
        }
    }
}
