using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TEngine.Editor.ExcelTool.Validators;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 共享缓存分析器 — 检测哪些字段适合启用共享缓存
    /// </summary>
    public class SharedCacheAnalyzer
    {
        private const double DuplicationThreshold = 0.08;

        /// <summary>
        /// 启用共享缓存的最小样本量。样本太少时重复率统计无意义（如 10 行有 1 个重复就 10%），
        /// 且去重省的几个实例抵不过每行索引(4B)+Dictionary 容器的开销，必然反向优化。
        /// </summary>
        private const int MinTotalCountForCache = 50;

        /// <summary>
        /// 启用共享缓存的最小去重实例数。去重数太少时优化收益小于新增索引/容器开销。
        /// </summary>
        private const int MinSavedInstancesForCache = 10;

        /// <summary>
        /// 启用共享缓存的最小净收益（字节）。净节省低于此值的字段不值得优化——
        /// 引入索引/Dictionary 开销换取的节省太小，徒增代码复杂度和二进制解析开销。
        /// 用绝对值而非百分比，避免误杀"绝对节省大但比率低"的好优化（如 60KB 节省但后/前=90%）。
        /// </summary>
        private const long MinNetBenefitBytes = 4096;

        /// <summary>
        /// 分析数据表中各字段的重复率，输出共享缓存信息
        /// </summary>
        /// <param name="className">表名（类名）</param>
        /// <param name="dataList">数据行列表</param>
        /// <param name="nameMap">列索引 -> 字段名映射</param>
        /// <param name="typeMap">列索引 -> 字段类型映射</param>
        /// <returns>字段共享缓存信息列表</returns>
        public List<FieldSharedCacheInfo> Analyze(
            string className,
            List<ConfigData[]> dataList,
            Dictionary<int, string> nameMap,
            Dictionary<int, string> typeMap)
        {
            var fieldCacheInfos = new List<FieldSharedCacheInfo>();

            // 遍历每一列（每个字段）
            foreach (var (colIndex, fieldName) in nameMap)
            {
                // 跳过服务器端-only列（typeMap中不存在）
                if (!typeMap.TryGetValue(colIndex, out var fieldType))
                    continue;

                // 检查是否符合优化条件
                if (!CanEnableSharedCache(fieldType))
                    continue;

                // 收集所有数据值
                var valueCounter = new Dictionary<string, int>();
                int totalCount = 0;

                foreach (var row in dataList)
                {
                    var id = row[0].Data;
                    foreach (var field in row)
                    {
                        if (field.Name != fieldName || string.IsNullOrEmpty(field.Data))
                            continue;

                        // string 跳过空字符串
                        if (fieldType == SupportType.String || fieldType == SupportType.StringFull)
                        {
                            if (string.IsNullOrEmpty(field.Data)) continue;
                        }

                        // 对自定义对象格式做验证，只有格式正确才加入共享缓存
                        bool isValid = true;
                        if (string.Equals(fieldType, SupportType.RewardObject, StringComparison.OrdinalIgnoreCase))
                            isValid = DataValidator.ValidateRewardObjectFormat(field.Data, className, id, field.Name);
                        else if (string.Equals(fieldType, SupportType.ConditionObject, StringComparison.OrdinalIgnoreCase))
                            isValid = DataValidator.ValidateConditionObjectFormat(field.Data, className, id, field.Name);
                        else if (string.Equals(fieldType, SupportType.EffectCfgObject, StringComparison.OrdinalIgnoreCase))
                            isValid = DataValidator.ValidateEffectCfgObjectFormat(field.Data, className, id, field.Name);
                        else if (string.Equals(fieldType, SupportType.GradedPriceObject, StringComparison.OrdinalIgnoreCase))
                            isValid = DataValidator.ValidateGradedPriceObjectFormat(field.Data, className, id, field.Name);

                        if (!isValid)
                        {
                            // 空内容(如 []、[[]] 等)会被 Validate* 判为不合法而跳过，但它是合法的空数据，
                            // 应作为共享缓存的一个唯一值收集——所有空内容行指向同一个空对象，
                            // 写入端写 index、读取端读 index 从缓存拿空对象，写读完全对称。
                            // 只有真正格式错误(无法解析)才跳过。
                            try
                            {
                                ReadExcel.ParseValue(fieldType, field.Data, className);
                            }
                            catch
                            {
                                continue; // 真正的格式错误，无法解析，跳过
                            }
                        }

                        valueCounter.TryAdd(field.Data, 0);
                        valueCounter[field.Data]++;
                        totalCount++;
                    }
                }

                if (totalCount == 0) continue;

                int uniqueCount = valueCounter.Count;
                int savedInstances = totalCount - uniqueCount;
                double duplicationRate = (double)savedInstances / totalCount;

                // 三重闸门：重复率达标 + 样本量足够 + 去重实例数足够。
                // 少量样本时即使重复率高也禁用——去重省的实例抵不过每行索引+Dictionary开销，反而反向优化。
                bool enableCache = duplicationRate >= DuplicationThreshold
                                   && totalCount >= MinTotalCountForCache
                                   && savedInstances >= MinSavedInstancesForCache;

                // 第四道闸门：净收益估算。前三道闸门过后，再实算"去重省的实例内存"是否大于
                // "优化新增的索引(4B/行)+Dictionary 容器开销"。净节省 <= 0 的字段不优化（避免反向优化）。
                // 估算用 valueCounter 的样本（此刻 IndexToValue 尚未构建），按类型粗略估算单实例大小。
                if (enableCache)
                {
                    long netBenefit = EstimateNetBenefit(fieldType, valueCounter, totalCount, uniqueCount, savedInstances);
                    // 净收益 < 4KB 不优化：节省太小不值得引入索引/容器开销。
                    // 用绝对值而非百分比，避免误杀"绝对节省大但比率低"的好优化。
                    if (netBenefit < MinNetBenefitBytes)
                    {
                        enableCache = false;
                    }
                }

                var cacheInfo = new FieldSharedCacheInfo
                {
                    FieldName = fieldName,
                    FieldType = fieldType,
                    EnableSharedCache = enableCache,
                    TotalCount = totalCount
                };

                // 如果开启优化，构建 value -> index 映射
                if (cacheInfo.EnableSharedCache)
                {
                    int index = 0;
                    foreach (var (value, _) in valueCounter)
                    {
                        cacheInfo.ValueToIndex[value] = index;
                        cacheInfo.IndexToValue.Add(ReadExcel.ParseValue(fieldType, value, className));
                        index++;
                    }
                    Debug.Log($"[共享缓存优化] 表:{className} 字段:{fieldName} 重复率:{duplicationRate:P} 总数:{totalCount} 去重:{savedInstances} 唯一值数:{uniqueCount} 开启优化");
                }

                fieldCacheInfos.Add(cacheInfo);
            }

            return fieldCacheInfos;
        }

        // ---- 净收益估算（第四道闸门）-----------------------------------------------
        // 与 SharedCacheSavingsReport 同口径，但用 analyzer 此刻可用的数据（valueCounter 的原始字符串 key）
        // 做轻量估算，判定净节省是否为正。不追求精确，只要能区分"明显收益"与"反向优化"。

        private const int ObjHeader = 16;     // 64位托管堆对象头
        private const int PtrSize = 8;        // 托管引用
        private const int ListOverhead = 40;  // List<T> 自身开销
        private const int DictOverhead = 80;  // Dictionary<,> 自身开销

        /// <summary>
        /// 估算启用共享缓存的净收益（字节）。正值=优化有收益，<=0=反向优化（不应启用）。
        /// 净收益 = 优化前内存 − 优化后内存
        ///        = totalCount×instanceSize − (uniqueCount×instanceSize + totalCount×4 + DictOverhead)
        ///        = savedInstances×instanceSize − totalCount×4 − DictOverhead
        /// </summary>
        private static long EstimateNetBenefit(string fieldType, Dictionary<string, int> valueCounter,
            int totalCount, int uniqueCount, int savedInstances)
        {
            int instanceSize = EstimateInstanceSize(fieldType, valueCounter);
            long saved = (long)savedInstances * instanceSize;
            long indexOverhead = (long)totalCount * 4; // 每行一个 int 索引
            // Dictionary 开销 = 容器头(80) + buckets数组(uniqueCount×4) + entries数组(uniqueCount×(hashCode+next+key+value))
            long dictOverhead = DictOverhead + uniqueCount * 4L + uniqueCount * (4L + 4 + PtrSize + PtrSize);
            return saved - indexOverhead - dictOverhead;
        }

        /// <summary>
        /// 估算单个实例的托管堆大小（字节）。按字段类型分派。
        /// analyzer 此刻只有 valueCounter 的原始字符串 key，对容器/对象类型用样本 key 解析后估算。
        /// </summary>
        private static int EstimateInstanceSize(string fieldType, Dictionary<string, int> valueCounter)
        {
            string t = (fieldType ?? "").Trim();
            string lower = t.ToLowerInvariant();

            // 标量：未优化时内联在 item 里，按值大小估
            if (lower == "long" || lower == "ulong" || lower == "double") return 8;
            if (lower == "int" || lower == "uint" || lower == "float" || lower == "l") return 4;
            if (lower == "short" || lower == "ushort") return 2;
            if (lower == "byte" || lower == "sbyte" || lower == "bool") return 1;

            // string：按 ParseValue（含 ProcessString 去引号/去扩展名）后的平均长度估，
            // 和写入端 WriteString(ProcessString(data)) 实际写入的内容一致。
            // 抽样最多 20 个 key 解析取平均，避免全量遍历。
            if (lower == "string" || lower == "str" || lower == "stringfull")
            {
                int avgLen = 8;
                if (valueCounter != null && valueCounter.Count > 0)
                {
                    var sample = valueCounter.Keys.Where(k => !string.IsNullOrEmpty(k)).Take(20).ToArray();
                    if (sample.Length > 0)
                    {
                        long total = 0;
                        foreach (var k in sample)
                        {
                            try { total += (((string)ReadExcel.ParseValue(t, k, "")).Length); }
                            catch { total += k.Length; }
                        }
                        avgLen = (int)(total / sample.Length);
                    }
                }
                return ObjHeader + PtrSize + (avgLen + 2) * 2;
            }

            // 容器/对象类型：取 valueCounter 第一个 key 解析成对象，反射估算其托管堆大小。
            if (valueCounter != null && valueCounter.Count > 0)
            {
                string sampleData = valueCounter.Keys.FirstOrDefault(k => !string.IsNullOrEmpty(k));
                if (sampleData != null)
                {
                    try
                    {
                        object sample = ReadExcel.ParseValue(t, sampleData, "");
                        if (sample != null)
                            return EstimateObjectSize(sample);
                    }
                    catch
                    {
                        // 解析失败（如空内容），按空容器估
                    }
                }
            }

            // 兜底：按普通引用对象估
            return ObjHeader + PtrSize * 2;
        }

        /// <summary>反射估算对象托管堆大小（含对象头+字段+容器开销，不含 GC padding）。</summary>
        private static int EstimateObjectSize(object obj)
        {
            if (obj == null) return ObjHeader;
            int size = ObjHeader;
            var t = obj.GetType();

            if (obj is string s)
                return ObjHeader + PtrSize + (s.Length + 2) * 2;

            if (obj is Array arr)
            {
                size += PtrSize + 8;
                var elemType = t.GetElementType();
                for (int i = 0; i < arr.Length; i++)
                {
                    var elem = arr.GetValue(i);
                    size += elemType != null && elemType.IsPrimitive
                        ? (elemType == typeof(long) || elemType == typeof(double) ? 8 : 4) : PtrSize;
                    if (elem != null && !elem.GetType().IsPrimitive && !(elem is string))
                        size += EstimateObjectSize(elem);
                }
                return Align(size);
            }

            if (obj is IList list)
            {
                size += ListOverhead;
                size += PtrSize + 8; // 内部数组引用 + 数组头
                Type elemType = list.Count > 0 ? list[0]?.GetType() : null;
                bool elemPrim = elemType != null && (elemType.IsPrimitive || elemType == typeof(string));
                for (int i = 0; i < list.Count; i++)
                {
                    size += elemPrim ? PrimSize(elemType) : PtrSize;
                    var elem = list[i];
                    if (elem is string es) size += (es.Length + 2) * 2;
                    else if (elem != null && !elemPrim) size += EstimateObjectSize(elem);
                }
                return Align(size);
            }

            if (obj is IDictionary dict)
            {
                size += DictOverhead;
                size += dict.Count * (PtrSize * 2 + 16);
                return Align(size);
            }

            if (t.IsGenericType && t.Name.StartsWith("ValueTuple"))
            {
                foreach (var f in t.GetFields())
                {
                    size += PrimSize(f.FieldType);
                    var v = f.GetValue(obj);
                    if (v is string vs) size += (vs.Length + 2) * 2;
                    else if (v != null && !f.FieldType.IsPrimitive) size += EstimateObjectSize(v);
                }
                return Align(size);
            }

            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                size += PrimSize(f.FieldType);
            return Align(size);
        }

        private static int PrimSize(System.Type t)
        {
            if (t == typeof(long) || t == typeof(ulong) || t == typeof(double)) return 8;
            if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) return 4;
            if (t == typeof(short) || t == typeof(ushort) || t == typeof(char)) return 2;
            if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool)) return 1;
            return PtrSize;
        }

        private static int Align(int n) => (n + 7) & ~7;

        /// <summary>
        /// 检查该字段是否可以启用共享缓存
        /// </summary>
        public static bool CanEnableSharedCache(string fieldType)
        {
            // 多语言字符串已经优化过了，跳过
            if (string.Equals(fieldType, SupportType.LanguageType, StringComparison.OrdinalIgnoreCase))
                return false;

            // 支持 string / stringfull
            if (string.Equals(fieldType, SupportType.String, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.StringFull, StringComparison.OrdinalIgnoreCase))
                return true;

            // 支持 List<T>
            if (fieldType.StartsWith(SupportType.List, StringComparison.OrdinalIgnoreCase) && fieldType.EndsWith(">"))
                return true;

            // 支持 Dictionary<TKey, TValue>
            if ((fieldType.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) ||
                 fieldType.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase)) && fieldType.EndsWith(">"))
                return true;

            // 支持自定义对象类型：RewardObject、ConditionObject 等，这些对象整体也可能重复
            if (string.Equals(fieldType, SupportType.RewardObject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.ConditionObject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.EffectCfgObject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.GradedPriceObject, StringComparison.OrdinalIgnoreCase))
                return true;

            // 支持 ValueTuple<T1,T2...>
            if (fieldType.StartsWith(SupportType.ValueTuple, StringComparison.OrdinalIgnoreCase) && fieldType.EndsWith(">"))
                return true;

            // 其他类型不优化
            return false;
        }

        // ========== 自定义对象格式验证方法已迁移至 DataValidator ==========
    }
}
