using TEngine;

namespace TEngine.Network.Codec.Protobuf
{
    /// <summary>
    /// 向 <see cref="CodecRegistry"/> 声明 Protobuf codec 实现。
    /// 标注 <see cref="CodecProviderAttribute"/> 后，运行时 <see cref="CodecRegistry.Scan"/> 反射扫描自动注册。
    /// 仅当编译符号 TENGINE_CODEC_PROTOBUF 已定义（即 protobuf-net dll 已安装）时，本程序集才参与编译。
    /// </summary>
    [CodecProvider(CodecType.Protobuf, typeof(ProtobufCodec))]
    internal static class ProtobufCodecProvider
    {
    }
}