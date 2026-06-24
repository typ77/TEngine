using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// TcpTransport 集成测试：用真实 <see cref="TcpListener"/> loopback 验证连接/收发/关闭/错误。
    /// </summary>
    /// <remarks>
    /// <b>线程模型</b>：测试方法均返回 void（Unity 内置 NUnit 不支持 async Task 测试方法）。
    /// ConnectAsync 返回 UniTask，其 continuation 走 PlayerLoop（主线程）——主线程阻塞等待会死锁，
    /// 故用 <see cref="Task.Run"/> 把 await 放到 ThreadPool 线程（无 SynchronizationContext），
    /// 主线程用 <see cref="Task.Wait(int)"/> 阻塞等待完成信号（来自 ThreadPool，不死锁）。
    /// socket 收发回调用 <see cref="TaskCompletionSource{T}"/>（工作线程 TrySetResult），主线程同样 Wait。
    /// </remarks>
    public class TcpTransportTests
    {
        /// <summary>启动监听随机 loopback 端口的 listener，返回其端口号。</summary>
        private static TcpListener StartLoopback(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return listener;
        }

        /// <summary>阻塞等待 socket 回调信号，超时则失败。</summary>
        private static void WaitForSignal<T>(TaskCompletionSource<T> tcs, int timeoutMs = 5000)
        {
            Assert.IsTrue(tcs.Task.Wait(timeoutMs), $"等待回调超时（{timeoutMs}ms）");
        }

        /// <summary>
        /// 在 ThreadPool 线程 await ConnectAsync（避免主线程 PlayerLoop 死锁）。
        /// 成功返回 null；失败返回捕获的异常。
        /// </summary>
        private static Exception AwaitConnect(TcpTransport transport, string host, int port, int timeoutMs = 5000)
        {
            var done = new TaskCompletionSource<bool>();
            Exception error = null;
            Task.Run(async () =>
            {
                try
                {
                    await transport.ConnectAsync(host, port);
                    done.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    error = ex;
                    done.TrySetResult(false);
                }
            });
            Assert.IsTrue(done.Task.Wait(timeoutMs), "ConnectAsync 超时");
            return error;
        }

        /// <summary>从流中同步精确读取 <paramref name="count"/> 字节（阻塞等待 socket 数据）。</summary>
        private static void ReadExact(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                Assert.Greater(read, 0, "对端提前关闭连接");
                offset += read;
            }
        }

        [Test]
        public void TransportType_Returns_Tcp()
        {
            using var transport = new TcpTransport();
            Assert.AreEqual(TransportType.Tcp, transport.TransportType);
        }

        [Test]
        public void Constructor_NonPositiveBufferSize_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransport(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TcpTransport(-8));
        }

        [Test]
        public void ConnectAsync_Loopback_ConnectedAndOnConnected()
        {
            var listener = StartLoopback(out int port);
            try
            {
                using var transport = new TcpTransport();
                bool connected = false;
                transport.OnConnected += () => connected = true;

                Exception error = AwaitConnect(transport, "127.0.0.1", port);
                Assert.IsNull(error, "ConnectAsync 不应抛异常");

                Assert.IsTrue(transport.Connected, "Connected 应为 true");
                Assert.IsTrue(connected, "OnConnected 应已触发");
                using var server = listener.AcceptTcpClient();
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Connect_Uri_Loopback_ConnectedAndOnConnected()
        {
            var listener = StartLoopback(out int port);
            try
            {
                using var transport = new TcpTransport();
                bool connected = false;
                transport.OnConnected += () => connected = true;

                // Connect(Uri) 内部走 Connect(host, port) 同步路径
                transport.Connect(new Uri($"tcp://127.0.0.1:{port}"));

                Assert.IsTrue(transport.Connected);
                Assert.IsTrue(connected);
                using var server = listener.AcceptTcpClient();
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void SendReceive_RoundTrip()
        {
            var listener = StartLoopback(out int port);
            try
            {
                using var transport = new TcpTransport();
                var receivedTcs = new TaskCompletionSource<byte[]>();
                transport.OnDataReceived += mem => receivedTcs.TrySetResult(mem.ToArray());

                Exception error = AwaitConnect(transport, "127.0.0.1", port);
                Assert.IsNull(error);
                using var server = listener.AcceptTcpClient();
                using var serverStream = server.GetStream();

                // client → server（同步发送/读取）
                transport.Send(new byte[] { 1, 2, 3, 4 });
                var serverBuf = new byte[4];
                ReadExact(serverStream, serverBuf, 4);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, serverBuf);

                // server → client（经 socket 工作线程的 OnDataReceived 回调）
                serverStream.Write(new byte[] { 5, 6, 7 }, 0, 3);
                WaitForSignal(receivedTcs);
                CollectionAssert.AreEqual(new byte[] { 5, 6, 7 }, receivedTcs.Task.Result);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Close_RaisesOnDisconnected()
        {
            var listener = StartLoopback(out int port);
            try
            {
                using var transport = new TcpTransport();
                bool disconnected = false;
                transport.OnDisconnected += () => disconnected = true;

                Exception error = AwaitConnect(transport, "127.0.0.1", port);
                Assert.IsNull(error);
                using var server = listener.AcceptTcpClient();

                transport.Close();

                Assert.IsTrue(disconnected, "Close 应触发 OnDisconnected");
                Assert.IsFalse(transport.Connected);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void ServerCloses_RaisesOnDisconnected()
        {
            var listener = StartLoopback(out int port);
            try
            {
                using var transport = new TcpTransport();
                var disconnectedTcs = new TaskCompletionSource<bool>();
                transport.OnDisconnected += () => disconnectedTcs.TrySetResult(true);

                Exception error = AwaitConnect(transport, "127.0.0.1", port);
                Assert.IsNull(error);
                using var server = listener.AcceptTcpClient();

                server.Close(); // 对端关闭 → client OnReceive 收 0 字节 → OnDisconnected

                WaitForSignal(disconnectedTcs);
                Assert.IsTrue(disconnectedTcs.Task.Result, "对端关闭应触发 OnDisconnected");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Send_NotConnected_Throws()
        {
            using var transport = new TcpTransport();
            Assert.Throws<InvalidOperationException>(() => transport.Send(new byte[] { 1 }));
            Assert.Throws<InvalidOperationException>(() =>
                transport.Send(new ReadOnlyMemory<byte>(new byte[] { 1 })));
        }

        [Test]
        public void ConnectAsync_Refused_RaisesOnErrorAndRethrows()
        {
            // 启动再立即停止监听，拿到一个几乎必然无人监听的端口
            var listener = StartLoopback(out int port);
            listener.Stop();

            using var transport = new TcpTransport();
            Exception onError = null;
            transport.OnError += ex => onError = ex;

            // 连接被拒 → catch 触发 OnError(TransportException) 并重抛原始 SocketException
            Exception thrown = AwaitConnect(transport, "127.0.0.1", port);

            Assert.IsNotNull(thrown, "ConnectAsync 连接被拒应重抛异常");
            Assert.IsInstanceOf<SocketException>(thrown, "应重抛原始 SocketException");
            Assert.IsNotNull(onError, "OnError 应被触发");
            Assert.IsInstanceOf<TransportException>(onError, "OnError 应包裹为 TransportException");
        }
    }
}
