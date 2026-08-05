using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Data
{
    /// <summary>
    /// 编辑器中红点节点的上下文数据（序列化到 .reddotgraph 文件）。
    /// </summary>
    [Serializable]
    public class RedDotNodeContext
    {
        public int id;
        public Vector2 position;
        public List<int> outNodeIds = new List<int>();
        public int keyId;
        public int funcId;
        public int redDotType;

        public void RemoveOutNode(int id) => outNodeIds.Remove(id);
        public void AddOutNode(int id) => outNodeIds.Add(id);
    }
}
