using System.Collections.Generic;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class BatchSchedulerTests : DataBindingTestBase
    {
        [Test]
        public void Flush_TriggersAllDirty()
        {
            var prop1 = new BindableProperty<int>(0);
            var prop2 = new BindableProperty<string>("x");

            int prop1Value = 0;
            string prop2Value = null;
            prop1.OnValueChanged += (_, newVal) => prop1Value = newVal;
            prop2.OnValueChanged += (_, newVal) => prop2Value = newVal;

            prop1.Value = 42;
            prop2.Value = "hello";
            FlushScheduler();

            Assert.AreEqual(42, prop1Value);
            Assert.AreEqual("hello", prop2Value);

            prop1.Dispose();
            prop2.Dispose();
        }

        [Test]
        public void SameProperty_Merges()
        {
            var prop = new BindableProperty<int>(0);
            var values = new List<int>();
            prop.OnValueChanged += (oldVal, newVal) => values.Add(newVal);

            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            FlushScheduler();

            // 同属性同帧合并为一次回调
            Assert.AreEqual(1, values.Count);
            Assert.AreEqual(30, values[0]);

            prop.Dispose();
        }

        [Test]
        public void TwoRoundFlush_DataContextToView()
        {
            // 模拟 Model→DC→View 链式更新
            var model = new BindableProperty<int>(0);
            var view = new BindableProperty<string>("");

            // DC 监听 Model 变化，转发到 View
            model.OnValueChanged += (_, newVal) =>
            {
                view.Value = newVal.ToString();
            };

            // View 记录接收到的值
            string receivedView = null;
            view.OnValueChanged += (_, newVal) => receivedView = newVal;

            model.Value = 42;
            FlushScheduler();

            // 两轮 Flush: 第一轮触发 model 回调（设置 view.Value），
            // 第二轮触发 view 回调
            Assert.AreEqual("42", receivedView);

            model.Dispose();
            view.Dispose();
        }

        [Test]
        public void EmptyFlush_NoOp()
        {
            // 无脏标记时 Flush 不抛异常
            Assert.DoesNotThrow(() => FlushScheduler());
        }

        [Test]
        public void HasPendingChanges_Correct()
        {
            var prop = new BindableProperty<int>(0);
            prop.OnValueChanged += (_, _) => { };

            Assert.IsFalse(BatchScheduler.Instance.HasPendingChanges);

            prop.Value = 1;
            Assert.IsTrue(BatchScheduler.Instance.HasPendingChanges);

            FlushScheduler();
            Assert.IsFalse(BatchScheduler.Instance.HasPendingChanges);

            prop.Dispose();
        }
    }
}
