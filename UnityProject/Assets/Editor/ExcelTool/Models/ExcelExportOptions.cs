using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    public enum ExportMode
    {
        Full,
        Selected,
        Compressed
    }

    public class ExcelExportOptions
    {
        public ExportMode Mode { get; set; } = ExportMode.Full;
        public List<string> SelectedSheets { get; set; }

        public bool BuildScript { get; set; } = true;
        public bool BuildBytes { get; set; } = true;
        public bool BuildLanguage { get; set; } = true;
        public bool EnableSharedCache { get; set; }
        public bool DeleteOldFiles { get; set; }

        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string LanguageFilePath { get; set; }
        public string ScriptOutputPath { get; set; }
    }
}
