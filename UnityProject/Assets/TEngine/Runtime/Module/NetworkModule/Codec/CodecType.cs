namespace TEngine
{
    /// <summary>
    /// 编解码器类型。
    /// </summary>
    public enum CodecType
    {
        /// <summary>MessagePack-C#（紧凑、高性能，默认推荐）。</summary>
        MessagePack,

        /// <summary>protobuf-net（兼容 Protobuf 生态）。</summary>
        Protobuf,
    }
}
