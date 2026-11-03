using System;
using System.Collections.Generic;
using TEngine;
using UnityEngine;
using RedDot.Runtime.RedDot;

namespace GameLogic
{
    /// <summary>
    /// RedDotGraph 红点库 TEngine 集成模块(P6-1/T1)。
    /// 职责:配置加载(YooAsset TextAsset→AddJsonStr)/Provider 注册事件/单帧全局模式(P6-3/T4)/
    /// Batched 刷新帧驱动(P6-2/T9)/Release 时 Reset(P4-1)。
    /// 启动链由 GameApp.StartGameLogic 调 EnsureStarted(首启激活;重登/切号再调即全量重建)。
    /// 配置约定:编辑器导出的红点 JSON 以 YooAsset 地址 <see cref="ConfigAssetAddress"/> 入库。
    /// </summary>
    public class RedDotGraphModule : Singleton<RedDotGraphModule>, ILateUpdate
    {
        /// <summary>红点配置资产地址(YooAsset)</summary>
        public const string ConfigAssetAddress = "RedDotGraphConfig";

        /// <summary>P6-3(T4)单帧全局模式唯一帧名:初始化 AddFrame 一次,此后永不切帧/删帧</summary>
        public const string GlobalFrameName = "Global";

        private bool _started;

        /// <summary>
        /// GameApp 启动链入口(每次登录调用):首次为激活(OnInit 已随 Instance 执行),
        /// 重登/切号走 Reinitialize 全量重建(P6 验收:图/帧/visitor/队列无残留)。
        /// </summary>
        public void EnsureStarted()
        {
            if (_started)
            {
                Reinitialize();
                return;
            }
            _started = true;
        }

        /// <summary>
        /// 全量重建:释放当前模块实例(帧驱动定时器+Manager Reset)后重走 OnInit。
        /// GM RedDotGraphReset 与重登/切号共用此入口。
        /// P6 复核 I-3:重建后必须在新实例上也置 _started——否则新实例 flag=false 会使下一次
        /// EnsureStarted 误判为首启而跳过重建,呈隔次重建(RR-R-R)。
        /// </summary>
        public void Reinitialize()
        {
            Release();
            var inst = Instance;
            inst._started = true;
        }

        protected override void OnInit()
        {
            var mgr = RedDotManager.Ins();
            //P0-3:配置加载为异步,先开未就绪缓冲——期间业务 DataChange/AddUI/SetNodeEnabled 暂存,
            //加载完成(SetConfigReady)后按 FIFO 重放;加载失败也置就绪(未知 key 走常规告警大声暴露)
            mgr.BeginConfigBuffering();
            //P6-3(T4) 单帧全局模式(P0-3 B 路线):AddFrame 一次,此后永不 ChangeToFrame/RemoveFrame/AddFrame;
            //被盖窗口(CanvasGroup alpha=0)绑定照常收通知,可见性跳过留待 Profiler 有数据后评估
            mgr.AddFrame(GlobalFrameName);
            //P6-2(T9) 项目侧默认 Batched:对齐旧 500ms 合并的抗风暴价值,延迟从 500ms 降到 1 帧
            mgr.RefreshMode = RedDotRefreshMode.Batched;
            //与 Ljdmw 差异:Ljdmw 用自改 AddGameTimer(1,1,0,FlushPending)(1ms 步进≈每帧一次)做帧驱动;
            //本项目标准 TEngine 的 1ms Repeat 在帧长抖动/失焦时触发追赶风暴(实测刷屏
            //"[Timer] 追赶步数超限"),故改为实现 ILateUpdate 帧末驱动——语义即"每帧一次",
            //且由 SingletonSystem 生命周期自动注册/注销,无需手动取消
            //配置加载(异步不阻塞初始化链;缺失时红点图保持为空)
            LoadConfigAsync();
        }

        protected override void OnRelease()
        {
            RedDotManager.Ins().Reset();
            base.OnRelease();
        }

        /// <summary>P6-2 业务透传:注册无 id checker</summary>
        public void RegisterExternalLogic(int externalId, RedDotExternalLogicVisitor.RedDotExternalLogic logic)
        {
            RedDotExternalLogicVisitor.Ins().RegExternalLogic(externalId, logic);
        }

        /// <summary>P6-2 业务透传:注册带 id checker</summary>
        public void RegisterExternalLogic(int externalId, RedDotExternalLogicVisitor.RedDotExternalLogicWithId logic)
        {
            RedDotExternalLogicVisitor.Ins().RegExternalLogic(externalId, logic);
        }

        /// <summary>
        /// P0-5 注册收口:业务零魔法数——externalId 由常量携带,业务只认 RedDotKeys.XXX。
        /// 聚合节点(externalId=-1)无外部数据源,拒绝注册并报错。
        /// </summary>
        public void RegisterChecker(RedDotKey key, RedDotExternalLogicVisitor.RedDotExternalLogic logic)
        {
            if (!ValidateKeyArg(key))
            {
                return;
            }
            RegisterExternalLogic(key.ExternalId, logic);
        }

        /// <summary>P0-5 注册收口(带 id 版,per-id 场景)</summary>
        public void RegisterChecker(RedDotKey key, RedDotExternalLogicVisitor.RedDotExternalLogicWithId logic)
        {
            if (!ValidateKeyArg(key))
            {
                return;
            }
            RegisterExternalLogic(key.ExternalId, logic);
        }

        /// <summary>P0-5:注册参数守卫(纯函数,U6 验证器直测)。无效返回 false 并报错。</summary>
        internal static bool ValidateKeyArg(RedDotKey key)
        {
            if (key == null || string.IsNullOrEmpty(key.Key))
            {
                Log.Error("RegisterChecker RedDotKey 无效(null 或空 key)");
                return false;
            }
            if (key.ExternalId == -1)
            {
                Log.Error("RegisterChecker {0} 为聚合节点(externalId=-1),无外部数据源不可注册 checker", key.Key);
                return false;
            }
            return true;
        }

        /// <summary>
        /// P0-5 一致性自检(配置加载成功后自动调用,GM/验证器可复跑):RedDotKeys 常量表 vs 当前图
        /// 双向对账,不一致一次性汇总 Warning——JSON(热更)与常量(热更 dll)发布节奏错配从运行期
        /// 静默错变为启动即报。空常量表(图未导出过)自动跳过。
        /// </summary>
        /// <returns>问题列表(空=一致)</returns>
        public List<string> ValidateKeysConsistency()
        {
            var keys = RedDotKeys.All;
            if (keys == null || keys.Length == 0)
            {
                return new List<string>();
            }
            return RedDotManager.ValidateKeysConsistency(RedDotManager.Ins().GraphForVerify, keys);
        }

        /// <summary>P0-3 业务透传:报脏(配置未就绪期由 Manager 缓冲,加载完成后重放)</summary>
        public void DataChange(string key, uint id = 0)
        {
            RedDotManager.Ins().DataChange(key, id);
        }

        // 注册活动期次等动态节点（幂等，key 形如 dyn:ownerKey）
        public void RegisterDynamicNode(string ownerKey, out string dynKey)
        {
            RedDotManager.Ins().RegisterDynamicNode(ownerKey, out dynKey);
        }

        // 动态节点挂到静态/动态父节点（未就绪会登记重放）
        public void SetDynamicParent(string dynKey, string parentKey)
        {
            RedDotManager.Ins().SetDynamicParent(dynKey, parentKey);
        }

        // 移除动态节点（摘边/摘 checker/对父补发；Demo 与业务共用入口）
        public void RemoveDynamicNode(string ownerKey)
        {
            RedDotManager.Ins().RemoveDynamicNode(ownerKey);
        }

        // 给动态叶子绑定 checker（重复调用覆盖委托）
        public void BindDynamicChecker(string dynKey, RedDotExternalLogicVisitor.RedDotExternalLogic logic)
        {
            RedDotManager.Ins().BindDynamicChecker(dynKey, logic);
        }

        /// <summary>P0-3 业务透传:UI 绑定(未就绪期缓冲;P0-4 新红点组件必须走此入口,直连 Manager 会绕过缓冲)</summary>
        public void AddUI(string key, Action cb, uint id = 0)
        {
            RedDotManager.Ins().AddUI(key, cb, id);
        }

        /// <summary>P0-3 业务透传:UI 解绑(未就绪期对冲撤销配对的暂存 AddUI)</summary>
        public void RemoveUI(string key, Action cb, uint id = 0)
        {
            RedDotManager.Ins().RemoveUI(key, cb, id);
        }

        /// <summary>P0-1/P0-3 业务透传:节点门控(未就绪期缓冲,解锁状态先于配置到达不丢失)</summary>
        public void SetNodeEnabled(string key, bool enabled)
        {
            RedDotManager.Ins().SetNodeEnabled(key, enabled);
        }

        /// <summary>P6-2 开窗/切帧/登录完成等节点的显式即时刷新入口</summary>
        public void FlushNow()
        {
            RedDotManager.Ins().FlushPendingChanges();
        }

        /// <summary>
        /// P0-4 业务透传:读红点状态(读时刷新语义)。新红点组件回调内取态唯一入口,
        /// 与 AddUI/RemoveUI 同走模块收口(不直连 Manager,保持"业务只认模块"契约)。
        /// </summary>
        public void GetRedDotStatus(string key, uint id, out RedDotStatus status, out int num)
        {
            RedDotManager.Ins().GetRedDotStatus(key, id, out status, out num);
        }

        /// <summary>
        /// P4-2 业务透传:按 keep 集收敛 per-id 槽位(长列表防只增不减;id=0 槽库侧强制保留)。
        /// keep 必须传"仍存在的全部 id"而非当前可见子集——被释放的 id 重绑时读 clean 新槽会显示 Null。
        /// </summary>
        public void ReleaseCellIds(string key, ICollection<uint> keep)
        {
            RedDotManager.Ins().ReleaseCellIds(key, keep);
        }

        /// <summary>上次 flush 的帧号（-1 保证首帧必执行）。</summary>
        private int _lastFlushFrame = -1;

        /// <summary>ILateUpdate 帧末驱动:Batched 模式每帧统一冲刷(SingletonSystem 随 Retain 自动注册,Release 自动注销)。</summary>
        public void OnLateUpdate()
        {
            FlushPending();
        }

        private void FlushPending()
        {
            // A6-1 帧号判重:保证严格每帧至多 flush 一次,FlushPendingChanges 的 MaxDispatchPerFrame=64
            // 削峰语义不被打穿(ILateUpdate 本身每帧一次,此处为防御性保留,防未来多处驱动叠加)
            int frame = Time.frameCount;
            if (frame == _lastFlushFrame)
            {
                return;
            }

            _lastFlushFrame = frame;
            RedDotManager.Ins().FlushPendingChanges();
        }

        private async void LoadConfigAsync()
        {
            //P1-3 审核:捕获当前实例,await 恢复后校验——重登 Reinitialize 会 Reset 管理器并重建模块实例,
            //陈旧续体严禁再触碰管线(旧实例 AddJsonStr 无害,但对当前实例提前 fullReplay 会把重放表的
            //静态父误判"配置错配"永久摘除)
            var mgr = RedDotManager.Ins();
            try
            {
                var textAsset = await GameModule.Resource.LoadAssetAsync<TextAsset>(ConfigAssetAddress);
                if (!ReferenceEquals(mgr, RedDotManager.Ins()))
                {
                    return;
                }
                if (textAsset == null)
                {
                    Log.Warning("RedDotGraph 配置缺失({0})，红点图保持为空", ConfigAssetAddress);
                    //P0-3:缺失/失败同样置就绪——直通后未知 key 走常规告警,防业务操作被无限缓冲
                    mgr.SetConfigReady();
                    return;
                }
                // checkVisitor=false:加载期 checker 注册完备性校验降为运行期 Warning 域——
                // EditorSimulateMode 下 YooAsset 加载可能同步完成,续体在 SystemMgr.OnInit 循环中途
                // 即跑校验(仅部分 Mgr 已注册),checkVisitor=true 会非确定性整份拒载(实测 82/102 项误报,
                // 图空全会话红点失效)。结构错误(重复顶点/坏边)仍硬失败;未注册节点由 Visit 期 Warning 大声暴露
                if (!mgr.AddJsonStr(textAsset.text, false))
                {
                    Log.Error("RedDotGraph 配置加载失败(详见上方聚合错误)");
                    mgr.SetConfigReady();
                    return;
                }
                //P0-5:常量表 vs 图一致性自检——错配一次性汇总 Warning(启动即报,不再运行期静默)
                var keyProblems = ValidateKeysConsistency();
                if (keyProblems.Count > 0)
                {
                    Log.Warning("RedDotKeys 常量表与图配置不一致 {0} 项(疑似 JSON/热更 dll 版本错配):{1}",
                        keyProblems.Count, string.Join("; ", keyProblems));
                }
                //P0-2:动态挂父重放挂配置加载完成回调(图就绪后重放才有顶点可挂),先于业务暂存操作重放
                mgr.ReplayDynamicParents();
                mgr.SetConfigReady();
            }
            catch (Exception e)
            {
                Log.Error("RedDotGraph 配置加载异常:{0}", e);
                mgr.SetConfigReady();
            }
        }
    }
}
