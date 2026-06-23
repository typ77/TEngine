using TEngine;

namespace TEngine.Network.Codec.MessagePack
{
    /// <summary>
    /// 向 <see cref="CodecRegistry"/> 声明 MessagePack codec 实现。
    /// 标注 <see cref="CodecProviderAttribute"/> 后，运行时 <see cref="CodecRegistry.Scan"/> 反射扫描自动注册。
    /// 仅当编译符号 TENGINE_CODEC_MESSAGEPACK 已定义（即 MessagePack dll 已安装）时，本程序集才参与编译。
    /// </summary>
    [CodecProvider(CodecType.MessagePack, typeof(MessagePackCodec))]
    internal static class MessagePackCodecProvider
    {
    }
}