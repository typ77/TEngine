using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExcelDataReader;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 单个单元格的值差异（两端共有行内，Excel 与 bytes 值不同）。
    /// </summary>
    public sealed class CellDiff
    {
        /// <summary>行主键（格式化文本）。</summary>
        public string Key;
        /// <summary>bytes 行下标。</summary>
        public int RowIndex;
        /// <summary>字段名。</summary>
        public string Field;
        /// <summary>Excel 侧值（按导表解析语义归一后的文本）。</summary>
        public string ExcelText;
        /// <summary>bytes 侧值。</summary>
        public string BytesText;
        /// <summary>Excel 单元格位置（如 F12）。</summary>
        public string ExcelCellRef;
    }

    /// <summary>
    /// 一行数据的快照：Excel 新增行存单元格原文与行号；bytes 独有行存格式化值。
    /// </summary>
    public sealed class RowSnapshot
    {
        /// <summary>行主键。</summary>
        public string Key;
        /// <summary>Excel 行号（仅 Excel 侧快照有效）。</summary>
        public int ExcelRowNumber;
        /// <summary>字段名 → 值文本（按列序）。</summary>
        public List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>();
    }

    /// <summary>
    /// 单表 Excel↔bytes 比对结果。
    /// </summary>
    public sealed class TableDiffResult
    {
        public string TableName;
        /// <summary>是否定位到源 sheet。</summary>
        public bool ExcelFound;
        /// <summary>源 xlsx 绝对路径。</summary>
        public string ExcelPath = string.Empty;
        /// <summary>非空表示比对过程出错（Excel 侧解析失败等）。</summary>
        public string Error = string.Empty;
        /// <summary>值修改（两端共有行）。</summary>
        public List<CellDiff> CellChanges = new List<CellDiff>();
        /// <summary>Excel 新增行（bytes 没有）。</summary>
        public List<RowSnapshot> ExcelOnlyRows = new List<RowSnapshot>();
        /// <summary>bytes 独有行（Excel 已删除）。</summary>
        public List<RowSnapshot> BytesOnlyRows = new List<RowSnapshot>();
        /// <summary>Excel 新增列（bytes 没有）。</summary>
        public List<string> ExcelOnlyColumns = new List<string>();
        /// <summary>Excel 删除列（bytes 有 Excel 没有，语言原始 index 列除外）。</summary>
        public List<string> BytesOnlyColumns = new List<string>();
        /// <summary>两端一致且可比对的行数。</summary>
        public int MatchedRows;
        /// <summary>比对时的源文件 UTC 修改时间（失效检测用）。</summary>
        public DateTime ExcelLastWriteUtc;

        /// <summary>是否存在任何差异。</summary>
        public bool HasChange =>
            CellChanges.Count > 0 || ExcelOnlyRows.Count > 0 || BytesOnlyRows.Count > 0 ||
            ExcelOnlyColumns.Count > 0 || BytesOnlyColumns.Count > 0;

        /// <summary>是否完全一致（找到源表、无错误、无差异）。</summary>
        public bool IsIdentical => ExcelFound && string.IsNullOrEmpty(Error) && !HasChange;
    }

    /// <summary>
    /// Excel↔bytes 比对内核。Excel 侧复用导表工具同一套标签行解析（SheetHeaderReader）与值解析（ReadExcel.ParseValue），
    /// 跳行/忽略列/重复主键等规则与导表读端一致，避免格式性假差异。按解析后语义比对，空单元格=默认值。
    /// </summary>
    public static class ConfigExcelDiff
    {
        private static Dictionary<string, string> _sheetToFileIndex;

        /// <summary>配置源表根目录（与导表工具同口径：dataPath + ConfigTablePath）。</summary>
        public static string GetTablesRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath));
        }

        /// <summary>bytes 表名 → 源 sheet 名（{Sheet}Config.bytes → Sheet）。</summary>
        public static string GetSheetName(string tableName)
        {
            return tableName.EndsWith("Config", StringComparison.Ordinal)
                ? tableName.Substring(0, tableName.Length - "Config".Length)
                : tableName;
        }

        /// <summary>
        /// sheet名 → xlsx路径 索引。只读工作簿元数据（不逐行解析），结果会话内缓存。
        /// </summary>
        public static Dictionary<string, string> GetSheetIndex(bool force = false)
        {
            if (!force && _sheetToFileIndex != null)
            {
                return _sheetToFileIndex;
            }
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            var root = GetTablesRoot();
            if (Directory.Exists(root))
            {
                var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    if (!fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (fileName.StartsWith("~", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    try
                    {
                        using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = ExcelReaderFactory.CreateReader(stream);
                        do
                        {
                            var sheetName = reader.Name;
                            if (!string.IsNullOrEmpty(sheetName) && !index.ContainsKey(sheetName))
                            {
                                index[sheetName] = file;
                            }
                        } while (reader.NextResult());
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[BytesViewer] 无法读取工作簿 {fileName}: {e.Message}");
                    }
                }
            }
            _sheetToFileIndex = index;
            return index;
        }

        /// <summary>清除源表索引缓存（Tables 目录内容变化后调用）。</summary>
        public static void InvalidateIndex()
        {
            _sheetToFileIndex = null;
        }

        /// <summary>
        /// 比对单表。excelPathOverride 供测试/特殊场景显式指定源 xlsx；默认按索引定位。
        /// </summary>
        public static TableDiffResult DiffTable(DecodedTable decoded, string excelPathOverride = null)
        {
            var result = new TableDiffResult { TableName = decoded.TableName };
            if (!decoded.Ok)
            {
                result.Error = "bytes 解码失败: " + decoded.Error;
                return result;
            }

            var sheetName = GetSheetName(decoded.TableName);
            var excelPath = excelPathOverride;
            if (excelPath == null)
            {
                var index = GetSheetIndex();
                index.TryGetValue(sheetName, out excelPath);
            }
            if (string.IsNullOrEmpty(excelPath))
            {
                result.ExcelFound = false;
                return result;
            }
            result.ExcelFound = true;
            result.ExcelPath = excelPath;
            try
            {
                result.ExcelLastWriteUtc = File.GetLastWriteTimeUtc(excelPath);
            }
            catch
            {
                // 取不到时间戳不影响比对，仅失效检测不可用
            }

            try
            {
                DiffCore(decoded, excelPath, sheetName, result);
            }
            catch (Exception e)
            {
                result.Error = "Excel 解析失败: " + (e.InnerException ?? e).Message;
            }
            return result;
        }

        private static void DiffCore(DecodedTable decoded, string excelPath, string sheetName, TableDiffResult result)
        {
            SheetData sheet = null;
            foreach (var s in SheetData.LoadWorkbook(excelPath))
            {
                if (s.SheetName == sheetName)
                {
                    sheet = s;
                    break;
                }
            }
            if (sheet == null)
            {
                result.Error = $"工作簿中未找到 sheet '{sheetName}'";
                return;
            }
            if (string.IsNullOrEmpty(sheet.GetCell(1, 1)) || !sheet.GetCell(1, 1).StartsWith("##", StringComparison.Ordinal))
            {
                result.Error = "该 sheet 非导表内容（A1 无 ## 标记）";
                return;
            }

            var header = SheetHeaderReader.Read(sheet, excelPath, sheetName);
            if (header.KeyIndex == 0 || !header.TypeMap.ContainsKey(header.KeyIndex))
            {
                result.Error = "无法确定主键列（##lb-labels## 行）";
                return;
            }

            // 有效列 = 有类型 && 有字段名（s 列 / 无类型列已被 SheetHeaderReader 排除）
            var excelCols = new List<int>(header.TypeMap.Keys);
            excelCols.RemoveAll(col => !header.NameMap.ContainsKey(col));
            excelCols.Sort();
            var excelColByName = new Dictionary<string, int>();
            foreach (var col in excelCols)
            {
                excelColByName[header.NameMap[col]] = col;
            }

            // bytes 侧参与比对的属性列：排除语言原始 index 属性（xxxKey 且存在 xxx 属性）
            var bytesColumnSet = new HashSet<string>(decoded.Columns);
            var bytesExportCols = new List<string>();
            foreach (var name in decoded.Columns)
            {
                if (name == "key")
                {
                    continue;
                }
                if (name.EndsWith("Key", StringComparison.Ordinal) &&
                    name.Length > 3 &&
                    bytesColumnSet.Contains(name.Substring(0, name.Length - 3)))
                {
                    continue;
                }
                bytesExportCols.Add(name);
            }

            // Excel 行集：重复主键只认首条（与写端一致）；##lb-row## 跳行；空主键截断
            var excelRowMap = new Dictionary<string, RowSnapshot>(StringComparer.Ordinal);
            if (header.DataStartRow >= 1)
            {
                for (var row = header.DataStartRow; row <= sheet.RowCount; row++)
                {
                    if (sheet.GetCell(row, 1) == "##lb-row##")
                    {
                        continue;
                    }
                    var keyCell = sheet.GetCell(row, header.KeyIndex);
                    if (string.IsNullOrEmpty(keyCell))
                    {
                        break;
                    }
                    var keyText = ExcelCellToText(header.TypeMap[header.KeyIndex], keyCell);
                    if (excelRowMap.ContainsKey(keyText))
                    {
                        continue;
                    }
                    var snapshot = new RowSnapshot { Key = keyText, ExcelRowNumber = row };
                    foreach (var col in excelCols)
                    {
                        snapshot.Fields.Add(new KeyValuePair<string, string>(
                            header.NameMap[col],
                            sheet.GetCell(row, col)));
                    }
                    excelRowMap[keyText] = snapshot;
                }
            }

            // 列集合差
            var excelFieldSet = new HashSet<string>(excelCols.Select(col => header.NameMap[col]));
            foreach (var name in bytesExportCols)
            {
                if (!excelFieldSet.Contains(name))
                {
                    result.BytesOnlyColumns.Add(name);
                }
            }
            foreach (var name in excelFieldSet)
            {
                if (!bytesExportCols.Contains(name))
                {
                    result.ExcelOnlyColumns.Add(name);
                }
            }

            // 行级比对
            var bytesColIndex = new Dictionary<string, int>();
            for (var c = 0; c < decoded.Columns.Length; c++)
            {
                bytesColIndex[decoded.Columns[c]] = c;
            }
            var bytesKeySet = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < decoded.RowTexts.Count; i++)
            {
                bytesKeySet.Add(decoded.RowTexts[i][0]);
            }
            for (var i = 0; i < decoded.RowTexts.Count; i++)
            {
                var key = decoded.RowTexts[i][0];
                if (!excelRowMap.TryGetValue(key, out var snapshot))
                {
                    var fields = new List<KeyValuePair<string, string>>();
                    foreach (var fieldName in bytesExportCols)
                    {
                        var fieldValue = decoded.Rows[i][bytesColIndex[fieldName]];
                        fields.Add(new KeyValuePair<string, string>(fieldName, BytesValueToText(fieldValue)));
                    }
                    result.BytesOnlyRows.Add(new RowSnapshot { Key = key, Fields = fields });
                    continue;
                }

                result.MatchedRows++;
                foreach (var fieldName in bytesExportCols)
                {
                    if (!excelColByName.TryGetValue(fieldName, out var col))
                    {
                        continue; // 列差已在列集合差中报告
                    }
                    var bytesText = BytesValueToText(decoded.Rows[i][bytesColIndex[fieldName]]);
                    var excelText = ExcelCellToText(header.TypeMap[col], sheet.GetCell(snapshot.ExcelRowNumber, col));
                    if (excelText != bytesText)
                    {
                        result.CellChanges.Add(new CellDiff
                        {
                            Key = key,
                            RowIndex = i,
                            Field = fieldName,
                            ExcelText = excelText,
                            BytesText = bytesText,
                            ExcelCellRef = ColumnLetter(col) + snapshot.ExcelRowNumber,
                        });
                    }
                }
            }
            foreach (var pair in excelRowMap)
            {
                if (!bytesKeySet.Contains(pair.Key))
                {
                    result.ExcelOnlyRows.Add(pair.Value);
                }
            }
        }

        /// <summary>
        /// Excel 单元格 → 归一比较文本。语言列取原文（与 bytes 解码后的还原文本同口径）；
        /// 其余类型走导表同一解析器后按可回导文本口径归一（与 bytes 反射值同口径，避免
        /// RewardObject/obj 等复合类型因形态不同产生假差异）；解析失败以醒目前缀呈现。
        /// </summary>
        private static string ExcelCellToText(string type, string cellText)
        {
            if (type == SupportType.LanguageType)
            {
                return CollapseEmptyForms(cellText ?? string.Empty);
            }
            if (string.IsNullOrEmpty(cellText))
            {
                cellText = string.Empty;
            }
            try
            {
                var value = ReadExcel.ParseValue(type, cellText, string.Empty);
                return CollapseEmptyForms(ConfigBytesExporter.ValueToExcelText(value, new List<string>()));
            }
            catch (Exception e)
            {
                return $"<解析失败:{(e.InnerException ?? e).Message}>";
            }
        }

        /// <summary>bytes 行值 → 归一比较文本（与 Excel 侧同口径）。</summary>
        private static string BytesValueToText(object value)
        {
            return CollapseEmptyForms(ConfigBytesExporter.ValueToExcelText(value, new List<string>()));
        }

        /// <summary>
        /// 空形态归一：显式空集合文本（"[]"、"[[]]"、花括号空、引号空）与空单元格语义等价
        /// （导表读端都落为默认空对象），不作为差异。
        /// </summary>
        private static string CollapseEmptyForms(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            switch (text)
            {
                case "[]":
                case "[[]]":
                case "{}":
                case "\"[]\"":
                case "\"\"":
                case "''":
                    return string.Empty;
                default:
                    return text;
            }
        }

        /// <summary>列号 → Excel 列字母（1→A）。</summary>
        public static string ColumnLetter(int col)
        {
            var text = string.Empty;
            while (col > 0)
            {
                var mod = (col - 1) % 26;
                text = (char)('A' + mod) + text;
                col = (col - 1) / 26;
            }
            return text;
        }
    }
}
