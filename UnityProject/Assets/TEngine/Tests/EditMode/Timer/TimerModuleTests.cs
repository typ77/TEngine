using System;
using System.Threading;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class TimerModuleTests
    {
        private TimerModule _module;

        [SetUp]
        public void SetUp()
        {
            _module = new TimerModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.Shutdown();
        }

        /// <summary>
        /// 模拟时间推进：scaled 和 unscaled 都使用相同的 delta（默认）。
        /// </summary>
        private void Tick(float deltaTime)
        {
            _module.Update(deltaTime, deltaTime);
        }

        /// <summary>
        /// 模拟时间推进：分别指定 scaled 和 unscaled delta。
        /// </summary>
        private void Tick(float elapseSeconds, float realElapseSeconds)
        {
            _module.Update(elapseSeconds, realElapseSeconds);
        }

        // =====================================================
        // 10.1：Delay 触发一次，触发后 IsValid==false
        // =====================================================
        [Test]
        public void Delay_TriggersOnceAtCorrectTime()
        {
            int callCount = 0;
            TimerHandle handle = _module.Delay(1f, () => callCount++);

            Tick(0.5f); Assert.AreEqual(0, callCount);
            Tick(0.5f); Assert.AreEqual(1, callCount);
            Tick(1f);   Assert.AreEqual(1, callCount); // 不再触发
            Assert.IsFalse(handle.IsValid);
        }

        // =====================================================
        // 10.2：Schedule(delay:0, interval:1, count:3) 在 t=0,1,2 触发
        // =====================================================
        [Test]
        public void Schedule_CountThree_TriggersAtCorrectTimes()
        {
            int callCount = 0;
            int[] ticksRemaining = new int[3];

            _module.Schedule(0f, 1f, args =>
            {
                ticksRemaining[callCount] = args.TicksRemaining;
                callCount++;
            }, count: 3);

            Tick(0f);   Assert.AreEqual(1, callCount);
            Tick(1f);   Assert.AreEqual(2, callCount);
            Tick(1f);   Assert.AreEqual(3, callCount);
            Tick(1f);   Assert.AreEqual(3, callCount);

            Assert.AreEqual(2, ticksRemaining[0]);
            Assert.AreEqual(1, ticksRemaining[1]);
            Assert.AreEqual(0, ticksRemaining[2]);
        }

        // =====================================================
        // 10.3：循环定时器时间对齐（无漂移）
        // =====================================================
        [Test]
        public void Repeat_NoTimeDrift_FireTimeAligned()
        {
            int count = 0;
            _module.Repeat(1f, args =>
            {
                count++;
            });

            // 模拟帧不对齐（0.016 精度误差）
            for (int i = 0; i < 5; i++) Tick(1.016f);

            // 应触发 5 次（不因 delta>1 导致额外触发）
            Assert.AreEqual(5, count);
        }

        // =====================================================
        // 10.4：stale handle 访问安全忽略
        // =====================================================
        [Test]
        public void TimerHandle_StaleVersion_SafeIgnore()
        {
            TimerHandle handle = _module.Delay(1f, () => { });
            Tick(1.1f); // 触发完毕，版本已变
            Assert.IsFalse(handle.IsValid);
            Assert.DoesNotThrow(() => handle.Cancel());
            Assert.DoesNotThrow(() => handle.Pause());
            Assert.DoesNotThrow(() => handle.Resume());
        }

        // =====================================================
        // 10.5：MaxCatchUpSteps 保护（deltaTime=30s，interval=0.1s）
        // =====================================================
        [Test]
        public void Repeat_LargeDeltaTime_CapsAtMaxCatchUpSteps()
        {
            _module.Configure(maxCatchUpSteps: 5, initialPoolCapacity: 128);
            int count = 0;
            _module.Repeat(0.1f, () => count++);

            Tick(30f);
            Assert.LessOrEqual(count, 6); // 最多触发 maxCatchUpSteps+1 = 6 次
        }

        // =====================================================
        // 10.6：CancellationToken 取消后回调不触发
        // =====================================================
        [Test]
        public void Repeat_CancelToken_StopsCallback()
        {
            var cts = new CancellationTokenSource();
            int count = 0;
            _module.Repeat(0.5f, () => count++, cancellationToken: cts.Token);

            Tick(0.6f); Assert.AreEqual(1, count);
            cts.Cancel();
            Tick(0.5f); Assert.AreEqual(1, count); // 已取消，不再触发
        }

        // =====================================================
        // 10.7：回调内 Cancel 后不重入堆
        // =====================================================
        [Test]
        public void Repeat_SelfCancelInCallback_StopsImmediately()
        {
            int count = 0;
            TimerHandle handle = default;
            handle = _module.Repeat(0.5f, args =>
            {
                count++;
                args.Handle.Cancel();
            });

            Tick(0.6f); Assert.AreEqual(1, count);
            Tick(0.5f); Assert.AreEqual(1, count); // 已取消
            Assert.IsFalse(handle.IsValid);
        }

        // =====================================================
        // 10.8：Pause + Resume 精确时间
        // =====================================================
        [Test]
        public void Pause_ThenResume_TriggersAtCorrectTime()
        {
            int count = 0;
            TimerHandle handle = _module.Delay(10f, () => count++);

            Tick(3f);          // t=3, 剩余 7 秒
            handle.Pause();
            Tick(5f);          // t=8, 暂停中，不触发
            Assert.AreEqual(0, count);
            handle.Resume();
            Tick(6.9f);        // t=14.9，还剩 0.1 秒
            Tick(0.2f);        // t=15.1，触发
            Assert.AreEqual(1, count);
        }

        // =====================================================
        // 10.9：Unscaled 定时器在 timeScale=0 时触发
        // =====================================================
        [Test]
        public void UnscaledTimer_WhenScaledTimeZero_StillFires()
        {
            int scaledCount = 0;
            int unscaledCount = 0;

            _module.Delay(1f, () => scaledCount++, TimeMode.Scaled);
            _module.Delay(1f, () => unscaledCount++, TimeMode.Unscaled);

            // 模拟 timeScale=0：elapseSeconds=0, realElapseSeconds=1
            Tick(0f, 1f);

            Assert.AreEqual(0, scaledCount);   // Scaled 未触发
            Assert.AreEqual(1, unscaledCount); // Unscaled 触发
        }

        // =====================================================
        // 10.10：回调异常不中断其他定时器
        // =====================================================
        [Test]
        public void Callback_ThrowsException_OtherTimersContinue()
        {
            int normalCount = 0;
            _module.Delay(1f, () => throw new Exception("故意抛出"));
            _module.Delay(1f, () => normalCount++);

            Tick(1.1f);  // 直接调用，让内部逻辑处理异常
            Assert.AreEqual(1, normalCount);
        }

        // =====================================================
        // TimerHandle.Remaining
        // =====================================================
        [Test]
        public void TimerHandle_Remaining_ReturnsCorrectValue()
        {
            TimerHandle h = _module.Delay(4f, () => { });
            Tick(1f);
            Assert.AreEqual(3f, h.Remaining, 0.001f);
        }

        // =====================================================
        // TimerHandle.Progress
        // =====================================================
        [Test]
        public void TimerHandle_Progress_ReturnsCorrectValue()
        {
            // Repeat(interval:1, count:10)：每 1 秒触发一次，共 10 次
            // Tick 5 次后 CompletedTicks=5, Progress=5/10=0.5
            TimerHandle h = _module.Repeat(1f, () => { }, count: 10);
            for (int i = 0; i < 5; i++) Tick(1f);
            Assert.AreEqual(0.5f, h.Progress, 0.001f);
        }

        // =====================================================
        // WaitFrames
        // =====================================================
        [Test]
        public void WaitFrames_FiresAtExactFrame()
        {
            int count = 0;
            _module.WaitFrames(3, () => count++);

            Tick(0f); Assert.AreEqual(0, count);
            Tick(0f); Assert.AreEqual(0, count);
            Tick(0f); Assert.AreEqual(1, count); // frame 3
            Tick(0f); Assert.AreEqual(1, count);
        }

        // =====================================================
        // NextFrame
        // =====================================================
        [Test]
        public void NextFrame_FiresOnNextFrame_NotCurrentFrame()
        {
            int count = 0;
            _module.NextFrame(() => count++);
            Assert.AreEqual(0, count);
            Tick(0f); Assert.AreEqual(1, count);
        }

        // =====================================================
        // Countdown
        // =====================================================
        [Test]
        public void Countdown_TicksRemainingDecreasesCorrectly()
        {
            int[] remaining = new int[3];
            int idx = 0;
            bool completed = false;

            _module.Countdown(3, 1f,
                args => remaining[idx++] = args.TicksRemaining,
                () => completed = true);

            Tick(1f); Tick(1f); Tick(1f);

            Assert.AreEqual(2, remaining[0]);
            Assert.AreEqual(1, remaining[1]);
            Assert.AreEqual(0, remaining[2]);
            Assert.IsTrue(completed);
        }

        // =====================================================
        // 诊断属性
        // =====================================================
        [Test]
        public void Diagnostics_ActiveTimerCount_IsAccurate()
        {
            _module.Delay(5f, () => { });
            _module.Repeat(1f, () => { });
            Assert.AreEqual(2, _module.ActiveTimerCount);
        }
    }
}
