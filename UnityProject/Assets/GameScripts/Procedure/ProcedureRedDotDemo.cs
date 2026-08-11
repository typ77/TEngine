using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.RedDot;
using GameLogic.RedDot.Core;
using GameLogic.RedDotDemo;
using TEngine;
using YooAsset;

namespace Procedure
{
    /// <summary>
    /// 红点 Demo 测试流程。
    /// 用于 RedDotDemoScene 中独立启动红点系统 + RedDotDemoUI。
    /// 由 GameEntry 启动流程触发（框架已初始化 YooAsset），本流程负责：
    /// 1. 初始化资源包
    /// 2. 初始化 RedDotSystem（加载内嵌 JSON、注册叶子逻辑、RefreshAll）
    /// 3. 显示 RedDotDemoUI
    /// </summary>
    public class ProcedureRedDotDemo : ProcedureBase
    {
        public override bool UseNativeDialog => false;

        /// <summary>内嵌红点树 JSON 配置（避免依赖外部资源文件）。</summary>
        private const string REDDOT_JSON = @"{
  ""vertexs"": [
    {""key"":""Root"",""redDotType"":2,""externalId"":-1},
    {""key"":""Mail"",""redDotType"":15,""externalId"":-1},
    {""key"":""Mail.Unread"",""redDotType"":2,""externalId"":1},
    {""key"":""Mail.Announce"",""redDotType"":1,""externalId"":2}
  ],
  ""edges"": [
    {""key"":""Root"",""outKeys"":[""Mail""]},
    {""key"":""Mail"",""outKeys"":[""Mail.Unread"",""Mail.Announce""]}
  ]
}";

        // 叶子逻辑 ID 用 RedDotDemoLogicIds 共享常量（避免魔法数字重复）

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
                Log.Error($"[ProcedureRedDotDemo] InitPackage failed: {initOp.Error}");
                return;
            }

            var versionOp = _resourceModule.RequestPackageVersionAsync();
            await versionOp.ToUniTask();
            if (versionOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[ProcedureRedDotDemo] RequestPackageVersion failed: {versionOp.Error}");
                return;
            }

            var manifestOp = _resourceModule.UpdatePackageManifestAsync(versionOp.PackageVersion);
            await manifestOp.ToUniTask();
            if (manifestOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[ProcedureRedDotDemo] UpdatePackageManifest failed: {manifestOp.Error}");
                return;
            }
            Log.Info("[ProcedureRedDotDemo] Resource package initialized.");

            // 2. 初始化红点系统
            InitRedDotSystem();

            // 3. 显示 Demo UI
            await UniTask.Yield();
            GameModule.UI.ShowUIAsync<RedDotDemoUI>();
            Log.Info("[ProcedureRedDotDemo] Demo UI shown.");
        }

        private void InitRedDotSystem()
        {
            var sys = RedDotSystem.Instance;

            // 加载内嵌配置。checkVisitor:false 因逻辑在配置加载后才注册（下文 RegLogic），
            // 此时 visitor 尚未就绪，校验会误报。生产中若逻辑先注册，应传 true 以校验配置笔误。
            sys.LoadJsonText(REDDOT_JSON, checkVisitor: false);
            Log.Info("[ProcedureRedDotDemo] RedDot tree loaded.");

            // 注册叶子逻辑 1：未读邮件数（数量型）
            sys.RegLogic(RedDotDemoLogicIds.Unread, (out RedDotStatus s, out int n) =>
            {
                int count = MailDemoData.Instance.UnreadCount;
                s = count > 0 ? RedDotStatus.Number : RedDotStatus.Null;
                n = count;
            });

            // 注册叶子逻辑 2：系统公告开关（布尔型 → Normal 红点）
            sys.RegLogic(RedDotDemoLogicIds.Announce, (out RedDotStatus s, out int n) =>
            {
                s = MailDemoData.Instance.HasAnnounce ? RedDotStatus.Normal : RedDotStatus.Null;
                n = 0;
            });

            // 注册叶子逻辑 3：新功能邮件（带 id，每个 id 一个 New 红点）
            sys.RegLogic(RedDotDemoLogicIds.NewMail, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.New;
                n = 0;
            });

            // 全量刷新一次，让初始状态正确计算
            sys.RefreshAll();
            Log.Info("[ProcedureRedDotDemo] RedDot logic registered & refreshed.");
        }
    }
}
