using System;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class IndexedMinHeapTests
    {
        private TimerNode[] _nodes;
        private IndexedMinHeap _heap;

        [SetUp]
        public void SetUp()
        {
            _nodes = new TimerNode[16];
            for (int i = 0; i < 16; i++)
                _nodes[i] = new TimerNode { Id = i, FireTime = 0 };
            _heap = new IndexedMinHeap(_nodes, 16);
        }

        [Test]
        public void Pop_AfterPushDisordered_ReturnsAscendingFireTime()
        {
            double[] times = { 5.0, 1.0, 3.0, 2.0, 4.0 };
            for (int i = 0; i < times.Length; i++)
            {
                _nodes[i].FireTime = times[i];
                _heap.Push(i);
            }

            double prev = double.MinValue;
            while (_heap.Count > 0)
            {
                int id = _heap.Pop();
                Assert.GreaterOrEqual(_nodes[id].FireTime, prev);
                prev = _nodes[id].FireTime;
            }
        }

        [Test]
        public void ChangeKey_ToLargerValue_SinksToBottom()
        {
            for (int i = 0; i < 5; i++)
            {
                _nodes[i].FireTime = i + 1.0;
                _heap.Push(i);
            }
            _heap.ChangeKey(0, double.MaxValue);
            Assert.AreNotEqual(0, _heap.Peek());
        }

        [Test]
        public void ChangeKey_ToSmallerValue_RisesToTop()
        {
            for (int i = 0; i < 5; i++)
            {
                _nodes[i].FireTime = i + 2.0;
                _heap.Push(i);
            }
            _heap.ChangeKey(4, 0.0);
            Assert.AreEqual(4, _heap.Peek());
        }

        [Test]
        public void NodePos_AfterSwap_RemainsConsistent()
        {
            for (int i = 0; i < 5; i++)
            {
                _nodes[i].FireTime = 5.0 - i;
                _heap.Push(i);
            }
            for (int heapIdx = 0; heapIdx < _heap.Count; heapIdx++)
            {
                int nodeId = _heap.HeapAt(heapIdx);
                Assert.AreEqual(heapIdx, _heap.NodePosOf(nodeId));
            }
        }

        [Test]
        public void Pop_EmptyHeap_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _heap.Pop());
        }

        [Test]
        public void Peek_SingleElement_ReturnsWithoutRemoving()
        {
            _nodes[0].FireTime = 42.0;
            _heap.Push(0);
            Assert.AreEqual(0, _heap.Peek());
            Assert.AreEqual(1, _heap.Count);
        }
    }
}
