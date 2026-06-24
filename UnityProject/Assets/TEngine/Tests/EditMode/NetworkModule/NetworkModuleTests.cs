using System;
using System.Collections.Generic;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// <see cref="NetworkModule"/> 顶层模块测试。
    /// 通过 Platform transport 注入 <see cref="FakeTransport"/>（Tcp/Kcp/WebSocket 真实实现待 T9-T12），
    /// 覆盖连接池管理、全局消息/拦截器注册、事件广播、Update 驱动与 Shutdown。
    /// </summary>
    public class NetworkModuleTests
    {
        private NetworkModule _module;

        [SetUp]
        public void SetUp()
        {
            // CodecRegistry 为静态全局状态，测试前重置并注册 fake codec，
            // 使 CreateConnection 内部的 CodecRegistry.Get(config.Codec) 不抛异常。
            CodecRegistry.ResetForTests();
            CodecRegistry.Register(CodecType.MessagePack, () => new FakeCodec());
            _module = new NetworkModule();
        }

        [TearDown]
        public void TearDown()
        {
            _module.Shutdown();
            CodecRegistry.ResetForTests();
        }

        // ============== 辅助 ==============

        /// <summary>注册一个 <see cref="FakeTransport"/> 作为 Platform transport，返回实例供断言。</summary>
        private FakeTransport RegisterPlatform(string platformId = "test")
        {
            var transport = new FakeTransport();
            _module.RegisterPlatformTransport(platformId, transport);
            return transport;
        }

        /// <summary>Platform + MessagePack 配置（platformId 对应已注册的 FakeTransport）。</summary>
        private static ConnectionConfig MakeConfig(string name, string platformId = "test")
            => new ConnectionConfig(name).UsePlatform(platformId).UseMessagePack().SetEndpoint("127.0.0.1", 9000);

        /// <summary>带 1s 心跳间隔的配置（用于 Update 驱动验证；关闭自动重连避免干扰）。</summary>
        private static ConnectionConfig MakeHeartbeatConfig(string name, string platformId)
            => new ConnectionConfig(name).UsePlatform(platformId).UseMessagePack().SetEndpoint("127.0.0.1", 9000)
                .SetHeartbeat(1, 30).SetReconnect(false, 100, 3);

        // ============== 创建 / 获取 ==============

        [Test]
        public void CreateConnection_Platform_ReturnsAndRegisters()
        {
            RegisterPlatform();
            var conn = _module.CreateConnection(MakeConfig("lobby"));

            Assert.IsNotNull(conn);
            Assert.AreEqual("lobby", conn.Name);
            Assert.IsTrue(_module.HasConnection("lobby"));
            Assert.AreSame(conn, _module.GetConnection("lobby"));
        }

        [Test]
        public void GetConnection_Unknown_ReturnsNull()
        {
            Assert.IsNull(_module.GetConnection("nope"));
            Assert.IsFalse(_module.HasConnection("nope"));
        }

        [Test]
        public void GetConnectionOrDefault_Unknown_FallsBackToAny()
        {
            RegisterPlatform();
            var conn = _module.CreateConnection(MakeConfig("a"));

            // 名不存在 → 回退到池中任意可用连接
            Assert.AreSame(conn, _module.GetConnectionOrDefault("missing"));
        }

        [Test]
        public void GetConnectionOrDefault_EmptyPool_ReturnsNull()
        {
            Assert.IsNull(_module.GetConnectionOrDefault("anything"));
        }

        // ============== 守卫 ==============

        [Test]
        public void CreateConnection_DuplicateName_Throws()
        {
            RegisterPlatform();
            _module.CreateConnection(MakeConfig("dup"));

            Assert.Throws<ArgumentException>(() => _module.CreateConnection(MakeConfig("dup")));
        }

        [Test]
        public void CreateConnection_Tcp_Throws_NotSupported()
        {
            // Tcp 真实实现待 T9-T12，应抛 NotSupportedException
            var config = new ConnectionConfig("tcp").UseTcp().UseMessagePack().SetEndpoint("127.0.0.1", 9000);
            Assert.Throws<NotSupportedException>(() => _module.CreateConnection(config));
        }

        [Test]
        public void CreateConnection_PlatformNotRegistered_Throws()
        {
            var config = MakeConfig("p", "unknown-platform");
            Assert.Throws<InvalidOperationException>(() => _module.CreateConnection(config));
        }

        [Test]
        public void CreateConnection_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _module.CreateConnection(null));
        }

        // ============== 消息注册 ==============

        [Test]
        public void RegisterMessage_SetsPriority()
        {
            _module.RegisterMessage<TestMessage>(1001, MessagePriority.High);
            Assert.AreEqual(MessagePriority.High, _module.GetMessagePriority(1001));
        }

        [Test]
        public void GetMessagePriority_Unregistered_ReturnsNormal()
        {
            Assert.AreEqual(MessagePriority.Normal, _module.GetMessagePriority(9999));
        }

        [Test]
        public void RegisterMessages_Batch_SetsAll()
        {
            var entries = new Dictionary<ushort, (Type, MessagePriority)>
            {
                { 2001, (typeof(TestMessage), MessagePriority.High) },
                { 2002, (typeof(TestMessage), MessagePriority.Low) },
            };
            _module.RegisterMessages(entries);

            Assert.AreEqual(MessagePriority.High, _module.GetMessagePriority(2001));
            Assert.AreEqual(MessagePriority.Low, _module.GetMessagePriority(2002));
        }

        // ============== 拦截器 ==============

        [Test]
        public void AddInterceptor_InjectedIntoNewConnection()
        {
            var interceptor = new CountInterceptor();
            _module.AddInterceptor(interceptor);
            RegisterPlatform();
            var conn = _module.CreateConnection(MakeConfig("c"));
            conn.ConnectAsync().GetAwaiter().GetResult();

            conn.Send(100, new TestMessage());
            _module.Update(0.016f, 0.016f); // flush SendQueue

            Assert.AreEqual(1, interceptor.SendCalls, "全局拦截器应注入新连接并在发送时生效");
        }

        [Test]
        public void RemoveInterceptor_NoLongerAffectsNewConnection()
        {
            var interceptor = new CountInterceptor();
            _module.AddInterceptor(interceptor);
            _module.RemoveInterceptor(CountInterceptor.NameConst);

            RegisterPlatform();
            var conn = _module.CreateConnection(MakeConfig("c"));
            conn.ConnectAsync().GetAwaiter().GetResult();
            conn.Send(100, new TestMessage());
            _module.Update(0.016f, 0.016f);

            Assert.AreEqual(0, interceptor.SendCalls, "移除后新建连接不应再注入该拦截器");
        }

        // ============== 事件广播 ==============

        [Test]
        public void OnConnectionStateChanged_Broadcast_ModuleEvent()
        {
            RegisterPlatform();
            var conn = _module.CreateConnection(MakeConfig("c"));

            string firedName = null;
            ConnectionState? firedState = null;
            _module.OnConnectionStateChanged += (n, s) => { firedName = n; firedState = s; };

            conn.ConnectAsync().GetAwaiter().GetResult(); // → Connected → OnStateChanged

            Assert.AreEqual("c", firedName);
            Assert.AreEqual(ConnectionState.Connected, firedState);
        }

        [Test]
        public void OnError_Broadcast_ModuleEvent()
        {
            var transport = RegisterPlatform();
            _module.CreateConnection(MakeConfig("c"));

            string firedName = null;
            Exception firedEx = null;
            _module.OnError += (n, e) => { firedName = n; firedEx = e; };

            var ex = new InvalidOperationException("boom");
            transport.SimulateError(ex);

            Assert.AreEqual("c", firedName);
            Assert.AreSame(ex, firedEx);
        }

        // ============== 销毁 / Shutdown ==============

        [Test]
        public void DestroyConnection_ReleasesTransportAndRemoves()
        {
            var transport = RegisterPlatform();
            _module.CreateConnection(MakeConfig("c"));

            _module.DestroyConnection("c");

            // FakeTransport.Dispose → Close → Connected=false
            Assert.IsFalse(transport.Connected, "transport 应被 Dispose 释放");
            Assert.IsFalse(_module.HasConnection("c"));
        }

        [Test]
        public void DestroyConnection_Idempotent()
        {
            RegisterPlatform();
            _module.CreateConnection(MakeConfig("c"));
            _module.DestroyConnection("c");

            Assert.DoesNotThrow(() => _module.DestroyConnection("c"));
            Assert.DoesNotThrow(() => _module.DestroyConnection("never-existed"));
        }

        [Test]
        public void DestroyConnection_StopsEventBroadcast()
        {
            var transport = RegisterPlatform();
            _module.CreateConnection(MakeConfig("c"));
            _module.DestroyConnection("c"); // 取消模块对该连接事件的订阅

            int fireCount = 0;
            _module.OnError += (n, e) => fireCount++;
            transport.SimulateError(new Exception("late"));

            Assert.AreEqual(0, fireCount, "销毁后模块不应再广播该连接事件");
        }

        // ============== Update 驱动 ==============

        [Test]
        public void Update_DrivesAllConnections()
        {
            var t1 = RegisterPlatform("p1");
            var t2 = RegisterPlatform("p2");
            var c1 = _module.CreateConnection(MakeHeartbeatConfig("c1", "p1"));
            var c2 = _module.CreateConnection(MakeHeartbeatConfig("c2", "p2"));
            c1.ConnectAsync().GetAwaiter().GetResult();
            c2.ConnectAsync().GetAwaiter().GetResult();

            // 到心跳间隔(1s) → 触发 SendHeartbeatPacket → 直接 transport.Send（不走队列）
            _module.Update(1.0f, 1.0f);

            Assert.Greater(t1.SentPackets.Count, 0, "连接1 应被 Update 驱动并发送心跳");
            Assert.Greater(t2.SentPackets.Count, 0, "连接2 应被 Update 驱动并发送心跳");
        }

        [Test]
        public void Shutdown_ClearsAllConnections()
        {
            RegisterPlatform("p1");
            RegisterPlatform("p2");
            _module.CreateConnection(MakeConfig("c1", "p1"));
            _module.CreateConnection(MakeConfig("c2", "p2"));

            _module.Shutdown();

            Assert.IsFalse(_module.HasConnection("c1"));
            Assert.IsFalse(_module.HasConnection("c2"));
        }

        // ============== 全局注册参数校验 ==============

        [Test]
        public void RegisterPlatformTransport_NullOrEmpty_Throws()
        {
            Assert.Throws<ArgumentException>(() => _module.RegisterPlatformTransport("", new FakeTransport()));
            Assert.Throws<ArgumentNullException>(() => _module.RegisterPlatformTransport("id", null));
        }

        [Test]
        public void SetPacketFormatter_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _module.SetPacketFormatter(null));
        }

        // ============== fakes ==============

        private class TestMessage : IMessage { }

        /// <summary>计数型拦截器：记录 OnSending 调用次数，始终放行。</summary>
        private class CountInterceptor : IMessageInterceptor
        {
            public const string NameConst = "count";
            public string Name => NameConst;
            public int Order => 0;
            public int SendCalls;

            public bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace)
            {
                SendCalls++;
                return true;
            }

            public bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace) => true;
        }
    }
}
