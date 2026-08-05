using Cysharp.Threading.Tasks;
using GameLogic.RedDot;
using GameLogic.RedDot.Core;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 红点 Demo 场景入口。挂在场景根节点，负责：
    /// 1. 初始化 YooAsset 资源包（抄 ProcedureFancyTest 的三步）
    /// 2. 初始化 RedDotSystem（加载内嵌 JSON、注册叶子逻辑、RefreshAll）
    /// 3. 显示 RedDotDemoUI
    ///
    /// 红点树结构（演示 4 大特性）：
    ///   Root(Number, 聚合)                        ← 总未读数
    ///   └─ Mail(Normal|Number|New|Full=15, 聚合)  ← 邮件入口，按最高优先级显示
    ///      ├─ Mail.Unread   (Number, 叶子, id=1)  ← 未读普通邮件（数量求和）
    ///      ├─ Mail.Announce (Normal, 叶子, id=2)  ← 系统公告开关（布尔）
    ///      └─ Mail.Item.{N} (New, 动态叶子, id=3) ← 新功能邮件（运行时增删）
    /// </summary>
    public class RedDotDemoBoot : MonoBehaviour
    {
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

        // 叶子逻辑 ID 常量
        private const int LOGIC_UNREAD = 1;
        private const int LOGIC_ANNOUNCE = 2;
        private const int LOGIC_NEW_MAIL = 3;

        private IResourceModule _resourceModule;

        private async void Start()
        {
            Log.Info("[RedDotDemo] Boot starting...");
            _resourceModule = GameModule.Resource;

            // 1. 初始化资源包（UI 资源需要）
            await InitResourcePackage();

            // 2. 初始化红点系统
            InitRedDotSystem();

            // 3. 显示 Demo UI
            GameModule.UI.ShowUIAsync<RedDotDemoUI>();
            Log.Info("[RedDotDemo] Demo UI shown.");
        }

        private async UniTask InitResourcePackage()
        {
            var initOp = await _resourceModule.InitPackage(_resourceModule.DefaultPackageName);
            if (initOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[RedDotDemo] InitPackage failed: {initOp.Error}");
                return;
            }

            var versionOp = _resourceModule.RequestPackageVersionAsync();
            await versionOp.ToUniTask();
            if (versionOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[RedDotDemo] RequestPackageVersion failed: {versionOp.Error}");
                return;
            }

            var manifestOp = _resourceModule.UpdatePackageManifestAsync(versionOp.PackageVersion);
            await manifestOp.ToUniTask();
            if (manifestOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[RedDotDemo] UpdatePackageManifest failed: {manifestOp.Error}");
                return;
            }
            Log.Info("[RedDotDemo] Resource package initialized.");
        }

        private void InitRedDotSystem()
        {
            var sys = RedDotSystem.Instance;

            // 加载内嵌配置
            sys.LoadJsonText(REDDOT_JSON, checkVisitor: false);
            Log.Info("[RedDotDemo] RedDot tree loaded.");

            // 注册叶子逻辑 1：未读邮件数（数量型）
            sys.RegLogic(LOGIC_UNREAD, (out RedDotStatus s, out int n) =>
            {
                int count = MailDemoData.Instance.UnreadCount;
                s = count > 0 ? RedDotStatus.Number : RedDotStatus.Null;
                n = count;
            });

            // 注册叶子逻辑 2：系统公告开关（布尔型 → Normal 红点）
            sys.RegLogic(LOGIC_ANNOUNCE, (out RedDotStatus s, out int n) =>
            {
                s = MailDemoData.Instance.HasAnnounce ? RedDotStatus.Normal : RedDotStatus.Null;
                n = 0;
            });

            // 注册叶子逻辑 3：新功能邮件（带 id，每个 id 一个 New 红点）
            sys.RegLogic(LOGIC_NEW_MAIL, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.New;
                n = 0;
            });

            // 全量刷新一次，让初始状态正确计算
            sys.RefreshAll();
            Log.Info("[RedDotDemo] RedDot logic registered & refreshed.");
        }
    }
}
