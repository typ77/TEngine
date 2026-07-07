using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// 方向 2 RPC 请求-响应往返测试。
    /// 验证 SendAsync 登记 pending、响应按 RequestId 匹配完成、未知 RequestId 忽略、await 返回 TResp。
    /// </summary>
    public class RpcRoundTripTests
    {
        private FakeTransport _transport;
        private FakeCodec _codec;
        private DefaultPacketFormatter _formatter;
        private NetworkConnection _conn;

        private const ushort MsgId = 100;

        [SetUp]
        public void SetUp()
        {
            _transport = new FakeTransport();
            _codec = new FakeCodec { DeserializeResult = new TestResp() };
            _formatter = new DefaultPacketFormatter();
            var config = new ConnectionConfig("rpc")
                .UseTcp().SetEndpoint("127.0.0.1", 8080).UseMessagePack();
            _conn = new NetworkConnection(config, _transport, _codec, _formatter, null, null, null);
        }

        /// <summary>SendAsync 登记 pending（PendingCount==1），响应后完成移除（==0）。</summary>
        [Test]
        public void RpcRoundTrip_RegistersAndCompletes()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();

            UniTask<TestResp> task = _conn.SendAsync<TestReq, TestResp>(MsgId, new TestReq(), RpcPolicy.FailOnDisconnect, 5000);
            Assert.AreEqual(1, _conn.RpcPendingCountForTests, "登记后 pending==1");

            _conn.Update(0.016f); // flush 请求包

            byte[] resp = _formatter.BuildPacket(MsgId, PacketFlags.Rpc, new byte[] { 0x01 });
            _formatter.WriteRequestId(resp, 1); // 首个 RPC requestId=1
            _transport.SimulateReceive(resp);
            _conn.Update(0.016f); // drain → HandleRpcResponse → TrySetResult + Remove

            Assert.AreEqual(0, _conn.RpcPendingCountForTests, "响应后 pending 应移除");
        }

        /// <summary>SendAsync await 返回反序列化的 TResp（验证 TrySetResult 同步推进 continuation）。</summary>
        [Test]
        public void RpcRoundTrip_ReturnsResponse()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            var expected = new TestResp();
            _codec.DeserializeResult = expected;

            UniTask<TestResp> task = _conn.SendAsync<TestReq, TestResp>(MsgId, new TestReq(), RpcPolicy.FailOnDisconnect, 5000);
            _conn.Update(0.016f);

            byte[] resp = _formatter.BuildPacket(MsgId, PacketFlags.Rpc, new byte[] { 0x01 });
            _formatter.WriteRequestId(resp, 1);
            _transport.SimulateReceive(resp);
            _conn.Update(0.016f);

            TestResp result = task.GetAwaiter().GetResult();
            Assert.AreSame(expected, result);
        }

        /// <summary>未知 RequestId 的 RPC 响应被忽略，不影响 pending。</summary>
        [Test]
        public void UnknownRequestId_Ignored()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _conn.SendAsync<TestReq, TestResp>(MsgId, new TestReq(), RpcPolicy.FailOnDisconnect, 5000);
            _conn.Update(0.016f);

            byte[] resp = _formatter.BuildPacket(MsgId, PacketFlags.Rpc, new byte[] { 0x01 });
            _formatter.WriteRequestId(resp, 999); // 未知 requestId
            _transport.SimulateReceive(resp);
            _conn.Update(0.016f);

            Assert.AreEqual(1, _conn.RpcPendingCountForTests, "未知响应不应影响 pending");
        }

        /// <summary>无响应超时：CheckTimeouts 抛 RpcTimeoutException。</summary>
        [Test]
        public void RpcTimeout_WhenNoResponse()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            UniTask<TestResp> task = _conn.SendAsync<TestReq, TestResp>(MsgId, new TestReq(), RpcPolicy.FailOnDisconnect, 5);
            _conn.Update(0.016f); // flush 请求

            System.Threading.Thread.Sleep(20); // 等超过 5ms timeout
            _conn.Update(0.016f); // CheckTimeouts → TrySetException(RpcTimeoutException)

            Assert.Throws<RpcTimeoutException>(() => task.GetAwaiter().GetResult());
        }

        /// <summary>主动 Disconnect 对 FailOnDisconnect 的 pending 抛 RpcAbortException。</summary>
        [Test]
        public void FailOnDisconnect_AbortsOnDisconnect()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            UniTask<TestResp> task = _conn.SendAsync<TestReq, TestResp>(MsgId, new TestReq(), RpcPolicy.FailOnDisconnect, 5000);
            _conn.Update(0.016f);

            _conn.Disconnect(); // 主动断开 → 遍历 pending，FailOnDisconnect 抛 RpcAbortException

            Assert.Throws<RpcAbortException>(() => task.GetAwaiter().GetResult());
        }

        /// <summary>拦截器拒绝 RPC 发送 → SendAsync 抛 RpcAbortException 且 pending 移除。</summary>
        [Test]
        public void InterceptorBlocks_ThrowsRpcAbort()
        {
            _conn.ConnectAsync().GetAwaiter().GetResult();
            _conn.AddInterceptor(new BlockAllInterceptor());

            Assert.Throws<RpcAbortException>(() =>
                _conn.SendAsync<TestReq, TestResp>(MsgId, new TestReq(), RpcPolicy.FailOnDisconnect, 5000).GetAwaiter().GetResult());
            Assert.AreEqual(0, _conn.RpcPendingCountForTests, "拦截器拒绝后 pending 应移除");
        }

        private class TestReq : IMessage { }
        private class TestResp : IMessage { }

        private class BlockAllInterceptor : IMessageInterceptor
        {
            public string Name => "block-rpc";
            public int Order => 0;
            public bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace) => false;
            public bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace) => true;
        }
    }
}
