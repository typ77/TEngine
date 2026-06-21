using System;

namespace TEngine
{
    /// <summary>
    /// 平台原生网络适配器。用于小游戏平台（微信/字节等）无法直接使用标准 socket 的场景。
    /// 由 <c>PlatformTransport</c> 包装，提供与 <see cref="ITransport"/> 一致的事件契约。
    /// </summary>
    public interface IPlatformNetworkAdapter
    {
        void Connect(string url);
        void Send(byte[] data);
        void Close();

        event Action OnConnected;
        event Action OnDisconnected;
        event Action<byte[]> OnDataReceived;
    }
}
