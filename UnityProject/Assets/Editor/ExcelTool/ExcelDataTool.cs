using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;


namespace TEngine.Editor.ExcelTool
{
    public class ExcelDataTool : ScriptableObject
    {
        private const string Label_Labels = "##lb-labels##";    //标签
        private const string Label_Keys = "##lb-keys##";        //类型
        private const string Label_Names = "##lb-names##";      //字段名
        private const string Label_Row = "##lb-row##";          //忽略行标识
        private const string Label_Target = "##lb-target##";    //导出平台
        private const string Label_Client = "c";    //客户端
        private const string Label_Server = "s";    //服务端
        private const string Label_Language = "l";  //多语言

        /// <summary>
        /// 无共享缓存版类（XxxItemUncompressed/XxxConfigUncompressed）的输出目录。
        /// 位于 Editor/ExcelTool/Report/Generated，被 TEngine.Editor.ExcelTool.Report asmdef 收纳，
        /// 仅 Editor-only 程序集编译，不入运行时 GameProto 程序集。
        /// </summary>
        private const string UncompressedScriptFolder = "Editor/ExcelTool/Report/Generated";

        /// <summary>
        /// 清理单次导表状态，防止跨次导出污染
        /// </summary>
        private static void ClearExportState(ExcelExportContext ctx)
        {
            ctx.DataDict.Clear();
            ctx.ClassDictInfo.Clear();
            ctx.ClassIndexType.Clear();
            ctx.SharedCacheInfos.Clear();
            ctx.CompoundIndexes.Clear();
            ctx.LocalizationService.Clear();
            ReadExcel.ClearTypeCache();
        }

        private static void ShowFileProgress(int fileIndex, int total, string fileName)
        {
            var progress = (float)fileIndex / total;
            EditorUtility.DisplayProgressBar("导表进度", $"正在读取: {fileName} ({fileIndex}/{total})", progress);
        }

        #region Menu接口

        private static ExcelExportContext CreateExportContext(bool useSharedCache)
        {
            var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
            var outputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath);
            var languagePath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath, "Localization.xlsx");
            return new ExcelExportContext(inputPath, outputPath, languagePath, useSharedCache);
        }

        /// <summary>
        /// 统一导出全部数据的私有入口，合并 ReLoadExcelData 和 BuildConfigBytes 的公共逻辑
        /// </summary>
        private static void ExportAll(bool useSharedCache)
        {
            var ctx = CreateExportContext(useSharedCache);
            LoadAllExcelData(ctx, buildScript: true, buildBytes: true, buildLanguage: true);
            ctx.Result.TableCount = ctx.DataDict.Count;
            ctx.Result.LogAllToConsole();
            ctx.Result.ShowDialog();
        }

        //[MenuItem("Tools/配置表工具/生成全部数据", false, 301)]
        //public static void ReLoadExcelData() => ExportAll(useSharedCache: false);

        [MenuItem("Tools/配置表工具/生成全部数据(数据压缩)", false, 303)]
        public static void BuildConfigBytes() => ExportAll(useSharedCache: true);
        
        #endregion
        private static void LoadAllExcelData(ExcelExportContext ctx, bool buildScript = true, bool buildBytes = true, bool buildLanguage = true)
        {
            try
            {
                ClearExportState(ctx);
                LoadExcelData(ctx);
                if (buildScript || buildBytes || buildLanguage)
                {
                    BuildExcelData(ctx, buildScript, buildBytes, buildLanguage);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 加载所有的Excel数据
        /// </summary>
        /// <exception cref="Exception"></exception>
        public static void GetExcelSheetName(ExcelExportContext ctx)
        {
            var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
            var files = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                .Where(s => (s.EndsWith(".xls") || s.EndsWith(".xlsx")) && !Path.GetFileName(s).StartsWith("~")).ToArray();
            if (files.Length == 0)
            {
                Log.Error($"在 {inputPath} 目录下没有找到任何的Excel文件！请检查目录或路径是否正确");
                return;
            }
            ctx.NameToFileMap.Clear();


            foreach (var filename in files)
            {
                var configFilename = Path.GetFileName(filename);
                ctx.NameToFileMap[configFilename] = filename;
            }
        }
        
        private static void CreateDirectory(string directory)
        {
            // If the directory doesn't exist, create it.
            if (Directory.Exists(directory) == false)
                Directory.CreateDirectory(directory);
        }
        
        public static void LoadExcelData(ExcelExportContext ctx)
        {
            //检查导出目录是否存在
            if (string.IsNullOrEmpty(Settings.UpdateSetting.ConfigTablePath))
            {
                //如果目录为空，则提示
                Log.Error("请先设置配置表导出目录[ConfigTablePath]。");
                SettingsService.OpenProjectSettings("Project/TEngine/UpdateSettings");
                return;
            }
            if (string.IsNullOrEmpty(Settings.UpdateSetting.ConfigOutPutPath))
            {
                //如果目录为空，则提示
                Log.Error("请先设置配置表导出目录[ConfigOutPutPath]。");
                SettingsService.OpenProjectSettings("Project/TEngine/UpdateSettings");
                return;
            }
            if (string.IsNullOrEmpty(Settings.UpdateSetting.ConfigLanguagePath))
            {
                //如果目录为空，则提示
                Log.Error("请先设置配置表导出目录[ConfigLanguagePath]。");
                SettingsService.OpenProjectSettings("Project/TEngine/UpdateSettings");
                return;
            }

            var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
            var languageFile = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath, "Localization.xlsx");
            //检查导入导出目录是否存在。不存在就创建相关目录
            CreateDirectory(inputPath);
            CreateDirectory(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath));
            CreateDirectory(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath));

            var files = Directory.GetFiles(inputPath, "*.*", SearchOption.AllDirectories)
                .Where(s => (s.EndsWith(".xls") || s.EndsWith(".xlsx")) && !Path.GetFileName(s).StartsWith("~")).ToArray();

            if (files.Length == 0)
            {
                Log.Error($"在 {inputPath} 目录下没有找到任何的Excel文件！请检查目录或路径是否正确");
                return;
            }
            //如果多语言目录下不存在多语言表，则拷贝默认多语言表
            if (File.Exists(languageFile) == false)
            {
                //拷贝默认多语言表
                File.Copy(Path.Combine(Application.dataPath, "Editor/ExcelTool/Config/Localization.xlsx"), languageFile);
            }

            ClearExportState(ctx);

            //读取多语言表。构建多语言表数据
            LocalizationExporter.GetLanguageExcelData(ctx, languageFile);

            //遍历所有的表
            int fileIndex = 0;
            foreach (var item in files)
            {
                fileIndex++;
                ShowFileProgress(fileIndex, files.Length, Path.GetFileName(item));
                GetExcelData(ctx, item);
            }
        }
        
        // 新增：单独的构建方法
        private static void BuildExcelData(ExcelExportContext ctx, bool buildScript = true, bool buildBytes = true, bool buildLanguage = true, bool isDelete = false)
        {
            //1.生成c#代码
            if (buildScript)
            {
                EditorUtility.DisplayProgressBar("导表进度", "正在生成代码...", 0.7f);
                GenerateCodeFiles(ctx, ctx.DataDict.Keys, isDelete);
            }

            //2.生成二进制数据文件
            if (buildBytes)
            {
                EditorUtility.DisplayProgressBar("导表进度", "正在写入bytes...", 0.85f);
                ConfigBinaryWriter.Serialize(ctx, ctx.DataDict.Keys);
            }
            if (buildLanguage)
            {
                var languageFile = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath, "Localization.xlsx");
                LocalizationExporter.SaveLanguageExcelData(ctx, languageFile);
                LocalizationExporter.SerializeLanguage(ctx);
            }
        }

        //读表数据
        internal static List<string> GetExcelData_Internal(ExcelExportContext ctx, string fileName, HashSet<string> sheetFilter = null)
        {
            var loadedClassNames = new List<string>();
            if (Path.GetFileName(fileName).StartsWith("~"))
            {
                return loadedClassNames;
            }
            try
            {
                foreach (var sheet in SheetData.LoadWorkbook(fileName))
                {
                    if (sheetFilter != null && !sheetFilter.Contains(sheet.SheetName))
                        continue;
                    var result = ParseWorksheet(ctx, sheet, fileName);
                    if (result != null) loadedClassNames.AddRange(result);
                }
            }
            catch (Exception e)
            {
                Log.Error($"错误信息： {e}");
                EditorUtility.DisplayDialog("注意！！！", $"\n表：{fileName}\n 错误信息:{e}！", "确定");
                throw new ValidationException($"请关闭 {fileName} 后再导表！");
            }

            return loadedClassNames;
        }

        public static List<string> GetExcelData(ExcelExportContext ctx, string fileName)
            => GetExcelData_Internal(ctx, fileName);

        /// <summary>
        /// 导出单个 sheet
        /// </summary>
        public static void GenerateSingleSheet(ExcelExportContext ctx, string fileName, string sheetName, bool isDelete = false)
        {
            try
            {
                var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
                var languageFile = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath, "Localization.xlsx");
                CreateDirectory(inputPath);
                CreateDirectory(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath));
                CreateDirectory(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath));
                if (!File.Exists(languageFile))
                    File.Copy(Path.Combine(Application.dataPath, "Editor/ExcelTool/Config/Localization.xlsx"), languageFile);

                ClearExportState(ctx);
                LocalizationExporter.GetLanguageExcelData(ctx, languageFile);

                if (ctx.NameToFileMap.Count == 0)
                    GetExcelSheetName(ctx);

                var sheetFilter = new HashSet<string> { sheetName };
                List<string> loadedClassNames = new List<string>();

                if (ctx.NameToFileMap.TryGetValue(fileName, out var filePath))
                {
                    loadedClassNames = GetExcelData_Internal(ctx, filePath, sheetFilter);
                }
                else
                {
                    Debug.LogWarning($"[GenerateSingleSheet] 未找到文件映射: '{fileName}'");
                }

                if (loadedClassNames.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("导表进度", "正在生成代码...", 0.7f);
                    GenerateCodeFiles(ctx, loadedClassNames, isDelete);

                    EditorUtility.DisplayProgressBar("导表进度", "正在写入bytes...", 0.85f);
                    ConfigBinaryWriter.Serialize(ctx, loadedClassNames);
                }

                EditorUtility.DisplayProgressBar("导表进度", "正在刷新资源...", 0.95f);
                LocalizationExporter.SaveLanguageExcelData(ctx, languageFile);
                LocalizationExporter.SerializeLanguage(ctx);

                var result = new ExcelExportResult();
                result.TableCount = loadedClassNames.Count;
                result.GeneratedFileCount = ctx.DataDict.Count;
                result.LogAllToConsole();
                result.ShowDialog();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 解析单个 worksheet，返回成功解析的 className 列表（跳过的 sheet 返回 null）
        /// </summary>
        private static List<string> ParseWorksheet(ExcelExportContext ctx,
            SheetData sheet, string fileName)
        {
            var className = sheet.SheetName;
            // 检查 className 第一个字符是否为 # 号。如果是则跳过
            if (className.StartsWith("#"))
            {
                return null;
            }
            ctx.Validator.ValidateCSharpIdentifier(className, "sheet className", fileName, sheet.SheetName);

            var startTag = sheet.GetCell(1, 1);
            if (string.IsNullOrEmpty(startTag) || !startTag.StartsWith("##"))
            {
                return null;
            }
            if (sheet.RowCount == 0)
            {
                return null;
            }
            //可以是同名的表，但是表不导出。不导出后才检查是否同名
            if (ctx.DataDict.ContainsKey(className))
            {
                throw new ValidationException($"Duplicate config sheet/className '{className}'. File: {fileName}, Sheet: {sheet.SheetName} old filename :{ctx.ClassDictInfo[className]}");
            }
            if (ctx.ClassIndexType.ContainsKey(className))
            {
                throw new ValidationException($"Duplicate config sheet/className '{className}' in classIndexType. File: {fileName}, Sheet: {sheet.SheetName} old filename :{ctx.ClassDictInfo[className]}");
            }
            var dataList = new List<ConfigData[]>();
            // 使用 SheetHeaderReader 解析表头
            var header = SheetHeaderReader.Read(sheet, fileName, className);

            var typeMap = header.TypeMap;
            var nameMap = header.NameMap;
            var ignoreRows = header.IgnoreRows;
            int keyIndex = header.KeyIndex;
            int dataStartRow = header.DataStartRow;

            // D25: KeyIndex=0 检查 — 无法确定主键列时提前报错
            if (keyIndex == 0 || !typeMap.ContainsKey(keyIndex))
                throw new ValidationException($"无法确定表 '{className}' 的主键列，请检查 ##lb-labels## 行");

            // 没有数据行（所有行都是表头标记），跳过该 sheet
            if (dataStartRow < 1)
                return null;

            // 验证类型
            var invalidTypes = new List<int>();
            foreach (var kvp in typeMap)
            {
                if (!SchemaValidator.IsKnownType(kvp.Value))
                {
                    Log.Warning($"未知数据类型 '{kvp.Value}'，已忽略该列。文件:{fileName} 表:{className} 列:{kvp.Key}");
                    invalidTypes.Add(kvp.Key);
                }
            }
            foreach (var col in invalidTypes)
            {
                typeMap.Remove(col);
                ignoreRows[col] = true;
            }

            // 验证字段名
            var fieldNames = new HashSet<string>();
            foreach (var kvp in nameMap)
            {
                ctx.Validator.EnsureUniqueFieldName(fieldNames, kvp.Value, fileName, sheet.SheetName, kvp.Key);
                ctx.Validator.ValidateCSharpIdentifier(kvp.Value, "field name", fileName, sheet.SheetName, -1, kvp.Key);
            }

            int rowCount = sheet.RowCount;
            int colCount = sheet.ColCount;

            for (int row = dataStartRow; row <= rowCount; row++)
            {
                // ##lb-row## 在数据区也跳过该行
                var firstValue = sheet.GetCell(row, 1);
                if (firstValue == Label_Row)
                    continue;

                var configDataList = new List<ConfigData>();
                bool isBreakRow = false;

                for (var col = 1; col <= colCount; col++)
                {
                    // 如果是忽略的数据，则跳过
                    if (ignoreRows.ContainsKey(col))
                    {
                        continue;
                    }

                    var data = new ConfigData
                    {
                        Type = typeMap[col],
                        Data = sheet.GetCell(row, col),
                        Name = nameMap[col]
                    };
                    //如果此行数据是key，并且数据为空的时候直接跳出循环
                    if (col == keyIndex && string.IsNullOrEmpty(data.Data))
                    {
                        isBreakRow = true;
                        break;
                    }
                    if (data.Type.ToLower() == Label_Language)
                    {
                        if (ctx.LocalizationService.TryGetLanguageId(data.Data, out var index))
                        {
                            data.Data = index.ToString();
                        }
                        else
                        {
                            var newId = ctx.LocalizationService.GetOrCreateLanguageId(data.Data);
                            data.Data = newId.ToString();
                        }
                    }
                    //这里处理多语言
                    configDataList.Add(data);
                }

                if (!isBreakRow)
                    dataList.Add(configDataList.ToArray());
                else
                    break;
            }

            if (string.IsNullOrEmpty(className))
            {
                throw new ValidationException($"空的类名（excel 页签名）, 路径:  {fileName}");
            }
            // 添加sheet到文件的映射
            ctx.ClassIndexType.Add(className, typeMap[keyIndex]);
            ctx.DataDict.Add(className, dataList);
            ctx.ClassDictInfo.Add(className, fileName);
            // 解析组合辅助索引（##lb-labels## 的 ~ 前缀声明）
            if (header.CompoundIndexLabels.Count > 0)
            {
                ctx.CompoundIndexes[className] = BuildCompoundIndexes(header.CompoundIndexLabels, nameMap, typeMap, className, fileName);
            }
            // 分析重复数据，构建共享缓存
            if (ctx.UseSharedCache)
            {
                var analyzer = new SharedCacheAnalyzer();
                ctx.SharedCacheInfos[className] = analyzer.Analyze(className, dataList, nameMap, typeMap);
            }

            return new List<string> { className };
        }

        /// <summary>
        /// 将组合索引成员列集合转为单个 CompoundIndex：所有 ##lb-labels## 的 ~ 标记列
        /// 合并为一个联合索引，组名用表名（英文）。字段名从 ##lb-names## 读，成员按列号顺序＝构造函数读取顺序。
        /// </summary>
        private static List<CompoundIndex> BuildCompoundIndexes(
            HashSet<int> memberCols,
            Dictionary<int, string> nameMap,
            Dictionary<int, string> typeMap,
            string className,
            string fileName)
        {
            var idx = new CompoundIndex { GroupName = className };
            foreach (var col in memberCols.OrderBy(c => c))
            {
                if (!nameMap.TryGetValue(col, out var fieldName))
                    throw new ValidationException($"表 '{className}' 的组合索引列 {col} 无对应字段名。文件:{fileName}");
                if (!typeMap.TryGetValue(col, out var fieldType))
                    throw new ValidationException($"表 '{className}' 的组合索引列 {col}（字段 {fieldName}）无对应类型。文件:{fileName}");

                idx.Members.Add(new CompoundIndexMember { Name = fieldName, Type = fieldType });
            }
            return new List<CompoundIndex> { idx };
        }


        private static void DeleteManagedGeneratedCodeFiles(string directory)
        {
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.GetFiles(directory, "*.cs"))
            {
                var fileName = Path.GetFileName(file);
                // 压缩版类 *Config.cs/*Item.cs（运行时 GameProto）+ 无缓存版 *Uncompressed.cs（Editor-only）
                if (!fileName.EndsWith("Config.cs") && !fileName.EndsWith("Item.cs") && !fileName.EndsWith("Uncompressed.cs"))
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                // 统一用 GeneratedCodeMarker（"<auto-generated by ExcelTool>"）识别自动生成文件。
                // 压缩版 *Item.cs 文件头是 "// " + GeneratedCodeMarker；
                // 无缓存版 *Uncompressed.cs 文件头是 "// <auto-generated by ExcelTool - Editor-only, ...>"，
                // 包含 GeneratedCodeMarker 子串。手写文件不含此标记，不会被误删。
                if (!content.Contains(ScriptGenerator.GeneratedCodeMarker))
                {
                    continue;
                }

                File.Delete(file);
            }
        }

        // 生成代码文件
        public static void GenerateCodeFiles(ExcelExportContext ctx, IEnumerable<string> selectedSheets, bool isDelete)
        {
            var templatePath = Path.Combine(Application.dataPath, "Editor/ExcelTool/TableBase.txt");
            var templateContent = File.ReadAllText(templatePath);
            //在指定目录下生成代码，此目录不可更改
            //1.先删除旧代码
            //2.生成新的CS文件
            if (isDelete)
            {
                DeleteManagedGeneratedCodeFiles(Path.Combine(Application.dataPath, ExcelExportContext.ScriptFolder));
                // 无缓存版类目录（Editor-only）也要清理旧文件，避免重命名/删表后残留孤儿类导致编译错
                DeleteManagedGeneratedCodeFiles(Path.Combine(Application.dataPath, UncompressedScriptFolder));
            }

            var tempFiles = new List<(string tempPath, string finalPath)>();
            try
            {
                // 第一遍：全部写入 .tmp 临时文件
                foreach (var className in selectedSheets)
                {
                    if (ctx.DataDict.TryGetValue(className, out var dataList))
                    {
                        ctx.SharedCacheInfos.TryGetValue(className, out var sharedCacheInfos);
                        ctx.CompoundIndexes.TryGetValue(className, out var compoundIndexes);
                        var scriptFolder = Path.Combine(Application.dataPath, ExcelExportContext.ScriptFolder);
                        var scriptGenerator = new ScriptGenerator(scriptFolder, className, dataList, sharedCacheInfos, ctx.ClassIndexType[className], compoundIndexes);
                        var code = scriptGenerator.CreateCodeV2(templateContent);
                        string finalPath = Path.Combine(scriptFolder, $"{className}Item.cs");
                        string tempPath = finalPath + ".tmp";
                        File.WriteAllText(tempPath, code);
                        tempFiles.Add((tempPath, finalPath));

                        // 无缓存版类写到 Editor-only 目录（不入运行时 GameProto 程序集）。
                        // GetUncompressedClasses 返回裸类文本，这里手动包 namespace TEngine { ... }，
                        // 与压缩版 *Item.cs 的命名空间保持一致（GameProto 与 Report asmdef 都在 TEngine 命名空间下用这些类型）。
                        string uncompressedCode = scriptGenerator.GetUncompressedClasses();
                        if (!string.IsNullOrWhiteSpace(uncompressedCode))
                        {
                            string uncFolder = Path.Combine(Application.dataPath, UncompressedScriptFolder);
                            Directory.CreateDirectory(uncFolder);
                            string uncFinalPath = Path.Combine(uncFolder, $"{className}Uncompressed.cs");
                            string uncTempPath = uncFinalPath + ".tmp";
                            string uncFileContent = "// <auto-generated by ExcelTool - Editor-only, 不入运行时>\n" +
                                                    "/*无共享缓存版本类，供报告工具加载 useSharedCache=false 导出的 bytes*/\n" +
                                                    "using System;\n" +
                                                    "using System.Collections.Generic;\n" +
                                                    "using UnityEngine.Scripting;\n" +
                                                    "using UnityEngine;\n" +
                                                    "using Luban;\n" +
                                                    "namespace TEngine\n{\n" +
                                                    uncompressedCode +
                                                    "\n}\n";
                            File.WriteAllText(uncTempPath, uncFileContent);
                            tempFiles.Add((uncTempPath, uncFinalPath));
                        }
                    }
                }

                // 注册表与表类同批生成：与既有条目合并（单表导出不丢注册），temp+rename 与表文件同批落盘
                var registryScriptFolder = Path.Combine(Application.dataPath, ExcelExportContext.ScriptFolder);
                string registryCode = ConfigTableRegistryGenerator.Build(registryScriptFolder, selectedSheets);
                string registryFinalPath = ConfigTableRegistryGenerator.GetRegistryPath(registryScriptFolder);
                string registryTempPath = registryFinalPath + ".tmp";
                File.WriteAllText(registryTempPath, registryCode);
                tempFiles.Add((registryTempPath, registryFinalPath));

                // 第二遍：全部成功后 rename 覆盖目标文件
                foreach (var (tempPath, finalPath) in tempFiles)
                {
                    if (File.Exists(finalPath))
                        File.Delete(finalPath);
                    File.Move(tempPath, finalPath);
                }
            }
            finally
            {
                // 清理残留的 .tmp 文件
                foreach (var (tempPath, _) in tempFiles)
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
        }

        // 新增：生成选中项的公共方法
        public static void GenerateSelectedSheets(ExcelExportContext ctx, IEnumerable<string> selectedSheets, bool isDelete)
        {
            try
            {
                // 初始化目录和语言表（与 LoadExcelData 对齐，但不扫描全部文件）
                var inputPath = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigTablePath);
                var languageFile = Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath, "Localization.xlsx");
                CreateDirectory(inputPath);
                CreateDirectory(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath));
                CreateDirectory(Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigLanguagePath));
                if (!File.Exists(languageFile))
                    File.Copy(Path.Combine(Application.dataPath, "Editor/ExcelTool/Config/Localization.xlsx"), languageFile);

                ClearExportState(ctx);

                var enumerable = selectedSheets as string[] ?? selectedSheets.ToArray();
                var selectedClassNames = new List<string>();
                LocalizationExporter.GetLanguageExcelData(ctx, languageFile);

                // 确保 NameToFileMap 已填充 — ctx 可能是新建的，需要扫描文件
                if (ctx.NameToFileMap.Count == 0)
                    GetExcelSheetName(ctx);

                int fileIndex = 0;
                foreach (var item in enumerable)
                {
                    fileIndex++;
                    ShowFileProgress(fileIndex, enumerable.Length, item);
                    if (ctx.NameToFileMap.TryGetValue(item, out var filePath))
                    {
                        selectedClassNames.AddRange(GetExcelData(ctx, filePath));
                    }
                    else
                    {
                        Debug.LogWarning($"[GenerateSelectedSheets] 未找到文件映射: '{item}'。NameToFileMap 中有 {ctx.NameToFileMap.Count} 个条目: {string.Join(", ", ctx.NameToFileMap.Keys.Take(5))}...");
                    }
                }

                EditorUtility.DisplayProgressBar("导表进度", "正在生成代码...", 0.7f);
                GenerateCodeFiles(ctx, selectedClassNames, isDelete);

                EditorUtility.DisplayProgressBar("导表进度", "正在写入bytes...", 0.85f);
                ConfigBinaryWriter.Serialize(ctx, selectedClassNames);

                EditorUtility.DisplayProgressBar("导表进度", "正在刷新资源...", 0.95f);
                LocalizationExporter.SaveLanguageExcelData(ctx, languageFile);
                LocalizationExporter.SerializeLanguage(ctx);

                var result = new ExcelExportResult();
                result.TableCount = selectedClassNames.Count;
                result.GeneratedFileCount = ctx.DataDict.Count;
                Debug.Log($"[GenerateSelectedSheets] 选中 {enumerable.Length} 个文件, 解析到 {selectedClassNames.Count} 个类名, DataDict 中有 {ctx.DataDict.Count} 个表");
                result.LogAllToConsole();
                result.ShowDialog();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}