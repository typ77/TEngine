using System;
using System.Collections.Generic;
using RedDot.Editor.Config;
using RedDot.Editor.Data;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace RedDot.Editor.Views
{
    public class RedDotGraphView : GraphView
    {
        private RedDotWindow _editorWindow;
        private RedDotGraphData _graphData;
        //K批6:画布持有的瞬时 ScriptableObject(每次重建画布若 CreateInstance 新建且不销毁=编辑会话泄漏),
        //统一懒建+CleanupTransient 回收(窗口层 RepaintGraph 丢弃旧画布时调用)
        private RedDotSearchWindowProvider _menuWindowProvider;
        private RedDotGroupPickProvider _groupPickProvider;

        /// <summary>丢弃画布前回收瞬时 ScriptableObject。</summary>
        internal void CleanupTransient()
        {
            if (_menuWindowProvider != null)
            {
                UnityEngine.Object.DestroyImmediate(_menuWindowProvider);
                _menuWindowProvider = null;
            }
            if (_groupPickProvider != null)
            {
                UnityEngine.Object.DestroyImmediate(_groupPickProvider);
                _groupPickProvider = null;
            }
        }

        public RedDotGraphView(RedDotWindow editorWindow, RedDotGraphData graphData)
        {
            _graphData = graphData;
            _editorWindow = editorWindow;

            //按照父级的宽高全屏填充
            this.StretchToParentSize();
            //滚轮缩放
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            //graphview窗口内容的拖动
            this.AddManipulator(new ContentDragger());
            //选中Node移动功能
            this.AddManipulator(new SelectionDragger());
            //多个node框选功能
            this.AddManipulator(new RectangleSelector());
        

            var menuWindowProvider = _menuWindowProvider ??= ScriptableObject.CreateInstance<RedDotSearchWindowProvider>();
            menuWindowProvider.OnSelectEntryHandler = OnMenuSelectEntry;

            nodeCreationRequest += context =>
            {
                SearchWindow.Open(new SearchWindowContext(
                    context.screenMousePosition
                    ),
                    menuWindowProvider);
            };

            //画布右键直建组(2026-09-16 交互批):此前只能建 reddot 节点,组只能去树面板建
            this.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("创建组", _ => CreateGroupAtCurrentContainer());
            }));

            graphViewChanged += RedDotGraphViewChanged;

            //工具栏/面包屑由窗口层挂载(BuildToolbar/BuildBreadcrumb),不再浮在画布上(2026-09-14 布局修)
            //分组导航态(读窗口 NavStack)+ MiniMap(右下角,避开左侧组树面板)
            _currentGroupId = _editorWindow.NavStack.Count > 0 ? _editorWindow.NavStack[_editorWindow.NavStack.Count - 1] : 0;
            var miniMap = new MiniMap();
            Add(miniMap);
            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                var rect = contentRect;
                miniMap.SetPosition(new Rect(rect.width - 210, rect.height - 160, 200, 150));
            });

            InitElements();
        }

        /// <summary>顶部工具栏行(窗口层独立占行,不再叠画布;按钮自然宽度不均分拉伸)。</summary>
        internal IMGUIContainer BuildToolbar()
        {
            return new IMGUIContainer(DrawToolbar);
        }

        /// <summary>面包屑行(窗口层挂载,位于工具栏与画布区之间)。</summary>
        internal IMGUIContainer BuildBreadcrumb()
        {
            return new IMGUIContainer(DrawBreadcrumb);
        }

        internal void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                _editorWindow.CheckAndSave();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("导出红点配置", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                _editorWindow.ExportTo();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Key设置", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                KeySetting();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("逻辑ID设置", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                ExternalIdSetting();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("新建引用节点", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                CreateRefNodeForSelection();
            }
            if (_currentGroupId != 0 && GUILayout.Button("返回上级", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                _editorWindow.ExitGroup();
            }
            GUILayout.Space(6);
            //K批9:编辑器使用说明(定义/操作/校验导出/业务接入/注意事项)
            if (GUILayout.Button("使用说明", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                RedDotHelpWindow.ShowWindow();
            }
            GUILayout.EndHorizontal();
        }

        private void KeySetting()
        {
            if (_editorWindow.keySettingWindow != null)
            {
                _editorWindow.keySettingWindow.Focus();
                return;
            }
            var window = EditorWindow.CreateWindow<RedDotEnumDictWindow>(typeof(RedDotEnumDictWindow), typeof(RedDotWindow));
            window.Initialize(_graphData.keyEnumDict, RedDotEditorConfig.KEY_WINDOW_TITLE, _graphData);
            window.Focus();
            _editorWindow.keySettingWindow = window;
        }

        private void ExternalIdSetting()
        {
            if (_editorWindow.externalIdSettingWindow != null)
            {
                _editorWindow.externalIdSettingWindow.Focus();
                return;
            }
            var window = EditorWindow.CreateWindow<RedDotEnumDictWindow>(typeof(RedDotEnumDictWindow), typeof(RedDotWindow));
            window.Initialize(_graphData.externalIdEnumDict, RedDotEditorConfig.EXTERNAL_ID_WINDOW_TITLE, _graphData);
            window.Focus();
            _editorWindow.externalIdSettingWindow = window;
        }

        private void InitElements()
        {
            _views.Clear();
            var visible = new HashSet<int>();
            foreach (var context in _graphData.GetVisibleContexts(_currentGroupId))
            {
                if (context.refGroupId > 0)
                {
                    //主图骨架出端口:仅主视图且该组有嵌套子组时需要(挂子组幻影框的层级显示边)
                    var showSkeletonOut = _currentGroupId == 0
                        && RedDotGroupTree.ChildrenOf(_graphData, context.refGroupId).Count > 0;
                    var refNode = new RedDotGroupRefNodeView(context, _graphData, showSkeletonOut);
                    refNode.OnEnterGroup = EnterGroup;
                    AddElement(refNode);
                    _views[context.id] = refNode;
                    visible.Add(context.id);
                }
                else
                {
                    var node = new RedDotNodeView(context, _graphData);
                    AddElement(node);
                    _views[context.id] = node;
                    visible.Add(context.id);
                }
            }

            foreach (var context in _graphData.nodeContexts)
            {
                if (!visible.Contains(context.id))
                {
                    continue;
                }
                foreach (var outNodeId in context.outNodeIds)
                {
                    if (!visible.Contains(outNodeId))
                    {
                        continue; // 目标不在当前视图(如组内边在组视图画)——由所在容器侧遍历
                    }
                    var from = GetView(context.id);
                    var to = GetView(outNodeId);
                    if (from is RedDotNodeView fromN && to is RedDotNodeView toN)
                    {
                        if (toN.InPort == null)
                        {
                            //组根无前置口(脏数据容错:指向组根的陈旧边不绘制)
                            TEngine.Log.Warning("跳过指向组根的陈旧边:{0} → {1}(组根不可被连入)", context.id, outNodeId);
                            continue;
                        }
                        fromN.OutPort.ConnectTo(toN.InPort);
                        var edge = new Edge()
                        {
                            output = fromN.OutPort,
                            input = toN.InPort,
                            pickingMode = PickingMode.Position,
                        };
                        AddElement(edge);
                    }
                    else if (from is RedDotNodeView pn && to is RedDotGroupRefNodeView rn)
                    {
                        // 父(普通节点) → 子(引用节点):宿主侧静态挂载边
                        pn.OutPort.ConnectTo(rn.InPort);
                        var edge = new Edge()
                        {
                            output = pn.OutPort,
                            input = rn.InPort,
                            pickingMode = PickingMode.Position,
                        };
                        AddElement(edge);
                    }
                }
            }

            //组结构可见化(2026-09-16 交互批;2026-09-21 三轮拍板终态):主图=组树骨架投影,含蓝色层级显示边
            //(容器从属的可视化信息,异色细线+tooltip 与真实数据边可辨,不落数据不导出);组视图不画
            //组根→未接线子组显示边(真节点间的同色线曾被误当真连线,正是本次整改的原始事故),接线一律真实化
            if (_currentGroupId == 0)
            {
                BuildGroupSkeleton(visible);
            }
        }

        private readonly Dictionary<int, Node> _views = new Dictionary<int, Node>();
        private readonly Dictionary<int, Node> _skeletonViews = new Dictionary<int, Node>();
        private int _currentGroupId;

        // ---- 组树骨架投影(2026-09-16 交互批) ----

        private const float SkeletonNodeWidth = 260f;
        private const float SkeletonGapY = 170f;
        private const float SkeletonTopY = 60f;

        private static bool IsFinite(Vector2 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);
        }

        private sealed class SkeletonLayout
        {
            public float CenterX;
            public float RightX;
            public readonly Dictionary<int, Vector2> Centers = new Dictionary<int, Vector2>();
            public readonly Dictionary<int, int> Depths = new Dictionary<int, int>();
        }

        /// <summary>子树布局:叶子横向推进、父居中于子;Centers 只存 x,y 由 depth 决定。</summary>
        private static SkeletonLayout LayoutSkeletonNode(RedDotTreeEntry entry, int depth, float leftX)
        {
            var layout = new SkeletonLayout();
            layout.Depths[entry.groupId] = depth;
            var kids = new List<RedDotTreeEntry>();
            foreach (var c in entry.children)
            {
                if (c.groupId > 0)
                {
                    kids.Add(c);
                }
            }
            if (kids.Count == 0)
            {
                layout.CenterX = leftX + SkeletonNodeWidth / 2;
                layout.RightX = leftX + SkeletonNodeWidth;
            }
            else
            {
                var cursor = leftX;
                var first = 0f;
                var last = 0f;
                var firstSet = false;
                foreach (var k in kids)
                {
                    var r = LayoutSkeletonNode(k, depth + 1, cursor);
                    if (!firstSet)
                    {
                        first = r.CenterX;
                        firstSet = true;
                    }
                    last = r.CenterX;
                    cursor = r.RightX;
                    foreach (var kv in r.Centers)
                    {
                        layout.Centers[kv.Key] = kv.Value;
                    }
                    foreach (var kv in r.Depths)
                    {
                        layout.Depths[kv.Key] = kv.Value;
                    }
                }
                layout.CenterX = (first + last) / 2;
                layout.RightX = cursor;
            }
            layout.Centers[entry.groupId] = new Vector2(layout.CenterX, 0f);
            return layout;
        }

        /// <summary>主图组树骨架(用户反馈#3:大图上看不到首尾相连的组结构):顶层组锚点=已有真实引用节点(位置不动)
        /// 否则幻影框;嵌套子组一律幻影框。与组树面板同源(DeriveGroupTree)。
        /// 幻影手工位置落资产(RedDotGroup.editorPos,2026-09-16 反馈#2:移动要被记录),任意层级生效;
        /// 无记录者用派生布局,并整体跟随锚点偏移(拖父带子)。
        /// 2026-09-21 终态:层级显示边恢复(蓝色细线标识容器从属,非数据边),主图子级连线信息回归。</summary>
        private void BuildGroupSkeleton(HashSet<int> visible)
        {
            _skeletonViews.Clear();
            var model = RedDotGroupTree.DeriveGroupTree(_graphData);
            var subtreeCursor = 0f;
            foreach (var entry in model.MainSection.children)
            {
                if (entry.groupId <= 0)
                {
                    continue; // 主图普通节点行,非组
                }
                var group = _graphData.GetGroup(entry.groupId);
                if (group == null)
                {
                    continue;
                }
                var layout = LayoutSkeletonNode(entry, 0, subtreeCursor);
                var ownRef = RedDotGroupTree.FindOwnRefNode(_graphData, entry.groupId);
                var anchorIsReal = ownRef != null && visible.Contains(ownRef.id) && GetView(ownRef.id) != null;
                //虚拟布局下锚点框左上;偏移以此为基准,使锚点/后代落位可由同一 offset 平移
                var anchorVirtualTopLeft = new Vector2(layout.CenterX - SkeletonNodeWidth / 2, SkeletonTopY);
                Vector2 offset;
                if (anchorIsReal)
                {
                    //读 SetPosition 写入的内联 style(GetPosition 依赖 layout,构建早期恒 0);+W/2 对齐中心
                    var view = GetView(ownRef.id);
                    var vx = view.style.left.value.value;
                    var vy = view.style.top.value.value;
                    if (float.IsNaN(vx))
                    {
                        vx = 0f;
                    }
                    if (float.IsNaN(vy))
                    {
                        vy = 0f;
                    }
                    offset = new Vector2(vx + SkeletonNodeWidth / 2, vy) - anchorVirtualTopLeft;
                }
                else if (group.hasEditorPos)
                {
                    offset = new Vector2(group.editorPosX, group.editorPosY) - anchorVirtualTopLeft;
                }
                else
                {
                    offset = Vector2.zero;
                }
                PlaceSkeletonEntry(entry, layout, offset, anchorIsReal);
                ConnectSkeletonEdges(entry);
                subtreeCursor = layout.RightX;
            }
        }

        /// <summary>按布局递归落幻影框:组有手工位置(hasEditorPos)按其落位,否则用派生布局+锚点偏移;
        /// 顶层锚点若已有真实引用节点则挂真实视图进骨架(不另建幻影)。</summary>
        private void PlaceSkeletonEntry(RedDotTreeEntry entry, SkeletonLayout layout, Vector2 offset, bool anchorIsReal)
        {
            var group = _graphData.GetGroup(entry.groupId);
            if (group == null)
            {
                return;
            }
            var depth = layout.Depths[entry.groupId];
            if (anchorIsReal && depth == 0)
            {
                _skeletonViews[entry.groupId] = GetView(RedDotGroupTree.FindOwnRefNode(_graphData, group.id).id);
            }
            else
            {
                Vector2 topLeft;
                if (group.hasEditorPos)
                {
                    topLeft = new Vector2(group.editorPosX, group.editorPosY);
                }
                else
                {
                    topLeft = new Vector2(layout.Centers[entry.groupId].x - SkeletonNodeWidth / 2 + offset.x,
                        SkeletonTopY + depth * SkeletonGapY + offset.y);
                }
                _skeletonViews[entry.groupId] = CreatePhantom(group, topLeft, depth > 0, CountGroupKids(entry) > 0);
            }
            foreach (var c in entry.children)
            {
                if (c.groupId > 0)
                {
                    //anchorIsReal 只在 depth==0 锚点生效,后代恒为幻影
                    PlaceSkeletonEntry(c, layout, offset, anchorIsReal);
                }
            }
        }

        /// <summary>幻影框落位=SetPosition 的矩形左上(与拖拽记录的 editorPos 同一口径)。
        /// 2026-09-21 终态:顶层未接线幻影(!isNested)带真实入端口,拖节点线到框=WirePhantom 生成
        /// 引用节点+真实边;嵌套幻影带层级显示入端口、有嵌套子组者带层级显示出端口(ConnectSkeletonEdges 用)。</summary>
        private RedDotGroupPhantomNodeView CreatePhantom(RedDotGroup group, Vector2 topLeft, bool isNested, bool hasNestedChildren)
        {
            var view = new RedDotGroupPhantomNodeView(_graphData, group, isNested, hasNestedChildren) { OnEnterGroup = EnterGroup };
            view.SetPosition(new Rect(topLeft.x, topLeft.y, 0, 0));
            AddElement(view);
            return view;
        }

        /// <summary>骨架层级显示边:父框显示出端口 → 子框显示入端口。派生绘制(2026-09-21 恢复):
        /// 表达组容器从属信息,不落数据、删除后重绘即回;蓝色细线+tooltip 与真实数据边一眼可辨。</summary>
        private void ConnectSkeletonEdges(RedDotTreeEntry entry)
        {
            Node parentView;
            if (_skeletonViews.TryGetValue(entry.groupId, out parentView))
            {
                foreach (var c in entry.children)
                {
                    if (c.groupId <= 0)
                    {
                        continue;
                    }
                    Node childView;
                    if (_skeletonViews.TryGetValue(c.groupId, out childView))
                    {
                        AddDisplayEdge(DisplayOutOf(parentView), DisplayInOf(childView));
                    }
                }
            }
            foreach (var c in entry.children)
            {
                if (c.groupId > 0)
                {
                    ConnectSkeletonEdges(c);
                }
            }
        }

        private static int CountGroupKids(RedDotTreeEntry entry)
        {
            var n = 0;
            foreach (var c in entry.children)
            {
                if (c.groupId > 0)
                {
                    n++;
                }
            }
            return n;
        }

        private static Port DisplayOutOf(Node view)
        {
            var phantom = view as RedDotGroupPhantomNodeView;
            if (phantom != null)
            {
                return phantom.DisplayOut;
            }
            var refView = view as RedDotGroupRefNodeView;
            return refView != null ? refView.DisplayOut : null;
        }

        private static Port DisplayInOf(Node view)
        {
            var phantom = view as RedDotGroupPhantomNodeView;
            return phantom != null ? phantom.DisplayIn : null;
        }

        /// <summary>层级从属显示边(蓝色细线,与真实数据边一眼可辨):派生绘制,不落数据,
        /// 删除后重绘按骨架重建;导出连线只认组视图内的真实接线。</summary>
        private void AddDisplayEdge(Port output, Port input)
        {
            if (output == null || input == null)
            {
                return;
            }
            output.ConnectTo(input);
            var edge = new Edge()
            {
                output = output,
                input = input,
                pickingMode = PickingMode.Position,
            };
            edge.tooltip = "层级从属(派生显示,非数据接线)";
            AddElement(edge);
            if (edge.edgeControl != null)
            {
                var displayBlue = new Color(0.5f, 0.7f, 1f);
                edge.edgeControl.outputColor = displayBlue;
                edge.edgeControl.inputColor = displayBlue;
                edge.edgeControl.edgeWidth = 1;
            }
            edge.UpdateEdgeControl();
        }

        /// <summary>幻影框拖线落真数据(2026-09-21 拍板"拖线即真数据"):为该组生成引用节点(落当前容器=主图,
        /// 位置接管幻影框)并从源节点连真实边,随后整画布重绘让真实引用节点+边呈现。
        /// 仅顶层幻影可达此(嵌套幻影无端口);AddEdge 自带环检,失败回滚引用节点防悬空。</summary>
        private bool WirePhantom(RedDotGroupPhantomNodeView phantom, RedDotNodeContext sourceCtx)
        {
            var group = _graphData.GetGroup(phantom.GroupId);
            if (group == null)
            {
                TEngine.Log.Warning("幻影框接线失败:组 {0} 不存在", phantom.GroupId);
                return false;
            }
            var refCtx = _graphData.CreateRefNode(group.id, phantom.GetPosition().position, _currentGroupId);
            if (!_graphData.AddEdge(refCtx, sourceCtx))
            {
                _graphData.RemoveNodeContext(refCtx);
                TEngine.Log.Warning("幻影框接线失败:组[{0}] 边建立被拒(源节点:{1})", group.name, sourceCtx.id);
                return false;
            }
            _editorWindow.RepaintGraphPublic();
            return true;
        }

        /// <summary>当前容器直建组(用户反馈#4):主图=新建顶层组(骨架以幻影框呈现);组内=建子组(容器内生成引用节点)。
        /// 两条路径均自动建组根(2026-09-16 组根批:组根与组页卡共存)。</summary>
        private void CreateGroupAtCurrentContainer()
        {
            if (_currentGroupId == 0)
            {
                var g = _graphData.CreateGroup("新建组");
                RedDotGroupTree.EnsureGroupRoot(_graphData, g.id);
            }
            else
            {
                RedDotGroupTree.CreateSubGroup(_graphData, _currentGroupId, "新建组");
            }
            _editorWindow.RepaintGraphPublic();
        }

        /// <summary>宿主窗口访问(节点右键菜单回刷画布用)。</summary>
        internal RedDotWindow Window => _editorWindow;

        private Node GetView(int contextId)
        {
            Node v;
            return _views.TryGetValue(contextId, out v) ? v : null;
        }

        internal void DrawBreadcrumb()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("主图", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
            {
                _editorWindow.ExitToMain();
            }
            foreach (var gid in _editorWindow.NavStack)
            {
                var g = _graphData.GetGroup(gid);
                GUILayout.Label(">", EditorStyles.miniLabel);
                if (GUILayout.Button(g != null ? g.name : "<组" + gid + ">", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                {
                    _editorWindow.JumpToGroup(gid);
                }
            }
            //K批(2026-09-17):双轴校验——结构(RedDotGroupValidator V1~V9)+ key/逻辑ID完整性(RedDotKeyValidator K1~K6)。
            //key 轴独立成轴原因:未设/悬空在合法编辑中间态是常态,并入结构轴会打爆 U7/U8 的 0 问题回归基线
            var problems = RedDot.Editor.Data.RedDotGroupValidator.ValidateAll(_graphData);
            var keyProblems = RedDot.Editor.Data.RedDotKeyValidator.ValidateAll(_graphData);
            GUILayout.FlexibleSpace();
            string checkLabel;
            if (problems.Count == 0 && keyProblems.Count == 0)
            {
                checkLabel = "校验: 通过";
            }
            else
            {
                checkLabel = "校验:";
                if (problems.Count > 0)
                {
                    checkLabel += " 结构" + problems.Count;
                }
                if (keyProblems.Count > 0)
                {
                    checkLabel += " key" + keyProblems.Count;
                }
                checkLabel += " 项问题";
            }
            GUILayout.Label(checkLabel, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private void EnterGroup(int groupId)
        {
            _editorWindow.EnterGroup(groupId);
        }

        /// <summary>大纲定位:选中目标节点视图并把节点平移到视口中心(UpdateViewTransform 确定可用)。</summary>
        public void FocusContext(int contextId)
        {
            var v = GetView(contextId);
            if (v == null)
            {
                return; // 不在当前视图(在其他组)——大纲行属于哪个视图由调用侧语义决定,这里安静返回
            }
            ClearSelection();
            AddToSelection(v);
            var pos = v.GetPosition();
            var scale = viewTransform.scale;
            var center = localBound.center;
            UpdateViewTransform(new Vector3(center.x - (pos.x + pos.width / 2) * scale.x, center.y - (pos.y + pos.height / 2) * scale.y, 0), scale);
        }

        /// <summary>框选归组建新组入口已迁至:节点右键"归组为新组…"(RedDotNodeView)与 RedDotGroupTree.GroupSelectionToNewGroup。</summary>

        /// <summary>为当前容器新建引用节点:分组选择 SearchWindow(D6 同机制;单拷贝 D1 过滤已被引用的组)。</summary>
        private void CreateRefNodeForSelection()
        {
            if (_graphData.groups.Count == 0)
            {
                TEngine.Log.Warning("无分组可引用,先框选归组");
                return;
            }
            _groupPickProvider ??= ScriptableObject.CreateInstance<RedDotGroupPickProvider>();
            _groupPickProvider.Init(_graphData);
            _groupPickProvider.OnPick = gid =>
            {
                _graphData.CreateRefNode(gid, new Vector2(400, 400), _currentGroupId);
                _editorWindow.RepaintGraphPublic();
            };
            var picker = _groupPickProvider;
            var fw = UnityEditor.EditorWindow.focusedWindow;
            var screen = (fw != null ? (Vector2)fw.position.position : Vector2.zero) + new Vector2(400, 300);
            SearchWindow.Open(new SearchWindowContext(screen), picker);
        }

        /// <summary>组选择器(新建引用节点用):列出未被引用的组,userData=groupId。</summary>
        private class RedDotGroupPickProvider : ScriptableObject, ISearchWindowProvider
        {
            public System.Action<int> OnPick;
            private RedDotGraphData _data;

            public void Init(RedDotGraphData data)
            {
                _data = data;
            }

            public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
            {
                var entries = new List<SearchTreeEntry> { new SearchTreeGroupEntry(new GUIContent("选择分组")) };
                var used = new HashSet<int>();
                foreach (var c in _data.nodeContexts)
                {
                    if (c.refGroupId > 0)
                    {
                        used.Add(c.refGroupId);
                    }
                }
                foreach (var g in _data.groups)
                {
                    if (used.Contains(g.id))
                    {
                        continue; // 单拷贝 D1:已被引用的组不再列出
                    }
                    entries.Add(new SearchTreeEntry(new GUIContent(g.name + (g.dynamicMount ? "(动态)" : ""))) { level = 1, userData = g.id });
                }
                return entries;
            }

            public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
            {
                if (entry.userData is int)
                {
                    OnPick((int)entry.userData);
                    return true;
                }
                return false;
            }
        }
    
        private bool OnMenuSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            var type = searchTreeEntry.userData as Type;
            var windowRoot = _editorWindow.rootVisualElement;
            var windowMousePosition = windowRoot.ChangeCoordinatesTo(windowRoot.parent, context.screenMousePosition - _editorWindow.position.position);
            var graphMousePosition = contentViewContainer.WorldToLocal(windowMousePosition);
            
            Node node = Activator.CreateInstance(type, _graphData.CreateNodeContext(graphMousePosition, _currentGroupId), _graphData) as Node;
            AddElement(node);
            return true;
        }
    
        public override List<Port> GetCompatiblePorts(Port startAnchor, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            foreach (var port in ports.ToList())
            {
                if (startAnchor.node == port.node ||
                    startAnchor.direction == port.direction ||
                    startAnchor.portType != port.portType)
                {
                    continue;
                }

                compatiblePorts.Add(port);
            }
            return compatiblePorts;
        }

        /// <summary>边端点数据上下文:真实边两端为普通/引用节点;幻影/显示出端口返回 null(不落数据)。
        /// 顺带修旧患:节点→引用节点的挂载边此前强转 RedDotNodeView 得 null,创建/删除即 NRE。</summary>
        private static RedDotNodeContext CtxOf(Node node)
        {
            var nv = node as RedDotNodeView;
            if (nv != null)
            {
                return nv.GetContext();
            }
            var rv = node as RedDotGroupRefNodeView;
            return rv != null ? rv.GetContext() : null;
        }

        private GraphViewChange RedDotGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.movedElements != null)
            {
                var dataMoved = false;
                foreach (var node in graphViewChange.movedElements)
                {
                    var nv = node as RedDotNodeView;
                    if (nv != null)
                    {
                        nv.NodeChanged();
                        dataMoved = true;
                        continue;
                    }
                    var rv = node as RedDotGroupRefNodeView;
                    if (rv != null)
                    {
                        rv.NodeChanged();
                        dataMoved = true;
                        continue;
                    }
                    var ph = node as RedDotGroupPhantomNodeView;
                    if (ph != null)
                    {
                        //幻影手工位置落资产(RedDotGroup.editorPos,随保存持久);布局早期 GetPosition 可能为 NaN,拒收
                        var pos = ph.GetPosition().position;
                        if (IsFinite(pos))
                        {
                            var pg = _graphData.GetGroup(ph.GroupId);
                            if (pg != null)
                            {
                                pg.editorPosX = pos.x;
                                pg.editorPosY = pos.y;
                                pg.hasEditorPos = true;
                                dataMoved = true;
                            }
                        }
                    }
                }
                if (dataMoved)
                {
                    _graphData.Dirty();
                }
            }

            if (graphViewChange.edgesToCreate != null)
            {
                List<Edge> addFailEdges = new List<Edge>();
                List<Edge> phantomDoneEdges = new List<Edge>();
                foreach (var edge in graphViewChange.edgesToCreate)
                {
                    //幻影框拖线(2026-09-21 拍板"拖线即真数据"):顶层未接线组框=生成引用节点+真实边
                    var phantomIn = edge.input != null ? edge.input.node as RedDotGroupPhantomNodeView : null;
                    if (phantomIn != null)
                    {
                        var srcCtx = CtxOf(edge.output != null ? edge.output.node : null);
                        if (srcCtx != null && WirePhantom(phantomIn, srcCtx))
                        {
                            phantomDoneEdges.Add(edge); // 数据已落且画布已重建,瞬态边不再上屏
                            continue;
                        }
                        addFailEdges.Add(edge);
                        continue;
                    }
                    var inCtx = CtxOf(edge.input != null ? edge.input.node : null);
                    var outCtx = CtxOf(edge.output != null ? edge.output.node : null);
                    if (inCtx == null || outCtx == null)
                    {
                        addFailEdges.Add(edge); // 幻影/显示端口不参与数据接线
                        continue;
                    }
                    if (!_graphData.AddEdge(inCtx, outCtx))
                    {
                        addFailEdges.Add(edge);
                    }
                }

                foreach (var edge in phantomDoneEdges)
                {
                    graphViewChange.edgesToCreate.Remove(edge);
                }
                foreach (var edge in addFailEdges)
                {
                    graphViewChange.edgesToCreate.Remove(edge);
                }
            }

            if (graphViewChange.elementsToRemove != null)
            {
                //组根等被拦截元素:遍历中不可 Remove 同一列表,先收集后剔除
                var rejected = new List<GraphElement>();
                foreach (var element in graphViewChange.elementsToRemove)
                {
                    if (element is Edge)
                    {
                        var edge = (Edge)element;
                        var inCtx = CtxOf(edge.input != null ? edge.input.node : null);
                        var outCtx = CtxOf(edge.output != null ? edge.output.node : null);
                        if (inCtx == null || outCtx == null)
                        {
                            continue; // 显示边/幻影端口:无数据可删,重绘时骨架重建
                        }
                        //主图约束(2026-09-21):组挂载边(→引用节点)不可在主图断开,组结构删减回子树/组树面板
                        if (_currentGroupId == 0 && inCtx.refGroupId > 0)
                        {
                            var wiredGroup = _graphData.GetGroup(inCtx.refGroupId);
                            TEngine.Log.Warning("主图不可断开组挂载边:组[{0}]的接线请在所属组视图内删减(含双端确认流)",
                                wiredGroup != null ? wiredGroup.name : inCtx.refGroupId.ToString());
                            rejected.Add(element);
                            continue;
                        }
                        _graphData.RemoveEdge(inCtx, outCtx);
                    }
                    else if (element is RedDotNodeView)
                    {
                        var nodeCtx = ((RedDotNodeView)element).GetContext();
                        if (RedDotGroupTree.IsGroupRoot(_graphData, nodeCtx))
                        {
                            //组根与组页卡共存,不可单独删除(2026-09-16 组根批)
                            var owner = _graphData.GetGroup(nodeCtx.groupId);
                            TEngine.Log.Warning("组根不可删除:节点 {0} 是组[{1}]的根,删组请用组树行右键删除组",
                                nodeCtx.id, owner != null ? owner.name : nodeCtx.groupId.ToString());
                            rejected.Add(element);
                            continue;
                        }
                        _graphData.RemoveNodeContext(nodeCtx);
                    }
                    else if (element is RedDotGroupRefNodeView)
                    {
                        //主图约束(2026-09-21):引用节点不可在主图删除,组挂载管理走组树面板右键(删组/主副迁移)
                        if (_currentGroupId == 0)
                        {
                            var refCtx = ((RedDotGroupRefNodeView)element).GetContext();
                            var owner = _graphData.GetGroup(refCtx.refGroupId);
                            TEngine.Log.Warning("引用节点不可在主图删除:组[{0}]的挂载请用组树面板右键管理(删组/主副迁移)",
                                owner != null ? owner.name : refCtx.refGroupId.ToString());
                            rejected.Add(element);
                            continue;
                        }
                        _graphData.RemoveNodeContext(((RedDotGroupRefNodeView)element).GetContext());
                    }
                }
                foreach (var r in rejected)
                {
                    graphViewChange.elementsToRemove.Remove(r);
                }
            }
//            Debug.Log(graphViewChange.moveDelta);
            return graphViewChange;
        }
    }
}