using System;
using System.IO;
using System.Text;
using GameLogic.RedDot.Editor.Config;
using GameLogic.RedDot.Editor.Data;
using GameLogic.RedDot.Editor.Importers;
using GameLogic.RedDot.Editor.Util;
using GameLogic.RedDot.Editor.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameLogic.RedDot.Editor
{
    /// <summary>
    /// 红点图编辑器主窗口。加载 .reddotgraph 资产、显示 GraphView、处理保存/导出。
    /// 双击 .reddotgraph 资产或通过 Inspector 的 "Open Red Dot Editor" 按钮打开。
    /// </summary>
    public class RedDotWindow : EditorWindow
    {
        private RedDotGraphView _graphView;
        private RedDotGraphData _graphData;
        public string curGuid;

        public RedDotEnumDictWindow keySettingWindow;
        public RedDotEnumDictWindow externalIdSettingWindow;

        private bool _lastDirtyState;
        private bool _assetMaybeDeleted;
        private bool _assetMaybeChangedOnDisk;

        public void Initialize(string assetGuid)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(assetGuid));
            if (asset == null) return;
            if (!EditorUtility.IsPersistent(asset)) return;
            if (curGuid == assetGuid) return;

            var path = AssetDatabase.GetAssetPath(asset);
            var extension = Path.GetExtension(path);
            if (extension == null) return;
            extension = extension.Substring(1).ToLowerInvariant();
            if (extension != RedDotEditorConfig.EXTENSION) return;

            curGuid = assetGuid;

            var scriptObj = MonoScript.FromScriptableObject(this);
            PathUtil.CurEditorPath = AssetDatabase.GetAssetPath(scriptObj).Replace("RedDotWindow.cs", "");

            var textGraph = File.ReadAllText(path, Encoding.UTF8);
            _graphData = JsonUtility.FromJson<RedDotGraphData>(textGraph);
            _graphData.ValidateGraph();
            UpdateTitle();
            _graphView = new RedDotGraphView(this, _graphData);
            rootVisualElement.Add(_graphView);
            Repaint();
        }

        private void OnFocus()
        {
            if (_graphData == null) return;
            if (!_graphData.IsDataCached())
            {
                _graphData.CacheData();
                RepaintGraph();
            }
        }

        private void Update()
        {
            if (_graphData == null) return;
            if (_graphData.IsDirty != _lastDirtyState)
            {
                UpdateTitle();
            }
            if (_assetMaybeChangedOnDisk)
            {
                RepaintGraph();
                _assetMaybeChangedOnDisk = false;
            }
        }

        public void AssetWasDeleted()
        {
            _assetMaybeDeleted = true;
            UpdateTitle();
        }

        public void CheckForChanges()
        {
            if (!_assetMaybeDeleted && _graphData != null)
            {
                _assetMaybeChangedOnDisk = true;
                UpdateTitle();
            }
        }

        private bool AssetFileExists()
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(curGuid);
            return File.Exists(assetPath);
        }

        public void UpdateTitle()
        {
            if (_graphData == null) return;
            _lastDirtyState = _graphData.IsDirty;
            string assetPath = AssetDatabase.GUIDToAssetPath(curGuid);
            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            string title = assetName;
            if (_graphData.IsDirty) title += "*";
            if (!AssetFileExists()) title += " (deleted)";
            var icon = Resources.Load<Texture2D>("Icons/icon_reddot_title");
            titleContent = new GUIContent(title, icon);
        }

        private void RepaintGraph()
        {
            if (_graphView != null)
            {
                _graphView.parent.Remove(_graphView);
                _graphView = null;
            }
            if (_graphData != null)
            {
                _graphData.ValidateGraph();
                _graphView = new RedDotGraphView(this, _graphData);
                rootVisualElement.Add(_graphView);
                Repaint();

                keySettingWindow?.Initialize(_graphData.keyEnumDict, RedDotEditorConfig.KEY_WINDOW_TITLE);
                externalIdSettingWindow?.Initialize(_graphData.externalIdEnumDict, RedDotEditorConfig.EXTERNAL_ID_WINDOW_TITLE);
            }
        }

        private void OnEnable()
        {
            RepaintGraph();
        }

        private void OnDisable()
        {
            _graphView = null;
            if (_graphData != null) _graphData.CacheData();
        }

        private void OnDestroy()
        {
            if (keySettingWindow != null)
            {
                keySettingWindow.Close();
                keySettingWindow = null;
            }
            if (externalIdSettingWindow != null)
            {
                externalIdSettingWindow.Close();
                externalIdSettingWindow = null;
            }
        }

        public void CheckAndSave()
        {
            if (_graphData != null)
            {
                if (!AssetFileExists())
                {
                    // 资产被删除，提示另存
                    var savedPath = SaveAsImplementation(false);
                    if (savedPath != null) curGuid = AssetDatabase.AssetPathToGUID(savedPath);
                    return;
                }
                SaveAsset();
            }
        }

        public void ExportTo()
        {
            if (curGuid == null || _graphData == null) return;
            var oldFilePath = AssetDatabase.GUIDToAssetPath(curGuid);
            if (string.IsNullOrEmpty(oldFilePath)) return;
            string oldDirectory = Path.GetDirectoryName(oldFilePath);
            var newFilePath = EditorUtility.SaveFilePanelInProject(
                "Export Red Dot Graph to Json",
                Path.GetFileNameWithoutExtension(oldFilePath),
                RedDotEditorConfig.EXPORT_EXTENSION, "", oldDirectory);
            if (!string.IsNullOrEmpty(newFilePath))
            {
                _graphData.ExportTo(newFilePath);
            }
        }

        private string SaveAsImplementation(bool openWhenSaved)
        {
            if (curGuid == null || _graphData == null) return null;
            var oldFilePath = AssetDatabase.GUIDToAssetPath(curGuid);
            if (string.IsNullOrEmpty(oldFilePath)) return null;
            string oldDirectory = Path.GetDirectoryName(oldFilePath);
            var newFilePath = EditorUtility.SaveFilePanelInProject(
                "Save Red Dot Graph As...",
                Path.GetFileNameWithoutExtension(oldFilePath),
                RedDotEditorConfig.EXTENSION, "", oldDirectory);
            if (string.IsNullOrEmpty(newFilePath) || newFilePath == oldFilePath) return null;

            bool success = FileUtilities.WriteRedDotGraphDataToDisk(newFilePath, _graphData) != null;
            AssetDatabase.ImportAsset(newFilePath);
            if (success && openWhenSaved)
            {
                RedDotGraphImporterEditor.ShowGraphEditWindow(newFilePath);
            }
            return success ? newFilePath : null;
        }

        private bool SaveAsset()
        {
            var pathName = AssetDatabase.GUIDToAssetPath(curGuid);
            return _graphData.SaveTo(pathName);
        }
    }
}
