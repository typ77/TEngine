using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class EventDelegateDataTests
    {
        private const int TestEventId = 1;
        private EventDelegateData _data;

        [SetUp]
        public void SetUp()
        {
            _data = new EventDelegateData(TestEventId);
        }

        [Test]
        public void Callback_NormalHandlers_AllExecuted()
        {
            var callOrder = new List<int>();
            _data.AddHandler(new Action(() => callOrder.Add(1)));
            _data.AddHandler(new Action(() => callOrder.Add(2)));
            _data.AddHandler(new Action(() => callOrder.Add(3)));
            _data.Callback();
            Assert.AreEqual(3, callOrder.Count);
            Assert.AreEqual(1, callOrder[0]);
            Assert.AreEqual(2, callOrder[1]);
            Assert.AreEqual(3, callOrder[2]);
        }

        [Test]
        public void Callback_SecondHandlerThrows_ThirdStillExecutes()
        {
            var callOrder = new List<int>();
            _data.AddHandler(new Action(() => callOrder.Add(1)));
            _data.AddHandler(new Action(() => { callOrder.Add(2); throw new InvalidOperationException("test"); }));
            _data.AddHandler(new Action(() => callOrder.Add(3)));
            _data.Callback();
            Assert.AreEqual(3, callOrder.Count);
            Assert.AreEqual(1, callOrder[0]);
            Assert.AreEqual(2, callOrder[1]);
            Assert.AreEqual(3, callOrder[2]);
        }

        [Test]
        public void Callback_AllHandlersThrow_CheckModifyStillRuns()
        {
            _data.AddHandler(new Action(() => throw new Exception("err1")));
            _data.AddHandler(new Action(() => throw new Exception("err2")));
            Assert.DoesNotThrow(() => _data.Callback());
            // _isExecute should be reset, so AddHandler should work
            bool added = _data.AddHandler(new Action(() => { }));
            Assert.IsTrue(added);
        }

        [Test]
        public void Callback_AddHandlerAfterException_NewHandlerApplied()
        {
            var called = false;
            _data.AddHandler(new Action(() => throw new Exception("err")));
            _data.Callback();
            _data.AddHandler(new Action(() => called = true));
            _data.Callback();
            Assert.IsTrue(called);
        }

        [Test]
        public void Callback_RmvHandlerAfterCallback_HandlerRemoved()
        {
            var handler1Called = false;
            Action handler1 = () => handler1Called = true;
            _data.AddHandler(handler1);
            _data.Callback();
            Assert.IsTrue(handler1Called);
            handler1Called = false;
            _data.RmvHandler(handler1);
            _data.Callback();
            Assert.IsFalse(handler1Called);
        }

        [Test]
        public void Callback_EmptyList_NoException()
        {
            Assert.DoesNotThrow(() => _data.Callback());
        }

        [Test]
        public void Callback_WithArg_ExceptionIsolation()
        {
            var received = new List<string>();
            _data.AddHandler(new Action<string>(s => received.Add(s)));
            _data.AddHandler(new Action<string>(s => { received.Add(s); throw new Exception("err"); }));
            _data.AddHandler(new Action<string>(s => received.Add(s)));
            _data.Callback("test");
            Assert.AreEqual(3, received.Count);
            Assert.AreEqual("test", received[0]);
            Assert.AreEqual("test", received[1]);
            Assert.AreEqual("test", received[2]);
        }
    }
}
