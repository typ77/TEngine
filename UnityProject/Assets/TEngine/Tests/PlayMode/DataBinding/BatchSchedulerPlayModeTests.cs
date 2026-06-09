using System.Collections;
using GameLogic;
using GameLogic.DataBinding;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests
{
    /// <summary>
    /// PlayMode 测试：验证 BatchScheduler 运行时帧级合并行为。
    ///
    /// EditMode 下 SafeMarkDirty 同步触发 FireCallback（无合并），
    /// PlayMode 下 BatchScheduler 单例运行，同帧多次赋值只触发一次回调。
    /// </summary>
    [TestFixture]
    public class BatchSchedulerPlayModeTests
    {
        private BindableProperty<int> _prop;

        [SetUp]
        public void SetUp()
        {
            _prop = new BindableProperty<int>(0);
        }

        [TearDown]
        public void TearDown()
        {
            _prop?.Dispose();
            // 清空 BatchScheduler 待处理脏标记
            if (Singleton<BatchScheduler>.IsValid)
            {
                Singleton<BatchScheduler>.Instance.OnLateUpdate();
            }
        }

        /// <summary>
        /// 验证：同帧多次赋值同一属性，回调只触发一次（合并）。
        /// </summary>
        [UnityTest]
        public IEnumerator SameFrame_MultipleAssignments_MergesToOneCallback()
        {
            int callCount = 0;
            _prop.OnValueChanged += (_, _) => callCount++;

            // 同帧内多次赋值
            _prop.Value = 10;
            _prop.Value = 20;
            _prop.Value = 30;

            // 等待一帧，让 BatchScheduler.LateUpdate 触发 Flush
            yield return null;

            // 运行时合并：3 次赋值只触发 1 次回调
            Assert.AreEqual(1, callCount, "同帧多次赋值应合并为一次回调");
        }

        /// <summary>
        /// 验证：合并后的回调，old 值为第一次赋值前的值，new 值为最终值。
        /// </summary>
        [UnityTest]
        public IEnumerator SameFrame_MergedCallback_HasOldestOldAndNewestValue()
        {
            int receivedOld = -1, receivedNew = -1;
            _prop.OnValueChanged += (oldVal, newVal) =>
            {
                receivedOld = oldVal;
                receivedNew = newVal;
            };

            _prop.Value = 10;
            _prop.Value = 20;
            _prop.Value = 30;

            yield return null;

            Assert.AreEqual(0, receivedOld, "old 应为原始值 0");
            Assert.AreEqual(30, receivedNew, "new 应为最终值 30");
        }

        /// <summary>
        /// 验证：同值赋值不触发回调（EqualityComparer 过滤）。
        /// </summary>
        [UnityTest]
        public IEnumerator SameValue_DoesNotFireCallback()
        {
            _prop.Value = 42;
            yield return null; // 先刷新初始赋值

            int callCount = 0;
            _prop.OnValueChanged += (_, _) => callCount++;

            _prop.Value = 42; // 同值不触发

            yield return null;

            Assert.AreEqual(0, callCount, "同值赋值不应触发回调");
        }

        /// <summary>
        /// 验证：链式更新（Model→DC→View）通过两轮 Flush 正确传播。
        /// </summary>
        [UnityTest]
        public IEnumerator ChainedUpdate_ModelToDCToView_PropagatesInOneFrame()
        {
            var model = new BindableProperty<int>(0);
            var view = new BindableProperty<string>("");

            // 模拟 DataContext 映射
            model.OnValueChanged += (_, newVal) => view.Value = $"Value: {newVal}";

            string receivedView = null;
            view.OnValueChanged += (_, newVal) => receivedView = newVal;

            model.Value = 42;

            // 等一帧让 BatchScheduler 处理两轮 Flush
            yield return null;

            Assert.AreEqual("Value: 42", receivedView, "链式更新应在一帧内传播到 View");

            model.Dispose();
            view.Dispose();
        }
    }
}
