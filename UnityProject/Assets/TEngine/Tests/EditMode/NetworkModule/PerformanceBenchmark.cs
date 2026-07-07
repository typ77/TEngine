using System;
using NUnit.Framework;
using TEngine;
using UnityEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// 方向 4 性能基准（量化驱动）。量化收发路径 per-packet / per-call GC 分配。
    /// 输出到 console（filter "[Benchmark]"）。
    /// <para>注：Phase 1 先以 [Test] 运行拿 before/after 数据；Phase 4 规范化时改 [Explicit]
    /// 移出常规回归套件（见 network-performance spec performance-benchmark）。</para>
    /// </summary>
    [Explicit]
    public class PerformanceBenchmark
    {
        private FakeTransport _transport;
        private FakeCodec _codec;
        private DefaultPacketFormatter _formatter;
        private NetworkConnection _conn;

        private const string Tag = "[Benchmark]";

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _codec = new FakeCodec();
            _formatter = new DefaultPacketFormatter();
            var config = new ConnectionConfig("bench")
                .UseTcp().SetEndpoint("127.0.0.1", 8080).UseMessagePack();
            _conn = new NetworkConnection(config, _transport, _codec, _formatter, null, null, null);
        }

        /// <summary>SendQueue.DequeueAll 隔离基准（Phase 2 改造的直接对比目标）。</summary>
        [Test]
        public void Bench_SendQueue_DequeueAll_PerCall()
        {
            var sq = new SendQueue();
            var sample = new byte[] { 1, 2, 3, 4 };
            const int warmup = 500, n = 5000;
            for (int i = 0; i < warmup; i++) { sq.Enqueue(sample); sq.DequeueAll(); }

            long before = AllocatedBytes();
            for (int i = 0; i < n; i++) { sq.Enqueue(sample); sq.DequeueAll(); }
            long after = AllocatedBytes();
            Report("SendQueue.DequeueAll per-call", before, after, n);
        }

        /// <summary>接收路径 per-packet（OnReceive 拷贝 + TryParse payload + drain 分发）。</summary>
        [Test]
        public void Bench_Receive_PerPacket()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.DeserializeResult = new TestMessage();
            _conn.On<TestMessage>(100, msg => { });
            byte[] packet = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });

            const int warmup = 500, n = 5000;
            for (int i = 0; i < warmup; i++) { _transport.SimulateReceive(packet); _conn.Update(0.016f); }

            long before = AllocatedBytes();
            for (int i = 0; i < n; i++) { _transport.SimulateReceive(packet); _conn.Update(0.016f); }
            long after = AllocatedBytes();
            Report("Receive per-packet", before, after, n);
        }

        /// <summary>发送路径 per-packet（Serialize + BuildPacket + Enqueue + flush）。
        /// 注：含 FakeTransport.Send 的记录副本分配（fake 行为，真实 transport 不累积）。</summary>
        [Test]
        public void Bench_Send_PerPacket()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            var msg = new TestMessage();
            const int warmup = 500, n = 5000;
            // 预热 + 防止 SentPackets 无限累积：每 500 包清一次
            for (int i = 0; i < warmup; i++) { _conn.Send(100, msg); _conn.Update(0.016f); }
            _transport.ClearSent();

            long before = AllocatedBytes();
            for (int i = 0; i < n; i++)
            {
                _conn.Send(100, msg);
                _conn.Update(0.016f);
                if ((i & 511) == 0) _transport.ClearSent();
            }
            long after = AllocatedBytes();
            Report("Send per-packet (含 FakeTransport 记录副本)", before, after, n);
        }

        /// <summary>当前托管堆字节数（近似测量）。
        /// 注：Unity Mono 的 GC.GetAllocatedBytesForCurrentThread 实测返回 0（未真正实现），
        /// 故退用 GC.GetTotalMemory(false) 测存量。短生命周期小对象（如 SendQueue 快照 List）
        /// 可能已被 GC 回收而测不到，此值反映"较持久的存活分配"，仅作量级参考。</summary>
        private static long AllocatedBytes() => GC.GetTotalMemory(false);

        private static void Report(string label, long before, long after, int n)
        {
            long delta = after - before;
            long per = n > 0 ? delta / n : 0;
            Debug.Log($"{Tag} {label}: total={delta}B, per-iter≈{per}B (n={n})");
        }

        private class TestMessage : IMessage { }
    }
}
