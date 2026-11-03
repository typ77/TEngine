using System;
using System.Collections.Generic;
using RedDot.Editor.Config;
using UnityEngine;

namespace RedDot.Editor.Data
{
    [Serializable]
    public class RedDotNodeContext
    {
        public int id;
        public Vector2 position;
        public List<int> outNodeIds = new List<int>();
        public int keyId;
        public int funcId;
        public int redDotType;
        public int groupId;      // 归属组(0=主图/未分组)
        public int refGroupId;   // >0=本节点是分组引用节点,指向 RedDotGroup.id(引用节点禁出边,D2)

        public void RemoveOutNode(int id)
        {
            outNodeIds.Remove(id);
        }

        public void AddOutNode(int id)
        {
            outNodeIds.Add(id);
        }
    }
}