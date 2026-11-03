#if UNITY_EDITOR
using System.Collections.Generic;
using RedDot.Editor.Data;
using TEngine;
using UnityEngine;

namespace RedDot.Editor.Data
{
    /// <summary>组树条目(派生视图,不落数据)。groupId>0=组行;contextId>0=节点行。</summary>
    public class RedDotTreeEntry
    {
        public int groupId;
        public int contextId;
        public string label;
        public bool isDynamic;
        public List<RedDotTreeEntry> children = new List<RedDotTreeEntry>();
    }

    /// <summary>组树派生模型。Main=主页卡(主图节点+顶层非动态组);Secondary=副页卡(顶层动态组)。</summary>
    public class RedDotTreeModel
    {
        public RedDotTreeEntry MainSection = new RedDotTreeEntry { label = "主页卡(主图)" };
        public RedDotTreeEntry SecondarySection = new RedDotTreeEntry { label = "副页卡(动态挂载)" };
    }

    /// <summary>
    /// 组树派生与操作纯函数层(2026-09-14 组树工作流,设计见 docs/reddot-editor-group-design/2026-09-14-组树工作流设计.md)。
    /// 派生规则:组Y的引用节点在容器X内(refGroupId=Y 且 groupId=X)⇒Y嵌套于X;顶层组按 dynamicMount 分主/副区。
    /// 全部只经 RedDotGraphData 公开 API 读写,UI 是薄壳。
    /// </summary>
    public static class RedDotGroupTree
    {
        public static RedDotTreeModel DeriveGroupTree(RedDotGraphData data)
        {
            EnsureRoots(data);
            var model = new RedDotTreeModel();
            // 组 → 引用节点所在容器(V6 双引用取 contextId 最小者——nodeContexts 为追加序,首见即最小)
            var containerOf = new Dictionary<int, int>();
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId > 0 && !containerOf.ContainsKey(c.refGroupId))
                {
                    containerOf[c.refGroupId] = c.groupId;
                }
            }

            var entryOf = new Dictionary<int, RedDotTreeEntry>();
            foreach (var g in data.groups)
            {
                entryOf[g.id] = new RedDotTreeEntry { groupId = g.id, label = g.name, isDynamic = g.dynamicMount };
            }
            foreach (var g in data.groups)
            {
                var entry = entryOf[g.id];
                int container;
                var nested = containerOf.TryGetValue(g.id, out container) && container > 0 && entryOf.ContainsKey(container);
                if (nested) entryOf[container].children.Add(entry);
                else if (g.dynamicMount) model.SecondarySection.children.Add(entry);
                else model.MainSection.children.Add(entry);
            }
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId > 0) continue; // 引用节点由其组行代表,不重复出行
                var row = new RedDotTreeEntry { contextId = c.id, label = NodeLabel(data, c) };
                if (c.groupId == 0) model.MainSection.children.Add(row);
                else if (entryOf.ContainsKey(c.groupId)) entryOf[c.groupId].children.Add(row);
                else Log.Warning("[组树] 节点 contextId:{0} 归属组 {1} 不存在,已跳过渲染", c.id, c.groupId);
            }
            return model;
        }

        /// <summary>组根自愈:rootNodeId 失效时重推导(组内无父普通节点恰一个则写回);空组跳过。导出 refToRoot 与引用节点展示依赖组根。</summary>
        public static void EnsureRoots(RedDotGraphData data)
        {
            var hasParent = new HashSet<int>();
            foreach (var p in data.nodeContexts)
            {
                foreach (var outId in p.outNodeIds) hasParent.Add(outId);
            }
            foreach (var g in data.groups)
            {
                var members = data.GetGroupNodes(g.id);
                if (members.Count == 0) continue;
                var root = data.GetContext(g.rootNodeId);
                if (root != null && root.groupId == g.id && root.refGroupId == 0) continue;
                RedDotNodeContext candidate = null;
                var count = 0;
                foreach (var m in members)
                {
                    if (!hasParent.Contains(m.id))
                    {
                        candidate = m;
                        count++;
                    }
                }
                if (count == 1)
                {
                    g.rootNodeId = candidate.id;
                    data.Dirty();
                }
            }
        }

        private static string NodeLabel(RedDotGraphData data, RedDotNodeContext c)
        {
            var desc = data.keyEnumDict.GetDesc(c.keyId);
            if (string.IsNullOrEmpty(desc) || desc == "null") desc = data.keyEnumDict.GetKey(c.keyId);
            return desc;
        }

        /// <summary>组的引用节点(V6 脏数据取 contextId 最小)。</summary>
        public static RedDotNodeContext FindOwnRefNode(RedDotGraphData data, int groupId)
        {
            RedDotNodeContext first = null;
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId == groupId && (first == null || c.id < first.id)) first = c;
            }
            return first;
        }

        /// <summary>contextId 的全部父 contextId(outNodeIds 反推)。</summary>
        public static List<int> GetParents(RedDotGraphData data, int contextId)
        {
            var acc = new List<int>();
            foreach (var p in data.nodeContexts)
            {
                if (p.outNodeIds.Contains(contextId) && !acc.Contains(p.id)) acc.Add(p.id);
            }
            return acc;
        }

        /// <summary>祖先链 [顶层组.., groupId](经引用节点容器关系;主图起点的顶层组链首即自身)。</summary>
        public static List<int> GetAncestorChain(RedDotGraphData data, int groupId)
        {
            var chain = new List<int>();
            var cur = groupId;
            var guard = 0;
            while (cur > 0 && guard++ < 64)
            {
                if (chain.Contains(cur)) break; // 脏数据防环
                chain.Insert(0, cur);
                var r = FindOwnRefNode(data, cur);
                cur = r != null ? r.groupId : 0;
            }
            return chain;
        }

        public static bool IsGroupInSubtree(RedDotGraphData data, int ancestorGroupId, int candidateGroupId)
        {
            var acc = new List<RedDotGroup>();
            CollectDescendants(data, ancestorGroupId, acc, new HashSet<int>());
            foreach (var d in acc)
            {
                if (d.id == candidateGroupId) return true;
            }
            return false;
        }

        /// <summary>直接子组(引用节点容器=groupId 的组)。</summary>
        public static List<RedDotGroup> ChildrenOf(RedDotGraphData data, int groupId)
        {
            var acc = new List<RedDotGroup>();
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId > 0 && c.groupId == groupId)
                {
                    var g = data.GetGroup(c.refGroupId);
                    if (g != null && !acc.Contains(g)) acc.Add(g);
                }
            }
            return acc;
        }

        private static void CollectDescendants(RedDotGraphData data, int groupId, List<RedDotGroup> acc, HashSet<int> visited)
        {
            if (!visited.Add(groupId)) return;
            foreach (var child in ChildrenOf(data, groupId))
            {
                if (!acc.Contains(child))
                {
                    acc.Add(child);
                    CollectDescendants(data, child.id, acc, visited);
                }
            }
        }

        /// <summary>组内是否含出向引用节点(纯目录组织组不算空)。</summary>
        public static bool HasOutRef(RedDotGraphData data, int groupId)
        {
            foreach (var c in data.nodeContexts)
            {
                if (c.groupId == groupId && c.refGroupId > 0) return true;
            }
            return false;
        }

    /// <summary>建子组:CreateGroup + 父容器内自动建指向新组的引用节点 + 新组自动建组根(2026-09-16:组根与组共存)。</summary>
    public static RedDotGroup CreateSubGroup(RedDotGraphData data, int parentGroupId, string name)
    {
        var g = data.CreateGroup(string.IsNullOrEmpty(name) ? "新建组" : name);
        data.CreateRefNode(g.id, RefSpawnPos(data, parentGroupId), parentGroupId);
        EnsureGroupRoot(data, g.id);
        return g;
    }

    /// <summary>
    /// 组根保障(2026-09-16 组根批):组必须有且恰有一个组根(本组起点/对外 key 载体)。
    /// rootNodeId 有效则复用;缺失/失效时在组视图默认位新建普通节点(CreateNodeContext 自动写 rootNodeId)。
    /// </summary>
    public static RedDotNodeContext EnsureGroupRoot(RedDotGraphData data, int groupId)
    {
        var g = data.GetGroup(groupId);
        if (g == null)
        {
            return null;
        }
        var root = data.GetContext(g.rootNodeId);
        if (root != null && root.groupId == g.id && root.refGroupId == 0)
        {
            return root;
        }
        g.rootNodeId = 0;
        return data.CreateNodeContext(new Vector2(80, 100), groupId);
    }

    /// <summary>是否组根:组根=组的起点,禁删除/禁移动/无前置连接口(视觉另加橙框+徽标)。</summary>
    public static bool IsGroupRoot(RedDotGraphData data, RedDotNodeContext ctx)
    {
        if (ctx == null || ctx.refGroupId > 0)
        {
            return false;
        }
        var g = data.GetGroup(ctx.groupId);
        return g != null && g.rootNodeId == ctx.id;
    }

        public static void RenameGroup(RedDotGraphData data, int groupId, string name)
        {
            var g = data.GetGroup(groupId);
            if (g == null || string.IsNullOrEmpty(name))
            {
                return;
            }
            g.name = name;
            data.Dirty();
        }

        /// <summary>
        /// 删组(目录条目语义):自动删自身引用节点及指向它的父边(V6 至多一个+V7 无环保证安全)。
        /// preserveChildren=true:含数据直接子组(普通节点或出向引用,纯目录不算空)转副页卡(dynamicMount=true,
        /// 其引用节点随本组内容一起消亡→自然成为顶层游离子树,结构原样保留);空直接子组连带删除。
        /// preserveChildren=false:全部后代组及其节点递归删除。
        /// </summary>
        public static bool DeleteGroupRecursive(RedDotGraphData data, int groupId, bool preserveChildren)
        {
            var g = data.GetGroup(groupId);
            if (g == null)
            {
                return false;
            }
            RemoveOwnRef(data, groupId);
            var directChildren = ChildrenOf(data, groupId);
            if (!preserveChildren)
            {
                var descendants = new List<RedDotGroup>();
                CollectDescendants(data, groupId, descendants, new HashSet<int>());
                foreach (var d in descendants) RemoveGroupContents(data, d.id);
                foreach (var d in descendants) data.groups.Remove(d);
            }
            else
            {
                foreach (var child in directChildren)
                {
                    var hasData = data.GetGroupNodes(child.id).Count > 0 || HasOutRef(data, child.id);
                    if (hasData)
                    {
                        child.dynamicMount = true;
                        continue;
                    }
                    RemoveGroupContents(data, child.id);
                    data.groups.Remove(child);
                }
            }
            RemoveGroupContents(data, groupId);
            data.groups.Remove(g);
            data.Dirty();
            return true;
        }

        /// <summary>主/副迁移。secondary=true:V8 要求删自身静态引用节点(有父边由 UI 预检确认后断);
        /// false:仅翻 flag,不自动建引用——接线走显式"生成引用节点"。</summary>
        public static bool SetGroupSecondary(RedDotGraphData data, int groupId, bool secondary)
        {
            var g = data.GetGroup(groupId);
            if (g == null)
            {
                return false;
            }
            if (secondary)
            {
                if (!g.dynamicMount) RemoveOwnRef(data, groupId);
                g.dynamicMount = true;
            }
            else
            {
                g.dynamicMount = false;
            }
            data.Dirty();
            return true;
        }

        /// <summary>
        /// 嵌套:把被拖组的引用节点移入目标容器;无引用则补建(嵌套数据面)。要求引用节点无父边(UI 预检先断)。
        /// 返回 false:组缺失/自身/目标在自身子树(防V7环)/动态组未先移回/引用节点仍带父边。
        /// </summary>
        public static bool NestGroupUnder(RedDotGraphData data, int draggedGroupId, int targetGroupId)
        {
            var dragged = data.GetGroup(draggedGroupId);
            var target = data.GetGroup(targetGroupId);
            if (dragged == null || target == null) return false;
            if (draggedGroupId == targetGroupId) return false;
            if (IsGroupInSubtree(data, draggedGroupId, targetGroupId)) return false;
            if (dragged.dynamicMount) return false;
            var r = FindOwnRefNode(data, draggedGroupId);
            if (r == null)
            {
                data.CreateRefNode(draggedGroupId, RefSpawnPos(data, targetGroupId), targetGroupId);
                return true;
            }
            if (GetParents(data, r.id).Count > 0) return false;
            r.groupId = targetGroupId;
            data.Dirty();
            return true;
        }

        /// <summary>删自身引用节点(目录条目)并断开指向它的父边。V6 脏数据只处理 contextId 最小者,余量由校验器报告。</summary>
        public static void RemoveOwnRef(RedDotGraphData data, int groupId)
        {
            var r = FindOwnRefNode(data, groupId);
            if (r == null)
            {
                return;
            }
            foreach (var pid in GetParents(data, r.id))
            {
                var p = data.GetContext(pid);
                if (p != null) data.RemoveEdge(r, p); // RemoveEdge(子,父)
            }
            data.RemoveNodeContext(r);
        }

        private static void RemoveGroupContents(RedDotGraphData data, int groupId)
        {
            for (var i = data.nodeContexts.Count - 1; i >= 0; i--)
            {
                if (data.nodeContexts[i].groupId == groupId) data.RemoveNodeContext(data.nodeContexts[i]);
            }
        }

        /// <summary>引用节点落点:容器最后一个成员右侧,空容器回画布默认点。</summary>
        public static Vector2 RefSpawnPos(RedDotGraphData data, int containerGroupId)
        {
            var members = data.GetGroupNodes(containerGroupId);
            if (members.Count > 0) return members[members.Count - 1].position + new Vector2(180, 0);
            return new Vector2(400, 400);
        }

        /// <summary>移入组预检结果。IsRefNode=引用节点禁走移入流(归嵌套流管);BreakParentIds=待断跨容器父边;SubtreeContextIds=随迁闭包(含自身)。</summary>
        public class MovePrecheck
        {
            public bool IsRefNode;
            public List<int> BreakParentIds = new List<int>();
            public List<int> SubtreeContextIds = new List<int>();
        }

        /// <summary>移入组预检(双向):出向闭包=随迁子树(引用节点同迁,其组随之换容器);入边跨容器=待断父边。</summary>
        public static MovePrecheck PrecheckMove(RedDotGraphData data, int contextId, int targetGroupId)
        {
            var r = new MovePrecheck();
            var ctx = data.GetContext(contextId);
            if (ctx == null || ctx.refGroupId > 0)
            {
                r.IsRefNode = true;
                return r;
            }
            var stack = new List<int> { ctx.id };
            r.SubtreeContextIds.Add(ctx.id);
            while (stack.Count > 0)
            {
                var id = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                var c = data.GetContext(id);
                if (c == null) continue;
                foreach (var outId in c.outNodeIds)
                {
                    if (r.SubtreeContextIds.Contains(outId)) continue;
                    if (data.GetContext(outId) == null) continue;
                    r.SubtreeContextIds.Add(outId);
                    stack.Add(outId);
                }
            }
            foreach (var pid in GetParents(data, contextId))
            {
                var p = data.GetContext(pid);
                if (p == null) continue;
                if (p.groupId == targetGroupId) continue;
                if (r.SubtreeContextIds.Contains(pid)) continue;
                r.BreakParentIds.Add(pid);
            }
            return r;
        }

        /// <summary>应用移入:断已确认父边后闭包整体换组,并对目标组做组根自愈。目标组已有树时 V2 提示多根=接线引导中间态。</summary>
        public static bool MoveNodeToGroup(RedDotGraphData data, int contextId, int targetGroupId, List<int> confirmedBreakParentIds)
        {
            var ctx = data.GetContext(contextId);
            if (ctx == null || ctx.refGroupId > 0) return false;
            if (targetGroupId != 0 && data.GetGroup(targetGroupId) == null) return false;
            var pre = PrecheckMove(data, contextId, targetGroupId);
            if (pre.IsRefNode) return false;
            if (confirmedBreakParentIds != null)
            {
                foreach (var pid in confirmedBreakParentIds)
                {
                    var p = data.GetContext(pid);
                    if (p != null) data.RemoveEdge(ctx, p);
                }
            }
            foreach (var id in pre.SubtreeContextIds) data.SetNodeGroup(id, targetGroupId);
            EnsureRoots(data);
            data.Dirty();
            return true;
        }

    /// <summary>框选归组建新组(原 GraphView.GroupSelection 抽取):组根推导恰一个无父,失败返回 null。
    /// 组根不可被归入别的组(与组共存,2026-09-16 组根批),候选中先行剔除。</summary>
    public static RedDotGroup GroupSelectionToNewGroup(RedDotGraphData data, List<RedDotNodeContext> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;
        candidates.RemoveAll(c => IsGroupRoot(data, c));
        if (candidates.Count == 0) return null;
        var rootId = RedDotGroupValidator.DeriveRootContextId(data, candidates);
            if (rootId == -1) return null;
            var rootCtx = data.GetContext(rootId);
            var g = data.CreateGroup(data.keyEnumDict.GetDesc(rootCtx.keyId));
            foreach (var c in candidates) data.SetNodeGroup(c.id, g.id);
            g.rootNodeId = rootId;
            return g;
        }
    }
}
#endif
