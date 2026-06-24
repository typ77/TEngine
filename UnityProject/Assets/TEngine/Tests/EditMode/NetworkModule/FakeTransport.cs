using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace TEngine.Tests
{
    /// <summary>
    /// 测试用 <see cref="ITransport"/> fake：记录所有 Send 的字节副本，暴露方法模拟服务端推送/断线/错误。
    /// 供 NetworkConnection（及阶段2/3）测试注入。
    /// </summary>
    public class FakeTransport : ITransport
    {
        /// <summary>已发送的包副本列表（按发送顺序）。</summary>
        public List<byte[]> SentPackets { get; } = new List<byte[]>();

        /// <summary>是否已连接（测试可读可写）。</summary>
        public bool Connected { get; set; }

        public TransportType TransportType => TransportType.Tcp;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<ReadOnlyMemory<byte>> OnDataReceived;
        public event Action<Exception> OnError;

        /// <summary>设非 null 时，Connect/ConnectAsync 抛此异常（模拟连接失败，供重连测试）。</summary>
        public Exception ConnectException { get; set; }

        public UniTask ConnectAsync(string host, int port)
        {
            if (ConnectException != null) throw ConnectException;
            Connected = true;
            OnConnected?.Invoke();
            return UniTask.CompletedTask;
        }

        public void Connect(string host, int port)
        {
            if (ConnectException != null) throw ConnectException;
            Connected = true;
            OnConnected?.Invoke();
        }

        public void Connect(Uri uri)
        {
            if (ConnectException != null) throw ConnectException;
            Connected = true;
            OnConnected?.Invoke();
        }

        public void Send(byte[] data)
        {
            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            SentPackets.Add(copy);
        }

        public void Send(ReadOnlyMemory<byte> data) => Send(data.ToArray());

        public void Close()
        {
            Connected = false;
            OnDisconnected?.Invoke();
        }

        public void Dispose() => Close();

        // ---- 测试触发器 ----

        /// <summary>模拟服务端推送原始字节（触发 OnDataReceived → NetworkConnection 解包）。</summary>
        public void SimulateReceive(byte[] data) => OnDataReceived?.Invoke(new ReadOnlyMemory<byte>(data));

        /// <summary>模拟底层断线（触发 OnDisconnected）。</summary>
        public void SimulateDisconnect()
        {
            Connected = false;
            OnDisconnected?.Invoke();
        }

        /// <summary>模拟底层错误（触发 OnError）。</summary>
        public void SimulateError(Exception ex) => OnError?.Invoke(ex);

        /// <summary>清空已发送包记录。</summary>
        public void ClearSent() => SentPackets.Clear();
    }
}
