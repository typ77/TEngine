using System;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 平台无关传输壳。通过注入 <see cref="IPlatformNetworkAdapter"/> 适配不同平台
    /// （微信/字节小游戏、WebGL 等）的底层网络 SDK，向 <see cref="NetworkConnection"/>
    /// 暴露统一的 <see cref="ITransport"/> 契约。
    /// </summary>
    /// <remarks>
    /// <para><b>错误传播</b>：<see cref="IPlatformNetworkAdapter"/> 契约不包含错误事件，
    /// 故 <see cref="OnError"/> 仅在 <see cref="Connect(string, int)"/> / <see cref="ConnectAsync"/>
    /// 捕获 adapter 同步抛出的异常时以 <see cref="TransportException"/> 转发；adapter 内部的
    /// 异步错误（如平台 SDK 回调失败）需由具体 adapter 实现自行兜底。</para>
    /// <para><b>连接状态</b>：<see cref="Connected"/> 由 adapter 的 OnConnected/OnDisconnected
    /// 事件驱动，而非以 adapter 是否注入为准。</para>
    /// <para><b>配套 adapter</b>：WebGL 平台的 <c>WebGLWebSocketAdapter</c>（封装 NativeWebSocket）
    /// 待任务 T11（安装 NativeWebSocket 后）补充。</para>
    /// </remarks>
    public class PlatformTransport : ITransport
    {
        /// <inheritdoc />
        public bool Connected => _connected;

        /// <inheritdoc />
        public TransportType TransportType => TransportType.Platform;

        private readonly IPlatformNetworkAdapter _adapter;
        private bool _connected;
        private bool _closed;
        private bool _disconnectedNotified;
        private UniTaskCompletionSource _connectTcs;

        /// <inheritdoc />
        public event Action OnConnected;

        /// <inheritdoc />
        public event Action OnDisconnected;

        /// <inheritdoc />
        public event Action<ReadOnlyMemory<byte>> OnDataReceived;

        /// <inheritdoc />
        public event Action<Exception> OnError;

        /// <param name="adapter">平台原生网络适配器（不可为 null）。</param>
        public PlatformTransport(IPlatformNetworkAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

            _adapter.OnConnected += HandleAdapterConnected;
            _adapter.OnDisconnected += HandleAdapterDisconnected;
            _adapter.OnDataReceived += HandleAdapterDataReceived;
        }

        // ============== 连接 ==============

        /// <inheritdoc />
        public void Connect(string host, int port)
        {
            ConnectCore($"ws://{host}:{port}");
        }

        /// <inheritdoc />
        public void Connect(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            ConnectCore(uri.ToString());
        }

        /// <summary>
        /// 发起连接：新建完成源，调用 adapter.Connect(url)。
        /// adapter 同步异常包装为 <see cref="TransportException"/> 并令 ConnectAsync 失败。
        /// </summary>
        private void ConnectCore(string url)
        {
            _connectTcs = new UniTaskCompletionSource();
            try
            {
                _adapter.Connect(url);
            }
            catch (Exception ex)
            {
                TransportException tex = new TransportException("Platform", "connect", ex);
                OnError?.Invoke(tex);
                _connectTcs.TrySetException(tex);
            }
        }

        /// <inheritdoc />
        public UniTask ConnectAsync(string host, int port)
        {
            Connect(host, port);
            return _connectTcs.Task;
        }

        // ============== 收发 ==============

        /// <inheritdoc />
        public void Send(byte[] data)
        {
            if (!Connected)
            {
                throw new InvalidOperationException("Not connected");
            }
            _adapter.Send(data);
        }

        /// <inheritdoc />
        public void Send(ReadOnlyMemory<byte> data)
        {
            if (!Connected)
            {
                throw new InvalidOperationException("Not connected");
            }
            _adapter.Send(data.ToArray());
        }

        // ============== 关闭 ==============

        /// <inheritdoc />
        public void Close()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            _connected = false;

            // adapter.Close() 可能同步触发 OnDisconnected（经 HandleAdapterDisconnected → NotifyDisconnected），
            // 也可能不触发；由 NotifyDisconnected 内部去重保证 OnDisconnected 仅触发一次。
            try
            {
                _adapter.Close();
            }
            catch
            {
                // adapter 关闭异常忽略：底层可能已断开
            }

            NotifyDisconnected();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Close();
        }

        // ============== adapter 事件转发 ==============

        private void HandleAdapterConnected()
        {
            _connected = true;
            // 先触发 OnConnected 事件，再完成 ConnectAsync：保证 await ConnectAsync 返回时
            // 订阅者已收到连接建立通知（TrySetResult 会内联推进 await continuation，
            // 若先 TrySetResult，事件可能在 await 方恢复后才触发，产生时序竞争）。
            OnConnected?.Invoke();
            _connectTcs?.TrySetResult();
        }

        private void HandleAdapterDisconnected()
        {
            _connected = false;
            NotifyDisconnected();
        }

        private void HandleAdapterDataReceived(byte[] data)
        {
            OnDataReceived?.Invoke(new ReadOnlyMemory<byte>(data));
        }

        /// <summary>
        /// 触发 OnDisconnected（去重：连接生命周期内仅通知一次）。
        /// 若断线发生在连接建立之前，令正在 await 的 ConnectAsync 以取消结束。
        /// </summary>
        private void NotifyDisconnected()
        {
            if (_disconnectedNotified)
            {
                return;
            }
            _disconnectedNotified = true;

            _connectTcs?.TrySetCanceled();

            OnDisconnected?.Invoke();
        }
    }
}
