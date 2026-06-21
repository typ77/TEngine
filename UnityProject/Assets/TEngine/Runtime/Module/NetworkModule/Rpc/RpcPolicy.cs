namespace TEngine
{
    /// <summary>
    /// RPC 在遭遇断线时的处置策略。
    /// </summary>
    public enum RpcPolicy
    {
        /// <summary>断线后挂起，重连成功后继续等待响应（断线恢复）。</summary>
        SafeRecover,

        /// <summary>断线即判定 RPC 失败并抛出 RpcAbortException。</summary>
        FailOnDisconnect,
    }
}
