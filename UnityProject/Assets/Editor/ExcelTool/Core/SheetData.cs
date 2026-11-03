using System.Collections.Generic;
using System.IO;
using ExcelDataReader;

namespace TEngine.Editor.ExcelTool
{
    public class SheetData
    {
        private readonly string[,] _cells;

        public SheetData(string sheetName, string[,] cells, int rowCount, int colCount)
        {
            SheetName = sheetName;
            _cells = cells;
            RowCount = rowCount;
            ColCount = colCount;
        }

        public string SheetName { get; }
        public int RowCount { get; }
        public int ColCount { get; }

        public string GetCell(int row, int col)
        {
            if (row < 1 || col < 1 || row > RowCount || col > ColCount)
            {
                return string.Empty;
            }

            return _cells[row - 1, col - 1];
        }

        public static List<SheetData> LoadWorkbook(string path)
        {
            var sheets = new List<SheetData>();
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            do
            {
                sheets.Add(ReadSheet(reader));
            } while (reader.NextResult());

            return sheets;
        }

        private static SheetData ReadSheet(IExcelDataReader reader)
        {
            var rows = new List<string[]>();
            var lastNonEmptyRow = 0;
            var maxNonEmptyCol = 0;

            while (reader.Read())
            {
                var row = new string[reader.FieldCount];
                var rowHasValue = false;

                for (var col = 0; col < reader.FieldCount; col++)
                {
                    var value = reader.GetValue(col)?.ToString() ?? string.Empty;
                    row[col] = value;

                    if (!string.IsNullOrEmpty(value))
                    {
                        rowHasValue = true;
                        if (col + 1 > maxNonEmptyCol)
                        {
                            maxNonEmptyCol = col + 1;
                        }
                    }
                }

                rows.Add(row);
                if (rowHasValue)
                {
                    lastNonEmptyRow = rows.Count;
                }
            }

            var cells = new string[lastNonEmptyRow, maxNonEmptyCol];
            for (var row = 0; row < lastNonEmptyRow; row++)
            {
                var sourceRow = rows[row];
                for (var col = 0; col < maxNonEmptyCol; col++)
                {
                    cells[row, col] = col < sourceRow.Length ? sourceRow[col] : string.Empty;
                }
            }

            return new SheetData(reader.Name, cells, lastNonEmptyRow, maxNonEmptyCol);
        }
    }
}