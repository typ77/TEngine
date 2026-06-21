using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class HeartbeatKeeperTests
    {
        [Test]
        public void Update_AtInterval_TriggersSendHeartbeat()
        {
            var hb = new HeartbeatKeeper(intervalSec: 2, timeoutSec: 10);
            int sendCount = 0;
            hb.OnSendHeartbeat += () => sendCount++;

            hb.Update(2f);
            Assert.AreEqual(1, sendCount);
        }

        [Test]
        public void Update_WhileWaitingReply_DoesNotResend()
        {
            // 等待回复期间不得重发心跳（避免对端连续收到重复心跳）
            var hb = new HeartbeatKeeper(intervalSec: 2, timeoutSec: 10);
            int sendCount = 0;
            hb.OnSendHeartbeat += () => sendCount++;

            hb.Update(2f);   // 发心跳
            hb.Update(2f);   // 等待回复中，不应重发
            Assert.AreEqual(1, sendCount);
        }

        [Test]
        public void Timeout_Triggers_WhenNoResponseWithinTimeout()
        {
            // 回归测试：修复前 OnTimeout 永不触发（waitingReply 期间停滞）
            var hb = new HeartbeatKeeper(intervalSec: 2, timeoutSec: 5);
            bool timeout = false;
            hb.OnTimeout += () => timeout = true;

            hb.Update(2f);   // 发心跳，进入等待
            Assert.IsFalse(timeout);

            hb.Update(5f);   // 等待 5s >= timeout → 超时
            Assert.IsTrue(timeout);
        }

        [Test]
        public void HeartbeatResponse_PreventsTimeout()
        {
            var hb = new HeartbeatKeeper(intervalSec: 2, timeoutSec: 5);
            bool timeout = false;
            hb.OnTimeout += () => timeout = true;

            hb.Update(2f);             // 发心跳
            hb.Update(3f);             // 等待 3s（未超时）
            hb.OnHeartbeatResponse();  // 收到回复，重置计时
            hb.Update(4f);             // 回复后重新计时，未到 timeout
            Assert.IsFalse(timeout);
        }

        [Test]
        public void Reset_ClearsWaitingState()
        {
            var hb = new HeartbeatKeeper(intervalSec: 2, timeoutSec: 10);
            int sendCount = 0;
            hb.OnSendHeartbeat += () => sendCount++;

            hb.Update(2f);   // 发心跳，进入等待
            hb.Reset();      // 重置
            hb.Update(2f);   // 重新到间隔，再发
            Assert.AreEqual(2, sendCount);
        }
    }
}
