using System;
using System.IO;
using System.Text;
using RedDot.Editor.Config;
using RedDot.Editor.Data;
using RedDot.Editor.Importers;
using RedDot.Editor.Util;
using RedDot.Editor.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace RedDot.Editor
{
    public class RedDotWindow : EditorWindow
    {
        private RedDotGraphView _graphView;
        private RedDotGraphData _graphData;
        public string curGuid;

        public RedDotEnumDictWindow keySettingWindow;
        public RedDotEnumDictWindow externalIdSettingWindow;

        /// <summary>节点 key/逻辑ID 搜索窗口的复用 provider(K批6:原来每次点击 CreateInstance 且不销毁=泄漏;
        /// 缓存在窗口层,OnDisable 统一回收,每次打开重新 Init+挂 OnSelect)。NodeView 经画布 Window 访问。</summary>
        public Views.RedDotKeySearchWindowProvider KeySearchProvider;

        /// <summary>分组导航栈(空=主图;末位=当前组视图)。仅是过滤选择器,数据不复制(R2)。</summary>
        public System.Collections.Generic.List<int> NavStack = new System.Collections.Generic.List<int>();

        public void EnterGroup(int groupId)
        {
            NavStack.Add(groupId);
            RepaintGraph();
        }

        public void ExitGroup()
        {
            if (NavStack.Count > 0)
            {
                NavStack.RemoveAt(NavStack.Count - 1);
            }
            RepaintGraph();
        }

        public void ExitToMain()
        {
            NavStack.Clear();
            RepaintGraph();
        }

        public void JumpToGroup(int groupId)
        {
            var idx = NavStack.IndexOf(groupId);
            if (idx < 0)
            {
                return;
            }
            NavStack.RemoveRange(idx, NavStack.Count - idx);
            RepaintGraph();
        }

        /// <summary>图数据只读访问(树面板等侧栏薄壳使用,不暴露写口)。</summary>
        public RedDotGraphData GraphData => _graphData;

        /// <summary>组树面板选中态(跨 RepaintGraph 存续:面板每次导航重建,选中态放窗口层)。
        /// TreeSelectedGroupId:-1=无;0=主页卡区头;>0=组 id。节点行选中存 TreeSelectedContextId。</summary>
        public int TreeSelectedGroupId = -1;
        public int TreeSelectedContextId = -1;

        /// <summary>组树面板过滤词(面板随 RepaintGraph 重建,过滤词放窗口层防丢失)。</summary>
        public string TreeFilterText = "";

        /// <summary>组树折叠组集(会话视图态:面板重建存续,不落资产;点行导航自动展开祖先链)。</summary>
        public System.Collections.Generic.HashSet<int> TreeCollapsedGroupIds = new System.Collections.Generic.HashSet<int>();

        /// <summary>组树导航:按祖先链重建 NavStack(面包屑全链,不做栈式跳级),再重绘。</summary>
        public void RebuildNavStackTo(int groupId)
        {
            if (_graphData == null)
            {
                return;
            }
            NavStack = RedDot.Editor.Data.RedDotGroupTree.GetAncestorChain(_graphData, groupId);
            RepaintGraph();
        }

        /// <summary>组树节点行点击:按祖先链切到所属组视图并定位选中。</summary>
        public void GoToContext(int contextId)
        {
            if (_graphData == null)
            {
                return;
            }
            var ctx = _graphData.GetContext(contextId);
            if (ctx == null)
            {
                return;
            }
            RebuildNavStackTo(ctx.groupId);
            if (_graphView != null)
            {
                _graphView.FocusContext(contextId);
            }
        }

        /// <summary>删组后清洗 NavStack 残留(残留会致画布空白、面包屑显示 &lt;组X&gt;)。</summary>
        public void CleanNavStackAfterDelete(int deletedGroupId)
        {
            NavStack.RemoveAll(id => id == deletedGroupId);
        }

        public void RepaintGraphPublic()
        {
            RepaintGraph();
        }

        private Views.RedDotGroupTreePanel _treePanel;
        private VisualElement _layoutRoot;
        private VisualElement _mainArea;

        /// <summary>窗口三段式布局:工具栏行 + 面包屑行 + 画布区(画布区内再叠组树侧栏)。
        /// 工具栏/面包屑独立占行不再浮在画布上,树面板 top=0 起点即画布区顶(2026-09-14 布局修)。</summary>
        private void BuildLayout()
        {
            if (_graphData == null || _graphView == null)
            {
                return;
            }
            if (_layoutRoot == null)
            {
                _layoutRoot = rootVisualElement.Q<VisualElement>("reddot-layout-root");
            }
            if (_layoutRoot == null)
            {
                _layoutRoot = new VisualElement { name = "reddot-layout-root" };
                _layoutRoot.style.flexGrow = 1;
                rootVisualElement.Add(_layoutRoot);
            }
            _layoutRoot.Clear();
            DetachSidePanels();
            _layoutRoot.Add(_graphView.BuildToolbar());
            _layoutRoot.Add(_graphView.BuildBreadcrumb());
            _mainArea = new VisualElement();
            _mainArea.style.flexGrow = 1;
            _mainArea.Add(_graphView);
            _layoutRoot.Add(_mainArea);
            AttachSidePanels();
        }

        /// <summary>挂载组树侧栏(Initialize 与 RepaintGraph 共用;重复调用前先 Detach)。
        /// 动态挂载区已由树面板副页卡节替代(2026-09-14 组树批,面板文件存 .bak 可回滚)。</summary>
        private void AttachSidePanels()
        {
            if (_graphData == null || _graphView == null || _mainArea == null)
            {
                return;
            }
            DetachSidePanels();
            _treePanel = new Views.RedDotGroupTreePanel(this, _graphData);
            _mainArea.Add(_treePanel);
        }

        private void DetachSidePanels()
        {
            if (_treePanel != null)
            {
                _treePanel.RemoveFromHierarchy();
                _treePanel = null;
            }
        }

        private bool lastDirtyState;
        private bool assetMaybeDeleted;
        private bool assetMaybeChangedOnDisk;

        public void Initialize(string assetGuid)
        {
            EditorApplication.wantsToQuit -= PromptSaveIfDirtyOnQuit;
            var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(assetGuid));
            if (asset == null)
                return;

            if (!EditorUtility.IsPersistent(asset))
                return;

            if (curGuid == assetGuid)
                return;

            var path = AssetDatabase.GetAssetPath(asset);
            var extension = Path.GetExtension(path);
            if (extension == null)
                return;
            extension = extension.Substring(1).ToLowerInvariant();
            if (extension != RedDotEditorConfig.EXTENSION)
            {
                return;
            }
            curGuid = assetGuid;

            var scriptObj = MonoScript.FromScriptableObject(this);
            PathUtil.CurEditorPath = AssetDatabase.GetAssetPath(scriptObj).Replace("RedDotWindow.cs", "");

            var textGraph = File.ReadAllText(path, Encoding.UTF8);
            _graphData = JsonUtility.FromJson<RedDotGraphData>(textGraph);
            _graphData.ValidateGraph();
            UpdateTitle();
            _graphView = new RedDotGraphView(this, _graphData);
            BuildLayout();
            Repaint();

            EditorApplication.wantsToQuit += PromptSaveIfDirtyOnQuit;
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
            if (_graphData.IsDirty != lastDirtyState)
            {
                UpdateTitle();
            }

            if (assetMaybeChangedOnDisk)
            {
                RepaintGraph();
                assetMaybeChangedOnDisk = false;
            }
        }

        public void AssetWasDeleted()
        {
//            Debug.Log("AssetWasDeleted");
            assetMaybeDeleted = true;
            UpdateTitle();
        }

        /// <summary>
        /// 文件被改了
        /// </summary>
        public void CheckForChanges()
        {
//            Debug.Log("CheckForChanges");
            if (!assetMaybeDeleted && _graphData != null)
            {
                assetMaybeChangedOnDisk = true;
                UpdateTitle();
            }
        }
        
        bool AssetFileExists()
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(curGuid);
            return File.Exists(assetPath);
        }
        public void UpdateTitle()
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(curGuid);
            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            string title = assetName;
            //K批6:数据未加载(Initialize 早退)时 Postprocessor 也会触发本方法,判空防 NRE
            if (_graphData != null)
            {
                lastDirtyState = _graphData.IsDirty;
                if (_graphData.IsDirty)
                {
                    title = title + "*";
                }
            }
            else
            {
                lastDirtyState = false;
            }

            if (!AssetFileExists())
            {
                title = title + " (deleted)";
            }
            Texture2D icon;
            {
                icon = Resources.Load<Texture2D>("Icons/icon_reddot_title");
            }
            titleContent = new GUIContent(title, icon);
        }

        /// <summary>当前画布对应的容器(0=主图,>0=组视图)。切换容器时视口归零,
        /// 同容器内的重建(建组/改名等树操作)保留平移缩放(2026-09-16 反馈:切组视口应归零而非继承上一画布)。</summary>
        private int _viewContainerId = int.MinValue;

        void RepaintGraph()
        {
            var containerId = NavStack.Count > 0 ? NavStack[NavStack.Count - 1] : 0;
            var hadTransform = false;
            var vtPos = Vector3.zero;
            var vtScale = Vector3.one;
            if (_graphView != null)
            {
                if (_viewContainerId == containerId)
                {
                    //同容器重建才保留视口;容器已切换则丢弃旧平移缩放,新画布从原点呈现
                    var vt = _graphView.viewTransform;
                    vtPos = vt.position;
                    vtScale = vt.scale;
                    hadTransform = true;
                }
                //K批6:销毁画布持有的瞬时 ScriptableObject(节点创建/组选择 provider),防逐次重建累积泄漏
                _graphView.CleanupTransient();
                _graphView.parent.Remove(_graphView);
                _graphView = null;
            }
            _viewContainerId = containerId;
            DetachSidePanels();
            if (_graphData != null)
            {
                _graphData.ValidateGraph();
                _graphView = new RedDotGraphView(this, _graphData);
                BuildLayout();
                if (hadTransform)
                {
                    _graphView.UpdateViewTransform(vtPos, vtScale);
                }
                Repaint();

                if (keySettingWindow != null)
                {
                    keySettingWindow.Initialize(_graphData.keyEnumDict, RedDotEditorConfig.KEY_WINDOW_TITLE, _graphData);
                }

                if (externalIdSettingWindow != null)
                {
                    externalIdSettingWindow.Initialize(_graphData.externalIdEnumDict, RedDotEditorConfig.EXTERNAL_ID_WINDOW_TITLE, _graphData);
                }
            }
        }
   
        void OnEnable()
        {
            // subscribe this event so it is registered on assembly reloads (we don't run Initialize on assembly reload)
            // doing remove before add ensures we never subscribe twice
            EditorApplication.wantsToQuit -= PromptSaveIfDirtyOnQuit;

            RepaintGraph();
            
            EditorApplication.wantsToQuit += PromptSaveIfDirtyOnQuit;
        }

        void OnDisable()
        {
            _graphView = null;
            //K批6:数据未加载时 OnDisable 也会跑(Initialize 早退后关窗),判空防 NRE;瞬时 provider 一并回收
            if (KeySearchProvider != null)
            {
                DestroyImmediate(KeySearchProvider);
                KeySearchProvider = null;
            }
            if (_graphData != null)
            {
                _graphData.CacheData();
            }
            EditorApplication.wantsToQuit -= PromptSaveIfDirtyOnQuit;
        }
        
        void OnDestroy()
        {
//            Debug.Log("====OnDestroy");
            RedDotWindow newWindow = null;
            if (!PromptSaveIfDirtyOnQuit())
            {
                newWindow = CreateWindow<RedDotWindow>(typeof(RedDotWindow));//, typeof(SceneView));
                newWindow.Initialize(curGuid);
            }
            else
            {
                // the window is closing for good.. cleanup undo history for the graph object
//                Undo.ClearUndo(graphObject);
            }

//            graphObject = null;
//            graphEditorView = null;

            // show new window if we have one
            if (newWindow != null)
            {
                newWindow.Show();
                newWindow.Focus();
            }

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
        
        // returns the asset path the file was saved to, or NULL if nothing was saved
        string SaveAsImplementation(bool openWhenSaved)
        {
            string savedFilePath = null;

            if (curGuid != null && _graphData != null)
            {
                var oldFilePath = AssetDatabase.GUIDToAssetPath(curGuid);
                if (string.IsNullOrEmpty(oldFilePath) || _graphData == null)
                    return null;

                // The asset's name needs to be removed from the path, otherwise SaveFilePanel assumes it's a folder
                string oldDirectory = Path.GetDirectoryName(oldFilePath);

                var extension = RedDotEditorConfig.EXTENSION;
                var newFilePath = EditorUtility.SaveFilePanelInProject("Save Red Dot Graph As...", Path.GetFileNameWithoutExtension(oldFilePath), extension, "", oldDirectory);
                newFilePath = newFilePath.Replace(Application.dataPath, "Assets");

                if (newFilePath != oldFilePath)
                {
                    if (!string.IsNullOrEmpty(newFilePath))
                    {
                        bool success = (FileUtilities.WriteRedDotGraphDataToDisk(newFilePath, _graphData) != null);
                        AssetDatabase.ImportAsset(newFilePath);
                        if (success)
                        {
                            if (openWhenSaved)
                                RedDotGraphImporterEditor.ShowGraphEditWindow(newFilePath);
                            savedFilePath = newFilePath;
                        }
                    }
                }
                else
                {
                    // saving to the current path
                    if (SaveAsset())
                    {
                        savedFilePath = oldFilePath;
                    }
                }
            }
            return savedFilePath;
        }
        
        // returns true when the graph has been successfully saved, or the user has indicated they are ok with discarding the local graph
        // returns false when saving has failed
        bool DisplayDeletedFromDiskDialog(bool reopen = true)
        {
            // first double check if we've actually been deleted
            bool saved = false;
            bool okToClose = false;
            string originalAssetPath = AssetDatabase.GUIDToAssetPath(curGuid);

            while (true)
            {
                int option = EditorUtility.DisplayDialogComplex(
                    "Graph removed from project",
                    "The file has been deleted or removed from the project folder.\n\n" +
                    originalAssetPath +
                    "\n\nWould you like to save your Graph Asset?",
                    "Save As...", "Cancel", "Discard Graph and Close Window");

                if (option == 0)
                {
                    string savedPath = SaveAsImplementation(false);
                    if (savedPath != null)
                    {
                        saved = true;

                        // either close or reopen the local window editor
                        _graphData = null;
                        curGuid = (reopen ? AssetDatabase.AssetPathToGUID(savedPath) : null);

                        break;
                    }
                }
                else if (option == 2)
                {
                    okToClose = true;
                    _graphData = null;
                    curGuid = null;
                    break;
                }
                else if (option == 1)
                {
                    // continue in deleted state...
                    break;
                }
            }

            return (saved || okToClose);
        }
        
        private bool PromptSaveIfDirtyOnQuit()
        {
            // only bother unless we've actually got data to preserve
            if (_graphData != null)
            {
                // if the asset has been deleted, ask the user what to do
                if (!AssetFileExists())
                    return DisplayDeletedFromDiskDialog(false);

                // if there are unsaved modifications, ask the user what to do
                if (_graphData.IsDirty)
                {
                    int option = EditorUtility.DisplayDialogComplex(
                        "Red Dot Graph Has Been Modified",
                        "Do you want to save the changes you made in the Red Dot Graph?\n\n" +
                        AssetDatabase.GUIDToAssetPath(curGuid) +
                        "\n\nYour changes will be lost if you don't save them.",
                        "Save", "Cancel", "Discard Changes");

                    if (option == 0) // save
                    {
                        return SaveAsset();
                    }
                    else if (option == 1) // cancel (or escape/close dialog)
                    {
                        return false;
                    }
                    else if (option == 2) // discard
                    {
                        return true;
                    }
                }
            }
            return true;
        }

        private bool SaveAsset()
        {
            var pathName = AssetDatabase.GUIDToAssetPath(curGuid);
            return _graphData.SaveTo(pathName);
        }

        public void CheckAndSave()
        {
            if (_graphData != null)
            {
                if (!AssetFileExists())
                {
                    string originalAssetPath = AssetDatabase.GUIDToAssetPath(curGuid);

                    while (true)
                    {
                        int option = EditorUtility.DisplayDialogComplex(
                            "Graph removed from project",
                            "The file has been deleted or removed from the project folder.\n\n" +
                            originalAssetPath +
                            "\n\nWould you like to save your Graph Asset?",
                            "Save As...", "Cancel", "Discard Graph and Close Window");

                        if (option == 0)
                        {
                            string savedPath = SaveAsImplementation(false);
                            if (savedPath != null)
                            {
                                curGuid = AssetDatabase.AssetPathToGUID(savedPath);
                                break;
                            }
                        }
                        else if (option == 2)
                        {
                            _graphData = null;
                            curGuid = null;
                            Close();
                            return;
                        }
                        else if (option == 1)
                        {
                            // continue in deleted state...
                            break;
                        }
                    }
                }

                if (!SaveAsset())
                {
                    //P7 审核 C-1 配套:保存失败(写盘被拒)显式告警,脏态保留、标题 * 不消
                    TEngine.Log.Warning("RedDotGraph 保存失败，改动未落盘(标题脏标记保留)");
                }
            }
        }
        
        public void ExportTo()
        {
            if (curGuid != null && _graphData != null)
            {
                var oldFilePath = AssetDatabase.GUIDToAssetPath(curGuid);
                if (string.IsNullOrEmpty(oldFilePath) || _graphData == null)
                {
                    Debug.LogError("Export error");
                    return;
                }

                //K批10:默认预填运行时标准落点(原默认=资产名.json 落在 Assets/Editor,游戏加载不到,
                //顶点缩水门禁也对比不到真实运行时文件)
                var extension = RedDotEditorConfig.EXPORT_EXTENSION;
                var newFilePath = EditorUtility.SaveFilePanelInProject("Export Red Dot Graph to Json",
                    Path.GetFileNameWithoutExtension(RedDotEditorConfig.DEFAULT_EXPORT_PATH), extension, "",
                    Path.GetDirectoryName(RedDotEditorConfig.DEFAULT_EXPORT_PATH));
                newFilePath = newFilePath.Replace(Application.dataPath, "Assets");

                if (!string.IsNullOrEmpty(newFilePath) && newFilePath != RedDotEditorConfig.DEFAULT_EXPORT_PATH)
                {
                    if (!EditorUtility.DisplayDialog("导出到非标准位置",
                        TEngine.Utility.Text.Format("运行时固定从 {0} 加载配置(YooAsset 按文件名寻址)。\n当前目标不是该文件,游戏不会加载到本次导出内容。\n\n仍要导出到该位置?", RedDotEditorConfig.DEFAULT_EXPORT_PATH),
                        "仍要导出", "取消"))
                    {
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(newFilePath))
                {
                    //K批(2026-09-17)门禁①:key/逻辑ID 完整性——K 类问题导出后运行时整份加载必失败,导出无意义
                    var keyProblems = Data.RedDotKeyValidator.ValidateAll(_graphData);
                    if (keyProblems.Count > 0)
                    {
                        if (!EditorUtility.DisplayDialog("key 配置有问题",
                            TEngine.Utility.Text.Format("当前图存在 {0} 项 key/逻辑ID 配置问题,导出的配置在运行时会加载失败。\n\n示例:{1}\n\n仍要导出?", keyProblems.Count, keyProblems[0]),
                            "仍要导出", "取消"))
                        {
                            return;
                        }
                    }

                    //K批(2026-09-17)门禁②:顶点数缩水确认(过渡态红线防护)——图未重建完成就导出会把
                    //目标运行时 JSON 从全量覆盖成子集(历史事故形态:202 顶点被覆盖为 10)
                    //K批6:构建一次复用,不再门禁算一遍导出又算一遍
                    var exportJson = _graphData.BuildExportJson();
                    var newCount = exportJson.vertexs.Count;
                    if (File.Exists(newFilePath))
                    {
                        var oldCfg = JsonUtility.FromJson<RedDot.Runtime.RedDot.RedDotExportJson>(File.ReadAllText(newFilePath, Encoding.UTF8));
                        if (oldCfg != null && oldCfg.vertexs != null && newCount < oldCfg.vertexs.Count)
                        {
                            if (!EditorUtility.DisplayDialog("导出确认(顶点数缩水)",
                                TEngine.Utility.Text.Format("目标文件现有 {0} 个顶点,本次导出仅 {1} 个。\n常见原因:图未重建完成就导出/选错了目标文件。\n\n确认覆盖?", oldCfg.vertexs.Count, newCount),
                                "覆盖", "取消"))
                            {
                                return;
                            }
                        }
                    }

                    _graphData.ExportTo(newFilePath, exportJson);
                }
            }
        }
    }
}