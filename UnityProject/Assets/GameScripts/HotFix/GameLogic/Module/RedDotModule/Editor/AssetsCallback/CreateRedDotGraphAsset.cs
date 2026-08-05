using GameLogic.RedDot.Editor.Config;
using GameLogic.RedDot.Editor.Data;
using UnityEditor;
using UnityEngine;

namespace GameLogic.RedDot.Editor.AssetsCallback
{
    /// <summary>
    /// Project 窗口右键菜单：创建新的红点图资产。
    /// </summary>
    public static class CreateRedDotGraphAsset
    {
        [MenuItem("Assets/Create/Red Dot Graph", false, 9999)]
        public static void CreateRedDotGraph()
        {
            var graphItem = ScriptableObject.CreateInstance<NewRedDotGraphAction>();
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, graphItem,
                $"New Red Dot Graph.{RedDotEditorConfig.EXTENSION}", null, null);
        }
    }
}
