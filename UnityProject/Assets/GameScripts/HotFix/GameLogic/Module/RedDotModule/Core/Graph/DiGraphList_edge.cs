using System;
using System.Collections.Generic;

namespace GameLogic.RedDot.Core.Graph
{
    /// <summary>
    /// <see cref="DiGraphList{TTv,TTe}"/> 的边管理部分。
    /// <para>出边存 <see cref="Edge{TTe}"/> 对象（含 ToVertexRank），入边存 int（源顶点 rank）。</para>
    /// </summary>
    public partial class DiGraphList<TTv, TTe>
    {
        /// <summary>
        /// 出度边表：outEdges[v] = v 的所有出边。
        /// </summary>
        protected List<List<Edge<TTe>>> outEdges = new List<List<Edge<TTe>>>();

        /// <summary>
        /// 入度边表：inEdges[u] = 指向 u 的所有源顶点 rank 列表。
        /// </summary>
        protected List<List<int>> inEdges = new List<List<int>>();

        /// <summary>
        /// 边总数。
        /// </summary>
        protected int eNum = 0;

        /// <summary>
        /// 边 (v, u) 是否存在。
        /// </summary>
        public virtual bool Exist(int v, int u)
        {
            foreach (var edge in outEdges[v])
            {
                if (edge.ToVertexRank == u) return true;
            }
            return false;
        }

        /// <summary>
        /// 在 (v, u) 之间插入权重为 weight 的边。已存在则返回 false。
        /// </summary>
        public virtual bool Insert(int v, int u, TTe edge = default(TTe), int weight = 0)
        {
            if (Exist(v, u))
            {
                return false;
            }
            outEdges[v].Add(new Edge<TTe>(u, edge, weight));
            inEdges[u].Add(v);
            eNum++;
            return true;
        }

        /// <summary>
        /// 删除边 (v, u)，返回该边数据。目前仅编辑器会用到，不做性能优化。
        /// </summary>
        public virtual TTe Remove(int v, int u)
        {
            for (int edgeIndex = 0; edgeIndex < outEdges[v].Count; edgeIndex++)
            {
                if (outEdges[v][edgeIndex].ToVertexRank == u)
                {
                    for (int inEdgeIndex = 0; inEdgeIndex < inEdges[u].Count; inEdgeIndex++)
                    {
                        if (inEdges[u][inEdgeIndex] == v)
                        {
                            inEdges[u].RemoveAt(inEdgeIndex); // 删除入度
                            break;
                        }
                    }

                    eNum--;
                    var temp = outEdges[v][edgeIndex];
                    outEdges[v].RemoveAt(edgeIndex); // 删除出度
                    return temp.Data;
                }
            }
            return default(TTe);
        }

        public virtual EType GetEType(int v, int u)
        {
            var edge = GetEdgeObj(v, u);
            if (edge != null) return edge.Etype;
            return EType.Undetermined;
        }

        public virtual void SetEType(int v, int u, EType type)
        {
            var edge = GetEdgeObj(v, u);
            if (edge != null) edge.Etype = type;
        }

        public virtual TTe GetEdge(int v, int u)
        {
            var edge = GetEdgeObj(v, u);
            if (edge != null) return edge.Data;
            return default(TTe);
        }

        public virtual void SetEdge(int v, int u, TTe edge)
        {
            var edgeObj = GetEdgeObj(v, u);
            if (edgeObj != null) edgeObj.Data = edge;
        }

        public virtual int GetWeight(int v, int u)
        {
            var edge = GetEdgeObj(v, u);
            if (edge != null) return edge.Weight;
            return 0;
        }

        public virtual void SetWeight(int v, int u, int weight)
        {
            var edge = GetEdgeObj(v, u);
            if (edge != null) edge.Weight = weight;
        }

        /// <summary>
        /// 把边 (v, oldU) 改为 (v, newU)，仅编辑器使用，未做性能优化。
        /// </summary>
        public virtual void ChangeEdgeTo(int v, int oldU, int newU)
        {
            var oldEdge = GetEdgeObj(v, oldU);
            Insert(v, newU, oldEdge.Data, oldEdge.Weight); // 先添加
            Remove(v, oldU); // 后删除
        }
    }
}
