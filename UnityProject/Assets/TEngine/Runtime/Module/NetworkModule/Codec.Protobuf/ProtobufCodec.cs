using System;
using TEngine;

namespace TEngine.Network.Codec.Protobuf
{
    /// <summary>
    /// 基于 protobuf-net 的 <see cref="ICodec"/> 实现。
    /// 【骨架】真实序列化逻辑待 T6b 安装 protobuf-net dll 后实现，当前方法体均抛 <see cref="NotImplementedException"/>。
    /// </summary>
    public sealed class ProtobufCodec : ICodec
    {
        /// <inheritdoc />
        public CodecType Type => CodecType.Protobuf;

        /// <inheritdoc />
        public void RegisterMessage<T>(ushort msgId) where T : IMessage
        {
            throw new NotImplementedException("待安装 protobuf-net dll 后实现，见 T6b。");
        }

        /// <inheritdoc />
        public byte[] Serialize<T>(T message) where T : IMessage
        {
            throw new NotImplementedException("待安装 protobuf-net dll 后实现，见 T6b。");
        }

        /// <inheritdoc />
        public object Deserialize(ushort msgId, byte[] data, int offset, int count)
        {
            throw new NotImplementedException("待安装 protobuf-net dll 后实现，见 T6b。");
        }
    }
}