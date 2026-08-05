using System;
using System.Collections.Generic;
using GameLogic.RedDot.Core.Graph;
using TEngine;

namespace GameLogic.RedDot.Core
{
    /// <summary>
    /// 红点图核心算法：状态聚合（DFS）+ 脏标记传播（BFS）。
    /// <para>边方向 = 父→子（outEdges 存子节点），状态聚合方向 = 子→父（DFS 后序）。</para>
    /// <para>移植自 fermsi/RedDotGraph。核心算法逻辑原样保留（含原作者标注的待处理分支），
    /// 行为由单元测试固化。改造点：日志改 TEngine.Log；新增 <see cref="OnVertexRefreshed"/> 推送钩子供 MVE 接入。</para>
    /// </summary>
    public class RedDotGraph : BaseRedDotGraph
    {
        /// <summary>
        /// 顶点状态刷新完成后的回调（id==0 时触发，用于把新状态推送到 BindableProperty）。
        /// 参数：(vertex, status, value)
        /// </summary>
        public Action<RedDotVertex, RedDotStatus, int> OnVertexRefreshed;

        private readonly List<string> _changedList = new List<string>();

        /// <summary>
        /// 判断 redDotType 的最高位状态是否恰好等于 baseStatus。
        /// <para>redDotType 是位掩码（如 Normal|Number|New = 7），baseStatus 是单个状态（如 Number = 2）。
        /// 当 redDotType 落在 [baseStatus, baseStatus*2) 区间时，其最高位恰为 baseStatus。</para>
        /// </summary>
        private bool CheckMaxDotTypeIs(int redDotType, RedDotStatus baseStatus)
        {
            return redDotType >= (int)baseStatus && redDotType < (int)baseStatus << 1;
        }

        /// <summary>
        /// 检测 redDotType 是否包含 baseStatus 这一位。
        /// </summary>
        private bool CheckHasDotStatus(int redDotType, RedDotStatus baseStatus)
        {
            return (redDotType & (int)baseStatus) > 0;
        }

        /// <summary>
        /// 判断 index 处的节点是否需要标记为脏（基于其子节点的脏状态与类型匹配）。
        /// </summary>
        private bool RecheckDataChange(int index, uint id)
        {
            RedDotVertex v = GetVertex(index);
            var info = v.GetRuntimeInfo(id);
            if (info.IsDataChange) return true;
            var edges = outEdges[index];
            // 数量型节点 vs 状态型节点，判断条件不同
            bool isNumType = CheckMaxDotTypeIs(v.RedDotType, RedDotStatus.Number);
            foreach (var edge in edges)
            {
                var uVertex = GetVertex(edge.ToVertexRank);
                var uInfo = uVertex.GetRuntimeInfo(id);
                if (uInfo.IsDataChange)
                {
                    // temp 节点是数量节点，或者当前 v 节点状态不是数量状态，则需要重新刷数据
                    if (isNumType
                            ? (CheckHasDotStatus(uVertex.RedDotType, RedDotStatus.Number)
                               || info.RedDotStatus != RedDotStatus.Number)
                            : (int)info.RedDotStatus <= uVertex.RedDotType) // 非数量节点，只需判断 v.redDotStatus <= temp.redDotType
                    {
                        info.RedDotStatus = RedDotStatus.Null;
                        info.IsDataChange = true;
                        _changedList.Add(v.Key);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 标记 key 节点数据已变更，并沿入边（父方向）BFS 传播脏标记。
        /// </summary>
        /// <param name="key">发生数据变更的节点 key</param>
        /// <param name="id">业务 id（0 表示无 id 红点）</param>
        /// <returns>本次被标脏的所有节点 key 列表（复用内部 list，调用方不可持有引用）</returns>
        public List<string> RedDotDataChange(string key, uint id = 0)
        {
            _changedList.Clear();
            if (VertexDict.TryGetValue(key, out int index))
            {
                var v = GetVertex(index);
                var info = v.GetRuntimeInfo(id);
                if (info.IsDataChange) return _changedList; // 已脏，幂等跳过
                info.RedDotStatus = RedDotStatus.Null;
                info.IsDataChange = true;
                _changedList.Add(key);
                // 宽度优先沿入边（父方向）传播
                Queue<int> needCheckQueue = new Queue<int>();
                needCheckQueue.Enqueue(index);
                while (needCheckQueue.Count > 0)
                {
                    index = needCheckQueue.Dequeue();
                    if (RecheckDataChange(index, id))
                    {
                        var edges = inEdges[index];
                        foreach (var edge in edges)
                        {
                            // 注意：inEdges[index] 存的是父节点 rank 列表，edge 这里就是父 rank（int）
                            needCheckQueue.Enqueue(edge);
                        }
                    }
                }

                return _changedList;
            }
            Log.Warning($"没有找到对应名字的红点节点：{key}");
            return _changedList;
        }

        /// <summary>
        /// 重新刷新 key 节点的红点状态（DFS 后序聚合子节点状态）。
        /// </summary>
        /// <param name="key">节点 key</param>
        /// <param name="id">业务 id</param>
        /// <param name="visitLeafVertex">叶子节点访问回调（计算叶子状态）</param>
        public void RefreshRedDotStatus(string key, uint id, Action<RedDotVertex, uint> visitLeafVertex)
        {
            if (VertexDict.TryGetValue(key, out int index))
            {
                RefreshRedDotStatus(index, id, visitLeafVertex);
                return;
            }
            Log.Warning($"没有找到对应名字的红点节点：{key}, id：{id}");
        }

        /// <summary>
        /// 刷新 index 节点的红点状态。核心聚合算法。
        /// <para>算法要点：
        /// 1. 叶子节点（ExternalId != -1）状态由外部逻辑决定；非叶子由子节点聚合。
        /// 2. 数量型节点（最高位=Number）：子节点 Number 状态的 Value 累加，状态取 max。
        /// 3. 状态型节点：子节点状态取 max，一旦达到 RedDotType 允许的最高位立即剪枝返回。
        /// 4. 刷新完成后通过 <see cref="OnVertexRefreshed"/> 回调通知（用于 MVE 推送）。</para>
        /// </summary>
        private void RefreshRedDotStatus(int index, uint id, Action<RedDotVertex, uint> visitLeafVertex)
        {
            var v = GetVertex(index);
            var info = v.GetRuntimeInfo(id);
            if (!info.IsDataChange)
            {
                return;
            }

            if (v.ExternalId != -1)
            {
                visitLeafVertex(v, id);
                info.StatusChangeTime = GetTime();
                // 原设计：只有叶子才能有 externalId；编辑器里所有节点都能配，故不 return，继续聚合子节点
            }
            info.IsDataChange = false;

            bool isNumType = CheckMaxDotTypeIs(v.RedDotType, RedDotStatus.Number);
            var edges = outEdges[index];
            RedDotVertex uVertex;
            int nextIndex;
            for (int i = 0; i < edges.Count; i++)
            {
                nextIndex = edges[i].ToVertexRank;
                uVertex = GetVertex(nextIndex);
                var uInfo = uVertex.GetRuntimeInfo(id);
                // 提前剪枝：当前状态已"够高"则不必再算子树
                //   数量型：info.Status > uVertex 允许的最高
                //   状态型：>= 即可停
                if (isNumType ? (int)info.RedDotStatus > uVertex.RedDotType : (int)info.RedDotStatus >= uVertex.RedDotType)
                {
                    // 当前红点状态已经达到最新了，不需要再运算下去
                    // 但仍需推送本节点最终状态（聚合可能已完成）
                    NotifyIfChanged(v, info, id);
                    return;
                }

                RefreshRedDotStatus(nextIndex, id, visitLeafVertex);

                // 聚合：子节点状态更高 → 取子节点状态
                if (uInfo.RedDotStatus > info.RedDotStatus)
                {
                    info.RedDotStatus = uInfo.RedDotStatus;
                    info.Value = uInfo.Value;
                    info.StatusChangeTime = GetTime();
                    if (!isNumType)
                    {
                        // 非数字红点，状态已达允许范围即可停止
                        int status = (int)info.RedDotStatus;
                        if (status > v.RedDotType)
                        {
                            // 算最高位
                            var num = v.RedDotType;
                            var highBit = -1;
                            while (num > 0)
                            {
                                highBit++;
                                num >>= 1;
                            }

                            info.RedDotStatus = (RedDotStatus)((int)Math.Pow(2, highBit));
                            NotifyIfChanged(v, info, id);
                            return;
                        }
                        // 因为是按位运算，若 redDotStatus 已是最大，则 redDotType 不可能达到 redDotStatus 的两倍
                        if (v.RedDotType >> 1 < status)
                        {
                            NotifyIfChanged(v, info, id);
                            return;
                        }
                    }
                }
                else if (info.RedDotStatus == RedDotStatus.Number && uInfo.RedDotStatus == RedDotStatus.Number)
                {
                    info.Value += uInfo.Value;
                    info.StatusChangeTime = GetTime();
                }
            }

            NotifyIfChanged(v, info, id);
        }

        /// <summary>
        /// 若状态发生变化，通过 <see cref="OnVertexRefreshed"/> 推送（仅 id==0 的全局红点走 MVE 通道）。
        /// </summary>
        private void NotifyIfChanged(RedDotVertex v, RedDotVertexRuntimeInfo info, uint id)
        {
            if (id != 0) return; // 带 id 的列表项红点走单独通道（Frame 回调）
            OnVertexRefreshed?.Invoke(v, info.RedDotStatus, info.Value);
        }
    }
}
