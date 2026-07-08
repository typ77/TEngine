using System;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class ConnectionConfigTests
    {
        // 反向退避系数（BackoffMultiplier < 1.0）会被 Validate 拦截，防重连间隔递减轰炸对端（修 L4）。
        [Test]
        public void Validate_BackoffMultiplierBelowOne_Throws()
        {
            var cfg = new ConnectionConfig("c1")
                .UseTcp()
                .UseMessagePack()
                .SetEndpoint("127.0.0.1", 8080)
                .SetReconnect(true, 1000, 5, 0.5f, 30000);

            var ex = Assert.Throws<ArgumentException>(() => cfg.Validate());
            Assert.That(ex.Message, Does.Contain("BackoffMultiplier"));
        }

        // 端口越界（> 65535）会被 Validate 拦截（修 L4）。
        [Test]
        public void Validate_PortOutOfRange_Throws()
        {
            var cfg = new ConnectionConfig("c1")
                .UseTcp()
                .UseMessagePack()
                .SetEndpoint("127.0.0.1", 70000);

            var ex = Assert.Throws<ArgumentException>(() => cfg.Validate());
            Assert.That(ex.Message, Does.Contain("Port"));
        }

        // HeartbeatTimeout < HeartbeatInterval 会被 Validate 拦截。
        [Test]
        public void Validate_HeartbeatTimeoutBelowInterval_Throws()
        {
            var cfg = new ConnectionConfig("c1")
                .UseTcp()
                .UseMessagePack()
                .SetEndpoint("127.0.0.1", 8080)
                .SetHeartbeat(30, 10); // timeout(10) < interval(30)

            Assert.Throws<ArgumentException>(() => cfg.Validate());
        }

        // MaxReconnectInterval < ReconnectInterval 会被 Validate 拦截。
        [Test]
        public void Validate_MaxReconnectIntervalBelowBase_Throws()
        {
            var cfg = new ConnectionConfig("c1")
                .UseTcp()
                .UseMessagePack()
                .SetEndpoint("127.0.0.1", 8080)
                .SetReconnect(true, 5000, 5, 2.0f, 1000); // maxInterval(1000) < interval(5000)

            Assert.Throws<ArgumentException>(() => cfg.Validate());
        }

        // 全部配置项合法时 Validate 正常返回不抛（修 L4 正向用例）。
        [Test]
        public void Validate_LegalConfig_DoesNotThrow()
        {
            var cfg = new ConnectionConfig("c1")
                .UseTcp()
                .UseMessagePack()
                .SetEndpoint("127.0.0.1", 8080)
                .SetHeartbeat(10, 30)
                .SetReconnect(true, 1000, 5, 2.0f, 30000);

            Assert.DoesNotThrow(() => cfg.Validate());
        }

        // url 模式（未设 Host）跳过端口校验，合法 url 通过。
        [Test]
        public void Validate_UrlMode_SkipsPortCheck()
        {
            var cfg = new ConnectionConfig("c1")
                .UseWebSocket()
                .UseMessagePack()
                .SetUrl("ws://127.0.0.1:8080");

            Assert.DoesNotThrow(() => cfg.Validate());
        }
    }
}
