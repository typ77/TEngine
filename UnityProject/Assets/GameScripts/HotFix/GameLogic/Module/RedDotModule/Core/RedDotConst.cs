namespace GameLogic.RedDot.Core
{
    /// <summary>
    /// 红点状态位掩码枚举。等级递增，可按位组合表示"节点允许出现的状态"。
    /// <para>运行时的 <see cref="RedDotVertexRuntimeInfo.RedDotStatus"/> 表示当前实际取到的最高状态。</para>
    /// </summary>
    public enum RedDotStatus
    {
        /// <summary>无红点。</summary>
        Null = 0,

        /// <summary>普通红点（布尔型，仅显示圆点）。</summary>
        Normal = 1,

        /// <summary>数量红点（显示具体数字）。</summary>
        Number = 2,

        /// <summary>新内容红点（"New" 标记）。</summary>
        New = 4,

        /// <summary>满额红点（如背包满）。</summary>
        Full = 8,
    }
}
