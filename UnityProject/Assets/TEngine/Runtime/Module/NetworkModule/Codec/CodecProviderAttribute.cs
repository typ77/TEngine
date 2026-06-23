using System;

namespace TEngine
{
    /// <summary>
    /// 标注一个 codec 实现类型，供 <see cref="CodecRegistry"/> 在运行时反射扫描自动注册。
    /// 通常放在可选扩展程序集（如 TEngine.Network.Codec.MessagePack）的 provider 类上：
    /// dll 未安装 / 编译符号未定义时，整个扩展程序集不参与编译，框架核心零依赖。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CodecProviderAttribute : Attribute
    {
        /// <summary>该实现对应的编解码器类型。</summary>
        public CodecType CodecType { get; }

        /// <summary>codec 实现类型（须实现 <see cref="ICodec"/> 且有无参构造）。</summary>
        public Type CodecImplementationType { get; }

        public CodecProviderAttribute(CodecType codecType, Type codecImplementationType)
        {
            CodecType = codecType;
            CodecImplementationType = codecImplementationType;
        }
    }
}