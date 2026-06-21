namespace TEngine
{
    /// <summary>
    /// RPC 被主动中止（如断线且策略为 FailOnDisconnect，或收到对端 Abort）。
    /// </summary>
    public class RpcAbortException : NetworkException
    {
        /// <summary>RPC 消息 Id。</summary>
        public ushort MsgId { get; }

        /// <summary>本次 RPC 的请求 Id。</summary>
        public uint RequestId { get; }

        /// <summary>中止原因。</summary>
        public string Reason { get; }

        public RpcAbortException(string connectionName, ushort msgId,
            uint reqId, string reason)
            : base(connectionName,
                $"RPC aborted: msgId={msgId}, reqId={reqId}, reason={reason}")
        {
            MsgId = msgId;
            RequestId = reqId;
            Reason = reason;
        }
    }
}
