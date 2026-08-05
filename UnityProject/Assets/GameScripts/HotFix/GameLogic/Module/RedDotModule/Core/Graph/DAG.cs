using System.Collections.Generic;

namespace GameLogic.RedDot.Core.Graph
{
    /// <summary>
    /// 有向无环图（DAG）。在 <see cref="Insert(int,int,TTe,int)"/> 时做环检测，构成环则拒绝插入。
    /// <para>移植自 fermsi/RedDotGraph。原实现用 <see cref="System.Console.WriteLine"/> 打日志，这里改为静默返回 false。</para>
    /// </summary>
    /// <typeparam name="TTv">顶点数据类型</typeparam>
    /// <typeparam name="TTe">边数据类型</typeparam>
    public class DAG<TTv, TTe> : DiGraphList<TTv, TTe>
    {
        /// <summary>
        /// 在 (v, u) 之间插入边。插入前会做环检测，若构成环则拒绝并返回 false。
        /// </summary>
        public override bool Insert(int v, int u, TTe edge = default(TTe), int weight = 0)
        {
            if (Exist(v, u))
            {
                return false;
            }

            Reset(); // 先重置状态
            SetStatus(v, VStatus.Discovered); // 标记起点为已发现
            // 检查是否构成环
            if (CheckCyclic(u))
            {
                return false;
            }

            return base.Insert(v, u, edge, weight);
        }

        /// <summary>
        /// 从 r 顶点出发采用 DFS 检测是否存在回到已发现节点的路径（即环）。
        /// </summary>
        private bool CheckCyclic(int r)
        {
            Stack<int> undiscoverStack = new Stack<int>();
            Stack<int> discoverStack = new Stack<int>(); // 用于通路回溯，记录当前通路各节点
            undiscoverStack.Push(r);
            while (undiscoverStack.Count > 0)
            {
                int index = undiscoverStack.Peek();
                if (discoverStack.Count == 0 || index != discoverStack.Peek())
                {
                    discoverStack.Push(index);
                    var temp = outEdges[index];
                    SetStatus(index, VStatus.Discovered);
                    for (int i = 0; i < temp.Count; i++)
                    {
                        var edge = temp[i];
                        if (GetStatus(edge.ToVertexRank) == VStatus.Discovered)
                        {
                            // 撞回已发现节点 → 构成环
                            return true;
                        }
                        if (GetStatus(edge.ToVertexRank) == VStatus.Undiscovered)
                            undiscoverStack.Push(edge.ToVertexRank);
                    }
                }
                else
                {
                    SetStatus(undiscoverStack.Pop(), VStatus.Visited);
                    discoverStack.Pop();
                }
            }

            return false;
        }
    }
}
