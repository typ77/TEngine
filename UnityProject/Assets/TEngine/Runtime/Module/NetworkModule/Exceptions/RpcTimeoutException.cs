namespace TEngine
{
    /// <summary>
    /// RPC 请求超时未收到响应。
    /// </summary>
    public class RpcTimeoutException : NetworkException
    {
        /// <summary>RPC 消息 Id。</summary>
        public ushort MsgId { get; }

        /// <summary>本次 RPC 的请求 Id。</summary>
        public uint RequestId { get; }

        /// <summary>超时阈值（毫秒）。</summary>
        public int TimeoutMs { get; }

        public RpcTimeoutException(string connectionName, ushort msgId,
            uint reqId, int timeoutMs)
            : base(connectionName,
                $"RPC timeout: msgId={msgId}, reqId={reqId}, timeout={timeoutMs}ms")
        {
            MsgId = msgId;
            RequestId = reqId;
            TimeoutMs = timeoutMs;
        }
    }
}
