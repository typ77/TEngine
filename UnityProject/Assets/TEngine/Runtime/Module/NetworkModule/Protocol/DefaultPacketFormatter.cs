using System;

namespace TEngine
{
    /// <summary>
    /// 默认包格式化器。
    /// <para>普通包：<c>MsgId(2B,BE) + Flags(1B) + Length(4B,BE) + Payload</c>，包头 7B。</para>
    /// <para>RPC 包：在 Flags 后插入 <c>RequestId(4B,BE)</c>，包头 11B。</para>
    /// <para><see cref="TryParse"/> 支持粘包切分与半包等待。</para>
    /// <para>注：组包/解包产出的数组都要求精确大小（交给上层 Codec/业务使用），因此直接 <c>new byte[]</c> 分配。</para>
    /// </summary>
    public class DefaultPacketFormatter : IPacketFormatter
    {
        // 包头: MsgId(2B) + Flags(1B) + Length(4B) = 7 bytes
        private const int HeaderSize = 7;
        // RPC 包头: + ReqId(4B)
        private const int RpcHeaderSize = 11;

        /// <inheritdoc />
        public byte[] BuildPacket(ushort msgId, byte flags, byte[] payload)
        {
            bool isRpc = (flags & PacketFlags.Rpc) != 0;
            int headerLen = isRpc ? RpcHeaderSize : HeaderSize;
            // 防御：payload 过大导致 totalLen 整数溢出（发送侧对称保护）。
            if (payload.Length > int.MaxValue - headerLen)
            {
                throw new InvalidOperationException(
                    $"Payload length {payload.Length} exceeds the maximum allowed size (int.MaxValue - {headerLen}).");
            }
            int totalLen = headerLen + payload.Length;

            byte[] packet = new byte[totalLen];
            int offset = 0;

            // MsgId big-endian
            packet[offset++] = (byte)(msgId >> 8);
            packet[offset++] = (byte)(msgId & 0xFF);

            // Flags
            packet[offset++] = flags;

            // Length big-endian (payload length, not including ReqId in length)
            int length = payload.Length;
            packet[offset++] = (byte)(length >> 24);
            packet[offset++] = (byte)(length >> 16);
            packet[offset++] = (byte)(length >> 8);
            packet[offset++] = (byte)(length & 0xFF);

            // ReqId placeholder (if RPC) — actual ReqId set by caller (RpcTracker)
            if (isRpc)
            {
                packet[offset++] = 0;
                packet[offset++] = 0;
                packet[offset++] = 0;
                packet[offset++] = 0;
            }

            // Payload
            Buffer.BlockCopy(payload, 0, packet, offset, payload.Length);
            return packet;
        }

        /// <inheritdoc />
        public PacketParseResult? TryParse(byte[] buffer, int offset, int count, int maxAllowed, out int consumed)
        {
            consumed = 0;

            if (count < HeaderSize)
                return null;

            int pos = offset;

            // MsgId
            ushort msgId = (ushort)((buffer[pos] << 8) | buffer[pos + 1]);
            pos += 2;

            // Flags
            byte flags = buffer[pos++];

            bool isRpc = (flags & PacketFlags.Rpc) != 0;
            int headerLen = isRpc ? RpcHeaderSize : HeaderSize;

            if (count < headerLen)
                return null;

            // Length
            int length = (buffer[pos] << 24) | (buffer[pos + 1] << 16)
                       | (buffer[pos + 2] << 8) | buffer[pos + 3];
            pos += 4;

            // 超限校验：length 为负、超过 maxAllowed 时判定字节流不可信。
            // 必须在 headerLen + length 加法之前校验——否则大 length（接近 int.MaxValue）
            // 会使 headerLen + length 整数溢出成负数，绕过下方 count < totalPacketSize 检查，
            // 导致 new byte[length] 分配 ~2GB 触发 OOM（本加固的目标漏洞）。
            if (length < 0 || length > maxAllowed)
            {
                return new PacketParseResult
                {
                    IsOversized = true,
                    MsgId = msgId,
                    Flags = flags,
                    Payload = null,
                };
            }

            // ReqId (if RPC)
            uint reqId = 0;
            if (isRpc)
            {
                reqId = (uint)((buffer[pos] << 24) | (buffer[pos + 1] << 16)
                             | (buffer[pos + 2] << 8) | buffer[pos + 3]);
                pos += 4;
            }

            // 检查负载是否完整
            int totalPacketSize = headerLen + length;
            if (count < totalPacketSize)
                return null;

            // 提取负载（脱离接收缓冲区；精确大小交给上层 Codec/业务）
            byte[] payload = new byte[length];
            Buffer.BlockCopy(buffer, pos, payload, 0, length);

            consumed = totalPacketSize;

            return new PacketParseResult
            {
                MsgId = msgId,
                Flags = flags,
                Payload = payload,
                RequestId = reqId,
            };
        }
    }
}
