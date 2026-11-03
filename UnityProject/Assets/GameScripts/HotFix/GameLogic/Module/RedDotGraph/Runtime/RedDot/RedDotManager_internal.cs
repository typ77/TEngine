using System.Collections.Generic;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    public partial class RedDotManager
    {
        private RedDotUIFrames _uiFramesNoId;
        private RedDotUIFrames _uiFramesHasId;
        protected RedDotGraph RedDotGrapic = new RedDotGraph();

        //P6-4(T7):动态节点 owner→dynKey 分配表(随 Reset 清)
        private readonly Dictionary<string, string> _dynamicNodes = new Dictionary<string, string>();
        //P6-5(N12):汇总节点 key→externalId 分配表(随 Reset 清)
        private readonly Dictionary<string, int> _summarizeExtIds = new Dictionary<string, int>();
        //K批2(2026-09-17 拍板):段约定废止,funcId/externalId 是代理键(业务零魔法数,只认 RedDotKeys.XXX)。
        //系统自动分配基点从 95000 挪到负数段:-1 已被聚合哨兵占用,自 -1000001 起递减,与全部正数配置空间
        //结构性隔离——杜绝 seed 递增爬进继承老 id 区间(100101+)后 RegExternalLogic 字典直写静默覆盖业务 checker。
        private static int s_summarizeExtIdSeed = -1000000;

        //P5-2(T10):刷新模式与脏合并队列(默认 Immediate 保持既有同帧语义;项目侧 Phase 6 T9 切 Batched)
        private RedDotRefreshMode _refreshMode = RedDotRefreshMode.Immediate;
        private readonly List<PendingChange> _pendingChanges = new List<PendingChange>();
        private readonly HashSet<PendingChange> _pendingSet = new HashSet<PendingChange>();
        private bool _flushing;
        /// <summary>Batched 模式每帧通知上限(登录风暴削峰)。口径=(key+id) 对,非按 key 合并——保 id 特定通知语义</summary>
        internal int MaxDispatchPerFrame = 64;

        //P0-3:配置未就绪缓冲。默认关闭(直通,验证器/GM 直连语义不变);模块 OnInit 开启,配置加载回调置就绪并重放。
        //暂存范围=DataChange/AddUI/RemoveUI/SetNodeEnabled;重放严格按 FIFO,先入队的绑定先收播。
        internal bool _bufferUntilReady;
        internal bool _configReady = true;
        private readonly List<PendingOp> _preReadyOps = new List<PendingOp>();

        //P0-1/P0-2:父列表与 per-id 槽位快照复用缓冲(补发为低频操作,成员复用避免分配)
        private readonly List<string> _parentKeyBuffer = new List<string>();
        private readonly List<uint> _idReplayBuffer = new List<uint>();
        private readonly List<string> _replayBuffer = new List<string>();

        /// <summary>
        /// 刷新模式开关:Immediate(同帧)/Batched(入队+帧末统一处理)。
        /// 切换契约:Batched→Immediate 前项目侧须自行 Flush,否则残留 pending 只能等下次 Flush 或 Reset(期间无通知)。
        /// </summary>
        public RedDotRefreshMode RefreshMode
        {
            get => _refreshMode;
            set => _refreshMode = value;
        }

        private RedDotManager()
        {
            _uiFramesNoId = new RedDotUIFrames();
            _uiFramesHasId = new RedDotUIFrames();
        }

        /// <summary>
        /// 添加红点顶点
        /// </summary>
        /// <param name="key"></param>
        /// <param name="redDotType"></param>
        /// <param name="externalId"></param>
        /// <returns></returns>
        protected int AddRedDotVertex(string key, int redDotType, int externalId = -1)
        {
            return RedDotGrapic.Insert(new RedDotVertex(key, redDotType, externalId));
        }

        /// <summary>
        /// 添加边
        /// </summary>
        /// <param name="vex1"></param>
        /// <param name="vex2"></param>
        /// <param name="weight"></param>
        /// <returns></returns>
        protected bool AddRedDotEdge(string vex1, string vex2, int weight = 0)
        {
            return RedDotGrapic.InsertEdge(vex1, vex2, weight);
        }

        private void TravelUIFrames(RedDotUIFrames redDotUiFrames)
        {
            //P2-8:无当前帧(尚未 AddFrame/帧全被移除)时安静返回,与 DataChange 的 ?. 语义对齐
            var frameNode = redDotUiFrames.GetCurrentFrameNode();
            if (frameNode == null)
            {
                return;
            }
            //R3(审查 2026-09-12):先快照再遍历——VisitRedDotUIStruct 同步执行 UI 回调,
            //回调内 RemoveUI/AddUI 改绑定会使惰性枚举失效(InvalidOperationException),与 DataChanged 快照同口径
            var snapshot = frameNode.SnapshotInfos();
            foreach (var info in snapshot)
            {
                VisitRedDotUIStruct(info);
            }
//            RedDotUIInfo info = redDotUiFrames.FirstRedDotStruct();
//            while (info != null)
//            {
//                VisitRedDotUIStruct(info);
//                info = redDotUiFrames.NextRedDotStruct();
//            }
        }

        /// <summary>
        /// 节点访问，就是具体的刷红点数据
        /// </summary>
        /// <param name="v"></param>
        private void VisitRedDotVertex(RedDotVertex v, uint id)
        {
            var info = v.GetRuntimeInfo(id);
            try
            {
                RedDotExternalLogicVisitor.Ins().Visit(v.ExternalId, id, out info.RedDotStatus, out info.Value);
            }
            catch (System.Exception e)
            {
                //P6-2(审核登记):checker 异常隔离——业务 checker 抛错不冲穿刷新链,该节点本轮记 Null
                Log.Error("红点 checker 抛出异常，key:{0}, id:{1}, ex:{2}", v.Key, id, e);
                info.RedDotStatus = RedDotStatus.RedDotStatusNull;
                info.Value = 0;
            }
        }

        /// <summary>
        /// visit某个红点
        /// </summary>
        /// <param name="redDotUiInfo"></param>
        private void VisitRedDotUIStruct(RedDotUIInfo redDotUiInfo)
        {
            //key的红点解决了，但带ID的红点也解决了
            var v = RedDotGrapic.GetRedDotByKey(redDotUiInfo.Key);
            if (v == null)
            {
                Log.Error("VisitRedDotUIStruct 未找到红点节点，key:{0}, id:{1}", redDotUiInfo.Key, redDotUiInfo.Id);
                return;
            }
            //P0-1 生效点⑤:巡检(RefreshRedDot 全量路径)跳过禁用节点——该路径直调刷新+直读 runtime info,
            //绕过 GetRedDotStatus 读门控,不拦会把禁用节点的脏缓存复活并触发 UI 回调
            if (!v.Enabled)
            {
                return;
            }
            var info = v.GetRuntimeInfo(redDotUiInfo.Id);
            if (info.IsDataChange)
            {
                RedDotGrapic.RefreshRedDotStatus(v.Key, redDotUiInfo.Id, VisitRedDotVertex);
            }
            if (info.StatusChangeTime > redDotUiInfo.LastChangeTime)
            {
                redDotUiInfo.LastChangeTime = info.StatusChangeTime;
                RedDotStatusChange(redDotUiInfo);
            }
        }

        /// <summary>
        /// 红点状态发生了变化，需要发消息出去，变更状态
        /// </summary>
        /// <param name="redDotUiInfo"></param>
        private void RedDotStatusChange(RedDotUIInfo redDotUiInfo)
        {
            redDotUiInfo.DoCallBack();
        }
    }
}