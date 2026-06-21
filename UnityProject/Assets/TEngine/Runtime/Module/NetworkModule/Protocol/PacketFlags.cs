namespace TEngine
{
    /// <summary>
    /// 包头 Flags 字段的位定义。
    /// </summary>
    public static class PacketFlags
    {
        /// <summary>bit0：本包为 RPC 请求/响应（包头额外携带 4 字节 RequestId）。</summary>
        public const byte Rpc = 0x01;

        /// <summary>bit1：负载已压缩。</summary>
        public const byte Compressed = 0x02;

        /// <summary>bit2：负载已加密。</summary>
        public const byte Encrypted = 0x04;
    }
}
