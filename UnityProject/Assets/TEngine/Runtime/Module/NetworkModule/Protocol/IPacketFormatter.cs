namespace TEngine
{
    /// <summary>
    /// 包格式化器：负责“业务字段 ↔ 线缆字节流”的编解码与粘包切分。
    /// </summary>
    public interface IPacketFormatter
    {
        /// <summary>
        /// 组装一个完整的网络包。
        /// </summary>
        /// <param name="msgId">消息 Id。</param>
        /// <param name="flags">包头标志位（见 <see cref="PacketFlags"/>）。</param>
        /// <param name="payload">负载数据。</param>
        /// <returns>含包头的完整字节数组。</returns>
        byte[] BuildPacket(ushort msgId, byte flags, byte[] payload);

        /// <summary>
        /// 尝试从缓冲区解析一个完整包（支持粘包切分）。
        /// </summary>
        /// <param name="buffer">数据缓冲区。</param>
        /// <param name="offset">起始偏移。</param>
        /// <param name="count">可用字节数。</param>
        /// <param name="consumed">本次实际消费的字节数（不完整包为 0）。</param>
        /// <returns>解析成功返回结果；数据不足返回 null。</returns>
        PacketParseResult? TryParse(byte[] buffer, int offset, int count, out int consumed);
    }
}
