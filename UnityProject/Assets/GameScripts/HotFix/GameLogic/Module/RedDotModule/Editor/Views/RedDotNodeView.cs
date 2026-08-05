using GameLogic.RedDot.Editor.Config;
using GameLogic.RedDot.Editor.Data;
using GameLogic.RedDot.Editor.Util;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic.RedDot.Editor.Views
{
    /// <summary>
    /// GraphView 中红点节点的可视化视图。配置 key、逻辑ID、可存在状态。
    /// </summary>
    public class RedDotNodeView : Node
    {
        public Port InPort;
        public Port OutPort;

        private readonly RedDotNodeContext _nodeContext;
        private readonly RedDotGraphData _graphData;

        public RedDotNodeView(RedDotNodeContext nodeContext, RedDotGraphData graphData)
        {
            _nodeContext = nodeContext;
            _graphData = graphData;

            InPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(Port));
            InPort.portName = " ";
            inputContainer.Add(InPort);

            OutPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(Port));
            OutPort.portName = "子节点";
            outputContainer.Add(OutPort);

            SetPosition(new Rect(nodeContext.position.x, nodeContext.position.y, 0, 0));
            RepaintElements();
            RefreshExpandedState();
        }

        private void RepaintElements()
        {
            // 可选加载 USS（缺失时降级为默认样式）
            var rootStyleSheet = SafeLoadStyleSheet(PathUtil.GetEditorFilePath("Resources/Styles/Node.uss"));
            if (rootStyleSheet != null) styleSheets.Add(rootStyleSheet);
            var labelStyleSheet = SafeLoadStyleSheet(PathUtil.GetEditorFilePath("Resources/Styles/Label.uss"));

            title = _graphData.keyEnumDict.GetNodeKey(_nodeContext.keyId);

            AddLabel(rootStyleSheet, labelStyleSheet, "红点key");

            var choices = _graphData.keyEnumDict.GetNodeKeys();
            var normalField = new PopupField<string>(choices, 0) { value = title };
            normalField.RegisterCallback<ChangeEvent<string>>(evt =>
            {
                _nodeContext.keyId = _graphData.keyEnumDict.GetIdByNodeKey(evt.newValue);
                _graphData.Dirty();
                title = evt.newValue;
            });
            AddField(normalField);

            AddLabel(rootStyleSheet, labelStyleSheet, "红点逻辑ID");

            var funcKeys = _graphData.externalIdEnumDict.GetNodeKeys();
            var funcField = new PopupField<string>(funcKeys, 0)
            {
                value = _graphData.externalIdEnumDict.GetNodeKey(_nodeContext.funcId)
            };
            funcField.RegisterCallback<ChangeEvent<string>>(evt =>
            {
                _nodeContext.funcId = _graphData.externalIdEnumDict.GetIdByNodeKey(evt.newValue);
                _graphData.Dirty();
            });
            AddField(funcField);

            AddLabel(rootStyleSheet, labelStyleSheet, "红点可存在的状态");

            var container = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            Add(container);
            container.Add(new RedDotNodeStatusItem(RedDotNodeContextStatus.Normal, _nodeContext, _graphData));
            container.Add(new RedDotNodeStatusItem(RedDotNodeContextStatus.New, _nodeContext, _graphData));
            container.Add(new RedDotNodeStatusItem(RedDotNodeContextStatus.Number, _nodeContext, _graphData));
            container.Add(new RedDotNodeStatusItem(RedDotNodeContextStatus.Full, _nodeContext, _graphData));
        }

        private void AddLabel(StyleSheet root, StyleSheet label, string text)
        {
            var lbl = new Label(text);
            if (label != null) lbl.styleSheets.Add(label);
            Add(lbl);
        }

        private void AddField(VisualElement field) => Add(field);

        private static StyleSheet SafeLoadStyleSheet(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
        }

        public void NodeChanged() => _nodeContext.position = GetPosition().position;

        public RedDotNodeContext GetContext() => _nodeContext;
    }
}
