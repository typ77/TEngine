using System;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class DefaultPacketFormatterTests
    {
        private DefaultPacketFormatter _formatter;

        [SetUp]
        public void SetUp()
        {
            _formatter = new DefaultPacketFormatter();
        }

        [Test]
        public void BuildPacket_Event_CorrectStructure()
        {
            var payload = new byte[] { 0xAA, 0xBB };
            var packet = _formatter.BuildPacket(0x1234, 0x00, payload);

            // MsgId 2B + Flags 1B + Length 4B + Payload 2B = 9 bytes
            Assert.AreEqual(9, packet.Length);
            // MsgId big-endian
            Assert.AreEqual(0x12, packet[0]);
            Assert.AreEqual(0x34, packet[1]);
            // Flags
            Assert.AreEqual(0x00, packet[2]);
            // Length big-endian
            Assert.AreEqual(0x00, packet[3]);
            Assert.AreEqual(0x00, packet[4]);
            Assert.AreEqual(0x00, packet[5]);
            Assert.AreEqual(0x02, packet[6]);
            // Payload
            Assert.AreEqual(0xAA, packet[7]);
            Assert.AreEqual(0xBB, packet[8]);
        }

        [Test]
        public void BuildPacket_Rpc_IncludesRequestId()
        {
            var payload = new byte[] { 0xCC };
            var packet = _formatter.BuildPacket(0x5001, PacketFlags.Rpc, payload);

            // MsgId 2 + Flags 1 + Length 4 + ReqId 4 + Payload 1 = 12
            Assert.AreEqual(12, packet.Length);
            Assert.AreEqual(PacketFlags.Rpc, packet[2]);
            // ReqId 由调用方（RpcTracker）写入，此处验证槽位存在
        }

        [Test]
        public void TryParse_CompletePacket_ReturnsResult()
        {
            var payload = new byte[] { 0xDD };
            var packet = _formatter.BuildPacket(0x2000, 0x00, payload);

            var result = _formatter.TryParse(packet, 0, packet.Length, out int consumed);

            Assert.IsNotNull(result);
            Assert.AreEqual(0x2000, result.Value.MsgId);
            Assert.AreEqual(0x00, result.Value.Flags);
            Assert.AreEqual(1, result.Value.Payload.Length);
            Assert.AreEqual(0xDD, result.Value.Payload[0]);
            Assert.AreEqual(packet.Length, consumed);
        }

        [Test]
        public void TryParse_HalfPacket_ReturnsNull()
        {
            var payload = new byte[] { 0x01, 0x02, 0x03 };
            var packet = _formatter.BuildPacket(0x1000, 0x00, payload);

            // 只给前 5 字节（不完整包头）
            var result = _formatter.TryParse(packet, 0, 5, out int consumed);

            Assert.IsNull(result);
            Assert.AreEqual(0, consumed);
        }

        [Test]
        public void TryParse_TwoPackets_StickyPacket()
        {
            var p1 = _formatter.BuildPacket(0x1001, 0x00, new byte[] { 0xAA });
            var p2 = _formatter.BuildPacket(0x1002, 0x00, new byte[] { 0xBB });

            var combined = new byte[p1.Length + p2.Length];
            Buffer.BlockCopy(p1, 0, combined, 0, p1.Length);
            Buffer.BlockCopy(p2, 0, combined, p1.Length, p2.Length);

            var r1 = _formatter.TryParse(combined, 0, combined.Length, out int c1);
            Assert.IsNotNull(r1);
            Assert.AreEqual(0x1001, r1.Value.MsgId);
            Assert.AreEqual(p1.Length, c1);

            var r2 = _formatter.TryParse(combined, c1, combined.Length - c1, out int c2);
            Assert.IsNotNull(r2);
            Assert.AreEqual(0x1002, r2.Value.MsgId);
            Assert.AreEqual(p2.Length, c2);
        }
    }
}
