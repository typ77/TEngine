using System.Collections.Generic;
using GameLogic.RedDot.Core.Graph;
using TEngine;

namespace GameLogic.RedDot.Core
{
    /// <summary>
    /// 红点图基类：在 <see cref="DAG{TTv,TTe}"/> 之上增加 key→rank 字典与全局状态变化时间戳。
    /// </summary>
    public class BaseRedDotGraph : DAG<RedDotVertex, int>
    {
        private static uint s_time = 0;

        /// <summary>
        /// 获取全局递增时间戳（用于 <see cref="RedDotVertexRuntimeInfo.StatusChangeTime"/> 比较去重）。
        /// </summary>
        protected static uint GetTime()
        {
            s_time++;
            return s_time;
        }

        /// <summary>
        /// key → rank（顶点下标）映射表。
        /// </summary>
        protected Dictionary<string, int> VertexDict = new Dictionary<string, int>();

        /// <summary>
        /// 插入红点顶点。若 key 已存在则返回 -1。
        /// </summary>
        public override int Insert(RedDotVertex v)
        {
            if (CheckHasRedDotByKey(v.Key))
            {
                Log.Warning($"已经有此红点了, key: {v.Key}");
                return -1;
            }
            int index = base.Insert(v);
            var vertex = GetVertex(index);
            VertexDict[vertex.Key] = index;
            return index;
        }

        /// <summary>
        /// 在 (vex1, vex2) 之间插入边（vex1 为父，vex2 为子）。
        /// </summary>
        public bool InsertEdge(string vex1, string vex2, int weight = 0)
        {
            if (!VertexDict.TryGetValue(vex1, out int v))
            {
                Log.Warning($"没找到顶点：{vex1}");
                return false;
            }
            if (!VertexDict.TryGetValue(vex2, out int u))
            {
                Log.Warning($"没找到顶点：{vex2}");
                return false;
            }
            return base.Insert(v, u, 0, weight);
        }

        /// <summary>
        /// 判断是否已存在某 key 的红点。
        /// </summary>
        protected bool CheckHasRedDotByKey(string key) => VertexDict.ContainsKey(key);

        /// <summary>
        /// 根据 key 获取红点顶点，不存在返回 null。
        /// </summary>
        public RedDotVertex GetRedDotByKey(string key)
        {
            if (VertexDict.TryGetValue(key, out int index))
            {
                return GetVertex(index);
            }
            return null;
        }

        /// <summary>
        /// 尝试获取 key 对应的 rank。
        /// </summary>
        public bool TryGetRank(string key, out int rank) => VertexDict.TryGetValue(key, out rank);

        // ─── 软删除支持（动态叶子节点） ───

        /// <summary>
        /// 断开从父节点 parentRank 到子节点 childRank 的边（双向：父出边 + 子入边）。
        /// <para>仅断边，不删除顶点，不触发全图 rank 重映射（区别于 <see cref="DiGraphList{TTv,TTe}.Remove(int)"/>）。
        /// 用于动态叶子节点的软删除。</para>
        /// </summary>
        internal void DetachEdgeInternal(int parentRank, int childRank)
        {
            // 移除父→子出边
            var parentOut = outEdges[parentRank];
            for (int i = parentOut.Count - 1; i >= 0; i--)
            {
                if (parentOut[i].ToVertexRank == childRank)
                {
                    parentOut.RemoveAt(i);
                    eNum--;
                }
            }
            // 移除子→父入边
            var childIn = inEdges[childRank];
            for (int i = childIn.Count - 1; i >= 0; i--)
            {
                if (childIn[i] == parentRank)
                {
                    childIn.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 从 key 映射表中移除指定 key（软删除的一部分，顶点对象本身保留在 List 中避免 rank 重映射）。
        /// </summary>
        internal void RemoveFromKeyDictInternal(string key) => VertexDict.Remove(key);

        /// <summary>
        /// 获取节点的所有父节点 rank 列表（入边源）。
        /// </summary>
        internal List<int> GetParentRanksInternal(int rank) => inEdges[rank];

        /// <summary>
        /// 获取节点的所有子节点 rank 列表（出边目标）。
        /// </summary>
        internal List<int> GetChildRanksInternal(int rank)
        {
            var result = new List<int>(outEdges[rank].Count);
            foreach (var edge in outEdges[rank])
            {
                result.Add(edge.ToVertexRank);
            }
            return result;
        }

        /// <summary>
        /// 通过顶点对象引用查找其 rank（朴素遍历）。动态叶子量级一般不大，无需优化。
        /// </summary>
        internal int FindRankByVertexInternal(RedDotVertex vertex)
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices[i] != null && ReferenceEquals(Vertices[i].Data, vertex))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
