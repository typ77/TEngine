using GameLogic.RedDot.Editor.Data;
using UnityEditor.ProjectWindowCallback;

namespace GameLogic.RedDot.Editor.AssetsCallback
{
    /// <summary>
    /// 创建新红点图资产时的命名编辑回调（写入空图数据）。
    /// </summary>
    public class NewRedDotGraphAction : EndNameEditAction
    {
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            var redDotGraphData = new RedDotGraphData();
            redDotGraphData.SaveTo(pathName);
        }
    }
}
