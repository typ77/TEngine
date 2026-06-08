using System.Collections.Generic;
using GameLogic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 帧级批次合并调度器。
    /// 收集同帧内的脏标记目标，在 LateUpdate 时统一触发回调。
    /// 支持两轮 Flush，以处理回调中产生的新脏标记（如 DataContext→View 链式更新）。
    /// </summary>
    public sealed class BatchScheduler : Singleton<BatchScheduler>, ILateUpdate
    {
        private readonly HashSet<IBatchDirtyTarget> _dirty = new();
        private bool _isFlushing;

        /// <summary>
        /// 标记目标为脏，将在下次 Flush 时触发回调。
        /// </summary>
        internal void MarkDirty(IBatchDirtyTarget target)
        {
            _dirty.Add(target);
        }

        /// <summary>
        /// LateUpdate 驱动，由 SingletonSystem 每帧调用。
        /// </summary>
        public void OnLateUpdate()
        {
            Flush();
        }

        /// <summary>
        /// 执行两轮 Flush：第一轮处理已有脏标记，第二轮处理回调中新产生的脏标记。
        /// </summary>
        internal void Flush()
        {
            if (_isFlushing || _dirty.Count == 0) return;
            _isFlushing = true;

            try
            {
                // 第一轮
                var round1 = new List<IBatchDirtyTarget>(_dirty);
                _dirty.Clear();
                foreach (var target in round1)
                    target.FireCallback();

                // 第二轮
                if (_dirty.Count > 0)
                {
                    var round2 = new List<IBatchDirtyTarget>(_dirty);
                    _dirty.Clear();
                    foreach (var target in round2)
                        target.FireCallback();
                }
            }
            finally
            {
                _isFlushing = false;
            }
        }

        /// <summary>
        /// 是否有待处理的脏标记。
        /// </summary>
        public bool HasPendingChanges => _dirty.Count > 0;
    }
}
