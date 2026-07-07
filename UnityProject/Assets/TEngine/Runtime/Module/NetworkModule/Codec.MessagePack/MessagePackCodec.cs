using System;
using TEngine;

namespace TEngine.Network.Codec.MessagePack
{
    /// <summary>
    /// 基于 MessagePack-C# 的 <see cref="ICodec"/> 实现。
    /// 【骨架】真实序列化逻辑待 T6b 安装 MessagePack dll 后实现，当前方法体均抛 <see cref="NotImplementedException"/>。
    /// </summary>
    public sealed class MessagePackCodec : ICodec
    {
        /// <inheritdoc />
        public CodecType Type => CodecType.MessagePack;

        /// <inheritdoc />
        public void RegisterMessage<T>(ushort msgId) where T : IMessage
        {
            throw new NotImplementedException("待安装 MessagePack dll 后实现，见 T6b。");
        }

        /// <inheritdoc />
        public byte[] Serialize<T>(T message) where T : IMessage
        {
            throw new NotImplementedException("待安装 MessagePack dll 后实现，见 T6b。");
        }

        /// <inheritdoc />
        public object Deserialize(ushort msgId, byte[] data, int offset, int count)
        {
            throw new NotImplementedException("待安装 MessagePack dll 后实现，见 T6b。");
        }

        /// <inheritdoc />
        public object Deserialize(Type responseType, byte[] data, int offset, int count)
        {
            throw new NotImplementedException("待安装 MessagePack dll 后实现，见 T6b。");
        }
    }
}