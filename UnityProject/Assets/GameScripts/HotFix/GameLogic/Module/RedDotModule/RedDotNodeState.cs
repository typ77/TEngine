using GameLogic.RedDot.Core;

namespace GameLogic.RedDot
{
    /// <summary>
    /// 红点节点的最终显示状态（值类型，接入 MVE 的 <c>BindableProperty&lt;RedDotNodeState&gt;</c>）。
    /// <para>用 struct 而非 class，使 <c>BindableProperty</c> 的默认 <c>EqualityComparer</c> 做值比较，
    /// 状态/数量任一变化都能触发回调。</para>
    /// </summary>
    public readonly struct RedDotNodeState : System.IEquatable<RedDotNodeState>
    {
        /// <summary>红点状态（位掩码，仅最高位有效，表示当前应显示的最高优先级样式）。</summary>
        public readonly RedDotStatus Status;

        /// <summary>红点数量（仅 <see cref="RedDotStatus.Number"/> 时有意义）。</summary>
        public readonly int Count;

        public RedDotNodeState(RedDotStatus status, int count)
        {
            Status = status;
            Count = count;
        }

        /// <summary>是否有红点（状态非 Null）。</summary>
        public bool IsActive => Status != RedDotStatus.Null;

        /// <summary>空状态（无红点）。</summary>
        public static readonly RedDotNodeState Empty = new RedDotNodeState(RedDotStatus.Null, 0);

        public bool Equals(RedDotNodeState other) => Status == other.Status && Count == other.Count;

        public override bool Equals(object obj) => obj is RedDotNodeState other && Equals(other);

        public override int GetHashCode() => (int)Status * 7 + Count;

        public static bool operator ==(in RedDotNodeState a, in RedDotNodeState b) => a.Status == b.Status && a.Count == b.Count;
        public static bool operator !=(in RedDotNodeState a, in RedDotNodeState b) => !(a == b);

        public override string ToString() => IsActive ? $"{Status}({Count})" : "Empty";
    }
}
