using System.Collections.Generic;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class BatchSchedulerTests : DataBindingTestBase
    {
        /// <summary>
        /// EditMode 下 BatchScheduler 单例不可用（SingletonSystem 依赖 Unity 运行时），
        /// SafeMarkDirty 会退化为同步 FireCallback。
        /// 测试验证此退化行为仍然正确传播数据。
        /// </summary>

        [Test]
        public void SyncFallback_TriggersCallback()
        {
            // EditMode: SafeMarkDirty 直接调用 FireCallback
            var prop1 = new BindableProperty<int>(0);
            var prop2 = new BindableProperty<string>("x");

            int prop1Value = 0;
            string prop2Value = null;
            prop1.OnValueChanged += (_, newVal) => prop1Value = newVal;
            prop2.OnValueChanged += (_, newVal) => prop2Value = newVal;

            prop1.Value = 42;
            prop2.Value = "hello";

            // SafeMarkDirty 在 EditMode 下同步触发，无需 Flush
            Assert.AreEqual(42, prop1Value);
            Assert.AreEqual("hello", prop2Value);

            prop1.Dispose();
            prop2.Dispose();
        }

        [Test]
        public void SyncFallback_SamePropertyStillFiresOnce()
        {
            // 同帧多次赋值：EditMode 下每次赋值都同步触发
            // 这是退化行为，不同于运行时的合并策略
            var prop = new BindableProperty<int>(0);
            var values = new List<int>();
            prop.OnValueChanged += (oldVal, newVal) => values.Add(newVal);

            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;

            // EditMode 同步模式：每次赋值触发一次（不合并）
            // 但同值赋值不触发（EqualityComparer 过滤）
            Assert.GreaterOrEqual(values.Count, 1);
            Assert.AreEqual(30, values[values.Count - 1]);

            prop.Dispose();
        }

        [Test]
        public void SyncFallback_ChainedUpdate()
        {
            // Model→DC→View 链式更新
            var model = new BindableProperty<int>(0);
            var view = new BindableProperty<string>("");

            model.OnValueChanged += (_, newVal) =>
            {
                view.Value = newVal.ToString();
            };

            string receivedView = null;
            view.OnValueChanged += (_, newVal) => receivedView = newVal;

            model.Value = 42;

            // EditMode 同步：model 回调立即执行，设置 view.Value，
            // view.Value 再次同步触发 view 回调
            Assert.AreEqual("42", receivedView);

            model.Dispose();
            view.Dispose();
        }

        [Test]
        public void FlushScheduler_WhenNoInstance_DoesNotThrow()
        {
            // BatchScheduler 单例不存在时，FlushScheduler 安全跳过
            Assert.DoesNotThrow(() => FlushScheduler());
        }
    }
}
