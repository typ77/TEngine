using RedDot.Editor.Data;
using UnityEditor.ProjectWindowCallback;

namespace RedDot.Editor.AssetsCallback
{
    public class NewRedDotGraphAction: EndNameEditAction
    {
        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            var redDotGraphData = new RedDotGraphData();
            //资产创建管线内不走模态弹窗,失败落日志(K批6:落盘失败向用户显式暴露,不再静默)
            if (!redDotGraphData.SaveTo(pathName, interactive: false))
            {
                TEngine.Log.Error("RedDotGraph 初始资产写盘失败:{0}", pathName);
            }

//            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<RedDotGraphData>(pathName);
//            Selection.activeObject = obj;
        }
    }
}