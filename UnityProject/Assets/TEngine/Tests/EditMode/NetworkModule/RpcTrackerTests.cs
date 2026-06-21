using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class RpcTrackerTests
    {
        private RpcTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _tracker = new RpcTracker("test");
        }

        [Test]
        public void Allocate_ReturnsIncrementingIds()
        {
            uint id1 = _tracker.AllocateId();
            uint id2 = _tracker.AllocateId();
            Assert.AreEqual(1u, id1);
            Assert.AreEqual(2u, id2);
        }

        [Test]
        public void TryGet_Existing_ReturnsTrue()
        {
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                tcs, typeof(object), 10000);
            Assert.IsTrue(_tracker.TryGet(1, out _));
        }

        [Test]
        public void TryGet_NonExisting_ReturnsFalse()
        {
            Assert.IsFalse(_tracker.TryGet(999, out _));
        }

        [Test]
        public void Remove_Existing()
        {
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                tcs, typeof(object), 10000);
            _tracker.Remove(1);
            Assert.IsFalse(_tracker.TryGet(1, out _));
        }

        [Test]
        public void PendingCount_TracksCorrectly()
        {
            Assert.AreEqual(0, _tracker.PendingCount);
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                tcs, typeof(object), 10000);
            Assert.AreEqual(1, _tracker.PendingCount);
        }
    }
}
