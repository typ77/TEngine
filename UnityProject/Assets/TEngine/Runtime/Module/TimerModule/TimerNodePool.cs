// Assets/TEngine/Runtime/Module/TimerModule/TimerNodePool.cs
using System;

namespace TEngine
{
    internal class TimerNodePool
    {
        private TimerNode[] _nodes;
        private int[] _freeStack;
        private int _freeTop;

        public int Capacity => _nodes.Length;
        public int UsedCount => _nodes.Length - _freeTop;
        public TimerNode[] Nodes => _nodes;

        public event Action<int> OnExpand;

        public TimerNodePool(int initialCapacity = 128)
        {
            _nodes = new TimerNode[initialCapacity];
            _freeStack = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
            {
                _nodes[i] = new TimerNode { Id = i, Version = 1 };
                _freeStack[i] = i;
            }
            _freeTop = initialCapacity;
        }

        public int Rent()
        {
            if (_freeTop == 0)
                Expand(_nodes.Length * 2);

            int nodeId = _freeStack[--_freeTop];
            _nodes[nodeId].Reset();
            _nodes[nodeId].Id = nodeId;
            return nodeId;
        }

        public void Return(int nodeId)
        {
            TimerNode node = _nodes[nodeId];
            node.Reset();
            unchecked { node.Version++; }
            if (node.Version == 0) node.Version = 1;
            _freeStack[_freeTop++] = nodeId;
        }

        private void Expand(int newCapacity)
        {
            Log.Info($"[Timer] Pool expanding: {_nodes.Length} → {newCapacity}");
            int oldCap = _nodes.Length;

            TimerNode[] newNodes = new TimerNode[newCapacity];
            Array.Copy(_nodes, newNodes, oldCap);

            int[] newFreeStack = new int[newCapacity];
            Array.Copy(_freeStack, newFreeStack, _freeTop);

            _nodes = newNodes;
            _freeStack = newFreeStack;

            for (int i = oldCap; i < newCapacity; i++)
            {
                _nodes[i] = new TimerNode { Id = i, Version = 1 };
                _freeStack[_freeTop++] = i;
            }

            OnExpand?.Invoke(newCapacity);
        }
    }
}
