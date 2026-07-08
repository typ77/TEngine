using System;

namespace TEngine
{
    /// <summary>
    /// 网络模块所有异常的基类。统一携带 ConnectionName 便于定位出错连接。
    /// </summary>
    public class NetworkException : Exception
    {
        /// <summary>引发异常的连接名称。</summary>
        public string ConnectionName { get; }

        public NetworkException(string connectionName, string message)
            : base($"[{connectionName}] {message}")
        {
            ConnectionName = connectionName;
        }

        public NetworkException(string connectionName, string message, Exception innerException)
            : base($"[{connectionName}] {message}", innerException)
        {
            ConnectionName = connectionName;
        }
    }
}
