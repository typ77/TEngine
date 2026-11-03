using System.Collections.Generic;
using System.IO;
using System;

namespace TEngine.Editor.ExcelTool
{
    public class LocalizationService
    {
        private const string Label_Row = "##lb-row##";
        private const string Label_Keys = "##lb-keys##";
        private const string Label_Names = "##lb-names##";
        private const string Label_Labels = "##lb-labels##";

        private Dictionary<int, string> _languageMap = new Dictionary<int, string>();
        private Dictionary<string, int> _languageKeyMap = new Dictionary<string, int>();
        private Dictionary<int, string> _newLanguageMap = new Dictionary<int, string>();
        private Dictionary<int, string> _oldLanguageMap = new Dictionary<int, string>();
        private Dictionary<int, bool> _usedLanguageMap = new Dictionary<int, bool>();
        private Dictionary<string, List<ConfigData[]>> _languageDataDict = new Dictionary<string, List<ConfigData[]>>();
        private int _maxLanguageId;

        public int NewEntryCount => _newLanguageMap.Count;

        public void Clear()
        {
            _maxLanguageId = 0;
            _languageMap.Clear();
            _languageKeyMap.Clear();
            _newLanguageMap.Clear();
            _oldLanguageMap.Clear();
            _usedLanguageMap.Clear();
            _languageDataDict.Clear();
        }

        public void Read(string file)
        {
            Clear();
            foreach (var sheet in SheetData.LoadWorkbook(file))
            {
                var className = sheet.SheetName;
                if (className.StartsWith("#")) continue;

                if (sheet.RowCount == 0) continue;
                var startTag = sheet.GetCell(1, 1);
                if (string.IsNullOrEmpty(startTag) || !startTag.StartsWith("##")) continue;

                int rowCount = sheet.RowCount;
                int colCount = sheet.ColCount;

                Dictionary<int, bool> ignoreRows = new Dictionary<int, bool>();
                Dictionary<int, string> typeMap = new Dictionary<int, string>();
                Dictionary<string, int> nameIndexMap = new Dictionary<string, int>();

                for (int row = 1; row <= rowCount; row++)
                {
                    var firstValue = sheet.GetCell(row, 1);
                    switch (firstValue)
                    {
                        case Label_Row:
                            continue;
                        case Label_Keys:
                            if (typeMap.Count > 0)
                            {
                                UnityEngine.Debug.LogError($"数据错误！[{className}]配置表！第 {row} 行 {file} 重复的 keys 标签");
                            }
                            else
                            {
                                for (int col = 1; col <= colCount; col++)
                                {
                                    var val = sheet.GetCell(row, col);
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        if (BracketUtils.CheckIgnore(val)) ignoreRows[col] = true;
                                        else typeMap[col] = val;
                                    }
                                    else ignoreRows[col] = true;
                                }
                            }
                            continue;
                        case Label_Names:
                            if (nameIndexMap.Count > 0)
                            {
                                UnityEngine.Debug.LogError($"数据错误！[{className}]配置表！第 {row} 行 {file} 重复的 names 标签");
                            }
                            else
                            {
                                for (int col = 2; col <= colCount; col++)
                                {
                                    var val = sheet.GetCell(row, col);
                                    if (!string.IsNullOrEmpty(val) && !ignoreRows.ContainsKey(col) && !BracketUtils.CheckIgnore(val))
                                    {
                                        string cleanedValue = val.Replace(" ", "").Replace("\r", "").Replace("\n", "");
                                        nameIndexMap[cleanedValue] = col;
                                    }
                                    else ignoreRows[col] = true;
                                }
                            }
                            continue;
                        case Label_Labels:
                            for (int col = 1; col <= colCount; col++)
                            {
                                var val = sheet.GetCell(row, col);
                                if (!string.IsNullOrEmpty(val) && BracketUtils.CheckIgnore(val))
                                    ignoreRows[col] = true;
                            }
                            continue;
                        default:
                            if (nameIndexMap.Count == 0)
                                throw new ValidationException("多语言表头格式有误，请检查表头");

                            if (!nameIndexMap.TryGetValue("Id", out var idColumn)
                                || !nameIndexMap.TryGetValue("ChineseSimplified", out var textIDColumn))
                                throw new ValidationException("多语言表缺少 'Id' 或 'ChineseSimplified' 列");

                            var text = sheet.GetCell(row, textIDColumn);
                            if (int.TryParse(sheet.GetCell(row, idColumn), out var textId))
                            {
                                if (!_languageMap.TryAdd(textId, text))
                                    UnityEngine.Debug.LogError($"多语言表存在重复的 id：{textId}，文案：{text}");
                                if (!_languageKeyMap.TryAdd(text, textId))
                                    UnityEngine.Debug.LogError($"多语言表存在重复的文案：{text}，id：{textId}");

                                _maxLanguageId = Math.Max(_maxLanguageId, textId);

                                var data = new ConfigData
                                {
                                    Type = "int", Data = textId.ToString(), Name = "Id"
                                };

                                using var itr = nameIndexMap.GetEnumerator();
                                while (itr.MoveNext())
                                {
                                    var key = itr.Current.Key;
                                    var value = itr.Current.Value;
                                    if (key != "Id")
                                    {
                                        var configDataList = new List<ConfigData> { data };
                                        if (!_languageDataDict.TryGetValue(key, out var item))
                                        {
                                            item = new List<ConfigData[]>();
                                            _languageDataDict[key] = item;
                                        }
                                        configDataList.Add(new ConfigData
                                        {
                                            Type = "string",
                                            Data = sheet.GetCell(row, value),
                                            Name = "text"
                                        });
                                        item.Add(configDataList.ToArray());
                                    }
                                }
                            }
                            break;
                    }
                }
            }
        }

        public int GetOrCreateLanguageId(string text)
        {
            if (_languageKeyMap.TryGetValue(text, out var existingId))
            {
                _usedLanguageMap[existingId] = true;
                return existingId;
            }
            _maxLanguageId++;
            _languageKeyMap[text] = _maxLanguageId;
            _languageMap[_maxLanguageId] = text;
            _newLanguageMap[_maxLanguageId] = text;
            _usedLanguageMap[_maxLanguageId] = true;
            return _maxLanguageId;
        }

        public bool TryGetLanguageId(string text, out int id)
        {
            return _languageKeyMap.TryGetValue(text, out id);
        }

        public Dictionary<string, List<ConfigData[]>> GetLanguageData()
        {
            return _languageDataDict;
        }

        public void Save(string file)
        {
            var excelName = new FileInfo(file);

            // S3 fix: null check for missing sheet
            using var package = new OfficeOpenXml.ExcelPackage(excelName);
            var worksheet = package.Workbook.Worksheets["LocalizedStrings"];
            if (worksheet == null)
            {
                UnityEngine.Debug.LogError($"'LocalizedStrings' sheet not found in language file: {file}");
                return;
            }
            if (worksheet.Dimension == null)
            {
                UnityEngine.Debug.LogError($"'LocalizedStrings' sheet is empty in language file: {file}");
                return;
            }

            // D15 fix: backup before write (verify source file is valid)
            string backupPath = file + ".bak";
            if (File.Exists(file) && new FileInfo(file).Length > 0)
            {
                try { File.Copy(file, backupPath, overwrite: true); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"Failed to create language backup: {ex.Message}"); }
            }

            // D20 fix: 先查找空行复用，没有空行才追加
            int nextRow = worksheet.Dimension.End.Row + 1;
            for (int r = 2; r <= worksheet.Dimension.End.Row; r++)
            {
                if ((worksheet.Cells[r, 2].Value == null || string.IsNullOrEmpty(worksheet.Cells[r, 2].Value?.ToString())) &&
                    (worksheet.Cells[r, 3].Value == null || string.IsNullOrEmpty(worksheet.Cells[r, 3].Value?.ToString())))
                {
                    nextRow = r;
                    break;
                }
            }
            using var itr = _newLanguageMap.GetEnumerator();
            while (itr.MoveNext())
            {
                worksheet.Cells[nextRow, 2].Value = itr.Current.Key;
                worksheet.Cells[nextRow, 3].Value = itr.Current.Value;
                nextRow++;
            }

            using var allItr = _languageMap.GetEnumerator();
            while (allItr.MoveNext())
            {
                var key = allItr.Current.Key;
                if (!_usedLanguageMap.ContainsKey(key))
                    _oldLanguageMap[key] = allItr.Current.Value;
            }

            package.Save();
        }

    }
}
