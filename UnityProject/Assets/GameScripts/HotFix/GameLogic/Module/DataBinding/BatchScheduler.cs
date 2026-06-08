namespace GameLogic.DataBinding
{
    /// <summary>
    /// 批量调度器，统一管理延迟回调。
    /// 最小化存根，任务 5 完成后替换为完整实现。
    /// </summary>
    public sealed class BatchScheduler : Singleton<BatchScheduler>
    {
        /// <summary>
        /// 标记目标为脏，将在下次 Flush 时触发回调。
        /// </summary>
        internal void MarkDirty(IBatchDirtyTarget target)
        {
            // 存根：暂不实现，任务 5 完成
        }
    }
}
