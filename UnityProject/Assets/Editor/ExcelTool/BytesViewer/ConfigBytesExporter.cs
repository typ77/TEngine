using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OfficeOpenXml;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// bytes → 可回导 Excel 的逆向导出（线上兜底用）。
    /// 产物符合导表规范：A 列标签行（##lb-row##/##lb-labels##/##lb-target##/##lb-keys##/##lb-names##）+ 数据行，
    /// 可被导表工具直接重导。多语言列还原为文本并在 target 行标 l；
    /// 不含服务器列（bytes 中不存在），导出后会列出源表中未被导出的列清单供与服务器核对。
    /// </summary>
    public static class ConfigBytesExporter
    {
        /// <summary>
        /// 导出为可回导的 xlsx。
        /// </summary>
        /// <param name="table">一期解码结果（须已成功解码且非语言表）。</param>
        /// <param name="savePath">保存路径。</param>
        /// <param name="sourceOnlyColumns">源表中存在但 bytes 未导出的列名（服务器列等，用于人工核对）；未找到源表时为 null。</param>
        /// <returns>错误信息，null 表示成功。</returns>
        public static string ExportToExcel(DecodedTable table, string savePath, out List<string> sourceOnlyColumns)
        {
            sourceOnlyColumns = null;
            if (table == null || !table.Ok)
            {
                return "bytes 未成功解码，无法导出";
            }
            if (table.IsLanguageFile)
            {
                return "语言表不支持导出（由配置表多语言列重新生成）";
            }

            var assembly = typeof(ConfigSystem).Assembly;
            var configType = assembly.GetType("TEngine." + table.TableName);
            var itemType = configType?.BaseType?.GetGenericArguments()[1];
            if (itemType == null)
            {
                return $"未找到生成类 TEngine.{table.TableName}";
            }

            // 导出字段 = Item 公共属性（声明序），排除语言原始 index 属性（xxxKey 且存在 xxx 属性）
            var props = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propNames = new HashSet<string>(props.Select(p => p.Name));
            var exportProps = new List<PropertyInfo>();
            foreach (var prop in props)
            {
                if (prop.Name.EndsWith("Key", StringComparison.Ordinal) &&
                    prop.Name.Length > 3 &&
                    propNames.Contains(prop.Name.Substring(0, prop.Name.Length - 3)))
                {
                    continue;
                }
                exportProps.Add(prop);
            }
            if (exportProps.Count == 0)
            {
                return "无可导出字段";
            }

            // 主键字段：格式化值与行 key 全行一致的第一个属性（抽样前 50 行）
            var colIndexOf = new Dictionary<string, int>();
            for (var c = 0; c < table.Columns.Length; c++)
            {
                colIndexOf[table.Columns[c]] = c;
            }
            PropertyInfo keyProp = null;
            var sample = Math.Min(table.Rows.Count, 50);
            foreach (var prop in exportProps)
            {
                var isKey = true;
                for (var i = 0; i < sample && isKey; i++)
                {
                    var v = table.Rows[i][colIndexOf[prop.Name]];
                    if (!string.Equals(ConfigBytesDecoder.FormatValue(v), table.RowTexts[i][0], StringComparison.Ordinal))
                    {
                        isKey = false;
                    }
                }
                if (isKey)
                {
                    keyProp = prop;
                    break;
                }
            }
            var warnings = new List<string>();
            if (keyProp == null)
            {
                keyProp = exportProps[0];
                warnings.Add("未能确定主键字段，已默认使用首字段");
            }
            // 主键列放到第一数据列（导表规范建议）
            exportProps.Remove(keyProp);
            exportProps.Insert(0, keyProp);

            try
            {
                using var package = new ExcelPackage();
                var sheetName = ConfigExcelDiff.GetSheetName(table.TableName);
                var ws = package.Workbook.Worksheets.Add(sheetName);

                const int headerRows = 4;
                const int labelRow = 5;
                const int targetRow = 6;
                const int keysRow = 7;
                const int namesRow = 8;
                const int dataStartRow = 9;

                for (var r = 1; r <= headerRows; r++)
                {
                    ws.Cells[r, 1].Value = "##lb-row##";
                }
                ws.Cells[labelRow, 1].Value = "##lb-labels##";
                ws.Cells[targetRow, 1].Value = "##lb-target##";
                ws.Cells[keysRow, 1].Value = "##lb-keys##";
                ws.Cells[namesRow, 1].Value = "##lb-names##";

                for (var f = 0; f < exportProps.Count; f++)
                {
                    var prop = exportProps[f];
                    var col = f + 2; // A 列为标签列
                    var isLanguage = propNames.Contains(prop.Name + "Key");
                    var isKey = prop == keyProp;

                    if (isKey)
                    {
                        ws.Cells[labelRow, col].Value = "#" + prop.Name;
                    }
                    if (isLanguage)
                    {
                        ws.Cells[targetRow, col].Value = "l";
                    }
                    ws.Cells[keysRow, col].Value = ClrTypeToExcelType(prop.PropertyType, warnings);
                    ws.Cells[namesRow, col].Value = prop.Name;
                }

                for (var i = 0; i < table.Rows.Count; i++)
                {
                    for (var f = 0; f < exportProps.Count; f++)
                    {
                        var prop = exportProps[f];
                        var value = table.Rows[i][colIndexOf[prop.Name]];
                        ws.Cells[dataStartRow + i, f + 2].Value = ValueToExcelText(value, warnings);
                    }
                }

                var fileInfo = new FileInfo(savePath);
                if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
                {
                    fileInfo.Directory.Create();
                }
                package.SaveAs(fileInfo);
            }
            catch (Exception e)
            {
                return "写 Excel 失败: " + (e.InnerException ?? e).Message;
            }

            // 源表核对：列出存在但 bytes 未导出的列（服务器列等）
            try
            {
                var index = ConfigExcelDiff.GetSheetIndex();
                if (index.TryGetValue(ConfigExcelDiff.GetSheetName(table.TableName), out var srcPath))
                {
                    foreach (var s in SheetData.LoadWorkbook(srcPath))
                    {
                        if (s.SheetName != ConfigExcelDiff.GetSheetName(table.TableName))
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(s.GetCell(1, 1)) || !s.GetCell(1, 1).StartsWith("##", StringComparison.Ordinal))
                        {
                            break;
                        }
                        var header = SheetHeaderReader.Read(s, srcPath, s.SheetName);
                        sourceOnlyColumns = header.NameMap.Values
                            .Where(name => !exportProps.Any(p => p.Name == name))
                            .ToList();
                        break;
                    }
                }
            }
            catch
            {
                sourceOnlyColumns = null; // 源表核对失败不阻断导出
            }

            foreach (var warning in warnings)
            {
                Log.Warning($"[BytesViewer] 导出 {table.TableName}: {warning}");
            }
            return null;
        }

        /// <summary>
        /// CLR 类型 → 导表类型语法（##lb-keys## 行）。未知类型降级为 string 并收集警告。
        /// </summary>
        private static string ClrTypeToExcelType(Type type, List<string> warnings)
        {
            if (type == typeof(int)) return SupportType.Int;
            if (type == typeof(long)) return SupportType.Long;
            if (type == typeof(short)) return SupportType.Short;
            if (type == typeof(byte)) return SupportType.Byte;
            if (type == typeof(sbyte)) return SupportType.SByte;
            if (type == typeof(ushort)) return SupportType.UShort;
            if (type == typeof(uint)) return SupportType.UInt;
            if (type == typeof(ulong)) return SupportType.ULong;
            if (type == typeof(bool)) return SupportType.Bool;
            if (type == typeof(float)) return SupportType.Float;
            if (type == typeof(double)) return SupportType.Double;
            if (type == typeof(string)) return SupportType.StringFull;
            if (type == typeof(DateTime)) return SupportType.DateTime;
            if (type == typeof(RewardObject)) return SupportType.RewardObject;
            if (type == typeof(System.Numerics.Vector2) || type == typeof(UnityEngine.Vector2)) return SupportType.Vector2;
            if (type == typeof(System.Numerics.Vector3) || type == typeof(UnityEngine.Vector3)) return SupportType.Vector3;
            if (type == typeof(System.Numerics.Vector4) || type == typeof(UnityEngine.Vector4)) return SupportType.Vector4;
            if (type == typeof(UnityEngine.Quaternion)) return SupportType.Quaternion;
            if (type == typeof(UnityEngine.Color)) return SupportType.Color;
            if (type.IsArray)
            {
                return ClrTypeToExcelType(type.GetElementType(), warnings) + SupportType.Array;
            }
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();
                if (definition == typeof(List<>))
                {
                    return SupportType.List + ClrTypeToExcelType(args[0], warnings) + ">";
                }
                if (definition == typeof(Dictionary<,>))
                {
                    return SupportType.Dictionary + ClrTypeToExcelType(args[0], warnings) + "," + ClrTypeToExcelType(args[1], warnings) + ">";
                }
                if (definition.Name.StartsWith("ValueTuple`", StringComparison.Ordinal))
                {
                    return SupportType.ValueTuple + string.Join(",", args.Select(a => ClrTypeToExcelType(a, warnings))) + ">";
                }
            }
            warnings.Add($"字段类型 {type.Name} 无法映射导表类型，已按 string 导出");
            return SupportType.StringFull;
        }

        /// <summary>
        /// 值 → 归一比较/可回导文本。Excel 比对双侧统一用本口径序列化（避免 RewardObject 等
        /// 复合类型因形态不同产生假差异）；需保证 ReadExcel.ParseValue 可重解析。
        /// 集合/复合类型按导表文档的括号语法序列化；无法归一的值降级 JSON 并收集警告。
        /// </summary>
        public static string ValueToExcelText(object value, List<string> warnings)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is string s)
            {
                return s;
            }
            if (value is bool b)
            {
                return b ? "TRUE" : "FALSE";
            }
            if (value is DateTime dt)
            {
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (value is float || value is double)
            {
                return ((IFormattable)value).ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is RewardObject rewardObject)
            {
                // 空集合导出为空单元格：源表中此类值本就是空单元格（导表时按默认值处理），保证回导形态一致
                if (rewardObject.Items.Count == 0)
                {
                    return string.Empty;
                }
                var sb = new StringBuilder("[");
                var items = rewardObject.Items;
                for (var i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var item = items[i];
                    sb.Append('[').Append(item.RewardType).Append(',').Append(item.CfgId).Append(',').Append(item.Amount).Append(']');
                }
                sb.Append(']');
                return sb.ToString();
            }
            if (value is IDictionary dict)
            {
                if (dict.Count == 0)
                {
                    return string.Empty;
                }
                var sb = new StringBuilder("[");
                var first = true;
                foreach (DictionaryEntry entry in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(ElementText(entry.Key, warnings)).Append(',').Append(ElementText(entry.Value, warnings));
                }
                sb.Append(']');
                return sb.ToString();
            }
            if (value is IEnumerable enumerable)
            {
                var sb = new StringBuilder("[");
                var first = true;
                var count = 0;
                foreach (var item in enumerable)
                {
                    count++;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(ElementText(item, warnings));
                }
                sb.Append(']');
                // 空集合导出为空单元格（与源表空值形态一致，见上）
                return count == 0 ? string.Empty : sb.ToString();
            }
            if (value is IFormattable)
            {
                return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            }

            // 复合标量：Vector/Quaternion/Color 等，按 X,Y,Z,W（或小写）轴属性递归展开，遇到缺失轴即截断
            var valueType = value.GetType();
            foreach (var names in new[] { new[] { "X", "Y", "Z", "W" }, new[] { "x", "y", "z", "w" } })
            {
                var sb = new StringBuilder("[");
                var axisCount = 0;
                foreach (var memberName in names)
                {
                    var memberValue = GetMemberValue(valueType, value, memberName);
                    if (memberValue == null)
                    {
                        break;
                    }
                    if (axisCount > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append(ElementText(memberValue, warnings));
                    axisCount++;
                }
                if (axisCount >= 2)
                {
                    sb.Append(']');
                    return sb.ToString();
                }
            }
            if (valueType.Name.StartsWith("ValueTuple`", StringComparison.Ordinal))
            {
                var sb = new StringBuilder("[");
                for (var i = 1; ; i++)
                {
                    var field = valueType.GetField($"Item{i}");
                    if (field == null) break;
                    if (i > 1) sb.Append(',');
                    sb.Append(ElementText(field.GetValue(value), warnings));
                }
                sb.Append(']');
                return sb.ToString();
            }

            warnings.Add($"值 {valueType.Name} 无法序列化为可回导文本，已降级 JSON（重导时该值需人工核对）");
            return ConfigBytesDecoder.FormatValue(value);
        }

        /// <summary>按字段/属性名取实例成员值，取不到返回 null。</summary>
        private static object GetMemberValue(Type type, object instance, string name)
        {
            var field = type.GetField(name);
            if (field != null)
            {
                return field.GetValue(instance);
            }
            return type.GetProperty(name)?.GetValue(instance);
        }

        /// <summary>集合/复合结构内的元素序列化：字符串含分隔符时加引号转义。</summary>
        private static string ElementText(object value, List<string> warnings)
        {
            var text = ValueToExcelText(value, warnings);
            if (value is string && (text.IndexOfAny(new[] { ',', '"', '[', ']', '\n', '\r' }) >= 0))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }
    }
}
