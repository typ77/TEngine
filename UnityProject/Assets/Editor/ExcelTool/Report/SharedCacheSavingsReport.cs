using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using TEngine;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor.ExcelTool.Report
{
    /// <summary>
    /// 共享缓存优化节省统计：导出后遍历 SharedCacheInfos，
    /// 对每个启用优化的字段估算运行时内存节省（去重掉的实例数 × 单实例估算大小）。
    /// 用于评估优化是否过度（重复率低/节省小的字段不值得优化）。
    /// </summary>
    public static class SharedCacheSavingsReport
    {
        // 64位托管堆经验值（字节）
        private const int PtrSize = 8;          // 托管引用/指针
        private const int ObjectHeader = 16;    // 对象头 + 同步块索引（64位）
        private const int ListOverhead = 40;    // List<T> 自身：对象头 + 数组引用 + _count + _version + 对齐
        private const int DictOverhead = 80;    // Dictionary<,> 自身粗估

        [MenuItem("Tools/配置表工具/测试/共享缓存优化节省统计")]
        public static void RunReport()
        {
            const string tag = "[节省统计]";
            try
            {
                EditorUtility.DisplayProgressBar("节省统计", "导出压缩 bytes（重读全部 Excel）...", 0.2f);
                object ctx = ExportCompressedViaReflection();
                var dataDict = GetDataDict(ctx);
                if (dataDict == null)
                {
                    Debug.LogError($"{tag} 无法获取 DataDict");
                    return;
                }

                var sharedCacheInfosProp = ctx.GetType().GetProperty("SharedCacheInfos");
                var sharedCacheInfos = sharedCacheInfosProp?.GetValue(ctx) as IDictionary;

                EditorUtility.DisplayProgressBar("节省统计", "统计中...", 0.7f);

                var rows = new List<SavingsRow>();
                long totalUnoptimized = 0;
                long totalOptimized = 0;

                if (sharedCacheInfos != null)
                {
                    foreach (DictionaryEntry tableEntry in sharedCacheInfos)
                    {
                        string className = tableEntry.Key as string;
                        var fieldList = tableEntry.Value as IList; // List<FieldSharedCacheInfo>
                        if (fieldList == null) continue;

                        foreach (var fi in fieldList) // fi 是 FieldSharedCacheInfo（跨程序集，用 dynamic）
                        {
                            dynamic d = fi;
                            if (!d.EnableSharedCache) continue;

                            int totalCount = (int)d.TotalCount;
                            int uniqueCount = (int)d.UniqueCount;
                            int savedInstances = totalCount - uniqueCount;
                            if (savedInstances <= 0) continue;

                            string fieldType = (string)d.FieldType;
                            string fieldName = (string)d.FieldName;
                            IList indexToValue = ((IEnumerable)d.IndexToValue).Cast<object>().ToList();

                            int instanceSize = EstimateInstanceSize(fieldType, indexToValue, d.ValueToIndex as IEnumerable);

                            // 优化前内存：每行一个独立实例
                            long unoptimizedBytes = (long)totalCount * instanceSize;

                            // 优化后内存：唯一实例 + 每行索引 + Dictionary 容器开销
                            long uniqueInstanceBytes = (long)uniqueCount * instanceSize;
                            long indexOverhead = (long)totalCount * 4; // 每行一个 int 索引
                            long dictOverhead = EstimateDictionaryOverhead(uniqueCount); // Dictionary<int,T> 容器
                            long optimizedBytes = uniqueInstanceBytes + indexOverhead + dictOverhead;

                            long savedBytes = unoptimizedBytes - optimizedBytes; // 净节省（已扣除新增开销）
                            double optimizedRatio = unoptimizedBytes > 0 ? (double)optimizedBytes / unoptimizedBytes : 0;

                            rows.Add(new SavingsRow
                            {
                                Table = className,
                                Field = fieldName,
                                Type = fieldType,
                                TotalCount = totalCount,
                                UniqueCount = uniqueCount,
                                SavedInstances = savedInstances,
                                InstanceBytes = instanceSize,
                                UnoptimizedBytes = unoptimizedBytes,
                                OptimizedBytes = optimizedBytes,
                                IndexOverhead = indexOverhead,
                                DictOverhead = dictOverhead,
                                SavedBytes = savedBytes,
                                OptimizedRatio = optimizedRatio,
                            });

                            totalUnoptimized += unoptimizedBytes;
                            totalOptimized += optimizedBytes;
                        }
                    }
                }

                rows.Sort((a, b) => b.SavedBytes.CompareTo(a.SavedBytes));

                // 输出到 txt 文件
                string txtPath = WriteReportToFile(rows, totalUnoptimized, totalOptimized);

                // Console 只打汇总 + 文件路径
                double overallRatio = totalUnoptimized > 0 ? (double)totalOptimized / totalUnoptimized : 0;
                long overallSaved = totalUnoptimized - totalOptimized;
                Debug.Log(
                    $"{tag} <color=green>统计完成</color>：{rows.Count} 个优化字段\n" +
                    $"  优化前预估 {FormatBytes(totalUnoptimized)} → 优化后预估 {FormatBytes(totalOptimized)}，" +
                    $"净节省 {FormatBytes(overallSaved)}（优化后/前 = {overallRatio:P1}）\n" +
                    $"  完整报告已写入: {txtPath}\n" +
                    $"  注: 单实例大小为反射估算（{PtrSize * 8}位托管堆经验值，含对象头/字段/容器开销，不含 GC padding），仅供参考。");

                // 过度优化预警（净节省 < 4KB，与分析器 MinNetBenefitBytes 阈值一致）
                var lowValue = rows.Where(r => r.SavedBytes < 4096).ToList();
                if (lowValue.Count > 0)
                {
                    Debug.LogWarning(
                        $"{tag} 可疑过度优化（净节省<4KB）共 {lowValue.Count} 个字段，" +
                        "优化收益小，导出时会被第五道闸门拦掉。详见报告文件。");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{tag} 异常: {e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ---- 实例大小估算 ------------------------------------------------

        /// <summary>
        /// 估算"未优化时每行各持有一个独立实例"的单实例托管堆大小（字节）。
        /// 按字段类型分派：标量按值大小；容器/对象按经验开销 + 深度估算元素。
        /// 这是粗略值，用于评估优化收益量级，不追求精确到字节。
        /// </summary>
        private static int EstimateInstanceSize(string type, IList indexToValue, IEnumerable valueToIndexKeys)
        {
            type = (type ?? "").Trim();

            if (IsScalar(type))
                return ScalarSize(type);

            if (IsString(type))
            {
                // 用 IndexToValue（解析后的 string 对象）算平均长度，比 ValueToIndexKeys（跨程序集 dynamic 可能丢数据）可靠
                int avgLen = 8;
                if (indexToValue != null && indexToValue.Count > 0)
                {
                    var lengths = indexToValue.OfType<string>().Select(s => s?.Length ?? 0).ToArray();
                    if (lengths.Length > 0) avgLen = (int)lengths.Average();
                }
                else if (valueToIndexKeys != null)
                {
                    var keys = valueToIndexKeys.Cast<object>().OfType<string>().Select(s => s?.Length ?? 0).ToArray();
                    if (keys.Length > 0) avgLen = (int)keys.Average();
                }
                return ObjectHeader + PtrSize + (avgLen + 2) * sizeof(char);
            }

            if (IsCustomObject(type) && indexToValue != null && indexToValue.Count > 0)
                return EstimateObjectSize(indexToValue[0]);

            if (indexToValue != null && indexToValue.Count > 0 &&
                (type.IndexOf("list", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 type.IndexOf("dict", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 type.EndsWith("[]")))
            {
                return EstimateObjectSize(indexToValue[0]);
            }

            return ObjectHeader + PtrSize * 2;
        }

        private static int EstimateObjectSize(object obj)
        {
            if (obj == null) return ObjectHeader;
            int size = ObjectHeader;
            var t = obj.GetType();

            // 字符串
            if (obj is string s)
                return ObjectHeader + PtrSize + (s.Length + 2) * sizeof(char);

            // 数组
            if (obj is Array arr)
            {
                size += PtrSize + 8; // 数组头额外
                var elemType = t.GetElementType();
                for (int i = 0; i < arr.Length; i++)
                {
                    var elem = arr.GetValue(i);
                    size += elemType != null && elemType.IsPrimitive
                        ? (elemType == typeof(long) || elemType == typeof(double) ? 8 : 4)
                        : PtrSize; // 引用元素只算指针，实际元素内存计入递归
                    if (elem != null && !elem.GetType().IsPrimitive && !(elem is string))
                        size += EstimateObjectSize(elem);
                }
                return Align(size);
            }

            // IList（List<T>）
            if (obj is IList list)
            {
                size += ListOverhead;
                // 内部数组
                int cap = list.Count;
                size += PtrSize + 8; // 数组引用 + 数组头
                Type elemType = null;
                if (list.Count > 0) elemType = list[0]?.GetType();
                bool elemPrimitive = elemType != null && (elemType.IsPrimitive || elemType == typeof(string));
                for (int i = 0; i < list.Count; i++)
                {
                    size += elemPrimitive ? ManagedSizeOfPrimitive(elemType) : PtrSize;
                    var elem = list[i];
                    if (elem != null && elemPrimitive && elem is string es)
                        size += (es.Length + 2) * sizeof(char);
                    else if (elem != null && !elemPrimitive)
                        size += EstimateObjectSize(elem);
                }
                return Align(size);
            }

            // IDictionary
            if (obj is IDictionary dict)
            {
                size += DictOverhead;
                size += dict.Count * (PtrSize * 2 + 16); // 每个 entry 粗估：key引用+value引用+entry结构
                return Align(size);
            }

            // ValueTuple：反射 Item1/Item2...
            if (t.IsGenericType && t.Name.StartsWith("ValueTuple"))
            {
                foreach (var f in t.GetFields())
                {
                    var v = f.GetValue(obj);
                    size += ManagedSizeOfPrimitive(f.FieldType);
                    if (v != null && !f.FieldType.IsPrimitive && !(v is string))
                        size += EstimateObjectSize(v);
                    else if (v is string vs)
                        size += (vs.Length + 2) * sizeof(char);
                }
                return Align(size);
            }

            // 普通对象（如 RewardDataItem）：反射字段
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                size += ManagedSizeOfPrimitive(f.FieldType);
                // 避免对自引用类型无限递归（RewardDataItem 字段都是标量，不会递归）
            }
            return Align(size);
        }

        private static int ManagedSizeOfPrimitive(Type t)
        {
            if (t == typeof(long) || t == typeof(ulong) || t == typeof(double)) return 8;
            if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) return 4;
            if (t == typeof(short) || t == typeof(ushort) || t == typeof(char)) return 2;
            if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool)) return 1;
            return PtrSize; // 引用类型字段算指针
        }

        private static int ScalarSize(string type)
        {
            switch (type.ToLowerInvariant())
            {
                case "long": case "ulong": case "double": return 8;
                case "int": case "uint": case "float": case "l": return 4;
                case "short": case "ushort": return 2;
                case "byte": case "sbyte": case "bool": return 1;
                default: return 4;
            }
        }

        private static bool IsScalar(string t)
            => t == "int" || t == "uint" || t == "long" || t == "ulong" ||
               t == "short" || t == "ushort" || t == "byte" || t == "sbyte" ||
               t == "bool" || t == "float" || t == "double" || t == "l";

        private static bool IsString(string t)
            => t == "string" || t == "str" || t == "stringfull";

        private static bool IsCustomObject(string t)
            => t == "RewardObject" || t == "ConditionObject" ||
               t == "EffectCfgObject" || t == "GradedPriceObject";

        private static int Align(int n) => (n + 7) & ~7;

        /// <summary>
        /// 估算 Dictionary&lt;int, T&gt; 的托管堆开销（不含 T 值本身，值在唯一实例里已算）。
        /// Dictionary = 对象头 + buckets(int[]) + entries[](struct: hashCode+next+key+value) + buckets扩容。
        /// 粗估 = DictOverhead(80) + buckets(capacity*4) + entries(capacity * (4+4+8+8))。
        /// </summary>
        private static long EstimateDictionaryOverhead(int uniqueCount)
        {
            int capacity = uniqueCount <= 0 ? 4 : uniqueCount;
            // Dictionary 实际容量按质数向上取，这里粗略按 capacity
            long buckets = capacity * 4L;                          // int[]
            long entries = capacity * (4 + 4 + PtrSize + PtrSize); // Entry: hashCode(int)+next(int)+key(int,值类型)+value(引用)
            return DictOverhead + buckets + entries;
        }

        private static string WriteReportToFile(List<SavingsRow> rows, long totalUnoptimized, long totalOptimized)
        {
            string dir = Path.Combine(Application.dataPath, "../Temp");
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(dir, $"SharedCacheSavings_{stamp}.md");

            using (var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                long overallSaved = totalUnoptimized - totalOptimized;
                double overallRatio = totalUnoptimized > 0 ? (double)totalOptimized / totalUnoptimized : 0;

                // 标题 + 汇总
                sw.WriteLine("# 共享缓存优化节省统计");
                sw.WriteLine();
                sw.WriteLine($"> 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sw.WriteLine();
                sw.WriteLine("## 汇总");
                sw.WriteLine();
                sw.WriteLine("| 指标 | 值 |");
                sw.WriteLine("|:---|---:|");
                sw.WriteLine($"| 优化字段数 | {rows.Count} |");
                sw.WriteLine($"| 优化前预估 | {FormatBytes(totalUnoptimized)} |");
                sw.WriteLine($"| 优化后预估 | {FormatBytes(totalOptimized)} |");
                sw.WriteLine($"| 净节省 | **{FormatBytes(overallSaved)}** |");
                sw.WriteLine($"| 优化比率（后/前） | **{overallRatio:P1}** |");
                sw.WriteLine();

                // 说明
                sw.WriteLine("## 计算口径");
                sw.WriteLine();
                sw.WriteLine($"- **单实例大小**: 反射估算（64位托管堆经验值），含对象头({ObjectHeader}B)/字段/容器开销，不含 GC padding");
                sw.WriteLine($"- **优化前** = 总出现次数 × 单实例大小（每行一个独立实例）");
                sw.WriteLine($"- **优化后** = 唯一实例(UniqueCount × 单实例) + 每行索引(4B/行) + Dictionary容器开销");
                sw.WriteLine($"- **净节省** = 优化前 − 优化后（已扣除优化新增的索引/容器开销）");
                sw.WriteLine($"- **优化比率** = 优化后 / 优化前（越小越好，>85% 视为过度优化）");
                sw.WriteLine();

                // 按表分组（每表一组字段，便于在长报告中导航）
                // 先按表聚合：表名 -> 该表所有字段行（保持原 rows 的净节省降序）
                var grouped = rows.GroupBy(r => r.Table)
                    .OrderByDescending(g => g.Sum(x => x.SavedBytes))
                    .ToList();

                // 章节导航（md 内部锚点跳转，便于在长报告中快速定位）
                var lowValue = rows.Where(r => r.SavedBytes < 4096).ToList();
                sw.WriteLine("## 导航");
                sw.WriteLine();
                sw.WriteLine("- [汇总](#汇总) — 优化前/后/净节省/优化比率");
                sw.WriteLine($"- [字段明细（按表分组）](#字段明细按表分组表内按净节省降序) — 共 {grouped.Count} 张表，{rows.Count} 个优化字段");
                if (lowValue.Count > 0)
                    sw.WriteLine($"- [可疑过度优化](#可疑过度优化净节省-4kb) — 共 {lowValue.Count} 个字段会被拦掉");
                else
                    sw.WriteLine("- [可疑过度优化](#可疑过度优化净节省-4kb) — 无");
                sw.WriteLine("- [按表汇总 Top 20](#按表汇总按优化后内存降序-top-20) — 快速概览");
                sw.WriteLine();

                // 明细：按表分组，每表一个小节
                sw.WriteLine("## 字段明细（按表分组，表内按净节省降序）");
                sw.WriteLine();
                int tableIdx = 0;
                foreach (var g in grouped)
                {
                    long tSaved = g.Sum(x => x.SavedBytes);
                    long tBefore = g.Sum(x => x.UnoptimizedBytes);
                    long tAfter = g.Sum(x => x.OptimizedBytes);
                    double tRatio = tBefore > 0 ? (double)tAfter / tBefore : 0;
                    sw.WriteLine($"### {tableIdx}-{g.Key}");
                    sw.WriteLine();
                    sw.WriteLine($"优化前 {FormatBytes(tBefore)} → 优化后 {FormatBytes(tAfter)}，净节省 {FormatBytes(tSaved)}（后/前 {tRatio:P0}）");
                    sw.WriteLine();
                    sw.WriteLine("| 字段 | 类型 | 总次数 | 唯一 | 去重 | 单实例 | 优化前 | 优化后 | 净节省 | 后/前 |");
                    sw.WriteLine("|:---|:---|---:|---:|---:|---:|---:|---:|---:|---:|");
                    foreach (var r in g.OrderByDescending(x => x.SavedBytes))
                    {
                        sw.WriteLine($"| {r.Field} | {r.Type} | {r.TotalCount:N0} | {r.UniqueCount:N0} | {r.SavedInstances:N0} | {FormatBytes(r.InstanceBytes)} | {FormatBytes(r.UnoptimizedBytes)} | {FormatBytes(r.OptimizedBytes)} | {FormatBytes(r.SavedBytes)} | {r.OptimizedRatio:P0} |");
                    }
                    sw.WriteLine();
                    sw.WriteLine("[⬆ 返回导航](#导航)");
                    sw.WriteLine();
                    tableIdx++;
                }

                // 过度优化预警（lowValue 已在导航段计算，此处复用）
                sw.WriteLine("## 可疑过度优化（净节省 < 4KB）");
                sw.WriteLine();
                if (lowValue.Count == 0)
                {
                    sw.WriteLine("无。所有优化字段净节省 ≥ 4KB。");
                }
                else
                {
                    sw.WriteLine($"共 **{lowValue.Count}** 个字段，净节省不足 4KB，不值得引入共享缓存的索引/容器开销。导出时会被第五道闸门拦掉。");
                    sw.WriteLine();
                    sw.WriteLine("| 表名 | 字段 | 类型 | 净节省 | 后/前 |");
                    sw.WriteLine("|:---|:---|:---|---:|---:|");
                    foreach (var r in lowValue)
                    {
                        sw.WriteLine($"| {r.Table} | {r.Field} | {r.Type} | {FormatBytes(r.SavedBytes)} | {r.OptimizedRatio:P0} |");
                    }
                }
                sw.WriteLine();

                // 按表汇总（优化后内存 Top 20）
                sw.WriteLine("## 按表汇总（按优化后内存降序 Top 20）");
                sw.WriteLine();
                sw.WriteLine("| 表名 | 优化字段数 | 优化前 | 优化后 | 净节省 | 后/前 |");
                sw.WriteLine("|:---|---:|---:|---:|---:|---:|");
                var byTable = rows.GroupBy(r => r.Table)
                    .Select(g => new { Table = g.Key, Before = g.Sum(x => x.UnoptimizedBytes), After = g.Sum(x => x.OptimizedBytes), Fields = g.Count() })
                    .OrderByDescending(x => x.After)
                    .Take(20);
                foreach (var t in byTable)
                {
                    long saved = t.Before - t.After;
                    double ratio = t.Before > 0 ? (double)t.After / t.Before : 0;
                    sw.WriteLine($"| {t.Table} | {t.Fields} | {FormatBytes(t.Before)} | {FormatBytes(t.After)} | {FormatBytes(saved)} | {ratio:P0} |");
                }
            }

            return path;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / 1024.0 / 1024.0:F2} MB";
        }

        // ---- 反射导出（复用 ConfigRoundTripTest 的模式）-----------------

        private static object ExportCompressedViaReflection()
        {
            Type excelDataToolType = FindType("TEngine.Editor.ExcelTool.ExcelDataTool");
            if (excelDataToolType == null)
                throw new InvalidOperationException("找不到 TEngine.Editor.ExcelTool.ExcelDataTool");

            var createCtx = excelDataToolType.GetMethod("CreateExportContext", BindingFlags.NonPublic | BindingFlags.Static);
            object ctx = createCtx.Invoke(null, new object[] { true });

            var loadMethod = excelDataToolType.GetMethod("LoadAllExcelData", BindingFlags.NonPublic | BindingFlags.Static);
            loadMethod.Invoke(null, new[] { ctx, (object)false, true, false });
            return ctx;
        }

        private static IDictionary GetDataDict(object ctx)
            => ctx.GetType().GetProperty("DataDict")?.GetValue(ctx) as IDictionary;

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private struct SavingsRow
        {
            public string Table;
            public string Field;
            public string Type;
            public int TotalCount;
            public int UniqueCount;
            public int SavedInstances;
            public int InstanceBytes;
            public long UnoptimizedBytes;   // 优化前内存 = TotalCount × 单实例
            public long OptimizedBytes;     // 优化后内存 = 唯一实例 + 索引开销 + Dictionary开销
            public long IndexOverhead;      // 索引开销 = TotalCount × 4(int) + int键数组
            public long DictOverhead;       // Dictionary<int,T> 容器开销
            public long SavedBytes;         // 节省 = 优化前 - 优化后（净节省，已扣除新增开销）
            public double OptimizedRatio;   // 优化比率 = 优化后 / 优化前（越小越好）
        }
    }
}
