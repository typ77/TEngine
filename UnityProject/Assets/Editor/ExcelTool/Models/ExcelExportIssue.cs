namespace TEngine.Editor.ExcelTool
{
    public enum IssueSeverity
    {
        Error,
        Warning
    }

    public class ExcelExportIssue
    {
        public IssueSeverity Severity { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public int Row { get; set; } = -1;
        public int Column { get; set; } = -1;
        public string FieldName { get; set; }

        public override string ToString()
        {
            var severity = Severity == IssueSeverity.Error ? "ERROR" : "WARNING";
            var location = "";
            if (!string.IsNullOrEmpty(FilePath))
                location += $" File: {FilePath}";
            if (!string.IsNullOrEmpty(SheetName))
                location += $" Sheet: {SheetName}";
            if (Row >= 0)
                location += $" Row: {Row}";
            if (Column >= 0)
                location += $" Col: {Column}";
            if (!string.IsNullOrEmpty(FieldName))
                location += $" Field: {FieldName}";
            return $"[{severity}]{location} {Message}";
        }
    }
}
