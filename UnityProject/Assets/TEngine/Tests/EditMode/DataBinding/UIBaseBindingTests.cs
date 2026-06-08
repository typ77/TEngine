// Assets/TEngine/Tests/EditMode/DataBinding/UIBaseBindingTests.cs
using System;
using GameLogic.DataBinding;
using GameLogic;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class UIBaseBindingTests : DataBindingTestBase
    {
        private UIBase _uiBase;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _uiBase = new TestUIBase();
        }

        [Test]
        public void Bind_ImmediateCallback()
        {
            var prop = new BindableProperty<string>("hello");
            string received = null;
            _uiBase.Bind(prop, v => received = v);
            Assert.AreEqual("hello", received);
        }

        [Test]
        public void Bind_WithOldValue_ImmediateCallback()
        {
            var prop = new BindableProperty<int>(42);
            int receivedOld = 0, receivedNew = 0;
            _uiBase.Bind(prop, (oldVal, newVal) => { receivedOld = oldVal; receivedNew = newVal; });
            Assert.AreEqual(42, receivedOld);
            Assert.AreEqual(42, receivedNew);
        }

        [Test]
        public void Bind_PropertyChange_TriggersCallback()
        {
            var prop = new BindableProperty<int>(0);
            int received = 0;
            _uiBase.Bind(prop, v => received = v);
            prop.Value = 99;
            FlushScheduler();
            Assert.AreEqual(99, received);
        }

        [Test]
        public void RemoveAllBindings_NoFurtherCallback()
        {
            var prop = new BindableProperty<int>(0);
            int callCount = 0;
            _uiBase.Bind(prop, v => callCount++);
            Assert.AreEqual(1, callCount);
            _uiBase.RemoveAllBindings();
            prop.Value = 99;
            FlushScheduler();
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void MultipleBinds_AllTrigger()
        {
            var prop = new BindableProperty<int>(0);
            int count1 = 0, count2 = 0;
            _uiBase.Bind(prop, v => count1++);
            _uiBase.Bind(prop, v => count2++);
            Assert.AreEqual(1, count1);
            Assert.AreEqual(1, count2);
            prop.Value = 1;
            FlushScheduler();
            Assert.AreEqual(2, count1);
            Assert.AreEqual(2, count2);
        }

        [Test]
        public void Bind_NullProperty_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _uiBase.Bind<int>(null, v => { }));
        }

        private class TestUIBase : UIBase { }
    }
}
