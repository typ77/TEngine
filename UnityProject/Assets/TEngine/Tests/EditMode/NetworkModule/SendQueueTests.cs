using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class SendQueueTests
    {
        private SendQueue _queue;

        [SetUp]
        public void SetUp()
        {
            _queue = new SendQueue();
        }

        [Test]
        public void DequeueAll_Empty_ReturnsNothing()
        {
            int count = 0;
            foreach (var _ in _queue.DequeueAll()) count++;
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Enqueue_Then_DequeueAll_ReturnsItems()
        {
            _queue.Enqueue(new byte[] { 0x01 });
            _queue.Enqueue(new byte[] { 0x02 });
            int count = 0;
            foreach (var item in _queue.DequeueAll())
            {
                count++;
            }
            Assert.AreEqual(2, count);
            Assert.AreEqual(0, _queue.Count);
        }

        [Test]
        public void Pause_KeepsMessages()
        {
            _queue.Enqueue(new byte[] { 0x01 });
            _queue.Pause();

            int count = 0;
            foreach (var _ in _queue.DequeueAll()) count++;
            Assert.AreEqual(0, count); // 暂停时不可取出
            Assert.AreEqual(1, _queue.Count); // 消息还在
        }

        [Test]
        public void Resume_AllowsDequeue()
        {
            _queue.Enqueue(new byte[] { 0x01 });
            _queue.Pause();
            _queue.Resume();

            int count = 0;
            foreach (var _ in _queue.DequeueAll()) count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Clear_RemovesAllMessages()
        {
            _queue.Enqueue(new byte[] { 0x01 });
            _queue.Enqueue(new byte[] { 0x02 });
            _queue.Clear();
            Assert.AreEqual(0, _queue.Count);
        }
    }
}
