using System;
using System.Collections.Generic;
using RedDot.Editor.Util;
using RedDot.Runtime.RedDot;
using TEngine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RedDot.Editor.Data
{
    [Serializable]
    public class RedDotGraphData
    {
        private static int s_curCount = 0;
        public List<RedDotGroup> groups = new List<RedDotGroup>();
        private static int s_groupCount = 0;
        public List<RedDotNodeContext> nodeContexts = new List<RedDotNodeContext>();
        public RedDotEnumDict keyEnumDict = new RedDotEnumDict();
        public RedDotEnumDict externalIdEnumDict = new RedDotEnumDict();
        private EditorRedDotGraph _editorRedDotGraph = new EditorRedDotGraph();
        
        public RedDotNodeContext CreateNodeContext(Vector2 position, int groupId = 0)
        {
            var context = new RedDotNodeContext();
            context.id = ++s_curCount;
            var key = context.id.ToString();
            context.position = position;
            context.groupId = groupId;
            nodeContexts.Add(context);
            var vertex = new RedDotVertex(key, 0, -1);
            _editorRedDotGraph.Insert(vertex);
            if (groupId > 0)
            {
                var g = GetGroup(groupId);
                if (g != null && g.rootNodeId == 0)
                {
                    g.rootNodeId = context.id; // 组内首个普通节点自动成为组根(空组豁免的解除点)
                }
            }
            Dirty();
            return context;
        }

        public void ValidateGraph()
        {
            s_curCount = 0;
            s_groupCount = 0;
            foreach (var g in groups)
            {
                if (g.id > s_groupCount)
                {
                    s_groupCount = g.id;
                }
            }

            keyEnumDict.ValidateData(this);
            externalIdEnumDict.ValidateData(this);

            //性能修复(2026-09-16 卡顿排查):ValidateGraph 在每次 RepaintGraph 都会执行,
            //内嵌图不清场时重复 Insert 全部命中"已存在"告警路径,170 节点实测单次白耗 ~106ms
            //(日志格式化+写盘),表现为每次导航/树操作固定卡顿。nodeContexts 是唯一真源,清场重建语义不变。
            _editorRedDotGraph.ClearGraph();
            foreach (var context in nodeContexts)
            {
                if (context.id > s_curCount)
                {
                    s_curCount = context.id;
                }
                var key = context.id.ToString();
                var vertex = new RedDotVertex(key, 0, -1);
                _editorRedDotGraph.Insert(vertex);
            }

            foreach (var context in nodeContexts)
            {
                foreach (var outId in context.outNodeIds)
                {
                    if (!_editorRedDotGraph.InsertEdge(context.id.ToString(), outId.ToString()))
                    {
                        Log.Warning("out:{0}, in:{1} insert edge fail", context.id, outId);
                    }
                }
            }
        }

    public bool RemoveNodeContext(RedDotNodeContext nodeContext)
    {
        if (nodeContext == null)
        {
            return false;
        }
        if (_editorRedDotGraph.RemoveByKey(nodeContext.id.ToString()) == null)
        {
            return false;
        }
        // 悬空清理:所有父节点 outNodeIds 摘除本节点(防导出"陈旧 outNodeId"路径,2026-09-14 组树批)
        for (var i = nodeContexts.Count - 1; i >= 0; i--)
        {
            nodeContexts[i].outNodeIds.Remove(nodeContext.id);
        }
        Dirty();
        return nodeContexts.Remove(nodeContext);
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

        public RedDotGroup CreateGroup(string name)
        {
            var g = new RedDotGroup { id = ++s_groupCount, name = string.IsNullOrEmpty(name) ? Utility.Text.Format("分组{0}", s_groupCount) : name };
            groups.Add(g);
            Dirty();
            return g;
        }

        public RedDotGroup GetGroup(int groupId)
        {
            foreach (var g in groups)
            {
                if (g.id == groupId)
                {
                    return g;
                }
            }
            return null;
        }

        /// <summary>删组:有引用节点(refGroupId 指向本组)时拒绝;成功后组内节点并入主图(groupId=0)。</summary>
        public bool RemoveGroup(int groupId)
        {
            foreach (var c in nodeContexts)
            {
                if (c.refGroupId == groupId)
                {
                    Log.Error("删除分组 {0} 失败:仍被引用节点 contextId:{1} 引用,先删引用节点", groupId, c.id);
                    return false;
                }
            }
            var removed = false;
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i].id == groupId)
                {
                    groups.RemoveAt(i);
                    removed = true;
                }
            }
            if (!removed)
            {
                return false;
            }
            foreach (var c in nodeContexts)
            {
                if (c.groupId == groupId)
                {
                    c.groupId = 0;
                }
            }
            Dirty();
            return true;
        }

        public void SetNodeGroup(int contextId, int groupId)
        {
            foreach (var c in nodeContexts)
            {
                if (c.id == contextId)
                {
                    c.groupId = groupId;
                    Dirty();
                    return;
                }
            }
            Log.Warning("SetNodeGroup 找不到节点 contextId:{0}", contextId);
        }

        public List<RedDotNodeContext> GetGroupNodes(int groupId)
        {
            var list = new List<RedDotNodeContext>();
            foreach (var c in nodeContexts)
            {
                if (c.groupId == groupId && c.refGroupId == 0)
                {
                    list.Add(c);
                }
            }
            return list;
        }

    /// <summary>创建引用节点:主图/组内占位,不占导出 key(D3)。位置=放置点画布坐标(全局,D4)。
    /// groupId=引用节点所在容器(嵌套数据面,2026-09-14 组树批)。</summary>
    public RedDotNodeContext CreateRefNode(int refGroupId, Vector2 position, int groupId = 0)
    {
        var context = new RedDotNodeContext();
        context.id = ++s_curCount;
        context.position = position;
        context.refGroupId = refGroupId;
        context.groupId = groupId;
        nodeContexts.Add(context);
        _editorRedDotGraph.Insert(new RedDotVertex(context.id.ToString(), 0, -1));
        Dirty();
        return context;
    }

        public RedDotNodeContext GetContext(int contextId)
        {
            foreach (var c in nodeContexts)
            {
                if (c.id == contextId)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>字典条目引用查询(K批 2026-09-17 删除预检):返回 keyId(或 funcId)等于该 id 的普通节点
        /// contextId 列表(引用节点无 key 配置,不计)。</summary>
        public List<int> GetDictEntryReferences(bool isFuncDict, int id)
        {
            var result = new List<int>();
            foreach (var c in nodeContexts)
            {
                if (c.refGroupId > 0)
                {
                    continue;
                }
                if (isFuncDict ? c.funcId == id : c.keyId == id)
                {
                    result.Add(c.id);
                }
            }
            return result;
        }

        /// <summary>字典条目组归属查询(K批3 2026-09-17 列表组列+筛选):引用该条目的普通节点所在组集合
        /// (去重,组序=节点序)。空集=未分组。key 正常态单组(跨组=K6 报错态);funcId 合法共用可多组。</summary>
        public List<int> GetDictEntryGroups(bool isFuncDict, int id)
        {
            var result = new List<int>();
            foreach (var c in nodeContexts)
            {
                if (c.refGroupId > 0)
                {
                    continue;
                }
                if (isFuncDict ? c.funcId == id : c.keyId == id)
                {
                    if (!result.Contains(c.groupId))
                    {
                        result.Add(c.groupId);
                    }
                }
            }
            return result;
        }

        /// <summary>D7 派生投影(不落数据):字典条目分组视图。
        /// key 字典:归属=引用它的节点的 groupId;func 字典:被多个组共用或未被引用→未分组。</summary>
        public List<RedDotKeyGroupSection> DeriveKeyGroups(RedDotEnumDict dict, bool isFuncDict)
        {
            var byKey = new Dictionary<int, int>();               // keyId → groupId
            var funcGroups = new Dictionary<int, HashSet<int>>(); // funcId → 组集合
            foreach (var c in nodeContexts)
            {
                if (!isFuncDict)
                {
                    byKey[c.keyId] = c.groupId;
                }
                else if (c.funcId > 0)
                {
                    HashSet<int> set;
                    if (!funcGroups.TryGetValue(c.funcId, out set))
                    {
                        set = new HashSet<int>();
                        funcGroups[c.funcId] = set;
                    }
                    set.Add(c.groupId);
                }
            }

            var result = new List<RedDotKeyGroupSection>();
            var used = new HashSet<int>();
            // key 字典:主图节 + 各组节
            AddSection(result, "主图", dict, id => !isFuncDict && byKey.ContainsKey(id) && byKey[id] == 0, used);
            foreach (var g in groups)
            {
                var gid = g.id;
                AddSection(result, g.name, dict, id => !isFuncDict && byKey.ContainsKey(id) && byKey[id] == gid, used);
            }
            // 未分组节:key=未被任何节点引用;func=未被引用或多组共用
            if (isFuncDict)
            {
                AddSection(result, "未分组", dict, id =>
                {
                    HashSet<int> s;
                    return !funcGroups.TryGetValue(id, out s) || s.Count != 1;
                }, used);
            }
            else
            {
                AddSection(result, "未分组", dict, id => !byKey.ContainsKey(id), used);
            }
            return result;
        }

        private static void AddSection(List<RedDotKeyGroupSection> result, string name, RedDotEnumDict dict, Func<int, bool> pred, HashSet<int> used)
        {
            var section = new RedDotKeyGroupSection { Name = name };
            foreach (var id in dict.GetIds())
            {
                if (pred(id) && used.Add(id))
                {
                    section.Ids.Add(id);
                }
            }
            if (section.Ids.Count > 0)
            {
                result.Add(section);
            }
        }

        /// <summary>视图可见集(D4:组=画布分区+过滤,坐标不变)。
        /// containerGroupId=0 主图:groupId==0 的节点,其中动态组(dynamicMount)的引用节点排除(显示在挂载区面板);
        /// 组视图:groupId==containerGroupId 的全部节点(含组内引用节点,D8 嵌套)。</summary>
        public List<RedDotNodeContext> GetVisibleContexts(int containerGroupId)
        {
            var list = new List<RedDotNodeContext>();
            foreach (var c in nodeContexts)
            {
                if (c.groupId != containerGroupId)
                {
                    continue;
                }
                if (containerGroupId == 0 && c.refGroupId > 0)
                {
                    var g = GetGroup(c.refGroupId);
                    if (g != null && g.dynamicMount)
                    {
                        continue;
                    }
                }
                list.Add(c);
            }
            return list;
        }

        private bool _dirty = false;
        public void Dirty()
        {
            _dirty = true;
        }

        public bool IsDirty => _dirty;

        public bool SaveTo(string pathName, bool interactive = true)
        {
            CacheData();
            //P7 审核 C-1(Phase 1 登记,用户拍板 Phase 7 首项):写盘失败保持脏态并返回 false,
            //让 PromptSaveIfDirtyOnQuit 能拦截退出,防"只读+用户取消"静默丢改动
            if (FileUtilities.WriteRedDotGraphDataToDisk(pathName, this, interactive) == null)
            {
                return false;
            }
            AssetDatabase.Refresh();
            _dirty = false;
            return true;
        }

        public void CacheData()
        {
            keyEnumDict.PrepareExport();
            externalIdEnumDict.PrepareExport();
        }

        public bool IsDataCached()
        {
            return keyEnumDict.IsCached && externalIdEnumDict.IsCached;
        }

        /// <summary>构建导出 JSON(不落盘,U2/U7/U8 验证器可断言)。分组展平规则(终版设计 §5.5 + 2026-09-14 组树批):
        /// 引用节点不产顶点;指向引用节点的父边替换为其目标组"组内森林根集合"——常规单树组=组根(与旧版一致),
        /// 目录组(无成员只含子组)=目录内未接线子组递归展开(多拼接,运行时多父 DAG 支持);
        /// 排序按目标"有效 redDotType"(引用节点取首个拼接根值)降序(P3-2 延续);dynamicMount 组根悬空天然成立。</summary>
        internal RedDotExportJson BuildExportJson()
        {
            var exportJson = new RedDotExportJson();
            var nodeContextDict = new Dictionary<int, RedDotNodeContext>();
            foreach (var nodeContext in nodeContexts)
            {
                nodeContextDict[nodeContext.id] = nodeContext;
            }
            // 引用节点 → 组内森林根集合(目录组多拼接;无可拼接根时报错跳过,校验器 V1/V5 已拦)
            var refToRoots = new Dictionary<int, List<RedDotNodeContext>>();
            foreach (var nodeContext in nodeContexts)
            {
                if (nodeContext.refGroupId > 0 && !refToRoots.ContainsKey(nodeContext.id))
                {
                    var roots = new List<RedDotNodeContext>();
                    ResolveForestRoots(nodeContext.refGroupId, roots, new HashSet<int>());
                    if (roots.Count == 0)
                    {
                        Log.Error("导出发现失效引用节点 contextId:{0}(组 {1} 缺失或无可拼接根),该引用边将被跳过", nodeContext.id, nodeContext.refGroupId);
                    }
                    refToRoots[nodeContext.id] = roots;
                }
            }

            foreach (var nodeContext in nodeContexts)
            {
                if (nodeContext.refGroupId > 0)
                {
                    continue; // 引用节点是纯编辑器占位,不产顶点(D3)
                }
                var node = new RedDotNodeJson();
                node.key = keyEnumDict.GetKey(nodeContext.keyId);
                //P7 审核 M2:空 key 检查上移,一处覆盖 root 与全部子节点
                if (string.IsNullOrEmpty(node.key))
                {
                    Log.Error("导出发现空 key 节点:contextId:{0},请在该节点的 Key 下拉中补配", nodeContext.id);
                }
                node.redDotType = nodeContext.redDotType;
                node.externalId = nodeContext.funcId;
                //K批8:key 中文描述随导出(生成器写进 RedDotKeys.cs 注释;miss=空串)
                node.desc = keyEnumDict.GetDesc(nodeContext.keyId);
                exportJson.vertexs.Add(node);
            }

            foreach (var nodeContext in nodeContexts)
            {
                if (nodeContext.refGroupId > 0)
                {
                    continue; // 引用节点无自身边;其作为"子"的边由各父展平时替换
                }
                var edge = new RedDotEdgeJson();
                edge.key = keyEnumDict.GetKey(nodeContext.keyId);
                //P3-2:按目标有效 redDotType 降序(引用节点取首个拼接根的 redDotType)
                var outIds = new List<int>(nodeContext.outNodeIds);
                outIds.Sort((a, b) => EffectiveRedDotType(nodeContextDict, refToRoots, b).CompareTo(EffectiveRedDotType(nodeContextDict, refToRoots, a)));
                foreach (var id in outIds)
                {
                    //P7(E8):陈旧 id 报错跳过
                    RedDotNodeContext target;
                    if (!nodeContextDict.TryGetValue(id, out target))
                    {
                        Log.Error("导出跳过陈旧 outNodeId:{0}(key:{1}),该节点已不存在", id, edge.key);
                        continue;
                    }
                    if (target.refGroupId > 0)
                    {
                        // 组树批:引用边按目标组森林根多拼接(常规单树组恰一根,行为同旧版)
                        List<RedDotNodeContext> roots;
                        if (!refToRoots.TryGetValue(target.id, out roots) || roots.Count == 0)
                        {
                            Log.Error("导出跳过失效引用边:{0} → 引用节点 {1}(目标组缺失或无根)", edge.key, target.id);
                            continue;
                        }
                        foreach (var root in roots)
                        {
                            edge.outKeys.Add(keyEnumDict.GetKey(root.keyId));
                        }
                        continue;
                    }
                    edge.outKeys.Add(keyEnumDict.GetKey(target.keyId));
                }
                //空边条目(叶子节点)运行时加载空转,不导出
                if (edge.outKeys.Count > 0)
                {
                    exportJson.edges.Add(edge);
                }
            }
            return exportJson;
        }

        /// <summary>解析组内森林根集合:成员树根(无父普通节点,0..1,V2 口径) + 目录内未接线子组递归展开(2026-09-14 组树批)。
        /// 子组引用节点若已接线(有父边)则由边拼接处理,不在此展开。</summary>
        private void ResolveForestRoots(int groupId, List<RedDotNodeContext> acc, HashSet<int> visited)
        {
            if (!visited.Add(groupId))
            {
                return;
            }
            if (GetGroup(groupId) == null)
            {
                return;
            }
            var hasParent = new HashSet<int>();
            foreach (var p in nodeContexts)
            {
                foreach (var outId in p.outNodeIds) hasParent.Add(outId);
            }
            foreach (var c in nodeContexts)
            {
                if (c.groupId != groupId || c.refGroupId > 0) continue;
                if (!hasParent.Contains(c.id)) acc.Add(c);
            }
            foreach (var c in nodeContexts)
            {
                if (c.groupId == groupId && c.refGroupId > 0 && !hasParent.Contains(c.id))
                {
                    ResolveForestRoots(c.refGroupId, acc, visited);
                }
            }
        }

        private static int EffectiveRedDotType(Dictionary<int, RedDotNodeContext> ctxOf, Dictionary<int, List<RedDotNodeContext>> refToRoots, int contextId)
        {
            RedDotNodeContext c;
            if (!ctxOf.TryGetValue(contextId, out c))
            {
                return 0;
            }
            if (c.refGroupId > 0)
            {
                List<RedDotNodeContext> roots;
                if (refToRoots.TryGetValue(c.id, out roots) && roots.Count > 0)
                {
                    return roots[0].redDotType;
                }
                return 0;
            }
            return c.redDotType;
        }

        /// <summary>导出落盘+同步再生成 key 常量表。prebuilt=窗口导出门禁已构建的 JSON(K批6 复用,免二次构建)。</summary>
        public void ExportTo(string pathName, RedDotExportJson prebuilt = null)
        {
            var exportJson = prebuilt ?? BuildExportJson();
            //运行时 JSON 紧凑序列化(随包下发,pretty 缩进实测占 46% 体积)
            FileUtilities.WriteRedDotGraphDataToDisk(pathName, exportJson, true, false);
#if UNITY_EDITOR
            //P0-5:导出同步再生成 key 常量表(业务编译期防错 + 加载期自检对账源)
            RedDot.Editor.RedDotKeysGenerator.GenerateFromExportJson(exportJson);
#endif
            AssetDatabase.Refresh();
        }
    }
}