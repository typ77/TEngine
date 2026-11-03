using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// Excel 导出上下文，封装单次导出操作的所有状态。
    /// 每次导出创建新实例，用完即丢，确保状态隔离。
    /// </summary>
    public class ExcelExportContext
    {
        // 路径配置
        public string InputPath { get; }
        public string OutputPath { get; }
        public string LanguageFilePath { get; }
        public bool UseSharedCache { get; }

        // 核心数据
        public Dictionary<string, List<ConfigData[]>> DataDict { get; } = new();
        public Dictionary<string, string> ClassDictInfo { get; } = new();
        public Dictionary<string, string> ClassIndexType { get; } = new();
        public Dictionary<string, string> NameToFileMap { get; } = new();

        // 共享缓存
        public Dictionary<string, List<FieldSharedCacheInfo>> SharedCacheInfos { get; } = new();

        // 组合辅助索引（##lb-labels## 的 ~ 前缀声明）
        public Dictionary<string, List<CompoundIndex>> CompoundIndexes { get; } = new();

        // 服务实例
        public LocalizationService LocalizationService { get; } = new();
        public SchemaValidator Validator { get; } = new();

        // 诊断
        public ExcelExportResult Result { get; } = new();

        public static readonly string ScriptFolder = "GameScripts/HotFix/GameProto/GameConfig";

        public ExcelExportContext(string inputPath, string outputPath,
                                  string languageFilePath, bool useSharedCache)
        {
            InputPath = inputPath;
            OutputPath = outputPath;
            LanguageFilePath = languageFilePath;
            UseSharedCache = useSharedCache;
        }

        public string GetScriptFullPath(string className)
            => Path.Combine(Application.dataPath, ScriptFolder, $"{className}Item.cs");

        public string GetBinaryFullPath(string className)
            => Path.Combine(Application.dataPath, OutputPath, $"{className}.bytes");

        public string GetLanguageFullPath()
            => Path.Combine(Application.dataPath, Path.GetDirectoryName(LanguageFilePath), "Localization.xlsx");
    }
}