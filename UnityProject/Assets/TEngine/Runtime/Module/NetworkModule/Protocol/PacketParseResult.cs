namespace TEngine
{
    /// <summary>
    /// 单个网络包解析结果。<see cref="IPacketFormatter.TryParse"/> 在成功解析一个完整包后返回。
    /// </summary>
    public struct PacketParseResult
    {
        /// <summary>消息 Id。</summary>
        public ushort MsgId;

        /// <summary>包标志位（见 <see cref="PacketFlags"/>）。</summary>
        public byte Flags;

        /// <summary>负载数据（已脱离接收缓冲区的副本）。</summary>
        public byte[] Payload;

        /// <summary>RPC 请求 Id（仅当 <see cref="Flags"/> 含 <see cref="PacketFlags.Rpc"/> 时有效）。</summary>
        public uint RequestId;

        /// <summary>
        /// 是否负载超限：声明的负载长度超过调用方给定的 <c>maxAllowed</c> 上限，或为负、或会导致整数溢出。
        /// 为 <c>true</c> 时调用方应判定字节流不可信并断开连接，此时 <see cref="Payload"/> 为 null。
        /// </summary>
        public bool IsOversized;
    }
}
