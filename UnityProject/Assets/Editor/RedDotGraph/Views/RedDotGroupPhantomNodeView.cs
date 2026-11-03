#if UNITY_EDITOR
using System;
using RedDot.Editor.Data;
using TEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace RedDot.Editor.Views
{
    /// <summary>
    /// 幻影组框(2026-09-16 交互批):主图组树骨架投影,仅显示不落数据。
    /// 覆盖两类在大图上原本不可见/不相连的结构:顶层未接线组(无引用节点)与嵌套子组(引用节点在组视图内)。
    /// 双击下钻进组;位置默认派生布局,手工拖拽位置记录在 RedDotGroup.editorPos 并随资产保存(2026-09-16 反馈#2)。
    /// 2026-09-21 终态(三轮拍板):顶层未接线幻影(!isNested)带真实入端口——拖节点线到本框=生成引用节点+
    /// 真实数据边(画布 WirePhantom);嵌套幻影(isNested)带层级显示入端口,接收父框的蓝色层级显示边
    /// (容器从属可视化,不落数据);有嵌套子组者另带层级显示出端口。显示端口 portType=本类型异型,
    /// GetCompatiblePorts 拒绝真实拖线,不会与数据边混淆。
    /// </summary>
    public class RedDotGroupPhantomNodeView : Node
    {
        /// <summary>真实接线入端口(仅顶层未接线幻影创建;拖线即真数据,画布 WirePhantom 落地)。</summary>
        public Port InPort;
        /// <summary>层级显示出端口(有嵌套子组时创建;portType=本类型,异型不参与真实接线)。</summary>
        public Port DisplayOut;
        /// <summary>层级显示入端口(仅嵌套子组幻影创建,接收父框蓝色层级显示边)。</summary>
        public Port DisplayIn;
        public readonly int GroupId;
        /// <summary>是否嵌套子组幻影(骨架 depth&gt;0):无真实端口,主图不可直连。</summary>
        public readonly bool IsNested;
        public Action<int> OnEnterGroup;

        public RedDotGroupPhantomNodeView(RedDotGraphData graphData, RedDotGroup group, bool isNested, bool hasNestedChildren)
        {
            GroupId = group.id;
            IsNested = isNested;
            title = "[组] " + group.name + (group.dynamicMount
                ? " (动态挂载)"
                : Utility.Text.Format(" ({0} 节点)", graphData.GetGroupNodes(group.id).Count));

            if (!isNested)
            {
                InPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(Port));
                InPort.portName = " ";
                InPort.portColor = new Color(0.3f, 0.7f, 1f);
                inputContainer.Add(InPort);
            }
            else
            {
                DisplayIn = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(RedDotGroupPhantomNodeView));
                DisplayIn.portName = " ";
                DisplayIn.portColor = new Color(0.65f, 0.65f, 0.65f);
                inputContainer.Add(DisplayIn);
            }
            if (hasNestedChildren)
            {
                DisplayOut = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(RedDotGroupPhantomNodeView));
                DisplayOut.portName = " ";
                DisplayOut.portColor = new Color(0.65f, 0.65f, 0.65f);
                outputContainer.Add(DisplayOut);
            }

            var root = graphData.GetContext(group.rootNodeId);
            var info = new Label("组根: " + graphData.keyEnumDict.GetNodeKey(root != null ? root.keyId : 0));
            info.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            mainContainer.Add(info);
            var hint = new Label(group.dynamicMount
                ? "动态挂载 · 双击进入组"
                : isNested
                    ? "嵌套子组 · 接线请进父组视图 · 双击进入组"
                    : "未接线 · 从节点拖线到本框即接入主树 · 双击进入组");
            hint.style.color = new StyleColor(new Color(0.55f, 0.75f, 1f));
            mainContainer.Add(hint);
            tooltip = isNested
                ? "幻影框=组树骨架的派生显示,不落数据。嵌套子组的接线在父组视图内(双击进入)。"
                : "幻影框=组树骨架的派生显示。从节点拖线到本框=生成引用节点+真实数据边(导出必有);或在组树面板该组行右键生成引用节点。";

            //可选可拖看位置,但不可删(派生物,真实操作走数据层入口)
            capabilities = Capabilities.Selectable | Capabilities.Movable;

            RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount == 2 && OnEnterGroup != null)
                {
                    OnEnterGroup(GroupId);
                }
            });
        }
    }
}
#endif
