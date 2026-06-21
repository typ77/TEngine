using System;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 传输层接口。抽象 TCP / KCP / WebSocket / Platform 等底层通道，
    /// 向上提供统一的连接、收发与事件回调。
    /// </summary>
    public interface ITransport : IDisposable
    {
        /// <summary>是否已连接。</summary>
        bool Connected { get; }

        /// <summary>传输类型。</summary>
        TransportType TransportType { get; }

        // ---- 连接 ----

        void Connect(string host, int port);
        void Connect(Uri uri);

        /// <summary>异步建立连接（推荐入口）。</summary>
        UniTask ConnectAsync(string host, int port);

        // ---- 收发 ----

        void Send(byte[] data);
        void Send(ReadOnlyMemory<byte> data);

        // ---- 关闭 ----

        void Close();

        // ---- 事件 ----

        event Action OnConnected;
        event Action OnDisconnected;
        event Action<ReadOnlyMemory<byte>> OnDataReceived;
        event Action<Exception> OnError;
    }
}
