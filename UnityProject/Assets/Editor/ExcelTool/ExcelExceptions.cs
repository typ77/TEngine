using System;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// Excel 导出异常基类 — 携带导出上下文信息。
    /// </summary>
    public class ExcelExportException : Exception
    {
        public string ClassName { get; }
        public string FilePath { get; }
        public int Row { get; }
        public int Column { get; }

        public ExcelExportException(string message,
            string className = null, string filePath = null,
            int row = -1, int column = -1, Exception inner = null)
            : base(message, inner)
        {
            ClassName = className;
            FilePath = filePath;
            Row = row;
            Column = column;
        }

        public override string Message => base.Message +
            (ClassName != null ? $"\n  表: {ClassName}" : "") +
            (Row >= 0 ? $"\n  行: {Row}, 列: {Column}" : "");
    }

    public class TypeNotSupportedException : ExcelExportException
    {
        public TypeNotSupportedException(string message,
            string className = null, string filePath = null,
            int row = -1, int column = -1, Exception inner = null)
            : base(message, className, filePath, row, column, inner) { }
    }

    public class SharedCacheException : ExcelExportException
    {
        public SharedCacheException(string message,
            string className = null, string filePath = null,
            int row = -1, int column = -1, Exception inner = null)
            : base(message, className, filePath, row, column, inner) { }
    }

    public class ValidationException : ExcelExportException
    {
        public ValidationException(string message,
            string className = null, string filePath = null,
            int row = -1, int column = -1, Exception inner = null)
            : base(message, className, filePath, row, column, inner) { }
    }
}