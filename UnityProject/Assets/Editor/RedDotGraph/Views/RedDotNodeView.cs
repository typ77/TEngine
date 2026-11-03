using System.Collections.Generic;
using RedDot.Editor.Config;
using RedDot.Editor.Data;
using RedDot.Editor.Util;
using TEngine;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RedDot.Editor.Views
{
    public class RedDotNodeView:Node
    {
        public Port InPort;
        public Port OutPort;
        private RedDotNodeContext _nodeContext;
        private RedDotGraphData _graphData;
        private Label _descLabel;
        public RedDotNodeView(RedDotNodeContext nodeContext, RedDotGraphData graphData)
        {
            _nodeContext = nodeContext;
            _graphData = graphData;
            var isGroupRoot = RedDotGroupTree.IsGroupRoot(graphData, nodeContext);
            if (!isGroupRoot)
            {
                //创建一个inputPort(组根无前置连接口:它是本组起点,连入会破坏组根语义)
                InPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(Port));
                //设置port显示的名称
                InPort.portName = " ";
                InPort.portColor = Color.red;
                //添加到inputContainer容器中
                inputContainer.Add(InPort);
            }
            OutPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(Port));
            OutPort.portName = "子节点";
            OutPort.portColor = Color.red;
            outputContainer.Add(OutPort);
            if (isGroupRoot)
            {
                //组根视觉区分:橙框 + 徽标(2026-09-16 组根批)
                style.borderTopWidth = 1.5f;
                style.borderBottomWidth = 1.5f;
                style.borderLeftWidth = 1.5f;
                style.borderRightWidth = 1.5f;
                style.borderTopColor = new Color(1f, 0.6f, 0.2f);
                style.borderBottomColor = new Color(1f, 0.6f, 0.2f);
                style.borderLeftColor = new Color(1f, 0.6f, 0.2f);
                style.borderRightColor = new Color(1f, 0.6f, 0.2f);
            }
            SetPosition(new Rect(nodeContext.position.x, nodeContext.position.y, 0, 0));
            RepaintElements(isGroupRoot);
            RefreshExpandedState();
            AttachMoveMenu();
        }

        /// <summary>节点右键菜单(2026-09-14 组树批):移入主图/移入组(带预检确认)/归组为新组。
        /// 组根不可移动/删除(与组共存),菜单只出禁用说明项。</summary>
        private void AttachMoveMenu()
        {
            this.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (RedDotGroupTree.IsGroupRoot(_graphData, _nodeContext))
                {
                    evt.menu.AppendAction("组根不可移动(与组页卡共存)", _ => { }, DropdownMenuAction.Status.Disabled);
                    evt.menu.AppendAction("组根不可删除(删组请用组树行右键)", _ => { }, DropdownMenuAction.Status.Disabled);
                    return;
                }
                if (_nodeContext.refGroupId > 0)
                {
                    return; // 引用节点走嵌套流,不走移入流
                }
                evt.menu.AppendAction("移入主图", _ => MoveToGroup(0));
                foreach (var g in _graphData.groups)
                {
                    var gid = g.id;
                    evt.menu.AppendAction("移入组/" + g.name + (g.dynamicMount ? "(动态)" : ""), _ => MoveToGroup(gid));
                }
                evt.menu.AppendAction("归组为新组…", _ => GroupToNewGroup());
            }));
        }

        private void MoveToGroup(int targetGroupId)
        {
            var pre = RedDotGroupTree.PrecheckMove(_graphData, _nodeContext.id, targetGroupId);
            if (pre.IsRefNode)
            {
                return;
            }
            var target = _graphData.GetGroup(targetGroupId);
            var targetName = targetGroupId == 0 ? "主图" : (target != null ? target.name : "?");
            if (pre.BreakParentIds.Count > 0 || pre.SubtreeContextIds.Count > 1)
            {
                if (!EditorUtility.DisplayDialog("移入组",
                    Utility.Text.Format("移入 [{0}]?断父边 {1} 条,随迁 {2} 节点。", targetName, pre.BreakParentIds.Count, pre.SubtreeContextIds.Count),
                    "继续", "取消"))
                {
                    return;
                }
            }
            RedDotGroupTree.MoveNodeToGroup(_graphData, _nodeContext.id, targetGroupId, pre.BreakParentIds);
            RepaintHost();
        }

        private void GroupToNewGroup()
        {
            var gv = this.GetFirstAncestorOfType<RedDotGraphView>();
            var candidates = new List<RedDotNodeContext>();
            if (gv != null)
            {
                foreach (var sel in gv.selection)
                {
                    var nv = sel as RedDotNodeView;
                    if (nv != null) candidates.Add(nv.GetContext());
                }
            }
            if (candidates.Count == 0) candidates.Add(_nodeContext);
            var g = RedDotGroupTree.GroupSelectionToNewGroup(_graphData, candidates);
            if (g == null)
            {
                EditorUtility.DisplayDialog("归组失败", "候选须恰一个无父节点(组根)", "好");
                return;
            }
            Log.Info("归组完成:{0}({1} 节点)", g.name, candidates.Count);
            RepaintHost();
        }

        private void RepaintHost()
        {
            var gv = this.GetFirstAncestorOfType<RedDotGraphView>();
            if (gv != null && gv.Window != null)
            {
                gv.Window.RepaintGraphPublic();
            }
        }

        private void RepaintElements(bool isGroupRoot = false)
        {
            var temp = new VisualElement();
            Add(temp);
            var root = temp;
            var rootStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PathUtil.GetEditorFilePath("Resources/Styles/Node.uss"));
            if (rootStyleSheet != null)
            {
                root.styleSheets.Add(rootStyleSheet); //兜底判空:路径解析失败时退化为默认样式而非中断构建
            }
            var labelStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PathUtil.GetEditorFilePath("Resources/Styles/Label.uss"));

            title = _graphData.keyEnumDict.GetNodeKey(_nodeContext.keyId);
            if (isGroupRoot)
            {
                var rootBadge = new Label("组根 · 本页卡起点");
                rootBadge.style.color = new StyleColor(new Color(1f, 0.6f, 0.2f));
                root.Add(rootBadge);
            }

            //节点注释:key 字典中文描述,标题下方加粗琥珀黄,与白色字段标签/橙色组根徽标区分(2026-09-16 反馈批)
            _descLabel = new Label(BuildDescText());
            _descLabel.style.color = new StyleColor(new Color(1f, 0.82f, 0.35f));
            _descLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _descLabel.style.fontSize = 11;
            _descLabel.style.whiteSpace = WhiteSpace.Normal;
            _descLabel.style.maxWidth = 240;
            _descLabel.style.marginLeft = 4;
            _descLabel.style.marginRight = 4;
            _descLabel.style.marginTop = 2;
            _descLabel.style.display = string.IsNullOrEmpty(_descLabel.text)
                ? DisplayStyle.None : DisplayStyle.Flex;
            root.Add(_descLabel);

            var label = new Label("红点key");
            if (labelStyleSheet != null) label.styleSheets.Add(labelStyleSheet);
            root.Add(label);

            //D6:平铺 PopupField → 分组搜索按钮(key 配置仍是本节点字段;引用节点无此配置见 RefNodeView)
            Button keyBtn = null;
            keyBtn = new Button(() => OpenKeySearch(keyBtn, false)) { text = title };
            root.Add(keyBtn);

            label = new Label("红点逻辑ID");
            if (labelStyleSheet != null) label.styleSheets.Add(labelStyleSheet);
            root.Add(label);

            Button funcBtn = null;
            funcBtn = new Button(() => OpenKeySearch(funcBtn, true)) { text = _graphData.externalIdEnumDict.GetNodeKey(_nodeContext.funcId) };
            root.Add(funcBtn);

            
            label = new Label("红点可存在的状态");
            if (labelStyleSheet != null) label.styleSheets.Add(labelStyleSheet);
            root.Add(label);
            
            var visualTreeNormal = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                PathUtil.GetEditorFilePath("Resources/Styles/NodeIconItemContainer.uxml"));
            VisualElement container = visualTreeNormal.Instantiate();
            root.Add(container);
            var iconsContainer = container.Q<VisualElement>("container");
            //六态勾选框按覆盖优先级(高→低)排列:满>红点>蓝点>红数字>蓝数字>新(见 RedDotStatusRules;
            //编辑器枚举成员名是 P0-2 历史命名,按值对应运行时位,见 RedDotNodeContextStatus 注释)
            var statusItem = new RedDotNodeStatusItem(RedDotNodeContextStatus.Full, _nodeContext, _graphData);
            iconsContainer.Add(statusItem);
            statusItem = new RedDotNodeStatusItem(RedDotNodeContextStatus.Tips, _nodeContext, _graphData);
            iconsContainer.Add(statusItem);

            visualTreeNormal =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    PathUtil.GetEditorFilePath("Resources/Styles/NodeIconItemContainer.uxml"));
            container = visualTreeNormal.Instantiate();
            root.Add(container);
            iconsContainer = container.Q<VisualElement>("container");
            statusItem = new RedDotNodeStatusItem(RedDotNodeContextStatus.BlueNoNumber, _nodeContext, _graphData);
            iconsContainer.Add(statusItem);
            statusItem = new RedDotNodeStatusItem(RedDotNodeContextStatus.Default, _nodeContext, _graphData);
            iconsContainer.Add(statusItem);

            visualTreeNormal =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                    PathUtil.GetEditorFilePath("Resources/Styles/NodeIconItemContainer.uxml"));
            container = visualTreeNormal.Instantiate();
            root.Add(container);
            iconsContainer = container.Q<VisualElement>("container");
            statusItem = new RedDotNodeStatusItem(RedDotNodeContextStatus.BlueNumber, _nodeContext, _graphData);
            iconsContainer.Add(statusItem);
            statusItem = new RedDotNodeStatusItem(RedDotNodeContextStatus.New, _nodeContext, _graphData);
            iconsContainer.Add(statusItem);
        }

        /// <summary>打开分组搜索选择器(D6)。选中后写回节点 keyId/funcId 并刷新按钮文案。
        /// K批6:provider 缓存在窗口层复用(原先每次点击 CreateInstance 且不销毁=编辑会话泄漏)。</summary>
        private void OpenKeySearch(Button source, bool isFunc)
        {
            var gv = this.GetFirstAncestorOfType<RedDotGraphView>();
            var window = gv != null ? gv.Window : null;
            if (window == null)
            {
                return;
            }
            if (window.KeySearchProvider == null)
            {
                window.KeySearchProvider = ScriptableObject.CreateInstance<RedDotKeySearchWindowProvider>();
            }
            var provider = window.KeySearchProvider;
            var dict = isFunc ? _graphData.externalIdEnumDict : _graphData.keyEnumDict;
            provider.Init(_graphData, dict, isFunc);
            provider.OnSelect = id =>
            {
                if (isFunc)
                {
                    _nodeContext.funcId = id;
                }
                else
                {
                    _nodeContext.keyId = id;
                    title = dict.GetNodeKey(id);
                    //key 切换同步刷新注释(2026-09-16 反馈批)
                    if (_descLabel != null)
                    {
                        _descLabel.text = BuildDescText();
                        _descLabel.style.display = string.IsNullOrEmpty(_descLabel.text)
                            ? DisplayStyle.None : DisplayStyle.Flex;
                    }
                }
                _graphData.Dirty();
                source.text = dict.GetNodeKey(id);
            };
            //UIElements 回调里 Event.current 为 null:按钮面板坐标 + 当前窗口原点换算屏幕坐标
            var fw = EditorWindow.focusedWindow;
            var screen = (fw != null ? (Vector2)fw.position.position : Vector2.zero) + source.worldBound.center;
            SearchWindow.Open(provider.CreateAdaptiveContext(screen), provider);
        }

        /// <summary>注释文本:key 字典中文描述;未配置(空/"null")返回空串隐藏。</summary>
        private string BuildDescText()
        {
            var desc = _graphData.keyEnumDict.GetDesc(_nodeContext.keyId);
            if (string.IsNullOrEmpty(desc) || desc == "null")
            {
                return "";
            }
            return desc;
        }

        public void NodeChanged()
        {
            _nodeContext.position = GetPosition().position;
        }

        public RedDotNodeContext GetContext()
        {
            return _nodeContext;
        }
    }
}