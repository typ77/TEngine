using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;

namespace Procedure
{
    /// <summary>
    /// FancyScrollView 示例测试流程。
    /// 用于 FancyScrollViewTestScene 中独立启动 FancyTestHomeUI。
    /// </summary>
    public class ProcedureFancyTest : ProcedureBase
    {
        public override bool UseNativeDialog => false;

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            ShowTestUI().Forget();
        }

        private async UniTaskVoid ShowTestUI()
        {
            await UniTask.Yield();
            // Phase 2 实现后将替换为：
            // GameModule.UI.ShowUIAsync<FancyTestHomeUI>();
            Debug.Log("[ProcedureFancyTest] Entered successfully. Waiting for FancyTestHomeUI (Phase 2).");
        }
    }
}