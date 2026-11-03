using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.RedDotDemo;
using RedDot.Runtime.RedDot;
using TEngine;
using YooAsset;

namespace Procedure
{
    /// <summary>
    /// 红点 Demo 测试流程（RedDotGraph 新图版）。
    /// 用于 RedDotDemoScene 中独立启动红点系统 + RedDotDemoUI。
    /// 由 GameEntry 启动流程触发（框架已初始化 YooAsset），本流程负责：
    /// 1. 初始化资源包
    /// 2. 初始化 RedDotGraph（走 RedDotGraphModule 生产链路：YooAsset 加载 RedDotGraphConfig
    ///    + P0-3 缓冲重放 RegisterChecker/DataChange + DataChange 触发）
    /// 3. 显示 RedDotDemoUI
    /// </summary>
    public class ProcedureRedDotDemo : ProcedureBase
    {
        public override bool UseNativeDialog => false;

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            InitAndShowUI().Forget();
        }

        private async UniTaskVoid InitAndShowUI()
        {
            // 1. 初始化资源包
            var initOp = await _resourceModule.InitPackage(_resourceModule.DefaultPackageName);
            if (initOp.Status != EOperationStatus.Succeed)
            {
                Log.Error("[ProcedureRedDotDemo] InitPackage failed: {0}", initOp.Error);
                return;
            }

            var versionOp = _resourceModule.RequestPackageVersionAsync();
            await versionOp.ToUniTask();
            if (versionOp.Status != EOperationStatus.Succeed)
            {
                Log.Error("[ProcedureRedDotDemo] RequestPackageVersion failed: {0}", versionOp.Error);
                return;
            }

            var manifestOp = _resourceModule.UpdatePackageManifestAsync(versionOp.PackageVersion);
            await manifestOp.ToUniTask();
            if (manifestOp.Status != EOperationStatus.Succeed)
            {
                Log.Error("[ProcedureRedDotDemo] UpdatePackageManifest failed: {0}", manifestOp.Error);
                return;
            }
            Log.Info("[ProcedureRedDotDemo] Resource package initialized.");

            // 2. 初始化红点图（Demo 特例直连 Manager 建图,正式业务走 RedDotGraphModule/YooAsset）
            InitRedDotGraph();

            // 3. 显示 Demo UI
            await UniTask.Yield();
            GameModule.UI.ShowUIAsync<RedDotDemoUI>();
            Log.Info("[ProcedureRedDotDemo] Demo UI shown.");
        }

        private void InitRedDotGraph()
        {
            // 2.1 首次触达模块：触发 RedDotGraphModule.OnInit（开启配置缓冲 + AddFrame(Global) + Batched 模式 + 帧驱动定时器）。
            // 配置由模块 OnInit 的 LoadConfigAsync 从 YooAsset 加载（Assets/AssetRaw/Configs/Other/RedDotGraphConfig.json,
            // 与编辑器 RedDotWindow 导出按钮的默认落点一致）；加载完成 SetConfigReady 后按 FIFO 重放下面的暂存操作。
            // 注册收口走模块 RegisterChecker(RedDotKeys.XXX)：externalId 由常量携带,业务零魔法数;
            // 聚合节点(externalId=-1)无外部数据源,不可注册。
            RegisterCheckers();

            // 2.2 初始报脏一轮,让聚合链路算出各节点初始状态（配置未就绪期由 P0-3 缓冲,就绪后重放;
            // Batched 模式入队,由模块帧驱动冲刷）
            var mgr = RedDotManager.Ins();
            mgr.DataChange(RedDotKeys.Mail_Unread);
            mgr.DataChange(RedDotKeys.Mail_Announce);
            mgr.DataChange(RedDotKeys.Shop_LimitTime);
            mgr.DataChange(RedDotKeys.Activity_SignIn);
            mgr.DataChange(RedDotKeys.Activity_Welfare);
            Log.Info("[ProcedureRedDotDemo] RedDot checkers registered & initial DataChange sent (config loading async).");
        }

        /// <summary>注册五个叶子的 checker（经模块 P0-5 收口,key 常量携带 externalId）。</summary>
        private void RegisterCheckers()
        {
            var module = RedDotGraphModule.Instance;

            // 未读邮件数（红数字态,数量型）
            module.RegisterChecker(RedDotKeys.Mail_Unread, (out RedDotStatus status, out int num) =>
            {
                int count = MailDemoData.Instance.UnreadCount;
                status = count > 0 ? RedDotStatus.RedDotStatusRedNumber : RedDotStatus.RedDotStatusNull;
                num = count;
            });

            // 系统公告开关（红点/蓝点双态,演示同一节点多状态切换）
            module.RegisterChecker(RedDotKeys.Mail_Announce, (out RedDotStatus status, out int num) =>
            {
                num = 0;
                switch (MailDemoData.Instance.AnnounceMode)
                {
                    case MailDemoData.AnnounceStateEnum.Red:
                        status = RedDotStatus.RedDotStatusRedDot;
                        break;
                    case MailDemoData.AnnounceStateEnum.Blue:
                        status = RedDotStatus.RedDotStatusBlueDot;
                        break;
                    default:
                        status = RedDotStatus.RedDotStatusNull;
                        break;
                }
            });

            // 限时商店（满溢态）
            module.RegisterChecker(RedDotKeys.Shop_LimitTime, (out RedDotStatus status, out int num) =>
            {
                status = MailDemoData.Instance.ShopFull ? RedDotStatus.RedDotStatusFull : RedDotStatus.RedDotStatusNull;
                num = 0;
            });

            // 每日签到（新标签态）
            module.RegisterChecker(RedDotKeys.Activity_SignIn, (out RedDotStatus status, out int num) =>
            {
                status = MailDemoData.Instance.SignInUnclaimed ? RedDotStatus.RedDotStatusTips : RedDotStatus.RedDotStatusNull;
                num = 0;
            });

            // 福利领取数（蓝数字态）
            module.RegisterChecker(RedDotKeys.Activity_Welfare, (out RedDotStatus status, out int num) =>
            {
                int count = MailDemoData.Instance.WelfareCount;
                status = count > 0 ? RedDotStatus.RedDotStatusBlueNumber : RedDotStatus.RedDotStatusNull;
                num = count;
            });
        }
    }
}
