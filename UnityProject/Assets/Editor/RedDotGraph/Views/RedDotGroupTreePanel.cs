#if UNITY_EDITOR
using System.Collections.Generic;
using RedDot.Editor.Data;
using TEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RedDot.Editor.Views
{
    /// <summary>
    /// 组树面板(2026-09-14 组树工作流,设计 docs/reddot-editor-group-design/2026-09-14-组树工作流设计.md):
    /// 主页卡/副页卡分区 + 过滤框 + 点击导航(祖先链重建 NavStack) + 校验徽标 + 右键组操作。
    /// 自绘缩进行而非 TreeView 组件:需要行级徽标/右键/拖拽全权控制,且规避 Tuanjie TreeView 组件可用性风险。
    /// </summary>
    public class RedDotGroupTreePanel : VisualElement
    {
        private readonly RedDotWindow _window;
        private readonly ScrollView _scroll = new ScrollView();
        private TextField _filter;
        private string _filterText = "";
        private Dictionary<int, List<string>> _problemsByGroup;

        /// <summary>选中态渲染锚点(每次 Rebuild 重建;选中值存窗口层跨导航存续)。</summary>
        private readonly Dictionary<int, VisualElement> _rowByGroupId = new Dictionary<int, VisualElement>();
        private readonly Dictionary<int, VisualElement> _rowByContextId = new Dictionary<int, VisualElement>();
        private VisualElement _mainHeader;
        private static readonly Color SelectedColor = new Color(0.24f, 0.39f, 0.60f);
        private static readonly Color SectionColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        public RedDotGroupTreePanel(RedDotWindow window, RedDotGraphData data)
        {
            _window = window;
            //首次打开跟随当前导航态(主图=区头选中;组视图=末位组行选中)
            if (_window.TreeSelectedGroupId == -1 && _window.TreeSelectedContextId == -1 && _window.NavStack.Count > 0)
            {
                _window.TreeSelectedGroupId = _window.NavStack[_window.NavStack.Count - 1];
            }
            //父容器已是画布区(工具栏/面包屑独立占行),top=0 即紧跟面包屑,不再硬编码避让(2026-09-14 布局修)
            style.width = 250;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.bottom = 0;
            style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f, 1f));
            style.borderRightWidth = 1;
            style.borderRightColor = new StyleColor(new Color(0f, 0f, 0f, 0.5f));

            _filter = new TextField("过滤");
            _filter.style.marginLeft = 4;
            _filter.style.marginRight = 4;
            _filter.style.marginTop = 4;
            _filter.style.marginBottom = 4;
            //过滤词存窗口层:面板随 RepaintGraph 频繁重建,避免树操作后过滤词丢失
            _filterText = _window.TreeFilterText ?? "";
            _filter.SetValueWithoutNotify(_filterText);
            _filter.RegisterValueChangedCallback(evt =>
            {
                _filterText = evt.newValue == null ? "" : evt.newValue.Trim();
                _window.TreeFilterText = _filterText;
                Rebuild();
            });
            Add(_filter);
            _scroll.style.flexGrow = 1;
            Add(_scroll);
            Rebuild();
        }

        public void Rebuild()
        {
            var data = _window.GraphData;
            var scrollOffset = _scroll.scrollOffset; //折叠/选中态切换重建后保持滚动位置
            _scroll.Clear();
            _rowByGroupId.Clear();
            _rowByContextId.Clear();
            _mainHeader = null;
            if (data == null)
            {
                return;
            }
            _problemsByGroup = BuildProblemIndex(data);
            var model = RedDotGroupTree.DeriveGroupTree(data);
            AddSection(model.MainSection, 0, true);
            AddSection(model.SecondarySection, 0, false);
            ApplySelectionHighlight();
            _scroll.scrollOffset = scrollOffset;
        }

        /// <summary>数据变更后的收口:重刷树+画布(2026-09-16 反馈:树面板操作后画布不同步)。
        /// RepaintGraph 会重建面板,本方法只应在变更路径末尾调用一次。</summary>
        private void RepaintAll()
        {
            _window.RepaintGraphPublic();
        }

        /// <summary>选中行/区头高亮(2026-09-16 交互批:点击树行有可见选中态)。</summary>
        private void ApplySelectionHighlight()
        {
            VisualElement el;
            if (_window.TreeSelectedContextId > 0
                && _rowByContextId.TryGetValue(_window.TreeSelectedContextId, out el))
            {
                el.style.backgroundColor = new StyleColor(SelectedColor);
            }
            else if (_window.TreeSelectedGroupId > 0
                && _rowByGroupId.TryGetValue(_window.TreeSelectedGroupId, out el))
            {
                el.style.backgroundColor = new StyleColor(SelectedColor);
            }
            else if (_window.TreeSelectedGroupId == 0 && _mainHeader != null)
            {
                _mainHeader.style.backgroundColor = new StyleColor(SelectedColor);
            }
        }

        private RedDotTreeEntry _dragPayload;

        private void AddSection(RedDotTreeEntry section, int depth, bool isMain)
        {
            var header = new Label(section.label) { style = { unityFontStyleAndWeight = FontStyle.Bold } };
            header.style.marginLeft = depth * 14;
            header.style.marginTop = 6;
            header.style.paddingLeft = 4;
            header.style.paddingRight = 4;
            header.style.paddingTop = 3;
            header.style.paddingBottom = 3;
            header.style.backgroundColor = new StyleColor(SectionColor);
            var targetGroupId = isMain ? 0 : -1;
            //点主页卡区头=切回主图并选中区头(2026-09-16 交互批)
            if (isMain)
            {
                _mainHeader = header;
                header.RegisterCallback<ClickEvent>(evt =>
                {
                    _window.TreeSelectedGroupId = 0;
                    _window.TreeSelectedContextId = -1;
                    _window.ExitToMain();
                });
            }
            header.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (isMain)
                {
                    evt.menu.AppendAction("新建空组(主页卡)", _ =>
                    {
                        var g = _window.GraphData.CreateGroup("新建组");
                        RedDotGroupTree.EnsureGroupRoot(_window.GraphData, g.id); //组根与组页卡共存
                        RepaintAll();
                    });
                }
                else
                {
                    evt.menu.AppendAction("新建副组(副页卡)", _ =>
                    {
                        var g = _window.GraphData.CreateGroup("新建副组");
                        RedDotGroupTree.SetGroupSecondary(_window.GraphData, g.id, true);
                        RedDotGroupTree.EnsureGroupRoot(_window.GraphData, g.id);
                        RepaintAll();
                    });
                }
            }));
            // 区头为投放目标:组→副页卡(脱离) / 组→主页卡(回归顶层)
            header.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var payload = DragAndDrop.GetGenericData("reddot_tree_entry") as RedDotTreeEntry;
                if (payload != null && payload.groupId > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    evt.StopPropagation();
                }
            });
            header.RegisterCallback<DragPerformEvent>(evt =>
            {
                var payload = DragAndDrop.GetGenericData("reddot_tree_entry") as RedDotTreeEntry;
                if (payload == null || payload.groupId <= 0)
                {
                    return;
                }
                DragAndDrop.AcceptDrag();
                var g = _window.GraphData.GetGroup(payload.groupId);
                if (g == null)
                {
                    return;
                }
                if (targetGroupId == -1)
                {
                    if (!g.dynamicMount) MoveSecondaryWithConfirm(_window.GraphData, g);
                }
                else
                {
                    MoveGroupToMainTop(_window.GraphData, g);
                }
                RepaintAll();
            });
            _scroll.Add(header);
            foreach (var child in section.children)
            {
                AddRow(child, depth + 1);
            }
        }

        private void AddRow(RedDotTreeEntry entry, int depth)
        {
            if (entry.groupId <= 0)
            {
                return; //menu tree 只显示组信息(2026-09-16 反馈);红点节点在画布/组视图查看
            }
            var data = _window.GraphData;
            {
                var g = data.GetGroup(entry.groupId);
                if (g == null)
                {
                    return;
                }
                var text = g.name + " (" + data.GetGroupNodes(g.id).Count + "节点)";
                if (g.dynamicMount) text += " ◉动态";
                var selfHit = _filterText.Length == 0 || text.Contains(_filterText);
                if (_filterText.Length > 0 && !selfHit && !SubtreeHit(entry))
                {
                    return;
                }
                var hasKids = CountSubGroups(entry) > 0;
                var expanded = hasKids && !_window.TreeCollapsedGroupIds.Contains(g.id);
                //行容器=折叠箭头 + 文本 + 徽标(2026-09-16 反馈:组可折叠/展开)
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginLeft = depth * 14;
                row.style.paddingLeft = 2;
                row.style.paddingRight = 2;
                row.style.minHeight = 20;
                if (hasKids)
                {
                    var arrow = new Label(expanded ? "▼" : "▶") { name = "arrow" };
                    arrow.style.width = 16;
                    arrow.style.paddingLeft = 2;
                    arrow.style.fontSize = 9;
                    arrow.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (evt.clickCount == 1)
                        {
                            if (!_window.TreeCollapsedGroupIds.Remove(g.id))
                            {
                                _window.TreeCollapsedGroupIds.Add(g.id);
                            }
                            Rebuild();
                            evt.StopPropagation();
                        }
                    });
                    row.Add(arrow);
                }
                else
                {
                    var spacer = new VisualElement();
                    spacer.style.width = 16;
                    row.Add(spacer);
                }
                var textLabel = new Label(text) { name = "text" };
                textLabel.style.flexShrink = 1;
                row.Add(textLabel);
                if (g.dynamicMount)
                {
                    row.tooltip = DynamicMountTip(data, g);
                }
                AppendProblemBadge(data, row, g.id);
                _rowByGroupId[g.id] = row;
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.clickCount == 1)
                    {
                        _window.TreeSelectedGroupId = g.id;
                        _window.TreeSelectedContextId = -1;
                        _window.TreeCollapsedGroupIds.Remove(g.id); //点行导航自动展开
                        _window.RebuildNavStackTo(g.id);
                    }
                });
                AttachGroupMenu(row, data, g);
                AttachDragSource(row, entry);
                AttachGroupDropTarget(row, data, g.id);
                _scroll.Add(row);
                if (expanded)
                {
                    foreach (var c in entry.children)
                    {
                        AddRow(c, depth + 1);
                    }
                }
            }
        }

        /// <summary>折叠箭头按"子组数"判定:只含节点行的组是叶组,不显示箭头(节点行已不在树渲染)。</summary>
        private static int CountSubGroups(RedDotTreeEntry entry)
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

        /// <summary>拖拽发起:左键按住位移后启原生 DragAndDrop,载荷=RedDotTreeEntry。</summary>
        private void AttachDragSource(VisualElement row, RedDotTreeEntry entry)
        {
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) _dragPayload = entry;
            });
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_dragPayload == null) return;
                var payload = _dragPayload;
                _dragPayload = null;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData("reddot_tree_entry", payload);
                DragAndDrop.StartDrag(payload.label ?? (payload.groupId > 0 ? "组" : "节点"));
            });
            row.RegisterCallback<PointerUpEvent>(evt => _dragPayload = null);
        }

        /// <summary>组行为投放目标:节点→组(移入) / 组→组(嵌套,防环守卫)。</summary>
        private void AttachGroupDropTarget(VisualElement row, RedDotGraphData data, int targetGroupId)
        {
            row.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                var payload = DragAndDrop.GetGenericData("reddot_tree_entry") as RedDotTreeEntry;
                if (payload != null && DropAllowed(data, payload, targetGroupId))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    evt.StopPropagation();
                }
            });
            row.RegisterCallback<DragPerformEvent>(evt =>
            {
                var payload = DragAndDrop.GetGenericData("reddot_tree_entry") as RedDotTreeEntry;
                if (payload == null || !DropAllowed(data, payload, targetGroupId))
                {
                    return;
                }
                DragAndDrop.AcceptDrag();
                DropEntry(data, payload, targetGroupId);
                RepaintAll();
            });
        }

        private bool DropAllowed(RedDotGraphData data, RedDotTreeEntry payload, int targetGroupId)
        {
            if (payload.groupId > 0)
            {
                if (targetGroupId == payload.groupId) return false;
                if (targetGroupId > 0 && RedDotGroupTree.IsGroupInSubtree(data, payload.groupId, targetGroupId)) return false;
                return true;
            }
            return targetGroupId >= 0; // 节点→组/主页卡头;副页卡头(-1)不收节点
        }

        private void DropEntry(RedDotGraphData data, RedDotTreeEntry payload, int targetGroupId)
        {
            if (payload.groupId > 0)
            {
                var g = data.GetGroup(payload.groupId);
                if (g == null) return;
                var target = data.GetGroup(targetGroupId);
                if (g.dynamicMount && target != null && !EditorUtility.DisplayDialog("嵌套组",
                    "副页卡组嵌套进主树将先移回主页卡(取消动态挂载),继续?", "继续", "取消"))
                {
                    return;
                }
                if (g.dynamicMount) RedDotGroupTree.SetGroupSecondary(data, g.id, false);
                var r = RedDotGroupTree.FindOwnRefNode(data, g.id);
                if (r != null && RedDotGroupTree.GetParents(data, r.id).Count > 0
                    && !EditorUtility.DisplayDialog("嵌套组", "该组引用节点已接线,嵌套将断开其父边,继续?", "继续", "取消"))
                {
                    return;
                }
                RedDotGroupTree.NestGroupUnder(data, g.id, targetGroupId);
            }
            else
            {
                var ctx = data.GetContext(payload.contextId);
                if (ctx == null || ctx.refGroupId > 0) return;
                var pre = RedDotGroupTree.PrecheckMove(data, ctx.id, targetGroupId);
                if (pre.IsRefNode) return;
                var target = data.GetGroup(targetGroupId);
                if (!EditorUtility.DisplayDialog("移入组",
                    Utility.Text.Format("移入 [{0}]?断父边 {1} 条,随迁 {2} 节点。", target != null ? target.name : "?", pre.BreakParentIds.Count, pre.SubtreeContextIds.Count),
                    "继续", "取消"))
                {
                    return;
                }
                RedDotGroupTree.MoveNodeToGroup(data, ctx.id, targetGroupId, pre.BreakParentIds);
            }
        }

        /// <summary>组拖到主页卡区头:动态组=移回(flag 翻转,不建引用);非动态嵌套组=其引用节点迁回主图顶层(有父边先确认断开)。</summary>
        private void MoveGroupToMainTop(RedDotGraphData data, RedDotGroup g)
        {
            if (g.dynamicMount)
            {
                RedDotGroupTree.SetGroupSecondary(data, g.id, false);
                return;
            }
            var r = RedDotGroupTree.FindOwnRefNode(data, g.id);
            if (r == null)
            {
                return; // 顶层未接线组,无操作
            }
            if (r.groupId == 0)
            {
                return; // 已在主图顶层
            }
            if (RedDotGroupTree.GetParents(data, r.id).Count > 0
                && !EditorUtility.DisplayDialog("移到主页卡顶层", "该组引用节点已接线,移动将断开其父边,继续?", "继续", "取消"))
            {
                return;
            }
            foreach (var pid in RedDotGroupTree.GetParents(data, r.id))
            {
                var p = data.GetContext(pid);
                if (p != null) data.RemoveEdge(r, p);
            }
            r.groupId = 0;
            data.Dirty();
        }

        private bool SubtreeHit(RedDotTreeEntry entry)
        {
            foreach (var c in entry.children)
            {
                if (c.groupId == 0 && c.label != null && c.label.Contains(_filterText)) return true;
                if (c.groupId > 0 && SubtreeHit(c)) return true;
            }
            return false;
        }

        private void AppendProblemBadge(RedDotGraphData data, VisualElement row, int groupId)
        {
            List<string> problems;
            if (_problemsByGroup == null || !_problemsByGroup.TryGetValue(groupId, out problems) || problems.Count == 0)
            {
                return;
            }
            var tip = "";
            foreach (var p in problems) tip += p + "\n";
            var badge = new Label("⚠" + problems.Count) { tooltip = tip };
            badge.style.color = new StyleColor(new Color(1f, 0.5f, 0.2f));
            row.Add(badge);
        }

        private static string DynamicMountTip(RedDotGraphData data, RedDotGroup g)
        {
            var rootKey = "<组根缺失>";
            var root = data.GetContext(g.rootNodeId);
            if (root != null) rootKey = data.keyEnumDict.GetKey(root.keyId);
            return "运行时 SetDynamicParent(" + rootKey + ", 宿主key) —— 服务器下发活动列表后动态接入";
        }

        /// <summary>校验问题索引:按组归集(组级消息解析组号;节点级按 contextId 归属组吸附)。每次 Rebuild 算一次。</summary>
        internal static Dictionary<int, List<string>> BuildProblemIndex(RedDotGraphData data)
        {
            var idx = new Dictionary<int, List<string>>();
            var ctxGroup = new Dictionary<int, int>();
            foreach (var c in data.nodeContexts) ctxGroup[c.id] = c.groupId;
            foreach (var p in RedDotGroupValidator.ValidateAll(data))
            {
                var gids = new List<int>();
                ParseGroupRefs(p, gids);
                var ci = p.IndexOf("contextId:");
                while (ci >= 0)
                {
                    var num = 0;
                    var j = ci + 10;
                    var ok = false;
                    while (j < p.Length && p[j] >= '0' && p[j] <= '9')
                    {
                        num = num * 10 + (p[j] - '0');
                        j++;
                        ok = true;
                    }
                    int gid;
                    if (ok && ctxGroup.TryGetValue(num, out gid)) gids.Add(gid);
                    ci = j < p.Length ? p.IndexOf("contextId:", j) : -1;
                }
                foreach (var gid in gids)
                {
                    List<string> list;
                    if (!idx.TryGetValue(gid, out list))
                    {
                        list = new List<string>();
                        idx[gid] = list;
                    }
                    list.Add(p);
                }
            }
            return idx;
        }

        /// <summary>解析消息中的组号:三种形态 "组N(" / "(组N)" / "组 N "(V6)。解析不出走节点级吸附。</summary>
        private static void ParseGroupRefs(string p, List<int> gids)
        {
            var i = p.IndexOf("组");
            while (i >= 0 && i + 1 < p.Length)
            {
                var j = i + 1;
                if (p[j] == ' ') j++;
                var num = 0;
                var ok = false;
                while (j < p.Length && p[j] >= '0' && p[j] <= '9')
                {
                    num = num * 10 + (p[j] - '0');
                    j++;
                    ok = true;
                }
                if (ok && j < p.Length && (p[j] == '(' || p[j] == ')' || p[j] == ' '))
                {
                    if (!gids.Contains(num)) gids.Add(num);
                }
                i = p.IndexOf("组", j);
            }
        }

        /// <summary>组行右键菜单:建子组/改名/主副迁移/生成引用节点/删组(三选一确认流)。</summary>
        private void AttachGroupMenu(VisualElement row, RedDotGraphData data, RedDotGroup g)
        {
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("新建子组", _ =>
                {
                    RedDotGroupTree.CreateSubGroup(data, g.id, "新建组");
                    RepaintAll();
                });
                evt.menu.AppendAction("重命名", _ => BeginRename(row, data, g));
                if (g.dynamicMount)
                {
                    evt.menu.AppendAction("移回主页卡", _ =>
                    {
                        RedDotGroupTree.SetGroupSecondary(data, g.id, false);
                        RepaintAll();
                    });
                }
                else
                {
                    evt.menu.AppendAction("移入副页卡", _ => MoveSecondaryWithConfirm(data, g));
                    if (RedDotGroupTree.FindOwnRefNode(data, g.id) == null)
                    {
                        evt.menu.AppendAction("生成引用节点(接入主树)", _ =>
                        {
                            data.CreateRefNode(g.id, RedDotGroupTree.RefSpawnPos(data, 0), 0);
                            RepaintAll();
                        });
                    }
                }
                evt.menu.AppendAction("删除组…", _ => DeleteWithDialog(data, g));
            }));
        }

        /// <summary>移入副页卡:引用节点已接线时先确认断父边(V8 要求动态组无静态引用)。</summary>
        private void MoveSecondaryWithConfirm(RedDotGraphData data, RedDotGroup g)
        {
            var r = RedDotGroupTree.FindOwnRefNode(data, g.id);
            if (r != null && RedDotGroupTree.GetParents(data, r.id).Count > 0
                && !EditorUtility.DisplayDialog("移入副页卡",
                    Utility.Text.Format("组[{0}]的引用节点已接线,移入将断开其父边,继续?", g.name), "继续", "取消"))
            {
                return;
            }
            RedDotGroupTree.SetGroupSecondary(data, g.id, true);
            RepaintAll();
        }

        /// <summary>删组:无含数据子组→直删(有父边先确认);含数据子组→三选一(全部删除/保留转副页卡/取消)。</summary>
        private void DeleteWithDialog(RedDotGraphData data, RedDotGroup g)
        {
            var directChildren = RedDotGroupTree.ChildrenOf(data, g.id);
            var dataChildren = new List<RedDotGroup>();
            foreach (var c in directChildren)
            {
                if (data.GetGroupNodes(c.id).Count > 0 || RedDotGroupTree.HasOutRef(data, c.id)) dataChildren.Add(c);
            }
            if (dataChildren.Count == 0)
            {
                var r = RedDotGroupTree.FindOwnRefNode(data, g.id);
                if (r != null && RedDotGroupTree.GetParents(data, r.id).Count > 0
                    && !EditorUtility.DisplayDialog("删除组",
                        Utility.Text.Format("组[{0}]已接线,删除将断开其父边,继续?", g.name), "继续", "取消"))
                {
                    return;
                }
                RedDotGroupTree.DeleteGroupRecursive(data, g.id, false);
            }
            else
            {
                var names = "";
                foreach (var c in dataChildren) names += c.name + "、";
                var option = EditorUtility.DisplayDialogComplex("删除组",
                    Utility.Text.Format("组[{0}]下有含数据的子组:{1}\n如何处理?", g.name, names),
                    "全部删除", "取消", "保留子组(转副页卡)");
                if (option == 1)
                {
                    return;
                }
                RedDotGroupTree.DeleteGroupRecursive(data, g.id, option == 0 ? false : true);
            }
            _window.CleanNavStackAfterDelete(g.id);
            RepaintAll();
        }

        /// <summary>行内改名:文本 Label 换 TextField,Enter/失焦提交,Escape 取消。</summary>
        private void BeginRename(VisualElement row, RedDotGraphData data, RedDotGroup g)
        {
            var textLabel = row.Q<Label>("text"); //行首是折叠箭头,文本 Label 按名取
            if (textLabel == null)
            {
                return;
            }
            var field = new TextField("") { value = g.name };
            field.style.width = 200;
            var committed = false;
            System.Action commit = () =>
            {
                if (committed)
                {
                    return;
                }
                committed = true;
                RedDotGroupTree.RenameGroup(data, g.id, field.value);
                RepaintAll();
            };
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    committed = true;
                    RepaintAll();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    commit();
                    evt.StopPropagation();
                }
            });
            field.RegisterCallback<BlurEvent>(evt => commit());
            textLabel.style.display = DisplayStyle.None;
            row.Add(field);
            field.Focus();
        }
    }
}
#endif
