using System;
using System.Collections.Generic;
using GameLogic.RedDot.Core;
using UnityEditor;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Data
{
    /// <summary>
    /// 红点图的编辑器数据模型（序列化到 .reddotgraph 文件）。
    /// 包含节点上下文列表、key 字典、逻辑ID 字典，以及编辑器侧的图实例（用于环检测）。
    /// </summary>
    [Serializable]
    public class RedDotGraphData
    {
        private static int s_curCount = 0;

        public List<RedDotNodeContext> nodeContexts = new List<RedDotNodeContext>();
        public RedDotEnumDict keyEnumDict = new RedDotEnumDict();
        public RedDotEnumDict externalIdEnumDict = new RedDotEnumDict();

        private readonly EditorRedDotGraph _editorRedDotGraph = new EditorRedDotGraph();

        public RedDotNodeContext CreateNodeContext(Vector2 position)
        {
            var context = new RedDotNodeContext
            {
                id = ++s_curCount,
                position = position
            };
            nodeContexts.Add(context);
            _editorRedDotGraph.Insert(new RedDotVertex(context.id.ToString(), 0, -1));
            Dirty();
            return context;
        }

        public void ValidateGraph()
        {
            s_curCount = 0;
            keyEnumDict.ValidateData(this);
            externalIdEnumDict.ValidateData(this);

            foreach (var context in nodeContexts)
            {
                if (context.id > s_curCount) s_curCount = context.id;
                _editorRedDotGraph.Insert(new RedDotVertex(context.id.ToString(), 0, -1));
            }

            foreach (var context in nodeContexts)
            {
                foreach (var outId in context.outNodeIds)
                {
                    _editorRedDotGraph.InsertEdge(context.id.ToString(), outId.ToString());
                }
            }
        }

        public bool RemoveNodeContext(RedDotNodeContext nodeContext)
        {
            if (nodeContext == null) return false;
            if (_editorRedDotGraph.RemoveByKey(nodeContext.id.ToString()) != null)
            {
                Dirty();
                return nodeContexts.Remove(nodeContext);
            }
            return false;
        }

        public bool RemoveEdge(RedDotNodeContext inNodeContext, RedDotNodeContext outNodeContext)
        {
            if (_editorRedDotGraph.RemoveEdge(outNodeContext.id.ToString(), inNodeContext.id.ToString()) != -1)
            {
                outNodeContext.RemoveOutNode(inNodeContext.id);
                Dirty();
                return true;
            }
            return false;
        }

        public bool AddEdge(RedDotNodeContext inNodeContext, RedDotNodeContext outNodeContext)
        {
            var success = _editorRedDotGraph.InsertEdge(outNodeContext.id.ToString(), inNodeContext.id.ToString());
            if (success)
            {
                outNodeContext.AddOutNode(inNodeContext.id);
                Dirty();
            }
            return success;
        }

        private bool _dirty = false;
        public void Dirty() => _dirty = true;
        public bool IsDirty => _dirty;

        public bool SaveTo(string pathName)
        {
            CacheData();
            Util.FileUtilities.WriteRedDotGraphDataToDisk(pathName, this);
            AssetDatabase.Refresh();
            _dirty = false;
            return true;
        }

        public void CacheData()
        {
            keyEnumDict.PrepareExport();
            externalIdEnumDict.PrepareExport();
        }

        public bool IsDataCached() => keyEnumDict.IsCached && externalIdEnumDict.IsCached;

        /// <summary>
        /// 导出为运行时 JSON 配置（RedDotExportJson 格式）。
        /// </summary>
        public void ExportTo(string pathName)
        {
            var exportJson = new RedDotExportJson();
            var nodeContextDict = new Dictionary<int, RedDotNodeContext>();
            foreach (var nodeContext in nodeContexts)
            {
                nodeContextDict[nodeContext.id] = nodeContext;
                exportJson.vertexs.Add(new RedDotNodeJson
                {
                    key = keyEnumDict.GetKey(nodeContext.keyId),
                    redDotType = nodeContext.redDotType,
                    externalId = nodeContext.funcId
                });
            }

            foreach (var nodeContext in nodeContexts)
            {
                var edge = new RedDotEdgeJson
                {
                    key = keyEnumDict.GetKey(nodeContext.keyId)
                };
                foreach (var id in nodeContext.outNodeIds)
                {
                    edge.outKeys.Add(keyEnumDict.GetKey(nodeContextDict[id].keyId));
                }
                exportJson.edges.Add(edge);
            }
            Util.FileUtilities.WriteRedDotGraphDataToDisk(pathName, exportJson);
            AssetDatabase.Refresh();
        }
    }
}
