using System;
using System.Collections.Generic;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class NetworkConnectionTests
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
            var config = new ConnectionConfig("test")
                .UseTcp()
                .SetEndpoint("127.0.0.1", 8080)
                .UseMessagePack();
            _conn = new NetworkConnection(config, _transport, _codec, _formatter, null, null, null);
        }

        // ---- 生命周期 ----

        [Test]
        public void ConnectAsync_FromNone_TransitionsToConnected()
        {
            bool connectedFired = false;
            _conn.OnConnected += () => connectedFired = true;

            _conn.ConnectAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ConnectionState.Connected, _conn.State);
            Assert.IsTrue(connectedFired);
        }

        // ---- 发送 ----

        [Test]
        public void Send_Connected_FlushesToTransport()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();

            _conn.Send(100, new TestMessage());

            // 未 Update 前，包在 SendQueue 中未送出
            Assert.AreEqual(0, _transport.SentPackets.Count);

            _conn.Update(0.016f);

            Assert.AreEqual(1, _transport.SentPackets.Count);
            Assert.AreEqual(1, _codec.SerializeCallCount);
        }

        [Test]
        public void Send_NotConnected_QueuesUntilConnected()
        {
            // 未连接时 Send 入队，Update 不 flush（非 Connected 态）
            _conn.Send(100, new TestMessage());
            _conn.Update(0.016f);
            Assert.AreEqual(0, _transport.SentPackets.Count);

            // 连接后 Update 刷新队列
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _conn.Update(0.016f);
            Assert.AreEqual(1, _transport.SentPackets.Count);
        }

        [Test]
        public void Send_SerializeFails_RaisesErrorWithoutThrowing()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.ThrowOnSerialize = true;
            Exception raised = null;
            _conn.OnError += ex => raised = ex;

            Assert.DoesNotThrow(() => _conn.Send(100, new TestMessage()));

            Assert.IsNotNull(raised);
            Assert.IsInstanceOf<SerializationException>(raised);
            // 序列化失败不应入队
            _conn.Update(0.016f);
            Assert.AreEqual(0, _transport.SentPackets.Count);
        }

        // ---- 接收 ----

        [Test]
        public void Receive_ParsesPacket_DispatchesToHandler()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            var received = new TestMessage();
            _codec.DeserializeResult = received;
            TestMessage dispatched = null;
            _conn.On<TestMessage>(100, msg => dispatched = msg);

            byte[] packet = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });
            _transport.SimulateReceive(packet);
            _conn.Update(0.016f);

            Assert.AreSame(received, dispatched);
            Assert.AreEqual(1, _codec.DeserializeCallCount);
        }

        [Test]
        public void Receive_HalfPacket_WaitsForMore()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.DeserializeResult = new TestMessage();
            bool dispatched = false;
            _conn.On<TestMessage>(100, msg => dispatched = true);

            byte[] packet = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });
            byte[] firstHalf = new byte[4]; // 不完整包头+部分负载
            byte[] secondHalf = new byte[packet.Length - 4];
            System.Buffer.BlockCopy(packet, 0, firstHalf, 0, 4);
            System.Buffer.BlockCopy(packet, 4, secondHalf, 0, secondHalf.Length);

            _transport.SimulateReceive(firstHalf);
            _conn.Update(0.016f);
            Assert.IsFalse(dispatched, "半包不应分发");

            _transport.SimulateReceive(secondHalf);
            _conn.Update(0.016f);
            Assert.IsTrue(dispatched, "补齐后应分发");
        }

        [Test]
        public void Receive_StickyPackets_DispatchesAll()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.DeserializeResult = new TestMessage();
            int count = 0;
            _conn.On<TestMessage>(100, msg => count++);

            // 两个包粘连在一起发送
            byte[] p1 = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });
            byte[] p2 = _formatter.BuildPacket(100, 0, new byte[] { 0x02 });
            byte[] merged = new byte[p1.Length + p2.Length];
            System.Buffer.BlockCopy(p1, 0, merged, 0, p1.Length);
            System.Buffer.BlockCopy(p2, 0, merged, p1.Length, p2.Length);

            _transport.SimulateReceive(merged);
            _conn.Update(0.016f);

            Assert.AreEqual(2, count);
        }

        // ---- 订阅 ----

        [Test]
        public void On_Off_UnregistersHandler()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _codec.DeserializeResult = new TestMessage();
            bool dispatched = false;
            _conn.On<TestMessage>(100, msg => dispatched = true);
            _conn.Off(100);

            byte[] packet = _formatter.BuildPacket(100, 0, new byte[] { 0x01 });
            _transport.SimulateReceive(packet);
            _conn.Update(0.016f);

            Assert.IsFalse(dispatched, "Off 后不应分发");
        }

        // ---- 拦截器 ----

        [Test]
        public void Interceptor_RejectsSend_DropsSilently()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _conn.AddInterceptor(new BlockInterceptor());

            _conn.Send(100, new TestMessage());
            _conn.Update(0.016f);

            Assert.AreEqual(0, _transport.SentPackets.Count, "拦截器拒绝应丢弃");
        }

        // ---- fakes ----

        private class TestMessage : IMessage { }

        private class BlockInterceptor : IMessageInterceptor
        {
            public string Name => "block";
            public int Order => 100;
            public bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace) => false;
            public bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace) => true;
        }
    }

    /// <summary>
    /// 可配置的 <see cref="ICodec"/> fake：Serialize 返回固定 payload，Deserialize 返回固定对象。
    /// </summary>
    public class FakeCodec : ICodec
    {
        public CodecType Type => CodecType.MessagePack;

        /// <summary>Serialize 返回的固定 payload。</summary>
        public byte[] SerializeResult { get; set; } = new byte[] { 0xAA };

        /// <summary>Deserialize 返回的固定对象（Receive 测试设为具体消息实例）。</summary>
        public object DeserializeResult { get; set; } = new object();

        /// <summary>设为 true 时 Serialize 抛异常（测试错误路径）。</summary>
        public bool ThrowOnSerialize { get; set; }

        public int SerializeCallCount;
        public int DeserializeCallCount;

        public void RegisterMessage<T>(ushort msgId) where T : IMessage { }

        public byte[] Serialize<T>(T message) where T : IMessage
        {
            SerializeCallCount++;
            if (ThrowOnSerialize) throw new System.Exception("fake serialize failure");
            return SerializeResult;
        }

        public object Deserialize(ushort msgId, byte[] data, int offset, int count)
        {
            DeserializeCallCount++;
            return DeserializeResult;
        }
    }
}
