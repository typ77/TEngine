// Assets/TEngine/Runtime/Module/TimerModule/IndexedMinHeap.cs
using System;

namespace TEngine
{
    /// <summary>
    /// 索引最小堆。堆键为 nodes[nodeId].FireTime（double）。
    /// _nodePos[nodeId] 存储 nodeId 在堆数组中的位置，支持 O(log n) ChangeKey。
    /// </summary>
    internal class IndexedMinHeap
    {
        private int[] _heap;      // _heap[heapPos] = nodeId
        private int[] _nodePos;   // _nodePos[nodeId] = heapPos，-1 表示不在堆中
        private int _count;
        private readonly TimerNode[] _nodes; // 共享引用，堆只读取 FireTime

        public int Count => _count;

        public IndexedMinHeap(TimerNode[] nodes, int capacity)
        {
            _nodes = nodes;
            _heap = new int[capacity];
            _nodePos = new int[capacity];
            for (int i = 0; i < capacity; i++) _nodePos[i] = -1;
        }

        public void Push(int nodeId)
        {
            if (_count >= _heap.Length)
                throw new InvalidOperationException("Heap is full. Call Expand first.");
            _heap[_count] = nodeId;
            _nodePos[nodeId] = _count;
            _count++;
            SiftUp(_count - 1);
        }

        public int Pop()
        {
            if (_count == 0) throw new InvalidOperationException("Heap is empty.");
            int top = _heap[0];
            _nodePos[top] = -1;
            _count--;
            if (_count > 0)
            {
                _heap[0] = _heap[_count];
                _nodePos[_heap[0]] = 0;
                SiftDown(0);
            }
            return top;
        }

        public int Peek()
        {
            if (_count == 0) throw new InvalidOperationException("Heap is empty.");
            return _heap[0];
        }

        public void ChangeKey(int nodeId, double newFireTime)
        {
            double oldFireTime = _nodes[nodeId].FireTime;
            _nodes[nodeId].FireTime = newFireTime;
            int pos = _nodePos[nodeId];
            if (pos < 0) return; // 不在堆中
            if (newFireTime < oldFireTime)
                SiftUp(pos);
            else
                SiftDown(pos);
        }

        public void Expand(int newCapacity)
        {
            int[] newHeap = new int[newCapacity];
            int[] newNodePos = new int[newCapacity];
            Array.Copy(_heap, newHeap, _count);
            Array.Copy(_nodePos, newNodePos, _nodePos.Length);
            for (int i = _nodePos.Length; i < newCapacity; i++) newNodePos[i] = -1;
            _heap = newHeap;
            _nodePos = newNodePos;
        }

        // 测试辅助——暴露内部状态
        internal int HeapAt(int heapIdx) => _heap[heapIdx];
        internal int NodePosOf(int nodeId) => _nodePos[nodeId];

        private void SiftUp(int pos)
        {
            while (pos > 0)
            {
                int parent = (pos - 1) >> 1;
                if (_nodes[_heap[parent]].FireTime <= _nodes[_heap[pos]].FireTime) break;
                Swap(parent, pos);
                pos = parent;
            }
        }

        private void SiftDown(int pos)
        {
            while (true)
            {
                int left = (pos << 1) + 1;
                int right = left + 1;
                int smallest = pos;
                if (left < _count && _nodes[_heap[left]].FireTime < _nodes[_heap[smallest]].FireTime)
                    smallest = left;
                if (right < _count && _nodes[_heap[right]].FireTime < _nodes[_heap[smallest]].FireTime)
                    smallest = right;
                if (smallest == pos) break;
                Swap(pos, smallest);
                pos = smallest;
            }
        }

        private void Swap(int i, int j)
        {
            int tmp = _heap[i];
            _heap[i] = _heap[j];
            _heap[j] = tmp;
            _nodePos[_heap[i]] = i;
            _nodePos[_heap[j]] = j;
        }
    }
}
