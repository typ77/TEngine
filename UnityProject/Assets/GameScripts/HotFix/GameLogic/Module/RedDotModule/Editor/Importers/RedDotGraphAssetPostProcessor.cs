using System;
using System.Linq;
using GameLogic.RedDot.Editor.Config;
using UnityEditor;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Importers
{
    /// <summary>
    /// 监听 .reddotgraph 资产的导入/删除/移动，同步更新已打开的编辑器窗口标题与状态。
    /// </summary>
    internal class RedDotGraphAssetPostProcessor : AssetPostprocessor
    {
        static void UpdateAfterAssetChange(string[] newNames)
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<RedDotWindow>())
            {
                foreach (var name in newNames)
                {
                    if (w.curGuid == AssetDatabase.AssetPathToGUID(name))
                        w.UpdateTitle();
                }
            }
        }

        static void DisplayDeletionDialog(string[] deletedAssets)
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<RedDotWindow>())
            {
                foreach (var deleted in deletedAssets)
                {
                    if (w.curGuid == AssetDatabase.AssetPathToGUID(deleted))
                        w.AssetWasDeleted();
                }
            }
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool anyMoved = movedAssets.Any(val => val.EndsWith(RedDotEditorConfig.EXTENSION, StringComparison.InvariantCultureIgnoreCase));
            if (anyMoved) UpdateAfterAssetChange(movedAssets);

            bool anyRemoved = deletedAssets.Any(val => val.EndsWith(RedDotEditorConfig.EXTENSION, StringComparison.InvariantCultureIgnoreCase));
            if (anyRemoved) DisplayDeletionDialog(deletedAssets);

            var windows = Resources.FindObjectsOfTypeAll<RedDotWindow>();
            var changedGraphGuids = importedAssets
                .Where(x => x.EndsWith(RedDotEditorConfig.EXTENSION, StringComparison.InvariantCultureIgnoreCase))
                .Select(AssetDatabase.AssetPathToGUID)
                .ToList();
            foreach (var window in windows)
            {
                if (changedGraphGuids.Contains(window.curGuid))
                {
                    window.CheckForChanges();
                }
            }
        }
    }
}
