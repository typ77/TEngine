using System;

namespace TEngine
{
    /// <summary>
    /// 传输层（TCP/KCP/WebSocket/Platform）底层读写异常。
    /// </summary>
    public class TransportException : NetworkException
    {
        public TransportException(string connectionName, string operation, Exception inner)
            : base(connectionName, $"Transport error during {operation}") { }
    }
}
