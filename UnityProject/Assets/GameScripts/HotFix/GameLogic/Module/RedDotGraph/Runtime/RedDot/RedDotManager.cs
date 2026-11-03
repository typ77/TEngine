using System;
using System.Collections.Generic;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    public partial class RedDotManager
    {
        private static RedDotManager s_ins = null;
        public static RedDotManager Ins()
        {
            if (s_ins == null)
            {
                s_ins = new RedDotManager();
            }
            return s_ins;
        }

        /// <summary>P4 验证/诊断:内部图只读访问(业务勿用)</summary>
        internal BaseRedDotGraph GraphForVerify => RedDotGrapic;

        /// <summary>
        /// UI绑定，目前还没有想好是用回调还是发消息的机制
        /// </summary>
        /// <param name="redDotKey">红点关键码</param>
        /// <param name="id">红点ID</param>
        /// <param name="cb"></param>
        public void AddUI(string redDotKey, Action cb, uint id = 0)
        {
            if (cb == null)
            {
                Log.Warning("AddUI 的红点cb为空，key:{0}", redDotKey);
                return;
            }
            //P0-3:配置未就绪期暂存,加载完成后按 FIFO 重放(先于 DataChange 入队的绑定先收播)
            if (BufferOp(PendingOpType.AddUI, redDotKey, id, cb))
            {
                return;
            }
            DoAddUI(redDotKey, cb, id);
        }

        private void DoAddUI(string redDotKey, Action cb, uint id)
        {
            RedDotUIInfo uiInfo;
            if (id == 0)
            {
                uiInfo = _uiFramesNoId.AddUI(redDotKey, cb);
            }
            else
            {
                uiInfo = _uiFramesHasId.AddUI(redDotKey, cb, id);
            }
            if (uiInfo == null)
            {
                return;
            }
            //P2-7 注册即回放:走读时刷新路径同步取一次状态;P6-2 审核 Minor①:
            //回放即同步 LastChangeTime,防止随后 RefreshRedDot 全量巡检对已亮红点重复回调
            GetRedDotStatus(redDotKey, id, out RedDotStatus status, out int num);
            if (RedDotGrapic.GetRedDotByKey(redDotKey) is { } vertex)
            {
                uiInfo.LastChangeTime = vertex.GetRuntimeInfo(id).StatusChangeTime;
            }
            //异常隔离单点=RedDotUIInfo.DoCallBack(P2-6),回放与批派发同走一处
            uiInfo.DoCallBack();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="redDotKey"></param>
        /// <param name="cb"></param>
        public void RemoveUI(string redDotKey, Action cb, uint id = 0)
        {
            if (string.IsNullOrEmpty(redDotKey))
            {
                Log.Warning("解绑定的红点key为空");
                return;
            }

            if (cb == null)
            {
                Log.Warning("解绑定的红点cb为空");
                return;
            }
            //P0-3:未就绪期的 AddUI 尚未真实生效,配对 RemoveUI 直接对冲撤销,避免重放期多余回放回调
            if (CancelBufferedAddUI(redDotKey, cb, id))
            {
                return;
            }
            if (id == 0)
            {
                _uiFramesNoId.RemoveUI(redDotKey, cb);
            }
            else
            {
                _uiFramesHasId.RemoveUI(redDotKey, cb, id);
            }
        }

        /// <summary>数据有变化。Immediate=同帧标记+通知;Batched=仅入队,帧末由 FlushPendingChanges 统一处理(P5-2)</summary>
        /// <param name="key"></param>
        /// <param name="caller">变更来源(P6-6 追踪,自动捕获)</param>
        public void DataChange(string key, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            //P0-3:配置未就绪期暂存,加载完成后按 FIFO 重放
            if (BufferOp(PendingOpType.DataChange, key, 0, null))
            {
                return;
            }
            //P0-1 生效点①⑨:禁用节点直接吞(不标脏不 BFS),未解锁系统的报脏零成本,重复入队路径短路
            if (IsNodeDisabled(key))
            {
                return;
            }
            RecordChangeSource(key, 0, caller);
            if (_refreshMode == RedDotRefreshMode.Batched)
            {
                EnqueuePending(key, 0);
                return;
            }
            DispatchChange(key, 0, true);
        }

        /// <summary>带 id 的数据有变化。Batched 语义同上,id 特定通知与无 id 广播各占一个待处理项</summary>
        /// <param name="key"></param>
        /// <param name="id"></param>
        /// <param name="caller">变更来源(P6-6 追踪,自动捕获)</param>
        public void DataChange(string key, uint id, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            //P0-3:配置未就绪期暂存,加载完成后按 FIFO 重放
            if (BufferOp(PendingOpType.DataChange, key, id, null))
            {
                return;
            }
            //P0-1 生效点①⑨:禁用节点直接吞(不标脏不 BFS)
            if (IsNodeDisabled(key))
            {
                return;
            }
            RecordChangeSource(key, id, caller);
            if (_refreshMode == RedDotRefreshMode.Batched)
            {
                EnqueuePending(key, id);
                if (id != 0)
                {
                    EnqueuePending(key, 0);
                }
                return;
            }
            DispatchChange(key, id, true);
        }

        /// <summary>
        /// P5-2(T10):Batched 模式帧驱动——按入队顺序统一 BFS+通知,每帧至多 MaxDispatchPerFrame 个,
        /// 余量留到下帧(登录风暴削峰)。项目侧挂帧级 Timer 调用(Phase 6 T9);稳态(队空)零分配早退。
        /// </summary>
        public void FlushPendingChanges()
        {
            //P5 审核 I-1:重入防护——派发回调内再调 Flush 会破坏遍历区间(越界/错位派发),直接忽略
            if (_flushing)
            {
                Log.Warning("FlushPendingChanges 重入调用被忽略(回调内请勿同步重入帧驱动)");
                return;
            }
            if (_pendingChanges.Count == 0)
            {
                return;
            }
            _flushing = true;
            try
            {
                int count = Math.Min(_pendingChanges.Count, MaxDispatchPerFrame);
                for (int i = 0; i < count; i++)
                {
                    var change = _pendingChanges[i];
                    //R2(审查 2026-09-12):派发前先出 set——派发回调内对同 (key,id) 的再变更能正常重新入队,
                    //下轮送达;否则在派项占住 set,再变更被 Add-false 折叠且窗口结束后移除=永久丢失
                    _pendingSet.Remove(change);
                    //Batched:入队侧已为带 id 变更追加 (key,0) 项,派发侧不再广播 id=0(A6-2 防双重派发)
                    DispatchChange(change.Key, change.Id, false);
                }
                _pendingChanges.RemoveRange(0, count);
            }
            finally
            {
                _flushing = false;
            }
        }

        private void EnqueuePending(string key, uint id)
        {
            //合并契约(R2 修订):同 (key,id) 在队列中未派发时合并去重;已派发项派发时即出 set,
            //其回调内再变更会重新入队(下轮送达)——pull 模型下 checker 现算保证状态最终正确
            var change = new PendingChange { Key = key, Id = id };
            if (_pendingSet.Add(change))
            {
                _pendingChanges.Add(change);
            }
        }

        /// <summary>
        /// 派发一次变更。
        /// </summary>
        /// <param name="key">红点 key</param>
        /// <param name="id">槽位 id（0=汇总槽）</param>
        /// <param name="broadcastNoId">
        /// id!=0 时是否额外对 id=0 槽 BFS 并广播无 id 帧。
        /// Immediate 模式传 true（不入队，这是 id=0 汇总的唯一途径）；
        /// Batched 队列派发传 false——入队侧已为带 id 变更追加 (key,0) 队列项，此处再广播
        /// 会使 id=0 槽跑两次 BFS、无 id UI 通知扇出两次（A6-2 修复：双重派发消除）。
        /// </param>
        private void DispatchChange(string key, uint id, bool broadcastNoId)
        {
            //P0-1 生效点①⑦:禁用节点不标脏不派发(入队后才被禁用的 pending 在此漏网拦截)
            if (IsNodeDisabled(key))
            {
                return;
            }
            var changedList = RedDotGrapic.RedDotDataChange(key, id);
            try
            {
                if (id == 0)
                {
                    _uiFramesNoId.GetCurrentFrameNode()?.DataChanged(changedList, 0);
                }
                else
                {
                    _uiFramesHasId.GetCurrentFrameNode()?.DataChanged(changedList, id);
                    if (broadcastNoId)
                    {
                        //Immediate 模式:带 id 变更同时广播无 id 帧(id=0 汇总唯一途径)
                        var broadcastList = RedDotGrapic.RedDotDataChange(key, 0);
                        try
                        {
                            _uiFramesNoId.GetCurrentFrameNode()?.DataChanged(broadcastList, 0);
                        }
                        finally
                        {
                            RedDotGrapic.ReturnDispatchList(broadcastList);
                        }
                    }
                }
            }
            finally
            {
                //A6-3:快照从小池租出,同步派发完归还(DataChanged 同步消费含 UI 回调,finally 保证回调异常也归还)
                RedDotGrapic.ReturnDispatchList(changedList);
            }
        }

        //IEquatable：HashSet 查重走强类型比较，消除默认 ObjectEqualityComparer 对 struct 的逐次装箱
        //（全量报脏 324×2 次 Add 的 GC 主力）；相等语义与字段级默认比较一致，纯实现声明无行为变化
        private struct PendingChange : IEquatable<PendingChange>
        {
            public string Key;
            public uint Id;

            public bool Equals(PendingChange other)
            {
                return Key == other.Key && Id == other.Id;
            }

            public override bool Equals(object obj)
            {
                return obj is PendingChange other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Key != null ? Key.GetHashCode() : 0) ^ (int)Id;
            }
        }

        /// <summary>
        /// 刷新红点(全量巡检入口)
        /// P3-5:读时刷新(GetRedDotStatus)为唯一刷新语义,本方法遍历当前帧全部绑定做主动巡检
        /// </summary>
        public void RefreshRedDot()
        {
            TravelUIFrames(_uiFramesNoId);
            TravelUIFrames(_uiFramesHasId);
        }

        /// <summary>
        /// 切换到某个全屏UI上
        /// </summary>
        /// <param name="name"></param>
        public void ChangeToFrame(string name)
        {
            _uiFramesNoId.ChangeToFrame(name);
            _uiFramesHasId.ChangeToFrame(name);
        }

        public void AddFrame(string name)
        {
            _uiFramesNoId.AddFrame(name);
            _uiFramesHasId.AddFrame(name);
        }

        public void RemoveFrame(string name)
        {
            _uiFramesNoId.RemoveFrame(name);
            _uiFramesHasId.RemoveFrame(name);
        }

        /// <summary>P4-2(N6):按本次列表刷新的全部 cellId 收敛 per-id 运行时信息,防长列表滚动只增不减。
        /// R4(审查 2026-09-12):id=0 为无 id 汇总槽位,永不随 cell 回收——回收后重开窗回放读 clean 新 info
        /// 会得到 Null/0(角标归零),库侧强制并入 keep。</summary>
        /// <param name="key">红点关键码</param>
        /// <param name="keep">本次仍存在的全部 cellId(空集=除 id=0 外全部释放)</param>
        public void ReleaseCellIds(string key, ICollection<uint> keep)
        {
            if (string.IsNullOrEmpty(key) || keep == null)
            {
                return;
            }
            var v = RedDotGrapic.GetRedDotByKey(key);
            if (v == null)
            {
                Log.Warning("ReleaseCellIds 未找到红点节点，key:{0}", key);
                return;
            }
            var keepSet = new HashSet<uint>(keep);
            keepSet.Add(0);
            v.ReleaseIdsExcept(keepSet);
        }

        /// <summary>
        /// P4-1(A6):重登/重建——清图、清帧绑定、清外部 checker 表,时间戳计数器归零,单例回到未初始化态。
        /// 调用后须重新加载配置并重建绑定;内部引用 s_ins 置空,下次 Ins() 返回全新实例。
        /// P0-2:动态挂父重放表在 static 层,Reset 不清(重登后业务重新 RegisterDynamicNode 时自动重挂)。
        /// </summary>
        public void Reset()
        {
            RedDotGrapic.ClearGraph();
            RedDotGrapic.ClearMaskFilterWarnings();
            BaseRedDotGraph.ResetTimeCounter();
            _pendingChanges.Clear();
            _pendingSet.Clear();
            _preReadyOps.Clear();
            _bufferUntilReady = false;
            _configReady = false;
            _dynamicNodes.Clear();
            // 注意：s_dynParents 不随 Reset 清（曾尝试 Clear 后 U3 动态节点用例首轮即挂，已回滚）——
            // 它是 P0-3 缓冲重放（ReplayDynamicParents）的数据源，生命周期契约比"随实例"复杂，
            // 残留导致的"同会话二次跑 U3 动态节点用例挂"为已知现象（挂账专项，勿随手清）
            _summarizeExtIds.Clear();
            Array.Clear(_changeRecords, 0, _changeRecords.Length);
            _changeRecordCursor = 0;
            _uiFramesNoId.Clear();
            _uiFramesHasId.Clear();
            RedDotExternalLogicVisitor.Ins().Clear();
            s_ins = null;
        }

        // ===== P6-4(T7) 动态节点 API =====

        // 动态叶子 checker 的 externalId 起点，避开图配置里的静态 id
        private int _dynExternalIdCursor = 900000;

        /// <summary>
        /// 注册动态节点(幂等):同一 ownerKey 生命周期内复用同一 dynKey(前缀 dyn: 隔离命名空间)。
        /// 对齐旧系统 RegisterDynamicNode(ownerSystem, entityId) 的活动期次场景;
        /// owner 分配表随 Reset 清空。
        /// </summary>
        /// <param name="ownerKey">业务所有者标识(如 "act:123")</param>
        /// <param name="dynKey">输出的动态节点 key</param>
        public void RegisterDynamicNode(string ownerKey, out string dynKey)
        {
            if (string.IsNullOrEmpty(ownerKey))
            {
                Log.Error("RegisterDynamicNode ownerKey 为空");
                dynKey = string.Empty;
                return;
            }
            if (_dynamicNodes.TryGetValue(ownerKey, out dynKey))
            {
                return;
            }
            dynKey = "dyn:" + ownerKey;
            if (RedDotGrapic.GetRedDotByKey(dynKey) == null)
            {
                //动态节点掩码给全态(聚合放行全部子贡献),自身状态由子节点聚合
                RedDotGrapic.Insert(new RedDotVertex(dynKey, RedDotStatusRules.AllStatusMask, -1));
            }
            _dynamicNodes[ownerKey] = dynKey;
            //P0-2:重登重建后自动重挂历史父边(父已就绪时);未就绪则留在重放表,挂配置加载完成回调补放
            ReplayDynamicParent(dynKey, false);
        }

        // 给动态叶子挂 checker：首次分配 externalId，重复调用覆盖委托
        public void BindDynamicChecker(string dynKey, RedDotExternalLogicVisitor.RedDotExternalLogic logic)
        {
            if (string.IsNullOrEmpty(dynKey) || logic == null)
            {
                Log.Error("BindDynamicChecker 参数无效，dynKey:{0}", dynKey);
                return;
            }

            var vertex = RedDotGrapic.GetRedDotByKey(dynKey);
            if (vertex == null)
            {
                Log.Error("BindDynamicChecker 节点不存在，dynKey:{0}", dynKey);
                return;
            }

            if (vertex.ExternalId == -1)
            {
                _dynExternalIdCursor++;
                vertex.ExternalId = _dynExternalIdCursor;
            }

            RedDotExternalLogicVisitor.Ins().RegExternalLogic(vertex.ExternalId, logic);
        }

        /// <summary>移除动态节点(含关联边);owner 分配表与汇总表同步清理。
        /// P0-2:删前快照父列表与全部 id 槽位,删后对每个父逐 id 补发 DataChange(防父缓存陈旧贡献);
        /// 顺手摘 visitor 陈旧 checker(汇总系统段与配置 externalId 双路);重放表同步摘除。</summary>
        /// <param name="ownerKey"></param>
        public void RemoveDynamicNode(string ownerKey)
        {
            if (string.IsNullOrEmpty(ownerKey) || !_dynamicNodes.TryGetValue(ownerKey, out var dynKey))
            {
                return;
            }
            _dynamicNodes.Remove(ownerKey);
            //P0-2:节点已删,历史挂父记录同步作废(业务重建节点时应自行重新 SetDynamicParent)
            s_dynParents.Remove(dynKey);
            var v = RedDotGrapic.GetRedDotByKey(dynKey);
            if (v == null)
            {
                return;
            }
            //P0-2:删前快照(删后顶点不可达,补发依赖快照)
            _parentKeyBuffer.Clear();
            RedDotGrapic.CollectParentKeys(dynKey, _parentKeyBuffer);
            _idReplayBuffer.Clear();
            v.CollectIds(_idReplayBuffer);
            //P6 审核 M7 + P0-2:被 SummarizeCells 过的动态节点,陈旧 checker 闭包(持已删顶点引用)一并清理
            int summExt = -1;
            if (_summarizeExtIds.TryGetValue(dynKey, out var foundExt))
            {
                summExt = foundExt;
                _summarizeExtIds.Remove(dynKey);
                RedDotExternalLogicVisitor.Ins().UnregisterExternalLogic(summExt);
            }
            if (v.ExternalId != -1 && v.ExternalId != summExt)
            {
                RedDotExternalLogicVisitor.Ins().UnregisterExternalLogic(v.ExternalId);
            }
            RedDotGrapic.RemoveVertexByKey(dynKey);
            NotifyParents(_parentKeyBuffer, _idReplayBuffer);
        }

        /// <summary>动态节点挂父边。走 DAG.Insert 环检测,环失败=Log.Error 硬失败(P3-6)。
        /// P0-0 方向修复:库铁律边=(父, 子)、传播沿 inEdges 向父,旧实现 InsertEdge(dynKey, parentKey)
        /// 把 dyn 插成了父,dyn 变更永远传不到 parent(2026-09-12 对抗审查实锤);成功后登记重放表。
        /// P0-2:顶点未就绪(典型=配置尚未加载完)不再硬失败丢边——登记重放表,由配置加载完成回调或
        /// RegisterDynamicNode 补放(总原则③:时序契约机制化,业务无须记住"必须晚于配置加载")。</summary>
        /// <param name="dynKey"></param>
        /// <param name="parentKey"></param>
        public void SetDynamicParent(string dynKey, string parentKey)
        {
            if (string.IsNullOrEmpty(dynKey) || string.IsNullOrEmpty(parentKey))
            {
                Log.Error("SetDynamicParent 参数为空，dyn:{0}, parent:{1}", dynKey, parentKey);
                return;
            }
            if (RedDotGrapic.GetRedDotByKey(dynKey) == null || RedDotGrapic.GetRedDotByKey(parentKey) == null)
            {
                Log.Warning("SetDynamicParent 顶点未就绪(稍后自动重放)，{0} <- {1}", parentKey, dynKey);
                RecordDynParent(dynKey, parentKey);
                return;
            }
            if (RedDotGrapic.HasEdgeByKey(parentKey, dynKey))
            {
                //幂等:重放已挂的边(P0-2 温热重跑/业务重复调用),补登记后静默返回
                RecordDynParent(dynKey, parentKey);
                return;
            }
            if (!RedDotGrapic.InsertEdge(parentKey, dynKey))
            {
                Log.Error("SetDynamicParent 挂边失败(已存在或构成环)，{0} <- {1}", parentKey, dynKey);
                return;
            }
            RecordDynParent(dynKey, parentKey);
            //挂边后父聚合需重算收敛(dyn 若已带脏状态,不补发则父陈旧不亮)
            DataChange(parentKey);
        }

        /// <summary>P0-2:登记 dynKey→parentKey 到重放表(挂边成功与顶点未就绪两路共用入口)。</summary>
        private void RecordDynParent(string dynKey, string parentKey)
        {
            if (!s_dynParents.TryGetValue(dynKey, out var parents))
            {
                parents = new HashSet<string>();
                s_dynParents[dynKey] = parents;
            }
            parents.Add(parentKey);
        }

        /// <summary>动态节点摘父边。P0-2:重放表同步摘除,摘后对该父逐 id 补发 DataChange(父重聚合熄灭)。</summary>
        /// <param name="dynKey"></param>
        /// <param name="parentKey"></param>
        public void RemoveDynamicParent(string dynKey, string parentKey)
        {
            if (string.IsNullOrEmpty(dynKey) || string.IsNullOrEmpty(parentKey))
            {
                return;
            }
            if (!RedDotGrapic.RemoveEdgeByKey(dynKey, parentKey))
            {
                Log.Warning("RemoveDynamicParent 边不存在，{0} <- {1}", parentKey, dynKey);
                return;
            }
            if (s_dynParents.TryGetValue(dynKey, out var parents))
            {
                parents.Remove(parentKey);
                if (parents.Count == 0)
                {
                    s_dynParents.Remove(dynKey);
                }
            }
            //P0-2:仅补发被摘的父(其余父聚合不受此边影响),逐 id 防陈旧 per-id 贡献
            _parentKeyBuffer.Clear();
            _parentKeyBuffer.Add(parentKey);
            _idReplayBuffer.Clear();
            RedDotGrapic.GetRedDotByKey(dynKey)?.CollectIds(_idReplayBuffer);
            NotifyParents(_parentKeyBuffer, _idReplayBuffer);
        }

        // ===== P0-2 动态挂父重放 =====

        /// <summary>P0-2:动态挂父重放表(dynKey→父 key 集)。static=Reset 不清层:重登 Reset 清空实例态后,
        /// 业务重新 RegisterDynamicNode 时按本表自动重挂,重建责任从业务收归库。
        /// RemoveDynamicNode/RemoveDynamicParent 同步摘除;父为 dyn: 前缀且尚未重建的条目保留待其注册。</summary>
        private static readonly Dictionary<string, HashSet<string>> s_dynParents = new Dictionary<string, HashSet<string>>();

        /// <summary>P0-2:配置加载完成后重放动态挂父表(模块 LoadConfigAsync 成功路径调用)。
        /// 动态顶点已由业务重建的直接重挂;业务未重建的保留(其 RegisterDynamicNode 时补放);
        /// 静态父 key 在配置中已不存在的摘除并 Warning(配置/dll 版本错配暴露点)。</summary>
        public void ReplayDynamicParents()
        {
            if (s_dynParents.Count == 0)
            {
                return;
            }
            //P1-1 审核:fullReplay 会摘空条目(ReplayDynamicParent 内 s_dynParents.Remove),字典枚举中删除
            //必炸版本校验且中断后续重放——key 快照后遍历(低频路径,分配可接受)
            var keys = new List<string>(s_dynParents.Keys);
            foreach (var dynKey in keys)
            {
                ReplayDynamicParent(dynKey, true);
            }
        }

        /// <summary>P0-2:重放单个动态节点的挂父记录。fullReplay=true(配置加载完成回调)时,
        /// 缺失的静态父按配置错配摘除告警;false(RegisterDynamicNode 时)保留——配置可能尚未加载完。
        /// dyn: 前缀父一律保留(该动态父可能稍后由业务重建);边已存在跳过(幂等对冲)。</summary>
        private void ReplayDynamicParent(string dynKey, bool fullReplay)
        {
            if (!s_dynParents.TryGetValue(dynKey, out var parents) || parents.Count == 0)
            {
                return;
            }
            if (RedDotGrapic.GetRedDotByKey(dynKey) == null)
            {
                //业务尚未重建该动态节点,留在表中待其注册时补放
                return;
            }
            _replayBuffer.Clear();
            _replayBuffer.AddRange(parents);
            foreach (var parent in _replayBuffer)
            {
                if (RedDotGrapic.GetRedDotByKey(parent) == null)
                {
                    if (!fullReplay || parent.StartsWith("dyn:"))
                    {
                        continue;
                    }
                    Log.Warning("ReplayDynamicParent 静态父不存在，疑似配置/常量版本错配，摘除 {0} <- {1}", parent, dynKey);
                    parents.Remove(parent);
                    continue;
                }
                if (RedDotGrapic.HasEdgeByKey(parent, dynKey))
                {
                    continue;
                }
                if (!RedDotGrapic.InsertEdge(parent, dynKey))
                {
                    Log.Error("ReplayDynamicParent 重挂失败，{0} <- {1}", parent, dynKey);
                    continue;
                }
                //重挂后父聚合重算收敛(dyn 可能在挂边前已带脏状态)
                DataChange(parent);
            }
            if (parents.Count == 0)
            {
                s_dynParents.Remove(dynKey);
            }
        }

        // ===== P0-1 节点 Enabled 门控 =====

        /// <summary>
        /// P0-1:节点启用门控。禁用:DataChange 吞、不作 BFS 中继、聚合跳过、读回 Null、巡检跳过;
        /// 启用:恢复参与。开关双向对父链补发 DataChange——只做启用方向会留永久陈旧红点
        /// (禁用瞬间父缓存了旧贡献,无脏不重算)。配置未就绪期调用走 P0-3 缓冲(门控先于数据到达不丢失)。
        /// </summary>
        /// <param name="key">红点关键码</param>
        /// <param name="enabled">true=启用(解锁),false=禁用(未解锁)</param>
        public void SetNodeEnabled(string key, bool enabled)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Error("SetNodeEnabled key 为空");
                return;
            }
            if (BufferOp(PendingOpType.SetNodeEnabled, key, 0, null, enabled))
            {
                return;
            }
            var v = RedDotGrapic.GetRedDotByKey(key);
            if (v == null)
            {
                Log.Warning("SetNodeEnabled 未找到红点节点，key:{0}", key);
                return;
            }
            if (v.Enabled == enabled)
            {
                return;
            }
            v.Enabled = enabled;
            if (enabled)
            {
                //启用方向:自身重算(解锁即亮)
                DataChange(key);
                //审核修订:节点可能带陈旧脏标记(禁用期前报脏未消化/P0-3 缓冲重放的 DataChange 先于本调用),
                //DataChange 已脏早退分支不再向上传播 → 父链须显式补发(与禁用方向同一 sweep 口径)
                _parentKeyBuffer.Clear();
                RedDotGrapic.CollectParentKeys(key, _parentKeyBuffer);
                if (_parentKeyBuffer.Count > 0)
                {
                    _idReplayBuffer.Clear();
                    v.CollectIds(_idReplayBuffer);
                    NotifyParents(_parentKeyBuffer, _idReplayBuffer);
                }
            }
            else
            {
                //禁用方向:父链重聚合时禁用子被跳过(生效点③)→ 父链熄灭;逐 id 防父陈旧 per-id 贡献
                _parentKeyBuffer.Clear();
                RedDotGrapic.CollectParentKeys(key, _parentKeyBuffer);
                _idReplayBuffer.Clear();
                v.CollectIds(_idReplayBuffer);
                NotifyParents(_parentKeyBuffer, _idReplayBuffer);
                //P2-1 审核:禁用节点自身已绑 UI 补一次回调(回调内读 GetRedDotStatus 得 Null → UI 熄灭),
                //与启用方向(自身 DataChange 入 BFS)对称——不补则亮→禁翻转后绑定 UI 永久残留亮态视觉
                NotifyDetachedSelfUIs(key);
            }
        }

        /// <summary>P2-1:禁用翻转时对绑定在该节点自身的 UI 派发一次通知。keyList/ids 局部分配——
        /// 回调重入不得污染本次派发(与 NotifyParents 快照同一防重入口径)。</summary>
        private void NotifyDetachedSelfUIs(string key)
        {
            var keyList = new List<string>(1) { key };
            //id=0 走无 id 帧;per-id 槽位逐个走带 id 帧并广播无 id 帧(与 DispatchChange 同口径)
            _uiFramesNoId.GetCurrentFrameNode()?.DataChanged(keyList, 0);
            var v = RedDotGrapic.GetRedDotByKey(key);
            if (v == null)
            {
                return;
            }
            var ids = new List<uint>();
            v.CollectIds(ids);
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == 0)
                {
                    continue;
                }
                _uiFramesHasId.GetCurrentFrameNode()?.DataChanged(keyList, ids[i]);
                _uiFramesNoId.GetCurrentFrameNode()?.DataChanged(keyList, 0);
            }
        }

        private bool IsNodeDisabled(string key)
        {
            var v = RedDotGrapic.GetRedDotByKey(key);
            return v != null && !v.Enabled;
        }

        /// <summary>P0-1/P0-2 共用:对一组父节点按 idSource 的现存 id 槽位逐个补发 DataChange
        /// (id=0 由带 id 变更自动广播);无任何槽位时退化为一次 id=0 补发。
        /// P1-4 审核(R3 同型):补发循环内 DataChange 在 Immediate 下同步执行 UI 回调,回调再入
        /// SetNodeEnabled/RemoveDynamicNode 会 Clear 复用缓冲——入口快照,防外层循环被踩坏/静默截断。</summary>
        private void NotifyParents(List<string> parents, List<uint> ids)
        {
            if (parents == null || parents.Count == 0)
            {
                return;
            }
            var parentSnap = new List<string>(parents);
            var idSnap = ids == null ? null : new List<uint>(ids);
            for (int i = 0; i < parentSnap.Count; i++)
            {
                var parent = parentSnap[i];
                if (idSnap == null || idSnap.Count == 0)
                {
                    DataChange(parent);
                    continue;
                }
                for (int j = 0; j < idSnap.Count; j++)
                {
                    DataChange(parent, idSnap[j]);
                }
            }
        }

        // ===== P6-5(N12) cell 汇总标准 checker =====

        /// <summary>
        /// cell 汇总标准 checker:节点 key 的 id=0 态由 cellIdsProvider 给出的 per-id 态聚合而来
        /// (状态按全序取高、数字态 Value 累加、mask 过滤)。业务一行接入,替代隐式 WithId delegate 契约。
        /// 路由(P6 审核 I-1):节点配置了 externalId 时,聚合 checker 注册到该 externalId 的无 id 槽位——
        /// Visit 对 id=0 优先查无 id 表、id≠0 走业务 WithId 表,per-id 路由不受影响;
        /// externalId=-1 的节点分配系统段 id(负数段自 -1000001 递减),该类节点不可再挂 per-id checker。
        /// R1(审查 2026-09-12):cell 态获取分两路——有业务 WithId checker 的节点逐 cell 现算
        /// (读缓存会因 per-id 只在被读/绑定时刷新而读到陈旧 Null,"列表未打开时新邮件角标不亮");
        /// externalId=-1 系统段直写节点保持读 runtime info 缓存契约。单 cell checker 异常隔离为该 cell 记 Null。
        /// 注意:列表刷新后调用 ReleaseCellIds 收敛 cell 缓存(id=0 槽位库侧强制保留,见 ReleaseCellIds)。
        /// </summary>
        /// <param name="key">汇总节点 key</param>
        /// <param name="cellIdsProvider">本次活跃 cellId 集合提供者(刷新时拉取)</param>
        /// <param name="mask">允许聚合的状态掩码(通常传该节点配置的 redDotType)</param>
        public void SummarizeCells(string key, Func<IEnumerable<uint>> cellIdsProvider, RedDotStatus mask)
        {
            var v = RedDotGrapic.GetRedDotByKey(key);
            if (v == null || cellIdsProvider == null)
            {
                Log.Error("SummarizeCells 参数无效，key:{0}", key);
                return;
            }
            if (!_summarizeExtIds.TryGetValue(key, out var extId))
            {
                if (v.ExternalId != -1)
                {
                    //配置节点(P6 审核 I-1):聚合 checker 注册到既有 externalId 的无 id 槽位——
                    //Visit 对 id=0 优先查无 id 表、id≠0 走业务 WithId 表,per-id 路由不受影响
                    extId = v.ExternalId;
                }
                else
                {
                    //无配置节点:分配系统段 id 并覆写 ExternalId(刷新路由门控在 ExternalId != -1),
                    //该类节点不可再挂 per-id checker,cell 态须业务直写 runtime info。
                    //K批2:系统段=负数段自 -1000001 递减,与正数配置空间结构性隔离(原 95000 正数段已废)
                    extId = --s_summarizeExtIdSeed;
                    v.ExternalId = extId;
                }
                _summarizeExtIds[key] = extId;
            }
            RedDotExternalLogicVisitor.Ins().RegExternalLogic(extId, (out RedDotStatus status, out int num) =>
            {
                status = RedDotStatus.RedDotStatusNull;
                num = 0;
                //P0-1 生效点⑥:禁用汇总节点记 Null——现算与读缓存两路都在此短路,不经过既有读门控
                if (!v.Enabled)
                {
                    return;
                }
                var visitor = RedDotExternalLogicVisitor.Ins();
                //R1:聚合时判定现算/读缓存路由(支持 checker 晚于 SummarizeCells 注册)
                bool freshCompute = visitor.HasWithIdExternal(extId);
                foreach (var cellId in cellIdsProvider())
                {
                    RedDotStatus cellStatus;
                    int cellNum;
                    if (freshCompute)
                    {
                        try
                        {
                            visitor.Visit(extId, cellId, out cellStatus, out cellNum);
                        }
                        catch (System.Exception e)
                        {
                            //单 cell checker 异常隔离为该 cell 记 Null,不拖垮整个汇总
                            Log.Error("SummarizeCells cell checker 抛出异常，key:{0}, cellId:{1}, ex:{2}", v.Key, cellId, e);
                            cellStatus = RedDotStatus.RedDotStatusNull;
                            cellNum = 0;
                        }
                    }
                    else
                    {
                        var cellInfo = v.GetRuntimeInfo(cellId);
                        cellStatus = cellInfo.RedDotStatus;
                        cellNum = cellInfo.Value;
                    }
                    int contrib = (int)cellStatus & (int)mask;
                    if (contrib == 0)
                    {
                        continue;
                    }
                    if (RedDotStatusRules.IsNumber((RedDotStatus)contrib))
                    {
                        num += cellNum;
                    }
                    if (RedDotStatusRules.PriorityOf((RedDotStatus)contrib) > RedDotStatusRules.PriorityOf(status))
                    {
                        status = (RedDotStatus)contrib;
                    }
                }
            });
        }

        // ===== P6-6(T12) GM 诊断支持 =====

        private struct ChangeRecord
        {
            public uint Time;
            public string Key;
            public uint Id;
            public string Caller;
        }

        private readonly ChangeRecord[] _changeRecords = new ChangeRecord[32];
        private int _changeRecordCursor;

        private void RecordChangeSource(string key, uint id, string caller)
        {
            _changeRecords[_changeRecordCursor] = new ChangeRecord
            {
                Time = BaseRedDotGraph.TimeForVerify,
                Key = key,
                Id = id,
                Caller = caller,
            };
            _changeRecordCursor = (_changeRecordCursor + 1) % _changeRecords.Length;
        }

        /// <summary>
        /// GM reddot print:节点当前状态(id=0)+配置信息+per-id 条目数。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string DumpRedDotStatus(string key)
        {
            var v = RedDotGrapic.GetRedDotByKey(key);
            if (v == null)
            {
                return "未找到红点节点:" + key;
            }
            var sb = new System.Text.StringBuilder();
            var info = v.GetRuntimeInfo(0);
            sb.AppendLine(Utility.Text.Format("key:{0} mask:{1}({2}) externalId:{3}", key, v.RedDotType, RedDotStatusRules.Describe(v.RedDotType), v.ExternalId));
            sb.AppendLine(Utility.Text.Format("id=0 status:{0}({1}) value:{2} dirty:{3}", info.RedDotStatus, RedDotStatusRules.Describe((int)info.RedDotStatus), info.Value, info.IsDataChange));
            sb.AppendLine(Utility.Text.Format("per-id 条目数:{0}", v.RuntimeInfoCount));
            return sb.ToString();
        }

        /// <summary>
        /// GM reddot why:最近 N 次(至多 8 条)针对该 key 的 DataChange 来源追踪。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string DumpChangeRecords(string key)
        {
            var sb = new System.Text.StringBuilder();
            int found = 0;
            for (int i = 0; i < _changeRecords.Length && found < 8; i++)
            {
                var idx = (_changeRecordCursor - 1 - i + _changeRecords.Length) % _changeRecords.Length;
                var r = _changeRecords[idx];
                if (r.Key != key)
                {
                    continue;
                }
                sb.AppendLine(Utility.Text.Format("  t{0} id:{1} from:{2}", r.Time, r.Id, r.Caller));
                found++;
            }
            if (found == 0)
            {
                sb.AppendLine("  (无变更记录)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 获取红点的状态
        /// </summary>
        /// <param name="key"></param>
        /// <param name="id"></param>
        /// <param name="status"></param>
        /// <param name="num"></param>
        public void GetRedDotStatus(string key, uint id, out RedDotStatus status, out int num)
        {
            var v = RedDotGrapic.GetRedDotByKey(key);
            if (v == null)
            {
                Log.Error("GetRedDotStatus 未找到红点节点，key:{0}, id:{1}", key, id);
                status = RedDotStatus.RedDotStatusNull;
                num = 0;
                return;
            }
            //P0-1 生效点④:禁用节点读回 Null(不触发刷新,缓存的禁用前状态不外泄)
            if (!v.Enabled)
            {
                status = RedDotStatus.RedDotStatusNull;
                num = 0;
                return;
            }
            var info = v.GetRuntimeInfo(id);
            //P3-5(B3):读时刷新固定启用(P2-7 注册即回放依赖此语义),删除 IsUseRefreshByGetRedDotStatus 开关,
            //刷新语义唯一入口
            if (info.IsDataChange)
            {
                RedDotGrapic.RefreshRedDotStatus(key, id, VisitRedDotVertex);
            }
            status = info.RedDotStatus;
            num = info.Value;
        }

        // ===== P0-3 配置未就绪缓冲重放 =====

        private enum PendingOpType
        {
            DataChange,
            AddUI,
            RemoveUI,
            SetNodeEnabled,
        }

        private struct PendingOp
        {
            public PendingOpType Type;
            public string Key;
            public uint Id;
            public Action Cb;
            public bool Enabled;
        }

        /// <summary>P0-3:开启未就绪缓冲。模块 OnInit 调用(配置加载为异步,期间业务报脏/绑定/门控先到);
        /// 默认关闭=直通,验证器与 GM 直连语义不变。重复开启先清残留(重登重建走全新实例,不达此处)。</summary>
        public void BeginConfigBuffering()
        {
            _preReadyOps.Clear();
            _bufferUntilReady = true;
            _configReady = false;
        }

        /// <summary>P0-3:配置加载完毕(无论成败)调用——置就绪并按 FIFO 重放暂存操作。
        /// 加载失败也必须调用:置直通后未知 key 走常规 Warning/Error 大声暴露,而非无限缓冲。</summary>
        public void SetConfigReady()
        {
            if (_configReady)
            {
                return;
            }
            _configReady = true;
            if (_preReadyOps.Count == 0)
            {
                return;
            }
            //重放期间回调可能再入(此时已就绪,直接走常规路径),先摘走队列防迭代中变更
            var ops = new List<PendingOp>(_preReadyOps);
            _preReadyOps.Clear();
            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                switch (op.Type)
                {
                    case PendingOpType.DataChange:
                        DataChange(op.Key, op.Id);
                        break;
                    case PendingOpType.AddUI:
                        DoAddUI(op.Key, op.Cb, op.Id);
                        break;
                    case PendingOpType.RemoveUI:
                        RemoveUI(op.Key, op.Cb, op.Id);
                        break;
                    case PendingOpType.SetNodeEnabled:
                        SetNodeEnabled(op.Key, op.Enabled);
                        break;
                }
            }
        }

        /// <summary>P0-3:未就绪期暂存操作;未开启缓冲或已就绪返回 false(调用方走常规路径)。
        /// 同 (key,id,cb) 的重复 AddUI 去重,防重放期触发"add same UI"告警。</summary>
        private bool BufferOp(PendingOpType type, string key, uint id, Action cb, bool enabled = false)
        {
            if (!_bufferUntilReady || _configReady)
            {
                return false;
            }
            if (type == PendingOpType.AddUI)
            {
                for (int i = 0; i < _preReadyOps.Count; i++)
                {
                    var op = _preReadyOps[i];
                    if (op.Type == PendingOpType.AddUI && op.Key == key && op.Id == id && op.Cb == cb)
                    {
                        return true;
                    }
                }
            }
            _preReadyOps.Add(new PendingOp { Type = type, Key = key, Id = id, Cb = cb, Enabled = enabled });
            return true;
        }

        /// <summary>P0-3:未就绪期对冲撤销同 (key,id,cb) 的暂存 AddUI(绑定未真实生效,直接摘除);
        /// 无配对项返回 false(调用方走常规 RemoveUI 路径并留档)。</summary>
        private bool CancelBufferedAddUI(string key, Action cb, uint id)
        {
            if (!_bufferUntilReady || _configReady)
            {
                return false;
            }
            for (int i = 0; i < _preReadyOps.Count; i++)
            {
                var op = _preReadyOps[i];
                if (op.Type == PendingOpType.AddUI && op.Key == key && op.Id == id && op.Cb == cb)
                {
                    _preReadyOps.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}