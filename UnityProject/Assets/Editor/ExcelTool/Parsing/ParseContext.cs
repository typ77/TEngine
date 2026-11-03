namespace TEngine.Editor.ExcelTool
{
    public class ParseContext
    {
        public string ClassName { get; set; }
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public int Row { get; set; } = -1;
        public int Column { get; set; } = -1;
        public ExcelExportResult Result { get; set; }

        public ParseContext WithLocation(int row, int col) => new()
        {
            ClassName = ClassName,
            FilePath = FilePath,
            SheetName = SheetName,
            Row = row,
            Column = col,
            Result = Result
        };
    }
}