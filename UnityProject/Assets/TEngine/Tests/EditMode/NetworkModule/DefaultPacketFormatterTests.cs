using System;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class DefaultPacketFormatterTests
    {
        private DefaultPacketFormatter _formatter;

        // 与 ConnectionConfig.MaxPacketSize 默认值一致（1 MiB）。
        private const int MaxAllowed = 1048576;

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

            var result = _formatter.TryParse(packet, 0, packet.Length, MaxAllowed, out int consumed);

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
            var result = _formatter.TryParse(packet, 0, 5, MaxAllowed, out int consumed);

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

            var r1 = _formatter.TryParse(combined, 0, combined.Length, MaxAllowed, out int c1);
            Assert.IsNotNull(r1);
            Assert.AreEqual(0x1001, r1.Value.MsgId);
            Assert.AreEqual(p1.Length, c1);

            var r2 = _formatter.TryParse(combined, c1, combined.Length - c1, MaxAllowed, out int c2);
            Assert.IsNotNull(r2);
            Assert.AreEqual(0x1002, r2.Value.MsgId);
            Assert.AreEqual(p2.Length, c2);
        }

        // ---- 超限保护（packet-validation）----

        /// <summary>
        /// 恶意包：length 声明为 int.MaxValue。必须返回 IsOversized 而非触发 OOM。
        /// 这是本加固的核心目标漏洞：旧实现 headerLen+length 溢出成负数 → 绕过 count 检查 → new byte[~2GB] 崩溃。
        /// </summary>
        [Test]
        public void TryParse_MaxIntLength_ReturnsIsOversized_WithoutOom()
        {
            // 手工构造包头：MsgId(2) + Flags(1) + Length(4=int.MaxValue BE)，不含负载。
            var malicious = new byte[]
            {
                0x12, 0x34,                     // MsgId
                0x00,                           // Flags
                0x7F, 0xFF, 0xFF, 0xFF,         // Length = int.MaxValue (big-endian)
            };

            var result = _formatter.TryParse(malicious, 0, malicious.Length, MaxAllowed, out int consumed);

            // 若发生 OOM，测试进程在此前已崩；能走到断言即证明无大内存分配。
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Value.IsOversized);
            Assert.AreEqual(0x1234, result.Value.MsgId);
            Assert.IsNull(result.Value.Payload);
            Assert.AreEqual(0, consumed);
        }

        /// <summary>length 最高字节 ≥ 0x80 时被解析为负数（符号位为 1），应判超限。</summary>
        [Test]
        public void TryParse_NegativeLength_ReturnsIsOversized()
        {
            var malicious = new byte[]
            {
                0x20, 0x00,                     // MsgId
                0x00,                           // Flags
                0x80, 0x00, 0x00, 0x00,         // Length 解析为 -2147483648（int 符号位）
            };

            var result = _formatter.TryParse(malicious, 0, malicious.Length, MaxAllowed, out int consumed);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Value.IsOversized);
        }

        /// <summary>自定义较小上限：length 超过 maxAllowed 即判超限（即便值本身合法）。</summary>
        [Test]
        public void TryParse_CustomMaxAllowed_RejectsOversized()
        {
            // 构造 length=20 的包头，但 maxAllowed=10；校验在 length 解析后立即触发，无需真实负载。
            var packet = new byte[]
            {
                0x10, 0x00,                     // MsgId
                0x00,                           // Flags
                0x00, 0x00, 0x00, 0x14,         // Length = 20
            };

            var result = _formatter.TryParse(packet, 0, packet.Length, 10, out int consumed);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Value.IsOversized);
            Assert.AreEqual(0, consumed);
        }

        /// <summary>合法大包（接近但未超上限）正常解析，不受超限校验误伤。</summary>
        [Test]
        public void TryParse_LegalLargePacket_ParsesOk()
        {
            const int payloadLen = 50000;
            var payload = new byte[payloadLen];
            payload[0] = 0xAB;
            payload[payloadLen - 1] = 0xCD;
            var packet = _formatter.BuildPacket(0x3000, 0x00, payload);

            var result = _formatter.TryParse(packet, 0, packet.Length, MaxAllowed, out int consumed);

            Assert.IsNotNull(result);
            Assert.IsFalse(result.Value.IsOversized);
            Assert.AreEqual(payloadLen, result.Value.Payload.Length);
            Assert.AreEqual(0xAB, result.Value.Payload[0]);
            Assert.AreEqual(0xCD, result.Value.Payload[payloadLen - 1]);
            Assert.AreEqual(packet.Length, consumed);
        }
    }
}
