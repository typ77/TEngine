using System;

namespace TEngine
{
    /// <summary>
    /// 连接状态机。封装 <see cref="ConnectionState"/> 的变迁与事件通知，
    /// 不强制校验非法迁移（调用方负责语义），保证任何 TransitionTo 都安全。
    /// </summary>
    internal class ConnectionStateMachine
    {
        public ConnectionState Current { get; private set; } = ConnectionState.None;

        /// <summary>状态变化时触发（相同状态不触发）。</summary>
        public event Action<ConnectionState> OnStateChanged;

        public void TransitionTo(ConnectionState state)
        {
            if (Current == state) return;
            Current = state;
            OnStateChanged?.Invoke(state);
        }
    }
}
