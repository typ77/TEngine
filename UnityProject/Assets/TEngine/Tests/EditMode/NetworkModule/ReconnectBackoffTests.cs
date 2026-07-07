using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    /// <summary>
    /// 重连退避测试。主测 <see cref="ReconnectPolicy"/> 的纯逻辑（同步、无 UniTask.Delay、无 EditMode 死锁风险），
    /// 按 <c>ReconnectAsync</c> 的真实调用顺序（先 <see cref="ReconnectPolicy.GetNextIntervalMs"/> 再
    /// <see cref="ReconnectPolicy.RecordAttempt"/>）验证退避序列：首次=base、之后指数递增、受上限约束、成功后重置。
    /// <para>ReconnectAsync 的接线（Delay 调用点）由 <c>NetworkConnectionTests.Reconnect_*</c> 覆盖（Task.Run 模板）；
    /// 墙钟时序不测（CI 抖动 + 真实等待数秒过慢），用纯逻辑测等价覆盖算法正确性。</para>
    /// </summary>
    public class ReconnectBackoffTests
    {
        [Test]
        public void GetNextIntervalMs_FirstAttempt_ReturnsBaseInterval()
        {
            var policy = new ReconnectPolicy(1000, 5, 2.0f, 30000);

            // 首次 Attempts=0：base × backoff^0 = 1000
            Assert.AreEqual(1000, policy.GetNextIntervalMs());
        }

        [Test]
        public void GetNextIntervalMs_Sequence_IncreasesExponentially()
        {
            var policy = new ReconnectPolicy(1000, 5, 2.0f, 30000);

            // 模拟 ReconnectAsync 循环：Delay(GetNextIntervalMs) → RecordAttempt
            Assert.AreEqual(1000, policy.GetNextIntervalMs());
            policy.RecordAttempt();   // Attempts 0→1
            Assert.AreEqual(2000, policy.GetNextIntervalMs());
            policy.RecordAttempt();   // 1→2
            Assert.AreEqual(4000, policy.GetNextIntervalMs());
        }

        [Test]
        public void GetNextIntervalMs_CappedByMaxInterval()
        {
            var policy = new ReconnectPolicy(1000, 10, 2.0f, 5000);

            // 推进 Attempts=3：base × 2^3 = 8000，超过 max 5000 → 截断
            policy.RecordAttempt();
            policy.RecordAttempt();
            policy.RecordAttempt();
            Assert.AreEqual(5000, policy.GetNextIntervalMs());
        }

        [Test]
        public void Reset_AfterSuccess_RestartsFromBase()
        {
            var policy = new ReconnectPolicy(1000, 5, 2.0f, 30000);
            policy.RecordAttempt();
            policy.RecordAttempt();
            Assert.AreEqual(4000, policy.GetNextIntervalMs()); // Attempts=2

            policy.Reset(); // 重连成功

            Assert.AreEqual(0, policy.Attempts);
            Assert.AreEqual(1000, policy.GetNextIntervalMs()); // 恢复 base
        }

        [Test]
        public void CanRetry_ExceedsMaxAttempts_ReturnsFalse()
        {
            var policy = new ReconnectPolicy(1000, 3, 2.0f, 30000);

            Assert.IsTrue(policy.CanRetry);   // Attempts=0 < 3
            policy.RecordAttempt();
            policy.RecordAttempt();
            Assert.IsTrue(policy.CanRetry);   // Attempts=2 < 3
            policy.RecordAttempt();
            Assert.IsFalse(policy.CanRetry);  // Attempts=3，不 < 3
        }

        [Test]
        public void BackoffMultiplier_One_KeepsConstantInterval()
        {
            // ConnectionConfig 默认 BackoffMultiplier=1.0：间隔恒为 base（不增长），退避接线仍按 base 等待。
            var policy = new ReconnectPolicy(1000, 5, 1.0f, 30000);

            Assert.AreEqual(1000, policy.GetNextIntervalMs());
            policy.RecordAttempt();
            Assert.AreEqual(1000, policy.GetNextIntervalMs());
        }
    }
}
