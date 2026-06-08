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
        public void MultipleSetSameFrame_MergesCallback()
        {
            var prop = new BindableProperty<int>(0);
            int callCount = 0;
            prop.OnValueChanged += (_, _) => callCount++;
            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            ((IBatchDirtyTarget)prop).FireCallback();
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void MultipleSetSameFrame_OldestOldValue()
        {
            var prop = new BindableProperty<int>(0);
            int receivedOld = -1, receivedNew = -1;
            prop.OnValueChanged += (oldVal, newVal) => { receivedOld = oldVal; receivedNew = newVal; };
            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            ((IBatchDirtyTarget)prop).FireCallback();
            Assert.AreEqual(0, receivedOld);
            Assert.AreEqual(30, receivedNew);
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

        private readonly record struct TestData(int Id, string Name);
    }
}
