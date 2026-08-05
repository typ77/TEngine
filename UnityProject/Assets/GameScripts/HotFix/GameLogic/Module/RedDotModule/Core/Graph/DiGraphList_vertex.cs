using System.Collections.Generic;

namespace GameLogic.RedDot.Core.Graph
{
    /// <summary>
    /// <see cref="DiGraphList{TTv,TTe}"/> 的顶点管理部分。
    /// </summary>
    public partial class DiGraphList<TTv, TTe>
    {
        /// <summary>
        /// 顶点列表，顶点的 rank 即为在此列表中的下标。
        /// </summary>
        protected List<Vertex<TTv>> Vertices = new List<Vertex<TTv>>();

        /// <summary>
        /// 顶点总数。
        /// </summary>
        protected int VNum = 0;

        /// <summary>
        /// 插入顶点，返回新顶点的 rank。
        /// </summary>
        public virtual int Insert(TTv v)
        {
            Vertices.Add(new Vertex<TTv>(v));
            outEdges.Add(new List<Edge<TTe>>());
            inEdges.Add(new List<int>());
            return VNum++;
        }

        /// <summary>
        /// 删除顶点及其关联边，返回该顶点数据。
        /// <para>注意：删除会触发全图 rank 重映射（O(V+E)），仅编辑器使用。
        /// 运行时红点系统不调用此方法——叶子节点采用软删除方案。</para>
        /// </summary>
        public virtual TTv Remove(int r)
        {
            Vertex<TTv> v = Vertices[r];
            List<Edge<TTe>> es = outEdges[r];
            // 删除所有出边
            for (int i = es.Count - 1; i >= 0; i--)
            {
                Remove(r, es[i].ToVertexRank);
            }

            // 删除所有入边
            List<int> temp = inEdges[r];
            for (int i = temp.Count - 1; i >= 0; i--)
            {
                Remove(i, r);
            }

            // 清空相关数据
            outEdges.RemoveAt(r);
            inEdges.RemoveAt(r);
            Vertices.RemoveAt(r);
            VNum--;

            // 以下效率性能不好，但是删除方法在游戏运行中一般是不用的，编辑器中可能需要
            // 所有出度边目标 rank 大于 r 的减 1
            for (int i = 0; i < outEdges.Count; i++)
            {
                foreach (var edge in outEdges[i])
                {
                    if (edge.ToVertexRank > r) edge.ToVertexRank--;
                }
            }
            // 所有入度边源 rank 大于 r 的减 1
            foreach (var lInEdge in inEdges)
            {
                for (int i = 0; i < lInEdge.Count; i++)
                {
                    if (lInEdge[i] > r) lInEdge[i]--;
                }
            }

            return v.Data;
        }

        /// <summary>
        /// 获取顶点数据（假定顶点存在）。
        /// </summary>
        public virtual TTv GetVertex(int r)
        {
            return Vertices[r].Data;
        }

        /// <summary>
        /// 顶点入度。
        /// </summary>
        public virtual int InDegree(int r)
        {
            return inEdges[r].Count;
        }

        /// <summary>
        /// 顶点出度。
        /// </summary>
        public virtual int OutDegree(int r)
        {
            return outEdges[r].Count;
        }

        /// <summary>
        /// 顶点的首个邻接顶点 rank，无则返回 -1。
        /// </summary>
        public virtual int FirstNbr(int r)
        {
            var list = outEdges[r];
            if (list.Count > 0) return list[0].ToVertexRank;
            return -1;
        }

        /// <summary>
        /// 顶点 v（相对于顶点 j 的）下一邻接顶点，暂未实现。
        /// </summary>
        public virtual int NextNbr(int r, int j)
        {
            return -1;
        }

        public virtual VStatus GetStatus(int r) => Vertices[r].Status;
        public virtual void SetStatus(int r, VStatus status) => Vertices[r].Status = status;
        public virtual int GetDTime(int r) => Vertices[r].DTime;
        public virtual void SetDTime(int r, int time) => Vertices[r].DTime = time;
        public virtual int GetFTime(int r) => Vertices[r].FTime;
        public virtual void SetFTime(int r, int time) => Vertices[r].FTime = time;
        public virtual int GetParent(int r) => Vertices[r].Parent;
        public virtual void SetParent(int r, int pr) => Vertices[r].Parent = pr;
        public virtual int GetPriority(int r) => Vertices[r].Priority;
        public virtual void SetPriority(int r, int priority) => Vertices[r].Priority = priority;
    }
}
