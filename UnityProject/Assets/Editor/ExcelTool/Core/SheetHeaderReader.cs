using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    public class SheetHeader
    {
        public string ClassName { get; set; }
        public Dictionary<int, string> TypeMap { get; set; } = new();      // col -> type string
        public Dictionary<int, string> NameMap { get; set; } = new();      // col -> field name
        public Dictionary<int, bool> IgnoreRows { get; set; } = new();     // col -> is ignored
        public int KeyIndex { get; set; }
        public int DataStartRow { get; set; } = -1;  // row where data starts (= row after last header row)
        public HashSet<int> CompoundIndexLabels { get; set; } = new HashSet<int>();  // 组合索引成员列号集合（##lb-labels## 的 ~ 前缀标识，只标记列，不携带组名）
    }

    public class SheetHeaderReader
    {
        private const string Label_Row = "##lb-row##";
        private const string Label_Keys = "##lb-keys##";
        private const string Label_Names = "##lb-names##";
        private const string Label_Labels = "##lb-labels##";
        private const string Label_Target = "##lb-target##";
        private const string Label_Client = "c";
        private const string Label_Server = "s";
        private const string Label_Language = "l";

        public static SheetHeader Read(SheetData data, string filePath, string sheetName)
        {
            var header = new SheetHeader { ClassName = sheetName };

            if (data.RowCount == 0)
                return header;

            int rowCount = data.RowCount;
            int colCount = data.ColCount;

            for (int row = 1; row <= rowCount; row++)
            {
                var firstValue = data.GetCell(row, 1);
                switch (firstValue)
                {
                    case Label_Row:
                        continue;
                    case Label_Keys:
                        ParseKeysRow(data, row, colCount, header);
                        continue;
                    case Label_Names:
                        ParseNamesRow(data, row, colCount, header, filePath, sheetName);
                        continue;
                    case Label_Labels:
                        ParseLabelsRow(data, row, colCount, header);
                        continue;
                    case Label_Target:
                        ParseTargetRow(data, row, colCount, header);
                        continue;
                    default:
                        header.DataStartRow = row;
                        return header;
                }
            }
            return header;
        }

        private static void ParseKeysRow(SheetData data, int row, int colCount, SheetHeader header)
        {
            for (int col = 1; col <= colCount; col++)
            {
                if (header.IgnoreRows.ContainsKey(col)) continue;
                if (header.TypeMap.ContainsKey(col)) continue;

                var value = data.GetCell(row, col);
                if (!string.IsNullOrEmpty(value) && !BracketUtils.CheckIgnore(value))
                    header.TypeMap[col] = value;
                else
                    header.IgnoreRows[col] = true;
            }
        }

        private static void ParseNamesRow(SheetData data, int row, int colCount,
                                    SheetHeader header, string filePath, string sheetName)
        {
            if (header.NameMap.Count > 0)
            {
                UnityEngine.Debug.LogError(
                    $"Duplicate names label at row {row}. File: {filePath}, Sheet: {sheetName}");
                return;
            }

            for (int col = 1; col <= colCount; col++)
            {
                var value = data.GetCell(row, col);
                if (!string.IsNullOrEmpty(value))
                {
                    if (BracketUtils.CheckIgnore(value))
                    {
                        header.IgnoreRows[col] = true;
                    }
                    else
                    {
                        string cleanedValue = value.Replace(" ", "").Replace("\r", "").Replace("\n", "");
                        header.NameMap[col] = cleanedValue;
                    }
                }
            }
        }

        private static void ParseLabelsRow(SheetData data, int row, int colCount, SheetHeader header)
        {
            for (int col = 1; col <= colCount; col++)
            {
                var value = data.GetCell(row, col);
                if (!string.IsNullOrEmpty(value))
                {
                    if (BracketUtils.CheckIgnore(value))
                        header.IgnoreRows[col] = true;

                    // ~ 前缀 = 辅助组合索引列标识（只标记该列为成员，不取 ~ 后内容；字段名从 ##lb-names## 读，组名由表名派生）
                    if (value.StartsWith("~") && value != Label_Labels)
                    {
                        header.CompoundIndexLabels.Add(col);
                    }
                    else if ((value.StartsWith("#") && value != Label_Labels) || col == 2)
                    {
                        if (header.KeyIndex == 0)
                            header.KeyIndex = col;
                    }
                }
            }
        }

        private static void ParseTargetRow(SheetData data, int row, int colCount, SheetHeader header)
        {
            for (int col = 1; col <= colCount; col++)
            {
                if (header.IgnoreRows.ContainsKey(col)) continue;
                var value = data.GetCell(row, col);
                if (!string.IsNullOrEmpty(value))
                {
                    if (value.ToLower().Contains(Label_Language))
                        header.TypeMap[col] = Label_Language;
                    if (value.ToLower().Contains(Label_Server) || BracketUtils.CheckIgnore(value))
                        header.IgnoreRows[col] = true;
                }
            }
        }

    }
}
