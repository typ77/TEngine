#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using RedDot.Runtime.RedDot;
using TEngine;
using UnityEditor;
using UnityEngine;

namespace RedDot.Editor.Data
{
    /// <summary>树形 key 下拉节点:Children != null = 文件夹(不可选);Children == null = 叶子(携带 Key 可选)。</summary>
    public class RedDotKeyTreeEntry
    {
        /// <summary>菜单显示文本(组名 / 中文desc或key / 兜底key)。</summary>
        public string Label;

        /// <summary>叶子携带的语义 key;文件夹为 null。</summary>
        public string Key;

        /// <summary>externalId(来自 RedDotKeys.All;图上未导出 key 记 -1)。</summary>
        public int ExternalId;

        /// <summary>key 不在 RedDotKeys.All(图上已建未导出)。</summary>
        public bool Unexported;

        public List<RedDotKeyTreeEntry> Children;
    }

    /// <summary>构建产物:树 + key 元数据(供 Drawer 收起态显示与未知 key 保值判定)。</summary>
    public class RedDotKeyTreeBuildResult
    {
        /// <summary>虚拟根(Children 为空列表=图上无可选 key,仍可回落平铺前给出纯兜底)。</summary>
        public RedDotKeyTreeEntry Root;

        /// <summary>全部可选 key → externalId(RedDotKeys.All ∪ 图上 key)。</summary>
        public readonly Dictionary<string, int> ExternalIdByKey = new Dictionary<string, int>();

        /// <summary>key 是否可从树上选到(未知 key 保值判据)。</summary>
        public bool IsKnown(string key)
        {
            return !string.IsNullOrEmpty(key) && ExternalIdByKey.ContainsKey(key);
        }
    }

    /// <summary>
    /// 树形 key 下拉构建器(2026-09-18,设计 docs/superpowers/specs/2026-09-18-reddotkey-tree-dropdown-design.md)。
    /// 复用编辑器组树派生 RedDotGroupTree.DeriveGroupTree(树面板同源);externalId 元数据经参数注入(Build 纯函数,验证器可造数);
    /// 未落在树上任何节点的 key 落"未编组"兜底文件夹。静态缓存由 RedDotGraphAssetPostProcessor 在图资产变更时失效。
    /// </summary>
    public static class RedDotKeyTreeBuilder
    {
        private const string SmokeTestMark = "SmokeTest";
        private const string MainSectionLabel = "主页卡(主图)";
        private const string SecondarySectionLabel = "副页卡(动态挂载)";
        private const string UngroupedLabel = "未编组";

        private static RedDotKeyTreeBuildResult _cache;

        public static void InvalidateCache()
        {
            _cache = null;
        }

        /// <summary>取树(静态缓存)。false=无图/加载失败,调用侧回落平铺。</summary>
        public static bool TryGetTree(out RedDotKeyTreeBuildResult result)
        {
            if (_cache != null)
            {
                result = _cache;
                return true;
            }
            var path = FindGraphAssetPath();
            if (string.IsNullOrEmpty(path))
            {
                result = null;
                return false;
            }
            try
            {
                var textGraph = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var data = JsonUtility.FromJson<RedDotGraphData>(textGraph);
                if (data == null)
                {
                    result = null;
                    return false;
                }
                result = _cache = Build(data, RedDotKeys.All);
                return true;
            }
            catch (Exception e)
            {
                Log.Error("[RedDotKeyTree] 图加载/构建失败({0}),key 下拉回落平铺:{1}", path, e);
                result = null;
                return false;
            }
        }

        /// <summary>纯函数:候选路径挑主图。排除 SmokeTest;多个取字典序首个并告警;空返回 null。</summary>
        public static string PickGraphPath(string[] paths)
        {
            if (paths == null)
            {
                return null;
            }
            var found = (string)null;
            var count = 0;
            foreach (var raw in paths)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                var normalized = raw.Replace('\\', '/');
                if (normalized.Contains(SmokeTestMark))
                {
                    continue;
                }
                count++;
                if (found == null || string.CompareOrdinal(normalized, found) < 0)
                {
                    found = normalized;
                }
            }
            if (count > 1)
            {
                Log.Warning("[RedDotKeyTree] 发现 {0} 个红点图,树形下拉使用 {1},请确认其余为废弃图", count, found);
            }
            return found;
        }

        /// <summary>纯函数:由图数据 + 全量 key 表构建树。空 key 节点跳过;图上未导出 key 标 Unexported。</summary>
        public static RedDotKeyTreeBuildResult Build(RedDotGraphData data, RedDotKey[] allKeys)
        {
            data.ValidateGraph(); // 与 RedDotWindow 加载口径一致(字典缓存重建/组根自愈前置)
            var ctx = new BuildContext();
            ctx.Data = data;
            if (allKeys != null)
            {
                foreach (var k in allKeys)
                {
                    if (k != null && !string.IsNullOrEmpty(k.Key) && ctx.AllKeySet.Add(k.Key))
                    {
                        ctx.Result.ExternalIdByKey[k.Key] = k.ExternalId;
                    }
                }
            }
            foreach (var info in data.keyEnumDict.enumInfos)
            {
                ctx.KeyById[info.id] = info.key;
                ctx.DescById[info.id] = info.desc;
                if (!string.IsNullOrEmpty(info.key) && !ctx.DescByKey.ContainsKey(info.key))
                {
                    ctx.DescByKey[info.key] = info.desc;
                }
            }

            var model = RedDotGroupTree.DeriveGroupTree(data);
            var root = new RedDotKeyTreeEntry { Children = new List<RedDotKeyTreeEntry>() };
            AddSection(root, model.MainSection, MainSectionLabel, ctx);
            AddSection(root, model.SecondarySection, SecondarySectionLabel, ctx);

            // 未编组兜底:可选项中没有落在树上任何节点的 key(未重建域/游离 key)
            var ungrouped = new List<RedDotKeyTreeEntry>();
            foreach (var kv in ctx.Result.ExternalIdByKey)
            {
                if (ctx.InTreeKeys.Contains(kv.Key))
                {
                    continue;
                }
                string desc;
                var label = ctx.DescByKey.TryGetValue(kv.Key, out desc) && !string.IsNullOrEmpty(desc) && desc != "null"
                    ? desc
                    : kv.Key;
                ungrouped.Add(new RedDotKeyTreeEntry { Label = label, Key = kv.Key, ExternalId = kv.Value });
            }
            if (ungrouped.Count > 0)
            {
                ungrouped.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                root.Children.Add(new RedDotKeyTreeEntry { Label = UngroupedLabel, Children = ungrouped });
            }
            ctx.Result.Root = root;
            return ctx.Result;
        }

        private static string FindGraphAssetPath()
        {
            try
            {
                var dataPath = Application.dataPath; // <项目>/Assets
                var files = Directory.GetFiles(dataPath, "*.reddotgraph", SearchOption.AllDirectories);
                var candidates = new string[files.Length];
                for (var i = 0; i < files.Length; i++)
                {
                    var normalized = files[i].Replace('\\', '/');
                    candidates[i] = normalized.StartsWith(dataPath)
                        ? "Assets" + normalized.Substring(dataPath.Length)
                        : normalized;
                }
                return PickGraphPath(candidates);
            }
            catch (Exception e)
            {
                Log.Error("[RedDotKeyTree] 扫描红点图失败:{0}", e.Message);
                return null;
            }
        }

        private static void AddSection(RedDotKeyTreeEntry root, RedDotTreeEntry section, string label, BuildContext ctx)
        {
            if (section == null || section.children == null || section.children.Count == 0)
            {
                return;
            }
            var folder = new RedDotKeyTreeEntry { Label = label, Children = new List<RedDotKeyTreeEntry>() };
            foreach (var child in section.children)
            {
                ConvertEntry(child, folder, ctx);
            }
            if (folder.Children.Count > 0)
            {
                root.Children.Add(folder);
            }
        }

        private static void ConvertEntry(RedDotTreeEntry src, RedDotKeyTreeEntry parent, BuildContext ctx)
        {
            if (src == null)
            {
                return;
            }
            if (src.groupId > 0)
            {
                var folder = new RedDotKeyTreeEntry { Label = src.label, Children = new List<RedDotKeyTreeEntry>() };
                foreach (var child in src.children)
                {
                    ConvertEntry(child, folder, ctx);
                }
                if (folder.Children.Count > 0) // 空组(纯目录无内容)不进选择菜单
                {
                    parent.Children.Add(folder);
                }
                return;
            }
            if (src.contextId <= 0)
            {
                return;
            }
            var node = ctx.Data.GetContext(src.contextId);
            if (node == null)
            {
                return;
            }
            string key;
            if (!ctx.KeyById.TryGetValue(node.keyId, out key) || string.IsNullOrEmpty(key))
            {
                return; // 空 key 节点(未配 key/悬空 keyId)跳过
            }
            string desc;
            ctx.DescById.TryGetValue(node.keyId, out desc);
            if (string.IsNullOrEmpty(desc) || desc == "null")
            {
                desc = null;
            }
            int ext;
            var unexported = !ctx.AllKeySet.Contains(key);
            if (!ctx.Result.ExternalIdByKey.TryGetValue(key, out ext))
            {
                ext = -1; // 图上已建未导出:可选择,ext 记 -1 并标 Unexported
                ctx.Result.ExternalIdByKey[key] = -1;
            }
            ctx.InTreeKeys.Add(key);
            parent.Children.Add(new RedDotKeyTreeEntry
            {
                Label = desc ?? key,
                Key = key,
                ExternalId = ext,
                Unexported = unexported,
            });
        }

        /// <summary>Build 过程上下文(收敛纯函数参数表)。</summary>
        private class BuildContext
        {
            public RedDotGraphData Data;
            public readonly Dictionary<int, string> KeyById = new Dictionary<int, string>();
            public readonly Dictionary<int, string> DescById = new Dictionary<int, string>();
            public readonly Dictionary<string, string> DescByKey = new Dictionary<string, string>();
            public readonly HashSet<string> AllKeySet = new HashSet<string>();
            public readonly HashSet<string> InTreeKeys = new HashSet<string>();
            public readonly RedDotKeyTreeBuildResult Result = new RedDotKeyTreeBuildResult();
        }
    }
}
#endif
