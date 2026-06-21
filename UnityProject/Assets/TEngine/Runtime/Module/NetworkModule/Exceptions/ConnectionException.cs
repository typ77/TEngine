namespace TEngine
{
    /// <summary>
    /// 连接建立/断开过程出错。
    /// </summary>
    public class ConnectionException : NetworkException
    {
        public ConnectionException(string connectionName, string reason)
            : base(connectionName, $"Connection failed: {reason}") { }
    }
}
