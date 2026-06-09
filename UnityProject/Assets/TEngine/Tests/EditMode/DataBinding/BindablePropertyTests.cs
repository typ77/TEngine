using System;
using System.Collections.Generic;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class BindablePropertyTests : DataBindingTestBase
    {
        [Test]
        public void Default_Value_IsDefault()
        {
            var prop = new BindableProperty<int>();
            Assert.AreEqual(0, prop.Value);
        }

        [Test]
        public void SetValue_UpdatesImmediately()
        {
            var prop = new BindableProperty<int>(42);
            prop.Value = 100;
            Assert.AreEqual(100, prop.Value);
        }

        [Test]
        public void SetSameValue_DoesNotMarkDirty()
        {
            var prop = new BindableProperty<int>(42);
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;
            prop.Value = 42;
            // 没有 BatchScheduler.Flush，但即使有也不应触发
            Assert.IsFalse(fired);
        }

        [Test]
        public void Flush_TriggersCallback()
        {
            var prop = new BindableProperty<int>(10);
            int receivedNew = 0;
            prop.OnValueChanged += (_, newVal) => receivedNew = newVal;
            prop.Value = 20;
            // BatchScheduler 未实现，手动通过接口触发
            ((IBatchDirtyTarget)prop).FireCallback();
            Assert.AreEqual(20, receivedNew);
        }

        [Test]
        public void Callback_HasOldAndNewValue()
        {
            var prop = new BindableProperty<int>(10);
            int receivedOld = 0, receivedNew = 0;
            prop.OnValueChanged += (oldVal, newVal) => { receivedOld = oldVal; receivedNew = newVal; };
            prop.Value = 20;
            ((IBatchDirtyTarget)prop).FireCallback();
            Assert.AreEqual(10, receivedOld);
            Assert.AreEqual(20, receivedNew);
        }

        [Test]
        public void MultipleSetSameFrame_EachValuePropagates()
        {
            // EditMode 下 SafeMarkDirty 同步触发 FireCallback，每次赋值立即回调
            var prop = new BindableProperty<int>(0);
            int callCount = 0;
            prop.OnValueChanged += (_, _) => callCount++;
            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            Assert.AreEqual(3, callCount, "EditMode 同步模式：每次赋值触发一次回调");
            Assert.AreEqual(30, prop.Value, "最终值应为最后一次赋值");
        }

        [Test]
        public void MultipleSetSameFrame_CallbackHasCorrectOldAndNew()
        {
            // EditMode 同步模式：每次回调携带本次的 old/new 值
            var prop = new BindableProperty<int>(0);
            int lastOld = -1, lastNew = -1;
            prop.OnValueChanged += (oldVal, newVal) => { lastOld = oldVal; lastNew = newVal; };
            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            // 最后一次回调：old=20, new=30
            Assert.AreEqual(20, lastOld, "最后一次回调的 old 应为 20");
            Assert.AreEqual(30, lastNew, "最后一次回调的 new 应为 30");
        }

        [Test]
        public void NoSubscriber_NoDirtyMark()
        {
            var prop = new BindableProperty<int>(0);
            Assert.DoesNotThrow(() => prop.Value = 10);
            Assert.AreEqual(10, prop.Value);
        }

        [Test]
        public void CustomComparer_Works()
        {
            var prop = new BindableProperty<string>("hello", StringComparer.OrdinalIgnoreCase);
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;
            prop.Value = "HELLO";
            Assert.IsFalse(fired);
        }

        [Test]
        public void Dispose_PreventsCallback()
        {
            var prop = new BindableProperty<int>(0);
            prop.OnValueChanged += (_, _) => { };
            prop.Dispose();
            Assert.IsTrue(prop.IsDisposed);
            Assert.DoesNotThrow(() => prop.Value = 10);
        }

        [Test]
        public void ForceNotify_TriggersCallback()
        {
            var prop = new BindableProperty<int>(42);
            int receivedNew = 0;
            prop.OnValueChanged += (_, newVal) => receivedNew = newVal;
            prop.ForceNotify();
            ((IBatchDirtyTarget)prop).FireCallback();
            Assert.AreEqual(42, receivedNew);
        }

        [Test]
        public void SetValueSilently_NoNotification()
        {
            var prop = new BindableProperty<int>(0);
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;
            prop.SetValueSilently(100);
            Assert.AreEqual(100, prop.Value);
            Assert.IsFalse(fired);
        }

        [Test]
        public void RecordStruct_ValueComparison()
        {
            var prop = new BindableProperty<TestData>(new TestData(1, "a"));
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;
            prop.Value = new TestData(1, "a");
            Assert.IsFalse(fired);
        }

        [Test]
        public void RecordStruct_DifferentValue_Fires()
        {
            var prop = new BindableProperty<TestData>(new TestData(1, "a"));
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;
            prop.Value = new TestData(2, "b");
            ((IBatchDirtyTarget)prop).FireCallback();
            Assert.IsTrue(fired);
        }

        private struct TestData : IEquatable<TestData>
        {
            public int Id;
            public string Name;

            public TestData(int id, string name) { Id = id; Name = name; }

            public bool Equals(TestData other) => Id == other.Id && Name == other.Name;
            public override bool Equals(object obj) => obj is TestData other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Id, Name);
            public static bool operator ==(TestData left, TestData right) => left.Equals(right);
            public static bool operator !=(TestData left, TestData right) => !left.Equals(right);
        }
    }
}
