using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExcelDataReader;

namespace TEngine.Editor.ExcelTool
{
    public static class SheetMetadataScanner
    {
        public static FileMetadata ScanFile(string filePath)
        {
            var meta = new FileMetadata
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                LastWriteTime = File.GetLastWriteTimeUtc(filePath).Ticks,
                Sheets = new List<SheetMetadata>()
            };

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = ExcelReaderFactory.CreateReader(stream);

                do
                {
                    var sheetName = reader.Name;
                    bool isValid = false;

                    if (!sheetName.StartsWith("#") && reader.Read())
                    {
                        var firstCell = reader.GetValue(0)?.ToString() ?? string.Empty;
                        if (firstCell.StartsWith("##"))
                            isValid = true;
                    }

                    meta.Sheets.Add(new SheetMetadata { Name = sheetName, IsValid = isValid });
                } while (reader.NextResult());
            }
            catch (System.Exception ex)
            {
                meta.ScanError = ex.Message;
            }

            return meta;
        }

        public static List<FileMetadata> ScanAllFiles(string directory, List<FileMetadata> cachedFiles)
        {
            var cacheMap = new Dictionary<string, FileMetadata>();
            foreach (var f in cachedFiles)
                cacheMap[f.FilePath] = f;

            var allFiles = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(s => (s.EndsWith(".xls") || s.EndsWith(".xlsx")) && !Path.GetFileName(s).StartsWith("~"))
                .ToArray();

            var result = new List<FileMetadata>();

            foreach (var filePath in allFiles)
            {
                var lw = File.GetLastWriteTimeUtc(filePath).Ticks;

                if (cacheMap.TryGetValue(filePath, out var cached) && cached.LastWriteTime == lw)
                {
                    result.Add(cached);
                    cacheMap.Remove(filePath);
                }
                else
                {
                    result.Add(ScanFile(filePath));
                }
            }

            return result;
        }
    }
}