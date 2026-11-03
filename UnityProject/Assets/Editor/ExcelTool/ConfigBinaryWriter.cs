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

                    // 写入缓存条目数量
                    headByteBuf.WriteInt(cacheInfo.UniqueCount);

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
                        // 共享缓存模式：只写入索引
                        if (string.IsNullOrEmpty(data.Data))
                        {
                            // 空字符串，索引设为 -1
                            byteBuf.WriteInt(-1);
                        }
                        else if (cacheInfo.ValueToIndex.TryGetValue(data.Data, out int index))
                        {
                            byteBuf.WriteInt(index);
                        }
                        else
                        {
                            // 值不在缓存中：说明 analyzer 把它当空数据跳过了（如 RewardObject 的 []、ConditionObject 的 [[]] 等，
                            // ValidateRewardObjectFormat 等返回 false 不收集）。读取端(ctor)对共享缓存字段固定读 ReadInt，
                            // 这里必须和空值一样写 -1，否则写完整对象会让读取端字节错位。
                            byteBuf.WriteInt(-1);
                        }
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
                        // 共享缓存模式：只写入索引
                        if (string.IsNullOrEmpty(data.Data))
                        {
                            // 空值，索引设为 -1
                            byteBuf.WriteInt(-1);
                        }
                        else if (cacheInfo.ValueToIndex.TryGetValue(data.Data, out int index))
                        {
                            byteBuf.WriteInt(index);
                        }
                        else
                        {
                            // 值不在缓存中：说明 analyzer 把它当空数据跳过了（如 RewardObject 的 []、ConditionObject 的 [[]] 等，
                            // ValidateRewardObjectFormat 等返回 false 不收集）。读取端(ctor)对共享缓存字段固定读 ReadInt，
                            // 这里必须和空值一样写 -1，否则写完整对象会让读取端字节错位。
                            byteBuf.WriteInt(-1);
                        }
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