using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// 方向 1b 线程安全（方案② 统一事件队列）验证：transport 事件在 I/O 线程仅入队，
    /// 主线程 <see cref="NetworkConnection.Update"/> 中 drain 处理。
    /// 验证核心承诺：处理在主线程、多线程并发入队不丢失/不崩溃。
    /// </summary>
    public class TransportThreadSafetyTests
    {
        private FakeTransport _transport;
        private FakeCodec _codec;
        private DefaultPacketFormatter _formatter;
        private NetworkConnection _conn;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _codec = new FakeCodec();
            _formatter = new DefaultPacketFormatter();
            var config = new ConnectionConfig("ts")
                .UseTcp()
                .SetEndpoint("127.0.0.1", 8080)
                .UseMessagePack();
            _conn = new NetworkConnection(config, _transport, _codec, _formatter, null, null, null);
        }

        /// <summary>
        /// 事件处理必须在主线程 Update drain，而非 I/O 线程直接处理（方案② 核心保证）。
        /// 若方案未生效（旧 HandleReceive 直接在回调线程处理），handlerThreadId 会是 ThreadPool 线程。
        /// </summary>
        [Test]
        public void Events_ProcessedOnMainThread_NotOnIoThread()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.DeserializeResult = new TestMessage();
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int handlerThreadId = -1;
            _conn.On<TestMessage>(100, msg => handlerThreadId = Thread.CurrentThread.ManagedThreadId);

            byte[] packet = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });
            // 从 ThreadPool 线程模拟 I/O 线程触发 OnDataReceived → 仅入队
            Task.Run(() => _transport.SimulateReceive(packet)).Wait(2000);

            // drain 前：handler 不应执行（事件在队列里，未处理）
            Assert.AreEqual(-1, handlerThreadId, "drain 前事件不应被处理");

            _conn.Update(0.016f); // drain → 主线程处理

            Assert.AreEqual(mainThreadId, handlerThreadId, "事件应在主线程 Update drain 处理，而非 I/O 线程");
        }

        /// <summary>
        /// 多线程并发入队（模拟高吞吐/多 I/O 线程）+ 主线程 drain，所有包无丢失、无异常。
        /// 验证 ConcurrentQueue 多生产者单消费者模型的正确性。
        /// </summary>
        [Test]
        public void ConcurrentEnqueue_AllDataProcessed_NoLoss()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.DeserializeResult = new TestMessage();
            int receivedCount = 0;
            _conn.On<TestMessage>(100, msg => Interlocked.Increment(ref receivedCount));

            const int total = 200;
            const int producerCount = 4;
            var producers = new Task[producerCount];
            for (int t = 0; t < producerCount; t++)
            {
                producers[t] = Task.Run(() =>
                {
                    for (int i = 0; i < total / producerCount; i++)
                    {
                        byte[] packet = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });
                        _transport.SimulateReceive(packet);
                    }
                });
            }
            Assert.IsTrue(Task.WaitAll(producers, 5000), "并发入队超时");

            // 主线程 drain：循环 Update 直到处理完或达到上限
            for (int i = 0; i < 20 && Volatile.Read(ref receivedCount) < total; i++)
            {
                _conn.Update(0.016f);
            }

            Assert.AreEqual(total, receivedCount, "所有并发入队的包应被 drain 处理，无丢失");
        }

        private class TestMessage : IMessage { }
    }
}
