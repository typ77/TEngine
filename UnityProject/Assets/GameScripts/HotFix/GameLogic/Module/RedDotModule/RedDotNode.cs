using GameLogic.DataBinding;
using GameLogic.RedDot.Core;

namespace GameLogic.RedDot
{
    /// <summary>
    /// 红点节点的对外门面，持有 <see cref="BindableProperty{T}"/> 作为 MVE 数据入口。
    /// <para>UI 侧通过 <c>UIBase.Bind(node.State, callback)</c> 订阅，状态变化时自动刷新，UI 销毁自动解绑。</para>
    /// <para>每个 <see cref="RedDotNode"/> 与图层的 <see cref="RedDotVertex"/> 一一对应，
    /// 通过 <c>vertex.UserData</c> 双向关联。</para>
    /// </summary>
    public sealed class RedDotNode
    {
        /// <summary>红点全局唯一 key。</summary>
        public string Key { get; }

        /// <summary>该节点允许的红点状态位掩码（来自配置）。</summary>
        public RedDotStatus AllowedStatus { get; }

        /// <summary>关联的图层顶点（内部用）。</summary>
        internal RedDotVertex Vertex { get; }

        /// <summary>
        /// 节点的当前显示状态（MVE 入口）。UI 通过 <c>Bind(State, ...)</c> 订阅。
        /// </summary>
        public BindableProperty<RedDotNodeState> State { get; }

        internal RedDotNode(RedDotVertex vertex)
        {
            Vertex = vertex;
            Key = vertex.Key;
            AllowedStatus = (RedDotStatus)vertex.RedDotType;
            State = new BindableProperty<RedDotNodeState>(RedDotNodeState.Empty);
        }

        /// <summary>
        /// 把图聚合后的新状态推送到 <see cref="State"/>（由 RedDotGraph 的 OnVertexRefreshed 回调）。
        /// <para>赋值会触发 <see cref="BindableProperty{T}"/> 的脏标记，由 BatchScheduler 在帧末统一推送 UI。</para>
        /// </summary>
        internal void ApplyRefreshedState(RedDotStatus status, int value)
        {
            State.Value = new RedDotNodeState(status, value);
        }

        /// <summary>
        /// 释放持有的 BindableProperty（模块销毁时调用）。
        /// </summary>
        internal void Dispose()
        {
            State.Dispose();
        }
    }
}
