#if UNITY_EDITOR
using System.Collections.Generic;
using TEngine;

namespace RedDot.Editor.Data
{
    /// <summary>
    /// key/逻辑ID 配置完整性校验器(K批 2026-09-17,纯函数编辑器侧)。
    /// 与结构校验器(RedDotGroupValidator)分轴:结构轴只管组/边拓扑(U7/U8 依赖其 0 问题断言),
    /// key 轴管未设置/悬空/重复——这些状态在合成测试图里是常态,并入会打爆回归基线。
    /// 消费面:面包屑校验标签(RedDotGraphView)+ 导出门禁(RedDotWindow.ExportTo)。
    /// 消息格式"[Kn 说明]",节点级消息含"contextId:N"便于定位。
    /// K1 未设置key(keyId=0) / K2 悬空keyId(字典无此id) / K3 未设置逻辑ID(funcId=0,导出externalId=0
    /// 加载必炸;纯聚合应为-1) / K4 悬空funcId / K5 字典key字符串重复或为空 / K6 同key配置到多个节点。
    /// </summary>
    public static class RedDotKeyValidator
    {
        public static List<string> ValidateAll(RedDotGraphData data)
        {
            var problems = new List<string>();
            if (data == null)
            {
                return problems;
            }

            // 字典侧:空/重复 key 字符串(重复会在导出产重复顶点,运行时整份加载被拒)
            var keyIdOfStr = new Dictionary<string, int>();
            foreach (var id in data.keyEnumDict.GetIds())
            {
                var k = data.keyEnumDict.GetKey(id);
                if (string.IsNullOrEmpty(k))
                {
                    problems.Add(Utility.Text.Format("[K5 key字典条目 {0} 的key为空——导出将产空key顶点]", id));
                    continue;
                }
                int owner;
                if (keyIdOfStr.TryGetValue(k, out owner))
                {
                    problems.Add(Utility.Text.Format("[K5 key字符串重复:{0} 同时是条目 {1} 与 {2}——导出将产重复顶点]", k, owner, id));
                }
                else
                {
                    keyIdOfStr[k] = id;
                }
            }

            // 节点侧
            var keyIds = new HashSet<int>(data.keyEnumDict.GetIds());
            var funcIds = new HashSet<int>(data.externalIdEnumDict.GetIds());
            var keyIdOwner = new Dictionary<int, int>();
            foreach (var c in data.nodeContexts)
            {
                if (c.refGroupId > 0)
                {
                    continue; // 引用节点无 key/func 配置(D3)
                }
                if (c.keyId == 0)
                {
                    problems.Add(Utility.Text.Format("[K1 节点 contextId:{0} 未设置key]", c.id));
                }
                else if (!keyIds.Contains(c.keyId))
                {
                    problems.Add(Utility.Text.Format("[K2 节点 contextId:{0} 悬空keyId:{1}(字典无此条目)]", c.id, c.keyId));
                }
                else
                {
                    int owner;
                    if (keyIdOwner.TryGetValue(c.keyId, out owner))
                    {
                        problems.Add(Utility.Text.Format("[K6 同key配置到多个节点:contextId:{0} 与 {1} 共用 keyId:{2}——导出将产重复顶点]", owner, c.id, c.keyId));
                    }
                    else
                    {
                        keyIdOwner[c.keyId] = c.id;
                    }
                }

                if (c.funcId == 0)
                {
                    problems.Add(Utility.Text.Format("[K3 节点 contextId:{0} 未设置逻辑ID(=0,导出externalId=0加载必炸;仅有子节点的聚合应设为-1)]", c.id));
                }
                else if (c.funcId != -1 && !funcIds.Contains(c.funcId))
                {
                    problems.Add(Utility.Text.Format("[K4 节点 contextId:{0} 悬空逻辑ID:{1}(字典无此条目)]", c.id, c.funcId));
                }
            }
            return problems;
        }
    }
}
#endif
