using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 基于 .NET <see cref="Socket"/> 的 TCP 传输实现。
    /// </summary>
    /// <remarks>
    /// <b>线程模型</b>：<see cref="BeginReceive"/> 的回调在 socket 工作线程触发，
    /// <see cref="OnDataReceived"/> / <see cref="OnDisconnected"/> / <see cref="OnError"/>
    /// 均可能在该工作线程被调用。上层 <see cref="NetworkConnection"/> 已通过事件队列将这些回调
    /// marshal 到主线程 <c>Update</c> 处理（见 <c>DrainTransportEvents</c>），订阅者无需自行加锁。
    /// </remarks>
    public class TcpTransport : ITransport
    {
        /// <inheritdoc />
        public bool Connected => _socket != null && _socket.Connected;

        /// <inheritdoc />
        public TransportType TransportType => TransportType.Tcp;

        // volatile：保证 I/O 线程（OnReceive）与主线程（Connect/Close）间 _socket 引用的内存可见性，
        // 避免 Close 置 null 后接收回调读到旧引用而 NRE 误报（修 M3）。
        private volatile Socket _socket;
        private readonly byte[] _receiveBuffer;

        /// <inheritdoc />
        public event Action OnConnected;

        /// <inheritdoc />
        public event Action OnDisconnected;

        /// <inheritdoc />
        public event Action<ReadOnlyMemory<byte>> OnDataReceived;

        /// <inheritdoc />
        public event Action<Exception> OnError;

        /// <param name="bufferSize">单次接收缓冲大小（字节，默认 64KB）。</param>
        public TcpTransport(int bufferSize = 65536)
        {
            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize), "bufferSize 必须为正");
            }
            _receiveBuffer = new byte[bufferSize];
        }

        // ============== 连接 ==============

        /// <inheritdoc />
        public void Connect(string host, int port)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(host, port);
                BeginReceive();
                OnConnected?.Invoke();
            }
            catch (Exception ex)
            {
                // 触发事件供事件驱动上层感知，同时重抛以便同步调用方 try-catch 捕获（与 ConnectAsync 一致，修 M4）
                OnError?.Invoke(new TransportException("Tcp", "connect", ex));
                throw;
            }
        }

        /// <inheritdoc />
        public void Connect(Uri uri) => Connect(uri.Host, uri.Port);

        /// <inheritdoc />
        public async UniTask ConnectAsync(string host, int port)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await _socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(host), port));
                BeginReceive();
                OnConnected?.Invoke();
            }
            catch (Exception ex)
            {
                // 触发事件供事件驱动上层感知，同时重抛以便 await 调用方捕获
                OnError?.Invoke(new TransportException("Tcp", "connect", ex));
                throw;
            }
        }

        // ============== 收发 ==============

        /// <inheritdoc />
        public void Send(byte[] data)
        {
            if (!Connected)
            {
                throw new InvalidOperationException("Not connected");
            }
            _socket.Send(data);
        }

        /// <inheritdoc />
        public void Send(ReadOnlyMemory<byte> data)
        {
            if (!Connected)
            {
                throw new InvalidOperationException("Not connected");
            }
            // 零拷贝快赢：ROM 通常由 byte[] 包装，TryGetArray 取底层 array 直接发；仅非连续 ROM 才回退 ToArray（修 D8 / 4.4）。
            // BuildPacket/TryParse 的池化涉及 buffer 所有权重构，移交 hotpath-allocation-reduction 独立 spec。
            if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment))
            {
                _socket.Send(segment.Array, segment.Offset, segment.Count, SocketFlags.None);
            }
            else
            {
                _socket.Send(data.ToArray());
            }
        }

        // ============== 关闭 ==============

        /// <inheritdoc />
        public void Close()
        {
            if (_socket != null)
            {
                try
                {
                    _socket.Close();
                }
                catch
                {
                    // 关闭异常忽略：socket 可能已断开
                }
                _socket = null;
                OnDisconnected?.Invoke();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Close();
        }

        // ============== 异步接收循环 ==============

        private void BeginReceive()
        {
            if (_socket == null || !Connected)
            {
                return;
            }
            try
            {
                _socket.BeginReceive(_receiveBuffer, 0, _receiveBuffer.Length,
                    SocketFlags.None, OnReceive, null);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(new TransportException("Tcp", "receive", ex));
            }
        }

        private void OnReceive(IAsyncResult ar)
        {
            // 防御性捕获局部引用：Close 可能在回调期间将 _socket 置 null，用局部变量避免 NRE 误报（修 M3）。
            Socket sock = _socket;
            if (sock == null)
            {
                return; // Close 已置 null，正常退出接收循环
            }
            try
            {
                int bytesReceived = sock.EndReceive(ar);
                if (bytesReceived == 0)
                {
                    // 对端正常关闭
                    OnDisconnected?.Invoke();
                    return;
                }

                // 防御性拷贝：_receiveBuffer 在下一次 BeginReceive 时会被复用，
                // 直接传 ReadOnlyMemory 引用存在被覆盖风险（若上层异步处理）。
                byte[] copy = new byte[bytesReceived];
                Buffer.BlockCopy(_receiveBuffer, 0, copy, 0, bytesReceived);
                OnDataReceived?.Invoke(new ReadOnlyMemory<byte>(copy));

                BeginReceive();
            }
            catch (ObjectDisposedException)
            {
                // socket 已 Close，正常退出接收循环
            }
            catch (Exception ex)
            {
                OnError?.Invoke(new TransportException("Tcp", "receive", ex));
            }
        }
    }
}
