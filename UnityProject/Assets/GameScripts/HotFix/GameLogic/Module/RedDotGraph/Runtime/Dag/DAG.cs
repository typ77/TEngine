using System.Collections.Generic;
using System.Text;
using RedDot.Runtime.DigraphList;
using TEngine;

namespace RedDot.Runtime.Dag
{
    /// <summary>
    /// 有向无环图
    /// </summary>
    /// <typeparam name="Tv"></typeparam>
    /// <typeparam name="Te"></typeparam>
    public class DAG<Tv, Te> : DigraphList.DiGraphList<Tv, Te>
    {
        /// <summary>
        /// 在(v, u)之间插入权重为weight的边e
        /// </summary>
        /// <param name="edge"></param>
        /// <param name="v"></param>
        /// <param name="u"></param>
        /// <param name="weight"></param>
        public override bool Insert(int v, int u, Te edge = default(Te), int weight = 0)
        {
            if (Exist(v, u))
            {
                Log.Warning("插入边({0}, {1})失败，该边已经存在", v, u);
                return false;
            }

            Reset();//先重置状态
            SetStatus(v, VStatus.Discovered);//标记开始点为被发现
            //检查是否有环
            if (CheckCyclic(u))
            {
                //P3-6(B6):环检测失败为配置级错误,升级硬报错(仍拒绝不崩溃,旁路进入的环由上层 visited 保证不死循环)
                Log.Error("插入边({0}, {1})失败，构成了环", v, u);
                return false;
            }

            //P1-4 审核 Minor②:成功路径不打日志(建图/加载逐边刷屏),失败路径保留 Warning/Error
            return base.Insert(v, u, edge, weight);
        }

        /// <summary>
        /// 从r顶点开始检测是否有环，采用dfs方式
        /// </summary>
        /// <param name="r"></param>
        /// <returns></returns>
        private bool CheckCyclic(int r)
        {
            Stack<int> undiscoverStack = new Stack<int>();
            Stack<int> discoverStack = new Stack<int>();//用于通路回溯，记录当前通路的各个节点，回溯的时候，pop就行
            undiscoverStack.Push(r);
            while (undiscoverStack.Count > 0)
            {
                int index = undiscoverStack.Peek();
                if (discoverStack.Count == 0 || index != discoverStack.Peek())
                {
                    //这里可以在加上一个GetStatus(index) == VStatus.VISITED则undiscoverStack.Pop(); continue;的判断
                    
                    discoverStack.Push(index);
                    var temp = outEdges[index];
                    SetStatus(index, VStatus.Discovered);
                    for (int i = 0; i < temp.Count; i++)
                    {
                        var edge = temp[i];
                        if (GetStatus(edge.ToVertexRank) == VStatus.Discovered)
                        {
                            var sb = new StringBuilder();
                            sb.Append(edge.ToVertexRank).Append(" <- ");
                            while (discoverStack.Count > 0)
                            {
                                sb.Append(discoverStack.Pop()).Append(" <- ");
                            }
                            sb.Append(edge.ToVertexRank);
                            Log.Error("构成了环，路径:{0}", sb.ToString());
                            return true;
                        }
                        //else
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