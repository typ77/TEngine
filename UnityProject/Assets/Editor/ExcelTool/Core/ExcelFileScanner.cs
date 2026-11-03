using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    public class ExcelFileScanner
    {
        public Dictionary<string, string> Scan(string inputPath)
        {
            var nameToFileMap = new Dictionary<string, string>();
            var files = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                .Where(s => (s.EndsWith(".xls") || s.EndsWith(".xlsx")) && !Path.GetFileName(s).StartsWith("~"))
                .ToArray();

            if (files.Length == 0)
            {
                Debug.LogError("在" + inputPath + "目录下没有找到任何的Excel文件！请检查目录或路径是否正确");
                return nameToFileMap;
            }

            foreach (var filename in files)
            {
                var configFilename = Path.GetFileName(filename);
                nameToFileMap[configFilename] = filename;
            }
            return nameToFileMap;
        }

        public static ExcelFileScanner Instance { get; } = new ExcelFileScanner();
    }
}
