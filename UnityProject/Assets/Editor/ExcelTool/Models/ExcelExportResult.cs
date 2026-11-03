using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace TEngine.Editor.ExcelTool
{
    public class ExcelExportResult
    {
        public List<ExcelExportIssue> Issues { get; } = new List<ExcelExportIssue>();
        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
        public bool Success => !HasErrors;
        public int TableCount { get; set; }
        public int GeneratedFileCount { get; set; }

        public void Error(string message, string filePath = null, string sheetName = null,
                           int row = -1, int col = -1, string fieldName = null)
        {
            Issues.Add(new ExcelExportIssue
            {
                Severity = IssueSeverity.Error,
                Message = message,
                FilePath = filePath,
                SheetName = sheetName,
                Row = row,
                Column = col,
                FieldName = fieldName
            });
        }

        public void Warning(string message, string filePath = null, string sheetName = null,
                             int row = -1, int col = -1, string fieldName = null)
        {
            Issues.Add(new ExcelExportIssue
            {
                Severity = IssueSeverity.Warning,
                Message = message,
                FilePath = filePath,
                SheetName = sheetName,
                Row = row,
                Column = col,
                FieldName = fieldName
            });
        }

        public string GetSummary()
        {
            var errorCount = Issues.Count(i => i.Severity == IssueSeverity.Error);
            var warningCount = Issues.Count(i => i.Severity == IssueSeverity.Warning);
            return $"导出完成: {TableCount} 表, {GeneratedFileCount} 文件, {errorCount} 错误, {warningCount} 警告";
        }

        public void LogAllToConsole()
        {
            foreach (var issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Error)
                    UnityEngine.Debug.LogError(issue.ToString());
                else
                    UnityEngine.Debug.LogWarning(issue.ToString());
            }
        }

        public void ShowDialog()
        {
            if (HasErrors)
                EditorUtility.DisplayDialog("导表失败", GetSummary() + "\n\n详情请查看 Console", "确定");
            else
                EditorUtility.DisplayDialog("导表完成", GetSummary(), "确定");
        }
    }
}
