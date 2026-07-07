using System;

namespace TEngine
{
    /// <summary>
    /// 消息编解码器：负责“IMessage 对象 ↔ byte[] 负载”的序列化与反序列化。
    /// </summary>
    public interface ICodec
    {
        /// <summary>编码器类型。</summary>
        CodecType Type { get; }

        /// <summary>注册消息类型到 msgId 的映射。</summary>
        void RegisterMessage<T>(ushort msgId) where T : IMessage;

        /// <summary>序列化消息为负载字节。</summary>
        byte[] Serialize<T>(T message) where T : IMessage;

        /// <summary>按 msgId 反序列化为消息对象（业务消息分发用）。</summary>
        object Deserialize(ushort msgId, byte[] data, int offset, int count);

        /// <summary>按类型反序列化为消息对象（RPC 响应用：匹配靠 RequestId，反序列化用 pending 的 ResponseType，不依赖 msgId）。</summary>
        object Deserialize(Type responseType, byte[] data, int offset, int count);
    }
}
