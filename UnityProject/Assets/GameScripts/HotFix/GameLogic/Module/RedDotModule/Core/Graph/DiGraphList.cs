using System.Collections.Generic;

namespace GameLogic.RedDot.Core.Graph
{
    /// <summary>
    /// 顶点在图遍历过程中的状态。
    /// </summary>
    public enum VStatus
    {
        Undiscovered,
        Discovered,
        Visited
    }

    /// <summary>
    /// 边在遍历树中所属的类型。
    /// </summary>
    public enum EType
    {
        Undetermined, // 未确定的
        Tree,
        Cross,
        Forward,
        Backward
    }

    /// <summary>
    /// 有向图，邻接链表实现。
    /// <para>移植自 fermsi/RedDotGraph。底层数据结构保持原实现：
    /// 顶点存在 <see cref="Vertices"/>（rank=下标），出边存 <see cref="Edge{TTe}"/> 对象，入边存 int 索引。
    /// 运行时红点系统不删除顶点（聚合节点静态配置），叶子节点采用软删除方案，故此处保留原 List 实现。</para>
    /// </summary>
    /// <typeparam name="TTv">顶点数据类型</typeparam>
    /// <typeparam name="TTe">边数据类型</typeparam>
    public partial class DiGraphList<TTv, TTe>
    {
        private Edge<TTe> GetEdgeObj(int v, int u)
        {
            foreach (var edge in outEdges[v])
            {
                if (edge.ToVertexRank == u) return edge;
            }
            return null;
        }

        protected virtual void Reset()
        {
            for (int i = 0; i < VNum; i++)
            {
                SetStatus(i, VStatus.Undiscovered);
            }
        }
    }
}
