using System;
using System.Collections.Generic;
using GameLogic.RedDot.Editor.Config;
using GameLogic.RedDot.Editor.Data;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameLogic.RedDot.Editor.Views
{
    /// <summary>
    /// 红点图的 GraphView 主视图。提供节点创建、连线、删除、保存、导出、Key/逻辑ID 设置。
    /// </summary>
    public class RedDotGraphView : GraphView
    {
        private readonly RedDotWindow _editorWindow;
        private readonly RedDotGraphData _graphData;

        public RedDotGraphView(RedDotWindow editorWindow, RedDotGraphData graphData)
        {
            _graphData = graphData;
            _editorWindow = editorWindow;

            this.StretchToParentSize();
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var menuWindowProvider = ScriptableObject.CreateInstance<RedDotSearchWindowProvider>();
            menuWindowProvider.OnSelectEntryHandler = OnMenuSelectEntry;

            nodeCreationRequest += context =>
            {
                SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), menuWindowProvider);
            };

            graphViewChanged += RedDotGraphViewChanged;

            var toolbar = new IMGUIContainer(() =>
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("保存", EditorStyles.toolbarButton))
                {
                    _editorWindow.CheckAndSave();
                }
                GUILayout.Space(6);
                if (GUILayout.Button("导出红点配置", EditorStyles.toolbarButton))
                {
                    _editorWindow.ExportTo();
                }
                GUILayout.Space(6);
                if (GUILayout.Button("Key设置", EditorStyles.toolbarButton))
                {
                    KeySetting();
                }
                GUILayout.Space(6);
                if (GUILayout.Button("逻辑ID设置", EditorStyles.toolbarButton))
                {
                    ExternalIdSetting();
                }
                GUILayout.EndHorizontal();
            });
            Add(toolbar);

            InitElements();
        }

        private void KeySetting()
        {
            if (_editorWindow.keySettingWindow != null)
            {
                _editorWindow.keySettingWindow.Focus();
                return;
            }
            var window = EditorWindow.CreateWindow<RedDotEnumDictWindow>(typeof(RedDotEnumDictWindow), typeof(RedDotWindow));
            window.Initialize(_graphData.keyEnumDict, RedDotEditorConfig.KEY_WINDOW_TITLE);
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
            window.Initialize(_graphData.externalIdEnumDict, RedDotEditorConfig.EXTERNAL_ID_WINDOW_TITLE);
            window.Focus();
            _editorWindow.externalIdSettingWindow = window;
        }

        private void InitElements()
        {
            var views = new Dictionary<int, RedDotNodeView>();
            foreach (var context in _graphData.nodeContexts)
            {
                var node = new RedDotNodeView(context, _graphData);
                AddElement(node);
                views[context.id] = node;
            }

            foreach (var context in _graphData.nodeContexts)
            {
                var outputNode = views[context.id];
                foreach (var outNodeId in context.outNodeIds)
                {
                    if (!views.TryGetValue(outNodeId, out var inputNode)) continue;
                    outputNode.OutPort.ConnectTo(inputNode.InPort);
                    var edge = new Edge()
                    {
                        output = outputNode.OutPort,
                        input = inputNode.InPort,
                        pickingMode = PickingMode.Position,
                    };
                    AddElement(edge);
                }
            }
        }

        private bool OnMenuSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            var type = searchTreeEntry.userData as Type;
            var windowRoot = _editorWindow.rootVisualElement;
            var windowMousePosition = windowRoot.ChangeCoordinatesTo(windowRoot.parent, context.screenMousePosition - _editorWindow.position.position);
            var graphMousePosition = contentViewContainer.WorldToLocal(windowMousePosition);

            if (type == typeof(RedDotNodeView))
            {
                var node = new RedDotNodeView(_graphData.CreateNodeContext(graphMousePosition), _graphData);
                AddElement(node);
                return true;
            }
            return false;
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

        private GraphViewChange RedDotGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.movedElements != null)
            {
                foreach (var node in graphViewChange.movedElements)
                {
                    (node as RedDotNodeView)?.NodeChanged();
                    _graphData.Dirty();
                }
            }

            if (graphViewChange.edgesToCreate != null)
            {
                var addFailEdges = new List<Edge>();
                foreach (var edge in graphViewChange.edgesToCreate)
                {
                    var inNode = edge.input.node as RedDotNodeView;
                    var outNode = edge.output.node as RedDotNodeView;
                    if (inNode == null || outNode == null) continue;
                    if (!_graphData.AddEdge(inNode.GetContext(), outNode.GetContext()))
                    {
                        addFailEdges.Add(edge);
                    }
                }
                foreach (var edge in addFailEdges)
                {
                    graphViewChange.edgesToCreate.Remove(edge);
                }
            }

            if (graphViewChange.elementsToRemove != null)
            {
                foreach (var element in graphViewChange.elementsToRemove)
                {
                    if (element is Edge edge)
                    {
                        var inNode = edge.input.node as RedDotNodeView;
                        var outNode = edge.output.node as RedDotNodeView;
                        if (inNode != null && outNode != null)
                        {
                            _graphData.RemoveEdge(inNode.GetContext(), outNode.GetContext());
                        }
                    }
                    else if (element is RedDotNodeView nodeView)
                    {
                        _graphData.RemoveNodeContext(nodeView.GetContext());
                    }
                }
            }
            return graphViewChange;
        }
    }
}
