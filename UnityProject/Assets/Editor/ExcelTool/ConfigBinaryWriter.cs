using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Luban;
using UnityEngine;
using UnityEditor;


namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 配置表二进制序列化写入器
    /// 从 ExcelDataTool 拆分出来的序列化相关方法
    /// </summary>
    public static class ConfigBinaryWriter
    {
        private const int ListPrefixLen = 5;         // "list<"
        private const int DictPrefixLen = 5;         // "dict<"
        private const int DictFullPrefixLen = 11;    // "dictionary<"

        #region 公共方法

        /// <summary>
        /// 序列化对象
        /// </summary>
        public static void Serialize(ExcelExportContext ctx, IEnumerable<string> selectedSheets)
        {
            if (ctx.DataDict.Count == 0)
            {
                Log.Error("没有可序列化的数据，请先加载Excel数据");
                return;
            }

            // 压缩版 bytes 写运行时目录（玩家热更包）；未压缩版写到 Editor-only 目录，
            // 供报告工具做双跑对比——不进运行时、不污染玩家的 AssetRaw/Configs/Bytes。
            string outputPath = ctx.UseSharedCache
                ? Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath)
                : Path.Combine(Application.dataPath, "Editor/ExcelTool/Report/GeneratedBytes");
            Directory.CreateDirectory(outputPath);

            foreach (var className in selectedSheets)
            {
                if (ctx.DataDict.TryGetValue(className, out var dataList))
                {
                    WriteByteFile(ctx, className, dataList, outputPath);
                }
            }

            EditorUtility.DisplayProgressBar("导表进度", "正在刷新资源...", 0.95f);
            AssetDatabase.Refresh();
            Log.Info($"成功序列化{ctx.DataDict.Count}个数据表到{outputPath}");
        }

        /// <summary>
        /// 写入整个二进制文件
        /// </summary>
        public static void WriteByteFile(ExcelExportContext ctx, string className, List<ConfigData[]> dataList, string outputPath, bool isConfig = true, ExcelExportResult result = null)
        {
            var headByteBuf = new ByteBuf();
            var dataByteBuf = new ByteBuf();
            var indexMap = new Dictionary<string, int>();
            var dataStartOffset = 0;

            // v2 槽0空值：须在写行数据之前确定各字段布局（行内索引是否整体 +1）
            if (ctx.SharedCacheInfos.TryGetValue(className, out var tableCacheInfos))
            {
                PrepareEmptySlots(className, tableCacheInfos, dataList);
            }

            // 3. 写入实际内容并记录索引
            foreach (var dataRow in dataList)
            {
                var id = dataRow[0].Data; // 主键ID

                // 主键查重必须先于写数据：重复只认首条，后续重复行整行丢弃、不写入数据区。
                // 头部行数与索引对数必须一致，否则读端 ReadHead 按行数读索引会把数据区当索引消费，偏移全部错位。
                if (!indexMap.TryAdd(id, dataStartOffset))
                {
                    if (result != null)
                        result.Error($"Duplicate primary key '{id}'", null, className, fieldName: "Id");
                    else
                        Log.Error($"主键ID {id} 在 {className} 中重复，仅保留首条，后续重复行已丢弃，请检查！");
                    continue;
                }

                dataByteBuf.WriterIndex = dataStartOffset; // 现在可以安全设置偏移量
                // 分离普通数据和集合数据
                var (primitiveData, collectionData) = SeparateDataTypes(dataRow, className, ctx.SharedCacheInfos);
                // 写入普通数据
                WritePrimitiveData(dataByteBuf, primitiveData, className, id, ctx.SharedCacheInfos);
                // 写入集合数据
                WriteCollectionData(dataByteBuf, collectionData, className, id, ctx.SharedCacheInfos);

                dataStartOffset = dataByteBuf.WriterIndex; // 更新下一条内容起始位置
            }

            var keyType = "int";

            if (ctx.ClassIndexType.TryGetValue(className, out var indexType))
            {
                if (!SchemaValidator.IsKeyType(indexType))
                {
                    if (result != null)
                        result.Error($"Invalid key type '{indexType}' for table", null, className, fieldName: "Id");
                    else
                        Log.Error($"{className}的索引类型{indexType}不是索引类型，请检查！");
                    return;
                }
                keyType = indexType.ToLower();
            }

                        // 写入共享缓存信息（如果有）
            List<FieldSharedCacheInfo> sharedCacheInfos = null;
            bool hasSharedCache = ctx.SharedCacheInfos.TryGetValue(className, out sharedCacheInfos)
                                  && sharedCacheInfos.Any(f => f.EnableSharedCache);

            if (hasSharedCache)
            {
                // 统计有多少个字段启用了共享缓存
                int enabledCount = sharedCacheInfos.Count(f => f.EnableSharedCache);

                headByteBuf.WriteInt(enabledCount);

                foreach (var cacheInfo in sharedCacheInfos)
                {
                    if (!cacheInfo.EnableSharedCache)
                        continue;

                    // 写入字段名
                    headByteBuf.WriteString(cacheInfo.FieldName);

                    // v2：存在空引用行的字段在槽0预置空值，条目数 +1；读端按普通缓存值读入 cache[0]
                    headByteBuf.WriteInt(cacheInfo.UniqueCount + (cacheInfo.ReserveEmptySlot ? 1 : 0));
                    if (cacheInfo.ReserveEmptySlot)
                    {
                        WriteValue(headByteBuf, cacheInfo.FieldType, CreateEmptyCacheValue(cacheInfo.FieldType));
                    }

                    // 逐个写入缓存值（已在分析阶段完成解析）
                    foreach (object parsedValue in cacheInfo.IndexToValue)
                    {
                        WriteValue(headByteBuf, cacheInfo.FieldType, parsedValue);
                    }
                }
            }

            // 行数必须等于实际写入的索引对数（重复主键已丢弃），读端按此数循环读 key+offset
            headByteBuf.WriteInt(indexMap.Count);
            foreach (var (id, offset) in indexMap)
            {
                WriteKey(keyType, id, offset, headByteBuf);
            }

            headByteBuf.WriteBytes(dataByteBuf.CopyData());

            var filePath = Path.Combine(outputPath, isConfig ? $"{className}Config.bytes" :$"{className}.bytes");
            string tempPath = filePath + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, headByteBuf.CopyData());
                if (File.Exists(filePath))
                    File.Delete(filePath);
                File.Move(tempPath, filePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

        }

        #endregion

        #region 内部方法

        // 根据类型写入值到ByteBuf
        internal static void WriteValue(ByteBuf byteBuf, string type, object value)
        {
            if (!TypeRegistry.Instance.IsKnownType(type))
            {
                throw new TypeNotSupportedException($"Unsupported config field type '{type}' when writing bytes.");
            }

            switch (type.ToLower())
            {
                case SupportType.Bool:
                    byteBuf.WriteBool(value != null && (bool)value);
                    break;
                case SupportType.Int:
                case SupportType.LanguageType:
                    byteBuf.WriteInt(value == null ? 0 : (int)value);
                    break;
                case SupportType.UInt:
                    var uintValue = value == null ? 0u : (uint)value;
                    if (uintValue > int.MaxValue)
                    {
                        throw new ValidationException($"uint value {uintValue} exceeds ByteBuf int range.");
                    }
                    byteBuf.WriteInt((int)uintValue);
                    break;
                case SupportType.Long:
                    byteBuf.WriteLong(value == null ? 0L : (long)value);
                    break;
                case SupportType.ULong:
                    var ulongValue = value == null ? 0UL : (ulong)value;
                    if (ulongValue > long.MaxValue)
                    {
                        throw new ValidationException($"ulong value {ulongValue} exceeds ByteBuf long range.");
                    }
                    byteBuf.WriteLong((long)ulongValue);
                    break;
                case SupportType.Float:
                    byteBuf.WriteFloat(value == null ? 0f : (float)value);
                    break;
                case SupportType.Double:
                    byteBuf.WriteDouble(value == null ? 0d : (double)value);
                    break;
                case SupportType.UShort:
                case SupportType.Short:
                    byteBuf.WriteShort(value == null ? (short)0 : (short)value);
                    break;
                case SupportType.Byte:
                    byteBuf.WriteByte(value == null ? (byte)0 : (byte)value);
                    break;
                case SupportType.String:
                case SupportType.StringFull:
                    byteBuf.WriteString(value?.ToString() ?? string.Empty);
                    break;
                case SupportType.Vector2:
                case SupportType.Vector2Full:
                    {
                        if (value == null)
                            byteBuf.WriteVector2(default(System.Numerics.Vector2));
                        else
                        {
                            var v = (Vector2)value;
                            byteBuf.WriteVector2(new System.Numerics.Vector2(v.x, v.y));
                        }
                    }
                    break;
                case SupportType.Vector3:
                case SupportType.Vector3Full:
                    {
                        if (value == null)
                            byteBuf.WriteVector3(default(System.Numerics.Vector3));
                        else
                        {
                            var v = (Vector3)value;
                            byteBuf.WriteVector3(new System.Numerics.Vector3(v.x, v.y, v.z));
                        }
                    }
                    break;
                case SupportType.Vector4:
                case SupportType.Vector4Full:
                    {
                        if (value == null)
                            byteBuf.WriteVector4(default(System.Numerics.Vector4));
                        else
                        {
                            var v = (Vector4)value;
                            byteBuf.WriteVector4(new System.Numerics.Vector4(v.x, v.y, v.z, v.w));
                        }
                    }
                    break;
                case SupportType.Quaternion:
                case SupportType.QuaternionFull:
                    {
                        if (value == null)
                            byteBuf.WriteQuaternion(default(System.Numerics.Quaternion));
                        else
                        {
                            var q = (Quaternion)value;
                            byteBuf.WriteQuaternion(new System.Numerics.Quaternion(q.x, q.y, q.z, q.w));
                        }
                    }
                    break;
                default:
                    if (value == null)
                    {
                        throw new InvalidOperationException(
                            $"配置表序列化错误：容器类型 '{type}' 的值为 null，无法写入二进制。请检查 Excel 数据。");
                    }
                    if (type.EndsWith(SupportType.Array, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteArray(byteBuf, (Array)value, type);
                    }
                    else if (type.StartsWith(SupportType.List, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteList(byteBuf, (IList)value, type);
                    }
                    else if (type.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) || type.StartsWith(SupportType.DictionaryFull,StringComparison.OrdinalIgnoreCase))
                    {
                        WriteDictionary(byteBuf, (IDictionary)value, type);
                    }
                    else if (type.StartsWith(SupportType.RewardObject, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteList(byteBuf, (IList)value, "list<list<long>>");
                    }
                    else if (type.StartsWith(SupportType.ConditionObject, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteList(byteBuf, (IList)value, "list<list<string>>");
                    }
                    else if (type.StartsWith(SupportType.EffectCfgObject, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteList(byteBuf, (IList)value, "list<list<string>>");
                    }
                    else if (type.StartsWith(SupportType.GradedPriceObject, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteList(byteBuf, (IList)value, "list<list<long>>");
                    }
                    else if (type.StartsWith(SupportType.ValueTuple, StringComparison.OrdinalIgnoreCase))
                    {
                        // 解析元组内部类型
                        string innerTypes = type.Substring(SupportType.ValueTuple.Length, type.Length - SupportType.ValueTuple.Length - 1);
                        string[] typeArgs = innerTypes.Split(',').Select(t => t.Trim()).ToArray();
                        int tupleSize = typeArgs.Length;

                        // 反射获取元组元素值并写入
                        var tupleType = value.GetType();
                        for (var i = 0; i < tupleSize; i++)
                        {
                            var itemType = typeArgs[i];
                            var itemProperty = tupleType.GetField($"Item{i + 1}");
                            if (itemProperty == null)
                            {
                                throw new ValidationException($"ValueTuple has no Item{i + 1} property");
                            }
                            var itemValue = itemProperty.GetValue(value);
                            WriteValue(byteBuf, itemType, itemValue);
                        }
                    }
                    else
                    {
                        throw new TypeNotSupportedException($"未知类型{type}");
                    }
                    break;
            }
        }

        // 写入数组类型
        private static void WriteArray(ByteBuf byteBuf, Array array, string type)
        {
            byteBuf.WriteSize(array.Length);
            string elementType = type.Substring(0, type.Length - 2);

            foreach (var item in array)
            {
                WriteValue(byteBuf, elementType, item);
            }
        }


        // 写入List类型
        private static void WriteList(ByteBuf byteBuf, IList list, string type)
        {
            byteBuf.WriteSize(list.Count);
            string elementType = type.Substring(ListPrefixLen, type.Length - 6);

            foreach (var item in list)
            {
                WriteValue(byteBuf, elementType, item);
            }
        }

        // 写入Dictionary类型
        private static void WriteDictionary(ByteBuf byteBuf, IDictionary dict, string type)
        {
            byteBuf.WriteSize(dict.Count);

            int commaIndex = type.IndexOf(',');
            if (commaIndex == -1)
            {
                throw new ValidationException("Dictionary 类型必须包含两个用逗号分隔的类型");
            }
            string keyType = null;
            if (type.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase))
            {
                keyType = type.Substring(DictPrefixLen, commaIndex - 5);
            }
            else if(type.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase))
            {
                keyType = type.Substring(DictFullPrefixLen, commaIndex - 11);
            }
            string valueType = type.Substring(commaIndex + 1, type.Length - commaIndex - 2);

            foreach (DictionaryEntry entry in dict)
            {
                WriteValue(byteBuf, keyType, entry.Key);
                WriteValue(byteBuf, valueType, entry.Value);
            }
        }

        private static void WriteKey(string type, string id, int offset, ByteBuf headBuf)
        {
            var value = ReadExcel.ParseValue(type, id, string.Empty);
            WriteValue(headBuf, type, value);
            headBuf.WriteInt(offset);
        }

        #region 共享缓存 v2「槽0空值」布局

        private const int EmptySlotIndex = 0;
        private const int LegacyNullIndex = -1;

        /// <summary>
        /// 预扫描本表各共享缓存字段是否存在空引用行；存在且类型支持时启用 v2「槽0空值」布局。
        /// 判定与行写入共用 TryGetSharedCacheIndex，保证表头与行编码一致。
        /// 主键重复的行在主循环里会被丢弃，本预扫描不排除这些行，可能多判一次"存在空引用"，
        /// 后果只是多写 1 字节的槽 0，读端仍然正确，属可接受的保守方向。
        /// </summary>
        private static void PrepareEmptySlots(string className, List<FieldSharedCacheInfo> cacheInfos, List<ConfigData[]> dataList)
        {
            foreach (var cacheInfo in cacheInfos)
            {
                cacheInfo.ReserveEmptySlot = false;
                if (!cacheInfo.EnableSharedCache || !SupportsEmptySlot(cacheInfo.FieldType))
                    continue;

                foreach (var row in dataList)
                {
                    var data = FindField(row, cacheInfo.FieldName);
                    if (data != null && !TryGetSharedCacheIndex(cacheInfo, data.Data, out _))
                    {
                        cacheInfo.ReserveEmptySlot = true;
                        Log.Info("[共享缓存] 表:{0} 字段:{1} 启用槽0空值(v2)", className, cacheInfo.FieldName);
                        break;
                    }
                }
            }
        }

        private static ConfigData FindField(ConfigData[] row, string fieldName)
        {
            foreach (var d in row)
            {
                if (d.Name == fieldName)
                    return d;
            }
            return null;
        }

        /// <summary>
        /// 写共享缓存行内索引。v2（ReserveEmptySlot）：空引用写 0（1 字节），有效值写 index + 1；
        /// v1：空引用写 -1、有效值写 index。读端 `idx &lt; 0 ? 空实例 : cache[idx]` 对两种布局均正确。
        /// </summary>
        private static void WriteSharedCacheIndex(ByteBuf byteBuf, FieldSharedCacheInfo cacheInfo, string rawData)
        {
            if (TryGetSharedCacheIndex(cacheInfo, rawData, out var index))
            {
                byteBuf.WriteInt(cacheInfo.ReserveEmptySlot ? index + 1 : index);
                return;
            }

            // 空引用：空单元格，或 analyzer 判为空数据未入缓存（如 RewardObject 的 []）——读端固定读一个 int
            byteBuf.WriteInt(cacheInfo.ReserveEmptySlot ? EmptySlotIndex : LegacyNullIndex);
        }

        private static bool TryGetSharedCacheIndex(FieldSharedCacheInfo cacheInfo, string rawData, out int index)
        {
            index = LegacyNullIndex;
            return !string.IsNullOrEmpty(rawData) && cacheInfo.ValueToIndex.TryGetValue(rawData, out index);
        }

        /// <summary>
        /// 槽0空值仅对「空值序列化为单个 WriteSize(0)，且反序列化结果与读端静态空实例等价」的类型启用：
        /// str/string、list&lt;&gt;、dict&lt;&gt;/dictionary&lt;&gt;、RewardObject/ConditionObject/EffectCfgObject/GradedPriceObject。
        /// ValueTuple 等其它类型（读端为 default(T)）沿用 v1（-1）编码。
        /// </summary>
        private static bool SupportsEmptySlot(string fieldType)
        {
            if (string.IsNullOrEmpty(fieldType))
                return false;

            // string / stringfull
            if (string.Equals(fieldType, SupportType.String, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.StringFull, StringComparison.OrdinalIgnoreCase))
                return true;

            // List<T>
            if (fieldType.StartsWith(SupportType.List, StringComparison.OrdinalIgnoreCase) && fieldType.EndsWith(">"))
                return true;

            // Dictionary<TKey, TValue>
            if ((fieldType.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) ||
                 fieldType.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase)) && fieldType.EndsWith(">"))
                return true;

            // 四种自定义对象
            if (string.Equals(fieldType, SupportType.RewardObject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.ConditionObject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.EffectCfgObject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fieldType, SupportType.GradedPriceObject, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// 生成槽0空值。经 WriteValue 序列化后均为单字节（WriteSize(0) / WriteString("")），
        /// 读端反序列化结果与各生成类的 `_{x}Empty` 静态空实例值等价（见 w4 方案文档 2.1.5/2.2.1）。
        /// </summary>
        private static object CreateEmptyCacheValue(string fieldType)
        {
            var t = fieldType.ToLower();

            // str/string：WriteString(string.Empty) → WriteSize(0) 单字节；读端 ReadString 返回 string.Empty
            if (t == SupportType.String || t == SupportType.StringFull)
                return string.Empty;

            // dict/dictionary：空 IDictionary → WriteSize(0)；读端 new Dictionary 后添加 0 个元素
            if (t.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase))
                return new Hashtable();

            // list<> 与四种自定义对象（WriteValue 均按 list<list<…>> 写）→ 空 IList → WriteSize(0)
            return Array.Empty<object>();
        }

        #endregion

        // 写入普通数据
        private static void WritePrimitiveData(ByteBuf byteBuf, List<ConfigData> primitiveData, string className, string id, Dictionary<string, List<FieldSharedCacheInfo>> sharedCacheInfos)
        {
            foreach (var data in primitiveData)
            {
                try
                {
                    // 检查该字段是否启用了共享缓存
                    FieldSharedCacheInfo cacheInfo = null;
                    if (sharedCacheInfos.TryGetValue(className, out var classCacheInfos))
                    {
                        cacheInfo = classCacheInfos.FirstOrDefault(f => f.FieldName == data.Name && f.EnableSharedCache);
                    }

                    if (cacheInfo != null && cacheInfo.EnableSharedCache)
                    {
                        // 共享缓存模式：只写入索引（布局见 WriteSharedCacheIndex）
                        WriteSharedCacheIndex(byteBuf, cacheInfo, data.Data);
                    }
                    else
                    {
                        // 普通模式：写入完整值
                        var value = ReadExcel.ParseValue(data.Type, data.Data, "");
                        WriteValue(byteBuf, data.Type, value);
                    }
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("注意！！！", $"\n普通数据导表错误表名:{className} 表字段名:{data.Name} 索引id：{id} 数据：{data.Data}\n" +
                                                             $" 错误信息{e}", "确定");
                    throw new ExcelExportException($"导表错误 错误信息{e}", className);
                }
            }
        }

        // 写入集合数据
        private static void WriteCollectionData(ByteBuf byteBuf, List<ConfigData> collectionData, string className, string id, Dictionary<string, List<FieldSharedCacheInfo>> sharedCacheInfos)
        {
            foreach (var data in collectionData)
            {
                try
                {
                    // 检查该字段是否启用了共享缓存
                    FieldSharedCacheInfo cacheInfo = null;
                    if (sharedCacheInfos.TryGetValue(className, out var classCacheInfos))
                    {
                        cacheInfo = classCacheInfos.FirstOrDefault(f => f.FieldName == data.Name && f.EnableSharedCache);
                    }

                    if (cacheInfo != null && cacheInfo.EnableSharedCache)
                    {
                        // 共享缓存模式：只写入索引（布局见 WriteSharedCacheIndex）。
                        // 注：SeparateDataTypes 已把启用缓存的字段全部归入 primitive 区，此分支实际不可达，仅保持行为对称。
                        WriteSharedCacheIndex(byteBuf, cacheInfo, data.Data);
                    }
                    else
                    {
                        // 普通模式：写入完整值
                        var value = ReadExcel.ParseValue(data.Type, data.Data, "");
                        WriteValue(byteBuf, data.Type, value);
                    }
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("注意！！！", $"\n集合数据导表错误\n表名:{className} 表字段名:{data.Name} 索引id：{id} 数据：{data.Data}\n" +
                                                             $" 错误信息{e}", "确定");
                    throw new ExcelExportException($"导表错误 错误信息{e}", className);
                }
            }
        }

        // 分离普通数据和集合数据
        // 共享缓存字段写入的是 int 索引（primitive 语义），必须归入 primitiveData，
        // 与生成代码 GetContainerString 的读取顺序对齐（共享缓存字段的 ReadInt 在 primitive 区）。
        // 否则容器类型(List/RewardObject 等)的索引会被写入 collectionData，与 ctor 读取顺序错位。
        private static (List<ConfigData>, List<ConfigData>) SeparateDataTypes(ConfigData[] dataRow,
            string className, Dictionary<string, List<FieldSharedCacheInfo>> sharedCacheInfos)
        {
            var primitiveData = new List<ConfigData>();
            var collectionData = new List<ConfigData>();

            // 预解析本表启用了共享缓存的字段名集合，避免逐字段 LINQ 查找
            HashSet<string> enabledCacheFields = null;
            if (sharedCacheInfos != null && sharedCacheInfos.TryGetValue(className, out var classCacheInfos))
            {
                enabledCacheFields = new HashSet<string>(
                    classCacheInfos.Where(c => c.EnableSharedCache).Select(c => c.FieldName),
                    StringComparer.OrdinalIgnoreCase);
            }

            foreach (var data in dataRow)
            {
                // 启用共享缓存的字段：写入的是 int 索引，归入 primitive 区
                if (enabledCacheFields != null && enabledCacheFields.Contains(data.Name))
                {
                    primitiveData.Add(data);
                    continue;
                }

                // 内联判断是否为集合类型
                if (data.Type.EndsWith(SupportType.Array) ||
                    data.Type.StartsWith(SupportType.List, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.ValueTuple, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.RewardObject, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.ConditionObject, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.EffectCfgObject, StringComparison.OrdinalIgnoreCase) ||
                    data.Type.StartsWith(SupportType.GradedPriceObject, StringComparison.OrdinalIgnoreCase))
                {
                    collectionData.Add(data);
                }
                else
                {
                    primitiveData.Add(data);
                }
            }

            return (primitiveData, collectionData);
        }

        // IsKeyType → SchemaValidator.IsKeyType (已在 Phase 1 迁移)
        // CanEnableSharedCache → SharedCacheAnalyzer.CanEnableSharedCache
        // Validate*ObjectFormat → DataValidator.Validate*ObjectFormat

        #endregion
    }
}