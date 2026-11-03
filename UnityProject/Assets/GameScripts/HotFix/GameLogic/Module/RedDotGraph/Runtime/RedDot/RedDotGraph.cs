using System;
using System.Collections.Generic;
using RedDot.Runtime.DigraphList;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// 红点图
    /// </summary>
    public class RedDotGraph : BaseRedDotGraph
    {
        /// <summary>
        /// 判断节点是否要设置为isDataChange
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        private bool RecheckDataChange(int index, uint id)
        {
            RedDotVertex v = GetVertex(index);
            //P0-1 生效点②:禁用节点不作 BFS 中继——不登记通知、不向父传播(重启用时经 SetNodeEnabled 补发收敛)
            if (!v.Enabled)
            {
                return false;
            }
            var info = v.GetRuntimeInfo(id);
            if (info.IsDataChange)
            {
                //P5-2:已脏节点(含刚标记的变更源、历史脏而未刷新的祖先)统一在此登记通知列表一次
                //(每节点每轮 BFS 至多经过此处一次,P3-3 visited 保证无重复登记)
                _changedList.Add(v.Key);
                return true;
            }
            var edges = outEdges[index];
            foreach (var edge in edges)
            {
                var uVertex = GetVertex(edge.ToVertexRank);
                var uInfo = uVertex.GetRuntimeInfo(id);
                if (!uInfo.IsDataChange) continue;
                // 宽松标记(P0-2 定案,与聚合条件对称):子允许集与父允许集有交集,或子已有非空状态 → 父需重算。
                // 标记时只置脏不清状态(旧状态保留到重算时统一复位,供变化检测比较);宁可误标记(重算幂等)不可漏标记。
                if (((int)uVertex.RedDotType & v.RedDotType) != 0
                    || uInfo.RedDotStatus != RedDotStatus.RedDotStatusNull)
                {
                    info.IsDataChange = true;
                    _changedList.Add(v.Key);
                    return true;
                }
            }

            return false;
        }

        List<string> _changedList = new List<string>();
        //P3-3(N4):BFS 去重入队集合——多父菱形图传播路径数从 2^d 降为节点数;随 _changedList 同型复用
        private readonly HashSet<int> _bfsVisited = new HashSet<int>();
        //P5-1(C2):BFS 队列成员复用,消除热路径每次变更的队列分配
        private readonly Queue<int> _bfsQueue = new Queue<int>();

        /// <summary>
        /// 派发结果快照小池（A6-3：消除每次派发的 new List&lt;string&gt;）。
        /// 池只复用<b>已归还</b>的实例；调用中（含回调重入）各自租独立实例，天然重入安全；
        /// 调用方同步消费完经 ReturnDispatchList 归还（忘归还不炸，仅退化为每次 new）。
        /// </summary>
        private readonly Stack<List<string>> _dispatchListPool = new Stack<List<string>>();
        private const int DispatchPoolCap = 4;

        internal List<string> RentDispatchList()
        {
            return _dispatchListPool.Count > 0 ? _dispatchListPool.Pop() : new List<string>();
        }

        internal void ReturnDispatchList(List<string> list)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            if (_dispatchListPool.Count < DispatchPoolCap)
            {
                _dispatchListPool.Push(list);
            }
        }

        /// <summary>
        /// 标记数据已经改变
        /// </summary>
        /// <param name="key"></param>
        /// <param name="id"></param>
        /// <returns>数据有改变的列表(快照,与内部复用列表不共享引用;从派发小池租出,调用方同步消费完须 ReturnDispatchList 归还)</returns>
        public List<string> RedDotDataChange(string key, uint id = 0)
        {
            _changedList.Clear();
            if (VertexDict.TryGetValue(key, out int index))
            {
                var v = GetVertex(index);
                var info = v.GetRuntimeInfo(id);
                if (info.IsDataChange)
                {
                    //P2-4:同帧二次 DataChange 仍把 key 加入返回列表,保证后绑定 UI 收到通知
                    //(首标脏已在首次调用完成,这里不重复向上传播)
                    _changedList.Add(key);
                }
                else
                {
                    // 只置脏不清状态:旧状态保留到重算时统一复位,供重算的变化检测比较
                    info.IsDataChange = true;
                    //P5-2:变更源 key 由下方 BFS 首次 Recheck 的已脏分支统一登记(见 RecheckDataChange)
                    //宽度优先
                    _bfsQueue.Clear();
                    _bfsVisited.Clear();
                    _bfsQueue.Enqueue(index);
                    _bfsVisited.Add(index);
                    // SetParent(index, -1);
                    while (_bfsQueue.Count > 0)
                    {
                        index = _bfsQueue.Dequeue();
                        if (RecheckDataChange(index, id))
                        {
                            var edges = inEdges[index];
                            foreach (var edge in edges)
                            {
                                //P3-3(N4):已入队顶点不再重复入队(多父 DAG 下防指数级重复处理)
                                if (_bfsVisited.Add(edge))
                                {
                                    _bfsQueue.Enqueue(edge);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                Log.Warning("没有找到对应名字的红点节点：{0}", key);
            }

            //P2-5:返回快照——与内部复用列表不共享引用;A6-3:快照从小池租出(调用方同步消费完归还),
            //调用方须同步消费即弃,勿跨帧持有(归还实例会被复用改写)
            var result = RentDispatchList();
            result.AddRange(_changedList);
            return result;
        }

        /// <summary>
        /// 重新刷新红点的状态
        /// </summary>
        /// <param name="key"></param>
        /// <param name="id"></param>
        /// <param name="visitLeafVertex"></param>
        public void RefreshRedDotStatus(string key, uint id, Action<RedDotVertex, uint> visitLeafVertex)
        {
            if (VertexDict.TryGetValue(key, out int index))
            {
                RefreshRedDotStatus(index, id, visitLeafVertex);
                return;
            }
            Log.Warning("没有找到对应名字的红点节点：{0}, id：{1}", key, id);
        }

        /// <summary>
        /// 刷新具体的红点(P0-2 定案版:六态过滤式聚合;2026-09-20 起按 RedDotStatusRules 覆盖优先级比较)。
        /// 规则:子贡献 = 子状态 & 父掩码,为零不采纳;同数字态叠加 Value,跨态/非数字态按覆盖优先级取高(满&gt;红点&gt;蓝点&gt;红数字&gt;蓝数字&gt;新)。
        /// 自身 checker 贡献与子贡献遵循同一掩码规则(归一+组合值取优先级最高位,保证运行时结果恒单态)。
        /// 仅状态或数值变化时推进 StatusChangeTime(熄灭→Null 属于变化,通知语义保留)。
        /// </summary>
        /// <param name="index"></param>
        /// <param name="id"></param>
        /// <param name="visitLeafVertex"></param>
        private void RefreshRedDotStatus(int index, uint id, Action<RedDotVertex, uint> visitLeafVertex)
        {
            var v = GetVertex(index);
            var info = v.GetRuntimeInfo(id);
            if (!info.IsDataChange)
            {
                return;
            }

            // 暂存重算前结果,供尾部变化检测
            var oldStatus = info.RedDotStatus;
            var oldValue = info.Value;

            // 重算前复位——必须在 visit 之前:visit 以 out 参数写入自身贡献,作为聚合初值参与后续比较
            info.IsDataChange = false;
            info.RedDotStatus = RedDotStatus.RedDotStatusNull;
            info.Value = 0;

            if (v.ExternalId != -1)
            {
                visitLeafVertex(v, id);
                // 自身贡献与子贡献对称的掩码归一:checker 返回掩码外的位直接滤除;
                // 掩码内多位组合值(配置失误)取覆盖优先级最高位,保证恒单态
                var raw = info.RedDotStatus;
                var self = (int)raw & v.RedDotType;
                //诊断:checker 输出含节点自身掩码外状态位(全部滤除=节点静默不亮,编辑器六态漏勾高发),editor 模式 Error 大声暴露
                if (raw != RedDotStatus.RedDotStatusNull && ((int)raw & ~v.RedDotType) != 0)
                {
                    WarnMaskFiltered(v.Key, id, raw, v.RedDotType, null);
                }
                info.RedDotStatus = self == 0 ? RedDotStatus.RedDotStatusNull : RedDotStatusRules.HighestPriorityBit(self);
                if (!RedDotStatusRules.IsNumber(info.RedDotStatus))
                {
                    info.Value = 0;
                }
            }

            var edges = outEdges[index];
            for (int i = 0; i < edges.Count; i++)
            {
                var nextIndex = edges[i].ToVertexRank;
                //P0-1 生效点③:禁用子跳过(不递归不采纳,贡献视同 Null),未解锁子树不污染父聚合
                if (!GetVertex(nextIndex).Enabled)
                {
                    continue;
                }
                RefreshRedDotStatus(nextIndex, id, visitLeafVertex);
                var uInfo = GetVertex(nextIndex).GetRuntimeInfo(id);

                // 过滤式采纳:子贡献必须落在父掩码允许集内
                var contrib = (RedDotStatus)((int)uInfo.RedDotStatus & v.RedDotType);
                //诊断:子节点状态含父掩码外状态位(全部滤除=父节点静默不亮,父编辑器六态漏勾高发),editor 模式 Error 大声暴露
                if (uInfo.RedDotStatus != RedDotStatus.RedDotStatusNull && ((int)uInfo.RedDotStatus & ~v.RedDotType) != 0)
                {
                    WarnMaskFiltered(GetVertex(nextIndex).Key, id, uInfo.RedDotStatus, v.RedDotType, v.Key);
                }
                if (contrib == RedDotStatus.RedDotStatusNull)
                {
                    continue;
                }

                if (RedDotStatusRules.IsNumber(contrib) && RedDotStatusRules.IsNumber(info.RedDotStatus))
                {
                    if (RedDotStatusRules.PriorityOf(contrib) > RedDotStatusRules.PriorityOf(info.RedDotStatus))
                    {
                        info.RedDotStatus = contrib;
                        info.Value = uInfo.Value;
                    }
                    else if (contrib == info.RedDotStatus)
                    {
                        info.Value += uInfo.Value;
                    }
                    // 低优先级数字态的 Value 不并入(定案③:不混淆可领取数/未读数异质计数)
                }
                else if (RedDotStatusRules.PriorityOf(contrib) > RedDotStatusRules.PriorityOf(info.RedDotStatus))
                {
                    info.RedDotStatus = contrib;
                    info.Value = RedDotStatusRules.IsNumber(contrib) ? uInfo.Value : 0;
                }
            }

            // 仅状态或数值变化时推进时间戳——值未变的重算不触发 UI 回调;熄灭(→Null)属于变化
            if (info.RedDotStatus != oldStatus || info.Value != oldValue)
            {
                info.StatusChangeTime = GetTime();
            }
        }

        // ===== 掩码滤除诊断告警 =====

        /// <summary>同 (key, 状态) 组合只告警一次的去重表——防故意滤除场景(掩码无 Tips 位等)高频重算刷屏;
        /// Reset 重建时清空,新会话/新配置下的问题可重新暴露。</summary>
        private readonly HashSet<(string Key, int Status)> _maskFilterWarned = new HashSet<(string, int)>();

        /// <summary>Reset 重建时清空告警去重表(重登/配置重建后同问题可再次暴露)。</summary>
        public void ClearMaskFilterWarnings()
        {
            _maskFilterWarned.Clear();
        }

        /// <summary>
        /// 掩码滤除告警(去重):checker/子贡献侧有非 Null 状态输出,但含节点六态掩码(编辑器勾选的状态集)之外的位,
        /// 超出位直接滤除——全部滤除时节点静默不亮,部分滤除时该状态不显示。该类问题无日志时极难定位
        /// (回调正常触发、checker 数据正常,唯独编辑器六态勾选不含该状态)。
        /// editor 模式报 Error(显著暴露配置问题),真机维持 Warning(故意滤除场景不刷错误)。
        /// parentKey 为空=被节点自身掩码滤除;非空=子贡献被父节点掩码滤除。
        /// </summary>
        private void WarnMaskFiltered(string key, uint id, RedDotStatus status, int mask, string parentKey)
        {
            if (!_maskFilterWarned.Add((key, (int)status)))
            {
                return;
            }
            var statusDesc = RedDotStatusRules.Describe((int)status);
            var maskDesc = RedDotStatusRules.Describe(mask);
            var outsideDesc = RedDotStatusRules.Describe((int)status & ~mask);
            //低频诊断路径(去重后每 key+状态 至多一条),id 拼接分配可接受;id=0 为无 id 汇总槽位不展示
            var idDesc = id != 0 ? Utility.Text.Format(", id:{0}", id) : string.Empty;
            var allFiltered = ((int)status & mask) == 0;
            string msg;
            if (string.IsNullOrEmpty(parentKey))
            {
                msg = Utility.Text.Format("红点checker状态含节点编辑器未勾选的状态位(超出位将被滤除{0}):key:{1}{2}, checker状态:{3}, 超出位:{4}, 节点掩码:{5}。若期望显示,请在红点图编辑器为该节点勾选对应状态位",
                    allFiltered ? ",节点将不亮" : string.Empty, key, idDesc, statusDesc, outsideDesc, maskDesc);
            }
            else
            {
                msg = Utility.Text.Format("红点子贡献状态含父节点编辑器未勾选的状态位(超出位将被滤除{0}):子key:{1}{2}, 子状态:{3}, 超出位:{4}, 父key:{5} 父掩码:{6}。若期望父节点点亮,请在红点图编辑器为父节点勾选对应状态位",
                    allFiltered ? ",父节点将不亮" : string.Empty, key, idDesc, statusDesc, outsideDesc, parentKey, maskDesc);
            }
#if UNITY_EDITOR
            Log.Error(msg);
#else
            Log.Warning(msg);
#endif
        }
    }
}
