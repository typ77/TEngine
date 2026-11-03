using RedDot.Editor.Config;
using UnityEditor;
using UnityEngine;

namespace RedDot.Editor.AssetsCallback
{
    public static class CreateRedDotGraphAsset
    {
        [MenuItem("Assets/Create/红点图", false, 9999)]
        public static void CreateRedDotGraph()
        {
            var graphItem = ScriptableObject.CreateInstance<NewRedDotGraphAction>();
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, graphItem,
                $"New Red Dot Graph.{RedDotEditorConfig.EXTENSION}", null, null);
        }
    }
}