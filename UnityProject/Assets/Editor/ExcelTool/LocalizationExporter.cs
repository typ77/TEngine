using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 多语言导出工具
    /// </summary>
    public static class LocalizationExporter
    {
        /// <summary>
        /// 读取多语言 Excel 文件
        /// </summary>
        public static void GetLanguageExcelData(ExcelExportContext ctx, string file)
        {
            ctx.LocalizationService.Read(file);
        }

        /// <summary>
        /// 保存多语言 Excel 文件
        /// </summary>
        public static void SaveLanguageExcelData(ExcelExportContext ctx, string file)
        {
            ctx.LocalizationService.Save(file);
        }

        /// <summary>
        /// 序列化多语言数据为二进制文件
        /// </summary>
        public static void SerializeLanguage(ExcelExportContext ctx)
        {
            //多语言导表逻辑
            //1.text 为默认文本，一般视为中文
            //2.遍历其他key，其他为多语言，但是key还是为text
            //3.最终输出文件名为：language_default language_ChineseSimplified language_English
            //4.需要重新读表，因为多语言表可能已经改变
            //_languageDataDictg 根据多语言版本生成多个byte
            var configDir = Path.GetDirectoryName(Settings.UpdateSetting.ConfigOutPutPath) ?? "";
            var outputPath = Path.Combine(Application.dataPath, configDir, "LanguageBytes");
            Directory.CreateDirectory(outputPath);
            var languageData = ctx.LocalizationService.GetLanguageData();
            using var itr = languageData.GetEnumerator();
            while (itr.MoveNext())
            {
                var language = itr.Current.Key;
                var dataList = itr.Current.Value;
                ConfigBinaryWriter.WriteByteFile(ctx, $"Language_{language}", dataList, outputPath, false);
            }
        }
    }
}