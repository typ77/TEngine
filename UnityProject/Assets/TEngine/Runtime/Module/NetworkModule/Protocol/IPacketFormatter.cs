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
        /// <param name="maxAllowed">单包负载上限（字节）。声明长度超过此值或为负时返回 <see cref="PacketParseResult.IsOversized"/> 为 true 的结果，不分配大缓冲。</param>
        /// <returns>解析成功返回结果；数据不足返回 null；负载超限返回 <see cref="PacketParseResult.IsOversized"/> 为 true 的结果。</returns>
        PacketParseResult? TryParse(byte[] buffer, int offset, int count, int maxAllowed, out int consumed);

        /// <summary>将 RPC RequestId 写入已组装的 RPC 包（<see cref="BuildPacket"/> 置 Rpc flag 时留的占位位置）。</summary>
        /// <param name="packet">含 Rpc flag 的包（由 BuildPacket 生成，ReqId 占位为 0）。</param>
        /// <param name="requestId">RPC 请求 Id。</param>
        void WriteRequestId(byte[] packet, uint requestId);
    }
}
