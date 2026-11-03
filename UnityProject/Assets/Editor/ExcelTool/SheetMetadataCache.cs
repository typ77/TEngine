using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    public static class SheetMetadataCache
    {
        private static readonly string CachePath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Library", "SheetMetadata.cache");

        public static List<FileMetadata> Load()
        {
            if (!File.Exists(CachePath))
                return new List<FileMetadata>();

            try
            {
                var json = File.ReadAllText(CachePath);
                var data = JsonUtility.FromJson<SheetMetadataCacheData>(json);
                return data?.Files ?? new List<FileMetadata>();
            }
            catch
            {
                return new List<FileMetadata>();
            }
        }

        public static void Save(List<FileMetadata> files)
        {
            try
            {
                var dir = Path.GetDirectoryName(CachePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var data = new SheetMetadataCacheData { Files = files };
                var json = JsonUtility.ToJson(data, true);
                File.WriteAllText(CachePath, json);
            }
            catch { }
        }

        public static void DetectDuplicates(List<FileMetadata> files)
        {
            foreach (var f in files)
                foreach (var s in f.Sheets)
                    s.IsDuplicate = false;

            var nameToFiles = new Dictionary<string, List<string>>();
            foreach (var f in files)
            {
                foreach (var s in f.Sheets)
                {
                    if (!s.IsValid) continue;
                    if (!nameToFiles.ContainsKey(s.Name))
                        nameToFiles[s.Name] = new List<string>();
                    nameToFiles[s.Name].Add(f.FileName);
                }
            }

            foreach (var kvp in nameToFiles)
            {
                if (kvp.Value.Count <= 1) continue;
                foreach (var f in files)
                {
                    foreach (var s in f.Sheets)
                    {
                        if (s.Name == kvp.Key)
                            s.IsDuplicate = true;
                    }
                }
            }
        }

        public static List<string> GetDuplicateNames(List<FileMetadata> files)
        {
            return files
                .SelectMany(f => f.Sheets)
                .Where(s => s.IsValid && s.IsDuplicate)
                .Select(s => s.Name)
                .Distinct()
                .ToList();
        }
    }
}