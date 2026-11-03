using System;
using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    [Serializable]
    public class SheetMetadata
    {
        public string Name;
        public bool IsValid;
        public bool IsDuplicate;
    }

    [Serializable]
    public class FileMetadata
    {
        public string FileName;
        public string FilePath;
        public long LastWriteTime;
        public List<SheetMetadata> Sheets = new();
        public string ScanError;
    }

    [Serializable]
    public class SheetMetadataCacheData
    {
        public List<FileMetadata> Files = new();
    }
}