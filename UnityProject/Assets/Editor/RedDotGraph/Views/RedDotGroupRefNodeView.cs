#if UNITY_EDITOR
using RedDot.Editor.Data;
using TEngine;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace RedDot.Editor.Views
{
    /// <summary>
    /// 分组引用节点:主图/组内占位,指向 RedDotGroup。仅入边端口(D2:禁出边),
    /// 无 key/逻辑ID 配置(D3:key 配置落组根);双击下钻进组。
    /// </summary>
    public class RedDotGroupRefNodeView : Node
    {
        public Port InPort;
        /// <summary>层级显示出端口(仅主图骨架且该组有嵌套子组时创建;portType=幻影标记异型,不参与真实接线)。</summary>
        public Port DisplayOut;
        private readonly RedDotNodeContext _nodeContext;
        public System.Action<int> OnEnterGroup;

        public RedDotGroupRefNodeView(RedDotNodeContext nodeContext, RedDotGraphData graphData, bool showDisplayOut = false)
        {
            _nodeContext = nodeContext;
            var group = graphData.GetGroup(nodeContext.refGroupId);
            var groupName = group != null ? group.name : "<失效组" + nodeContext.refGroupId + ">";
            var nodeCount = group != null ? graphData.GetGroupNodes(nodeContext.refGroupId).Count : 0;
            title = "[组] " + groupName + (group != null && group.dynamicMount
                ? " (动态挂载)"
                : Utility.Text.Format(" ({0} 节点)", nodeCount));

            InPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(Port));
            InPort.portName = " ";
            InPort.portColor = new Color(0.3f, 0.7f, 1f);
            inputContainer.Add(InPort);
            if (showDisplayOut)
            {
                DisplayOut = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(RedDotGroupPhantomNodeView));
                DisplayOut.portName = " ";
                DisplayOut.portColor = new Color(0.65f, 0.65f, 0.65f);
                outputContainer.Add(DisplayOut);
            }

            var rootKey = graphData.keyEnumDict.GetNodeKey(GroupRootKeyId(graphData, nodeContext));
            var info = new Label("组根: " + rootKey);
            info.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            mainContainer.Add(info);
            if (group != null && group.dynamicMount)
            {
                var dyn = new Label("运行时 SetDynamicParent(组根, 宿主) 挂载");
                dyn.style.color = new StyleColor(new Color(1f, 0.6f, 0.2f));
                mainContainer.Add(dyn);
            }

            SetPosition(new Rect(nodeContext.position.x, nodeContext.position.y, 0, 0));
            RefreshExpandedState();

            RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount == 2 && OnEnterGroup != null)
                {
                    OnEnterGroup(_nodeContext.refGroupId);
                }
            });
        }

        private static int GroupRootKeyId(RedDotGraphData graphData, RedDotNodeContext nodeContext)
        {
            var g = graphData.GetGroup(nodeContext.refGroupId);
            if (g == null)
            {
                return 0;
            }
            var root = graphData.GetContext(g.rootNodeId);
            return root != null ? root.keyId : 0;
        }

        public RedDotNodeContext GetContext()
        {
            return _nodeContext;
        }

        public void NodeChanged()
        {
            _nodeContext.position = GetPosition().position;
        }
    }
}
#endif
