using Cysharp.Threading.Tasks;
using GameLogic;
using TEngine;
using YooAsset;

namespace Procedure
{
    /// <summary>
    /// FancyScrollView 示例测试流程。
    /// 用于 FancyScrollViewTestScene 中独立启动 FancyTestHomeUI。
    /// 在编辑器模式下初始化资源包和清单后直接显示测试 UI。
    /// </summary>
    public class ProcedureFancyTest : ProcedureBase
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
                Log.Error($"[ProcedureFancyTest] InitPackage failed: {initOp.Error}");
                return;
            }
            Log.Info("[ProcedureFancyTest] Package initialized.");

            // 2. 获取清单版本号
            var versionOp = _resourceModule.RequestPackageVersionAsync();
            await versionOp.ToUniTask();
            if (versionOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[ProcedureFancyTest] RequestPackageVersion failed: {versionOp.Error}");
                return;
            }
            Log.Info($"[ProcedureFancyTest] Package version: {versionOp.PackageVersion}");

            // 3. 更新清单
            var manifestOp = _resourceModule.UpdatePackageManifestAsync(versionOp.PackageVersion);
            await manifestOp.ToUniTask();
            if (manifestOp.Status != EOperationStatus.Succeed)
            {
                Log.Error($"[ProcedureFancyTest] UpdatePackageManifest failed: {manifestOp.Error}");
                return;
            }
            Log.Info("[ProcedureFancyTest] Manifest loaded. Showing FancyTestHomeUI.");

            // 4. 显示测试 UI
            await UniTask.Yield();
            GameModule.UI.ShowUIAsync<FancyTestHomeUI>();
        }
    }
}