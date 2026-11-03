using System;
using System.Linq;
using RedDot.Editor.Config;
using UnityEditor;
using UnityEngine;

namespace RedDot.Editor.Importers
{
    class RedDotGraphAssetPostProcessor : AssetPostprocessor
    {
        static void UpdateAfterAssetChange(string[] newNames)
        {
            // This will change the title of the window.
            RedDotWindow[] windows = Resources.FindObjectsOfTypeAll<RedDotWindow>();
            foreach (var matGraphEditWindow in windows)
            {
                for (int i = 0; i < newNames.Length; ++i)
                {
                    if (matGraphEditWindow.curGuid == AssetDatabase.AssetPathToGUID(newNames[i]))
                        matGraphEditWindow.UpdateTitle();
                }
            }
        }

        static void DisplayDeletionDialog(string[] deletedAssets)
        {
            RedDotWindow[] windows = Resources.FindObjectsOfTypeAll<RedDotWindow>();
            foreach (var matGraphEditWindow in windows)
            {
                for (int i = 0; i < deletedAssets.Length; ++i)
                {
                    if (matGraphEditWindow.curGuid == AssetDatabase.AssetPathToGUID(deletedAssets[i]))
                        matGraphEditWindow.AssetWasDeleted();
                }
            }
        }

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            //P7(E16):三处各自单条件——原代码同条件重复判定两遍,删除冗余
            //K批6:变量名勘误(模板复制残留的 Shaders→Graphs,实际处理的是 .reddotgraph)
            // Moved assets
            bool anyMovedGraphs = movedAssets.Any(val => val.EndsWith(RedDotEditorConfig.EXTENSION, StringComparison.InvariantCultureIgnoreCase));
            if (anyMovedGraphs)
                UpdateAfterAssetChange(movedAssets);

            // Deleted assets
            bool anyRemovedGraphs = deletedAssets.Any(val => val.EndsWith(RedDotEditorConfig.EXTENSION, StringComparison.InvariantCultureIgnoreCase));
            if (anyRemovedGraphs)
                DisplayDeletionDialog(deletedAssets);

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

            // 树形 key 下拉缓存失效(2026-09-18):图内容变更/移动/删除都经本钩子
            if (anyMovedGraphs || anyRemovedGraphs || changedGraphGuids.Count > 0)
            {
                RedDot.Editor.Data.RedDotKeyTreeBuilder.InvalidateCache();
            }
        }
    }
}
