using System;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class CodecRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            CodecRegistry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            CodecRegistry.ResetForTests();
        }

        // 手动注册后 Get 返回对应实例。
        [Test]
        public void Register_And_Get_ReturnsCodec()
        {
            CodecRegistry.Register<FakeManualCodec>(CodecType.MessagePack);

            ICodec codec = CodecRegistry.Get(CodecType.MessagePack);

            Assert.IsNotNull(codec);
            Assert.IsInstanceOf<FakeManualCodec>(codec);
            Assert.AreEqual(CodecType.MessagePack, codec.Type);
        }

        // 同一 CodecType 多次 Get 返回同一实例（单例缓存，保留 RegisterMessage 注册的状态）。
        [Test]
        public void Get_ReturnsCachedInstance_ForSameType()
        {
            CodecRegistry.Register<FakeManualCodec>(CodecType.MessagePack);

            ICodec first = CodecRegistry.Get(CodecType.MessagePack);
            ICodec second = CodecRegistry.Get(CodecType.MessagePack);

            Assert.AreSame(first, second);
        }

        // 未注册的 CodecType 抛 InvalidOperationException（含排查提示）。
        [Test]
        public void Get_Unregistered_ThrowsInvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => CodecRegistry.Get(CodecType.MessagePack));

            Assert.That(ex.Message, Does.Contain("未注册 codec MessagePack"));
        }

        // 首次 Get 触发 EnsureInitialized → Scan，发现顶级 [CodecProvider] 自动注册。
        [Test]
        public void Get_AfterScan_ReturnsProviderCodec()
        {
            ICodec codec = CodecRegistry.Get(CodecType.Protobuf);

            Assert.IsInstanceOf<FakeScannedCodec>(codec);
        }
    }

    // ---- 顶级 fixtures：Assembly.GetTypes() 能扫到，Activator.CreateInstance 能实例化 ----

    public sealed class FakeManualCodec : ICodec
    {
        public CodecType Type => CodecType.MessagePack;
        public void RegisterMessage<T>(ushort msgId) where T : IMessage { }
        public byte[] Serialize<T>(T message) where T : IMessage => Array.Empty<byte>();
        public object Deserialize(ushort msgId, byte[] data, int offset, int count) => null;
    }

    public sealed class FakeScannedCodec : ICodec
    {
        public CodecType Type => CodecType.Protobuf;
        public void RegisterMessage<T>(ushort msgId) where T : IMessage { }
        public byte[] Serialize<T>(T message) where T : IMessage => Array.Empty<byte>();
        public object Deserialize(ushort msgId, byte[] data, int offset, int count) => null;
    }

    // 用 Protobuf 与手动注册用例的 MessagePack 错开，避免 [CodecProvider] 自动注册污染手注册断言。
    [CodecProvider(CodecType.Protobuf, typeof(FakeScannedCodec))]
    internal static class FakeScannedCodecProvider
    {
    }
}