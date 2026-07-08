using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// 测试用 <see cref="IPlatformNetworkAdapter"/>：记录 Connect 的 url、所有 Send 的字节副本，
    /// 暴露方法模拟连接成功/断线/收包，供 <see cref="PlatformTransportTests"/> 注入。
    /// </summary>
    public class FakePlatformNetworkAdapter : IPlatformNetworkAdapter
    {
        /// <summary>最近一次 Connect 的 url（null 表示尚未 Connect）。</summary>
        public string LastConnectUrl;

        /// <summary>已发送的字节副本列表（按发送顺序）。</summary>
        public List<byte[]> SentData { get; } = new List<byte[]>();

        /// <summary>Close 是否被调用。</summary>
        public bool IsClosed;

        /// <summary>设非 null 时，Connect 抛此异常（模拟连接失败）。</summary>
        public Exception ConnectException { get; set; }

        /// <inheritdoc />
        public event Action OnConnected;

        /// <inheritdoc />
        public event Action OnDisconnected;

        /// <inheritdoc />
        public event Action<byte[]> OnDataReceived;

        /// <inheritdoc />
        public event Action<Exception> OnError;

        /// <inheritdoc />
        public void Connect(string url)
        {
            LastConnectUrl = url;
            if (ConnectException != null)
            {
                throw ConnectException;
            }
        }

        /// <inheritdoc />
        public void Send(byte[] data)
        {
            byte[] copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            SentData.Add(copy);
        }

        /// <inheritdoc />
        public void Close()
        {
            IsClosed = true;
        }

        // ---- 测试触发器 ----

        /// <summary>模拟底层连接成功（触发 OnConnected）。</summary>
        public void SimulateConnected() => OnConnected?.Invoke();

        /// <summary>模拟底层断线（触发 OnDisconnected）。</summary>
        public void SimulateDisconnected() => OnDisconnected?.Invoke();

        /// <summary>模拟底层收包（触发 OnDataReceived）。</summary>
        public void SimulateReceive(byte[] data) => OnDataReceived?.Invoke(data);

        /// <summary>模拟底层异步错误（触发 OnError，验证 M6 转发）。</summary>
        public void SimulateError(Exception ex) => OnError?.Invoke(ex);
    }

    /// <summary>
    /// <see cref="PlatformTransport"/> 单元测试：用 <see cref="FakePlatformNetworkAdapter"/>
    /// 验证连接/url 拼接/事件转发/收发/关闭幂等/ConnectAsync 完成与失败。
    /// </summary>
    /// <remarks>
    /// <b>线程模型</b>：测试方法返回 void（Unity 内置 NUnit 不支持 async Task 测试方法）。
    /// ConnectAsync 返回 UniTask，其 continuation 走 PlayerLoop（主线程）——主线程阻塞等待会死锁，
    /// 故用 <see cref="Task.Run"/> 把 await 放到 ThreadPool 线程（无 SynchronizationContext），
    /// 连接成功/断线信号由另一 ThreadPool 任务触发，主线程用 <see cref="Task.Wait(int)"/> 等待完成。
    /// （与 <see cref="TcpTransportTests"/> 同源，见 unity-editmode-async-test-pitfalls。）
    /// </remarks>
    public class PlatformTransportTests
    {
        // ============== 构造 / 基本属性 ==============

        [Test]
        public void Constructor_NullAdapter_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlatformTransport(null));
        }

        [Test]
        public void TransportType_Returns_Platform()
        {
            using var transport = new PlatformTransport(new FakePlatformNetworkAdapter());
            Assert.AreEqual(TransportType.Platform, transport.TransportType);
        }

        [Test]
        public void Connected_False_BeforeConnect()
        {
            using var transport = new PlatformTransport(new FakePlatformNetworkAdapter());
            Assert.IsFalse(transport.Connected);
        }

        // ============== 连接 ==============

        [Test]
        public void Connect_HostPort_BuildsWsUrl()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);

            transport.Connect("127.0.0.1", 9000);

            Assert.AreEqual("ws://127.0.0.1:9000", adapter.LastConnectUrl);
        }

        [Test]
        public void Connect_Uri_PassesToString()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);

            var uri = new Uri("wss://example.com/path");
            transport.Connect(uri);

            Assert.AreEqual(uri.ToString(), adapter.LastConnectUrl);
        }

        [Test]
        public void Connect_Uri_Null_Throws()
        {
            using var transport = new PlatformTransport(new FakePlatformNetworkAdapter());
            Assert.Throws<ArgumentNullException>(() => transport.Connect((Uri)null));
        }

        [Test]
        public void Connect_RaisesOnError_OnAdapterException()
        {
            var adapter = new FakePlatformNetworkAdapter
            {
                ConnectException = new InvalidOperationException("boom")
            };
            using var transport = new PlatformTransport(adapter);
            Exception onError = null;
            transport.OnError += ex => onError = ex;

            transport.Connect("127.0.0.1", 9000);

            Assert.IsNotNull(onError, "adapter 同步异常应触发 OnError");
            Assert.IsInstanceOf<TransportException>(onError);
        }

        // ============== 事件转发 ==============

        [Test]
        public void OnConnected_Forwarded_And_SetsConnected()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            bool raised = false;
            transport.OnConnected += () => raised = true;

            adapter.SimulateConnected();

            Assert.IsTrue(raised, "OnConnected 应转发");
            Assert.IsTrue(transport.Connected, "Connected 应为 true");
        }

        [Test]
        public void OnDisconnected_Forwarded_And_ClearsConnected()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();
            Assert.IsTrue(transport.Connected);

            bool raised = false;
            transport.OnDisconnected += () => raised = true;
            adapter.SimulateDisconnected();

            Assert.IsTrue(raised, "OnDisconnected 应转发");
            Assert.IsFalse(transport.Connected, "Connected 应为 false");
        }

        [Test]
        public void OnDataReceived_Forwarded_AsReadOnlyMemory()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            byte[] received = null;
            transport.OnDataReceived += mem => received = mem.ToArray();

            adapter.SimulateReceive(new byte[] { 7, 8, 9 });

            CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, received);
        }

        [Test]
        public void AdapterError_ForwardedToOnError()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            Exception received = null;
            transport.OnError += ex => received = ex;

            var error = new InvalidOperationException("adapter async fail");
            adapter.SimulateError(error);

            Assert.AreSame(error, received, "adapter OnError 应转发为 transport.OnError（M6）");
        }

        // ============== 收发 ==============

        [Test]
        public void Send_NotConnected_Throws()
        {
            using var transport = new PlatformTransport(new FakePlatformNetworkAdapter());
            Assert.Throws<InvalidOperationException>(() => transport.Send(new byte[] { 1 }));
            Assert.Throws<InvalidOperationException>(() =>
                transport.Send(new ReadOnlyMemory<byte>(new byte[] { 1 })));
        }

        [Test]
        public void Send_ByteArray_ForwardsToAdapter()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();

            transport.Send(new byte[] { 1, 2, 3 });

            Assert.AreEqual(1, adapter.SentData.Count);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, adapter.SentData[0]);
        }

        [Test]
        public void Send_ReadOnlyMemory_ForwardsToAdapter()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();

            transport.Send(new ReadOnlyMemory<byte>(new byte[] { 4, 5 }));

            Assert.AreEqual(1, adapter.SentData.Count);
            CollectionAssert.AreEqual(new byte[] { 4, 5 }, adapter.SentData[0]);
        }

        // ============== 关闭 ==============

        [Test]
        public void Close_ForwardsToAdapter_And_NotifiesDisconnected()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();

            bool raised = false;
            transport.OnDisconnected += () => raised = true;

            transport.Close();

            Assert.IsTrue(adapter.IsClosed, "adapter.Close 应被调用");
            Assert.IsTrue(raised, "Close 应触发 OnDisconnected");
            Assert.IsFalse(transport.Connected);
        }

        [Test]
        public void Close_Idempotent_NotifiesOnce()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();

            int count = 0;
            transport.OnDisconnected += () => count++;

            transport.Close();
            transport.Close();   // 重复 Close
            transport.Dispose(); // Dispose 等价 Close

            Assert.AreEqual(1, count, "OnDisconnected 应仅触发一次");
        }

        [Test]
        public void AdapterDisconnect_NotifiesOnce_EvenAfterClose()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();

            int count = 0;
            transport.OnDisconnected += () => count++;

            transport.Close();
            adapter.SimulateDisconnected(); // Close 后 adapter 再触发断线

            Assert.AreEqual(1, count, "Close 后的 adapter 断线不应重复通知");
        }

        // ============== 会话化（WS-A）：Connect 自清洁，支持重连复用 ==============

        [Test]
        public void Reconnect_AfterClose_RaisesOnDisconnectedAgain()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            adapter.SimulateConnected();
            Assert.IsTrue(transport.Connected);

            int disconnectCount = 0;
            transport.OnDisconnected += () => disconnectCount++;
            transport.Close();   // 第一次断开（_disconnectedNotified=true）
            Assert.AreEqual(1, disconnectCount);

            // 重连（ConnectCore 复位一次性标志）
            transport.Connect("127.0.0.1", 9000);
            adapter.SimulateConnected();
            Assert.IsTrue(transport.Connected, "重连应成功");

            // 第二次断开
            adapter.SimulateDisconnected();

            Assert.AreEqual(2, disconnectCount, "重连后再次断开应通知（S1：不复位标志会丢失）");
        }

        // ============== ConnectAsync（EditMode 异步需 ThreadPool）==============

        /// <summary>
        /// 在 ThreadPool 线程 await ConnectAsync（避免主线程 PlayerLoop 死锁）；
        /// 待 ConnectAsync 发起（adapter 收到 url）后，在另一后台任务触发连接成功/断线。
        /// 成功返回 null；失败返回捕获的异常。
        /// </summary>
        private static Exception AwaitConnect(PlatformTransport transport, FakePlatformNetworkAdapter adapter,
            bool succeed, int timeoutMs = 5000)
        {
            var done = new TaskCompletionSource<bool>();
            Exception error = null;
            Task.Run(async () =>
            {
                try
                {
                    await transport.ConnectAsync("127.0.0.1", 9000);
                    done.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    error = ex;
                    done.TrySetResult(false);
                }
            });

            // 轮询等待 ConnectAsync 已发起（adapter.LastConnectUrl 被设置），再在后台触发结果，
            // 避免 TrySetResult 与主线程内联导致的死锁。
            Task.Run(() =>
            {
                for (int i = 0; i < 100 && adapter.LastConnectUrl == null; i++)
                {
                    Thread.Sleep(10);
                }
                if (succeed)
                {
                    adapter.SimulateConnected();
                }
                else
                {
                    adapter.SimulateDisconnected();
                }
            });

            Assert.IsTrue(done.Task.Wait(timeoutMs), "ConnectAsync 超时");
            return error;
        }

        [Test]
        public void ConnectAsync_Completes_OnAdapterConnected()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);
            bool raised = false;
            transport.OnConnected += () => raised = true;

            Exception error = AwaitConnect(transport, adapter, succeed: true);

            Assert.IsNull(error, "ConnectAsync 不应抛异常");
            Assert.IsTrue(raised, "OnConnected 应触发");
            Assert.IsTrue(transport.Connected);
        }

        [Test]
        public void ConnectAsync_Fails_WhenDisconnectedBeforeConnected()
        {
            var adapter = new FakePlatformNetworkAdapter();
            using var transport = new PlatformTransport(adapter);

            Exception error = AwaitConnect(transport, adapter, succeed: false);

            Assert.IsNotNull(error, "连接前断线应令 ConnectAsync 失败");
            Assert.IsFalse(transport.Connected);
        }

        [Test]
        public void ConnectAsync_Fails_OnAdapterConnectException()
        {
            var adapter = new FakePlatformNetworkAdapter
            {
                ConnectException = new InvalidOperationException("boom")
            };
            using var transport = new PlatformTransport(adapter);
            Exception onError = null;
            transport.OnError += ex => onError = ex;

            // adapter.Connect 同步抛异常 → ConnectCore catch → OnError + TrySetException，
            // 无需外部触发，ConnectAsync 的 await 立即失败。
            var done = new TaskCompletionSource<bool>();
            Exception error = null;
            Task.Run(async () =>
            {
                try
                {
                    await transport.ConnectAsync("127.0.0.1", 9000);
                    done.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    error = ex;
                    done.TrySetResult(false);
                }
            });

            Assert.IsTrue(done.Task.Wait(5000), "ConnectAsync 超时");
            Assert.IsNotNull(error, "adapter.Connect 同步异常应令 ConnectAsync 失败");
            Assert.IsInstanceOf<TransportException>(error);
            Assert.IsNotNull(onError, "OnError 应被触发");
        }
    }
}
