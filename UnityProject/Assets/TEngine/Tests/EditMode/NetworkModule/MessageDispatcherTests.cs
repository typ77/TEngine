using System.Collections.Generic;
using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class MessageDispatcherTests
    {
        private MessageDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new MessageDispatcher(maxPerFrame: 2, maxTimeMs: 100);
        }

        [Test]
        public void Dispatch_HighPriority_ExecutesImmediately()
        {
            int callCount = 0;
            _dispatcher.RegisterHandler<object>(1001, _ => callCount++);

            var trace = new MessageTrace();
            _dispatcher.Enqueue(1001, new object(), MessagePriority.High, in trace);
            _dispatcher.Enqueue(1001, new object(), MessagePriority.High, in trace);
            _dispatcher.Enqueue(1001, new object(), MessagePriority.High, in trace);

            _dispatcher.Dispatch(0.016f);
            Assert.AreEqual(3, callCount); // High 不受 budget 限制
        }

        [Test]
        public void Dispatch_NormalPriority_RespectsBudget()
        {
            int callCount = 0;
            _dispatcher.RegisterHandler<object>(2001, _ => callCount++);

            var trace = new MessageTrace();
            for (int i = 0; i < 5; i++)
                _dispatcher.Enqueue(2001, new object(), MessagePriority.Normal, in trace);

            _dispatcher.Dispatch(0.016f);
            Assert.AreEqual(2, callCount); // maxPerFrame=2

            _dispatcher.Dispatch(0.016f);
            Assert.AreEqual(4, callCount);

            _dispatcher.Dispatch(0.016f);
            Assert.AreEqual(5, callCount);
        }

        [Test]
        public void Dispatch_LowPriority_OnlyAfterNormalEmpty()
        {
            var received = new List<MessagePriority>();
            _dispatcher.RegisterHandler<object>(3001, _ => received.Add(MessagePriority.High));
            _dispatcher.RegisterHandler<object>(3002, _ => received.Add(MessagePriority.Normal));
            _dispatcher.RegisterHandler<object>(3003, _ => received.Add(MessagePriority.Low));

            var trace = new MessageTrace();
            _dispatcher.Enqueue(3003, new object(), MessagePriority.Low, in trace);
            _dispatcher.Enqueue(3002, new object(), MessagePriority.Normal, in trace);

            _dispatcher.Dispatch(0.016f);
            // Normal 先执行，Low 不执行（Normal 未清空）
            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(MessagePriority.Normal, received[0]);
        }

        [Test]
        public void UnregisterHandler_StopsReceiving()
        {
            int callCount = 0;
            _dispatcher.RegisterHandler<object>(4001, _ => callCount++);
            _dispatcher.UnregisterHandler(4001);

            var trace = new MessageTrace();
            _dispatcher.Enqueue(4001, new object(), MessagePriority.High, in trace);
            _dispatcher.Dispatch(0.016f);

            Assert.AreEqual(0, callCount);
        }
    }
}
