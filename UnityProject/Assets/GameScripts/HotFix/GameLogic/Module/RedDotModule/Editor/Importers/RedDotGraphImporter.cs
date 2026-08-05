using GameLogic.RedDot.Editor.Config;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Importers
{
    /// <summary>
    /// .reddotgraph 文件的 ScriptedImporter。使 Unity 识别该扩展名为可导入资产，
    /// 双击通过 <see cref="RedDotGraphImporterEditor"/> 打开图编辑器。
    /// </summary>
    [ScriptedImporter(1, RedDotEditorConfig.EXTENSION)]
    public class RedDotGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var mainObject = new GameObject("RedDotImportAsset");
            Texture2D texture = Resources.Load<Texture2D>("Icons/icon_reddot_title");
            ctx.AddObjectToAsset("RedDotImportAsset", mainObject, texture);
            ctx.SetMainObject(mainObject);
        }
    }
}
