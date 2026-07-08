namespace TEngine
{
    /// <summary>
    /// RPC 在遭遇断线时的处置策略。
    /// </summary>
    public enum RpcPolicy
    {
        /// <summary>
        /// 断线后挂起，重连成功后继续等待响应（断线恢复）。
        /// <para><b>当前版本等价于 <see cref="FailOnDisconnect"/></b>：重连成功后以
        /// <see cref="RpcAbortException"/> 中止 pending RPC，而非继续等待原响应。
        /// 真正的断线恢复协商协议（RpcRecoverRequest/Response）待后续版本实现。</para>
        /// </summary>
        SafeRecover,

        /// <summary>断线即判定 RPC 失败并抛出 <see cref="RpcAbortException"/>。</summary>
        FailOnDisconnect,
    }
}
