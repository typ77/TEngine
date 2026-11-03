#if UNITY_EDITOR
using System.Collections.Generic;
using RedDot.Editor.Data;
using TEngine;

namespace RedDot.Editor.Data
{
    /// <summary>
    /// 分组结构校验器(纯函数,编辑器侧)。返回问题列表,空=合法。
    /// 消息格式"[Vn 说明]"便于验证器与窗口提示精确匹配。
    /// V1 组根存在且属于本组 / V2 组内单树(仅组根无父) / V3 任意边两端同组(封装) /
    /// V4 引用节点禁出边 / V5 引用指向存在的组 / V6 单拷贝(每组至多一个引用节点) /
    /// V7 引用链无环(含嵌套) / V8 dynamicMount 组不得再被静态引用 / V9 非动态组引用节点应有父(警告级)。
    /// </summary>
    public static class RedDotGroupValidator
    {
        public static List<string> ValidateAll(RedDotGraphData data)
        {
            var problems = new List<string>();
            // 父表:childId → 父 contextId 列表(由 outNodeIds 反推)
            var parents = new Dictionary<int, List<int>>();
            foreach (var c in data.nodeContexts)
            {
                parents[c.id] = new List<int>();
            }
            foreach (var p in data.nodeContexts)
            {
                foreach (var outId in p.outNodeIds)
                {
                    List<int> list;
                    if (parents.TryGetValue(outId, out list) && !list.Contains(p.id))
                    {
                        list.Add(p.id);
                    }
                }
            }
            var ctxOf = new Dictionary<int, RedDotNodeContext>();
            foreach (var c in data.nodeContexts)
            {
                ctxOf[c.id] = c;
            }

            foreach (var g in data.groups)
            {
                var members = data.GetGroupNodes(g.id);
                // V1 组根(空/目录组豁免:无普通节点即无树无根可验——含纯目录组织组,2026-09-14 组树批)
                if (members.Count > 0)
                {
                    RedDotNodeContext root;
                    var rootOk = ctxOf.TryGetValue(g.rootNodeId, out root) && root.groupId == g.id && root.refGroupId == 0;
                    if (!rootOk)
                    {
                        problems.Add(Utility.Text.Format("[V1 组{0}({1}) 组根 contextId:{2} 不存在或不属于本组]", g.id, g.name, g.rootNodeId));
                    }
                }
                // V2 组内单树:组内非根节点都应有父(无成员自然跳过)
                foreach (var c in members)
                {
                    if (c.id == g.rootNodeId)
                    {
                        continue;
                    }
                    List<int> ps;
                    var hasParent = parents.TryGetValue(c.id, out ps) && ps.Count > 0;
                    if (!hasParent)
                    {
                        problems.Add(Utility.Text.Format("[V2 组{0}({1}) 多根:节点 contextId:{2} 无父(组内应只有组根无父)]", g.id, g.name, c.id));
                    }
                }
            }

            // V3 边两端同组(封装);V4 引用节点禁出边
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId > 0 && c.outNodeIds.Count > 0)
                {
                    problems.Add(Utility.Text.Format("[V4 引用节点 contextId:{0} 禁止出边(子树由组内连线表达)]", c.id));
                }
                foreach (var outId in c.outNodeIds)
                {
                    RedDotNodeContext child;
                    if (!ctxOf.TryGetValue(outId, out child))
                    {
                        continue; // 陈旧 id 由导出链 P7-E8 报,这里不重复
                    }
                    if (child.groupId != c.groupId)
                    {
                        problems.Add(Utility.Text.Format("[V3 跨组边:{0}(组{1}) → {2}(组{3})——组内只允许组根对外(经引用节点)]",
                            c.id, c.groupId, child.id, child.groupId));
                    }
                }
            }

            // V5/V6/V8 引用节点侧
            var refCountOf = new Dictionary<int, int>();
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId <= 0)
                {
                    continue;
                }
                int n;
                refCountOf[c.refGroupId] = refCountOf.TryGetValue(c.refGroupId, out n) ? n + 1 : 1;
                var target = data.GetGroup(c.refGroupId);
                if (target == null)
                {
                    problems.Add(Utility.Text.Format("[V5 引用节点 contextId:{0} 指向不存在的组 {1}]", c.id, c.refGroupId));
                }
                // V8:动态组不得被静态引用(其引用节点即静态挂载)
                if (target != null && target.dynamicMount)
                {
                    problems.Add(Utility.Text.Format("[V8 动态组 {0}({1}) 不得再挂静态引用节点(contextId:{2}),挂载走运行时 SetDynamicParent]", target.id, target.name, c.id));
                }
                // V9(警告级):主图引用节点无父=悬空未挂(2026-09-14 收窄:组内引用无父=嵌套数据面常态,不报)
                List<int> ps;
                var noParent = !parents.TryGetValue(c.id, out ps) || ps.Count == 0;
                if (noParent && c.groupId == 0 && target != null && !target.dynamicMount)
                {
                    problems.Add(Utility.Text.Format("[V9 引用节点 contextId:{0} 无父(悬空未挂,警告)]", c.id));
                }
            }
            foreach (var kv in refCountOf)
            {
                if (kv.Value > 1)
                {
                    problems.Add(Utility.Text.Format("[V6 组 {0} 被 {1} 个引用节点引用——单拷贝硬约束(D1)]", kv.Key, kv.Value));
                }
            }

            // V7 引用链环:组 → 引用它的节点 → 目标组,DFS
            var visiting = new HashSet<int>();
            var done = new HashSet<int>();
            foreach (var g in data.groups)
            {
                CheckRefCycle(data, g.id, visiting, done, problems);
            }
            return problems;
        }

        private static void CheckRefCycle(RedDotGraphData data, int groupId, HashSet<int> visiting, HashSet<int> done, List<string> problems)
        {
            if (done.Contains(groupId))
            {
                return;
            }
            if (!visiting.Add(groupId))
            {
                problems.Add(Utility.Text.Format("[V7 引用链成环:组 {0} 在自身引用链上(含经嵌套)]", groupId));
                return;
            }
            foreach (var c in data.nodeContexts)
            {
                // 沿"组内引用节点 → 其目标组"方向递归(主图 groupId=0 不是组,不参与环)
                if (c.refGroupId > 0 && c.groupId == groupId && data.GetGroup(c.refGroupId) != null)
                {
                    CheckRefCycle(data, c.refGroupId, visiting, done, problems);
                }
            }
            visiting.Remove(groupId);
            done.Add(groupId);
        }

        /// <summary>框选归组时自动推导组根:候选=集合中无父(全图口径)的节点;恰一个返回其 id,零或多个返回 -1。</summary>
        public static int DeriveRootContextId(RedDotGraphData data, List<RedDotNodeContext> candidates)
        {
            var inSet = new HashSet<int>();
            foreach (var c in candidates)
            {
                inSet.Add(c.id);
            }
            var hasParent = new HashSet<int>();
            foreach (var p in data.nodeContexts)
            {
                foreach (var outId in p.outNodeIds)
                {
                    if (inSet.Contains(outId))
                    {
                        hasParent.Add(outId);
                    }
                }
            }
            RedDotNodeContext root = null;
            var rootCount = 0;
            foreach (var c in candidates)
            {
                if (!hasParent.Contains(c.id))
                {
                    root = c;
                    rootCount++;
                }
            }
            return rootCount == 1 ? root.id : -1;
        }
    }
}
#endif
