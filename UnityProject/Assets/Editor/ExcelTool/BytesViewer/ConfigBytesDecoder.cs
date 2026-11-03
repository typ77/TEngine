using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Luban;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 单张配置表 bytes 的反向解码结果。
    /// </summary>
    public sealed class DecodedTable
    {
        /// <summary>文件绝对路径。</summary>
        public string FilePath;
        /// <summary>文件名（含扩展名）。</summary>
        public string FileName;
        /// <summary>表名（文件名去扩展名，与生成类名一致）。</summary>
        public string TableName;
        /// <summary>是否为多语言表（Language_*.bytes）。</summary>
        public bool IsLanguageFile;
        /// <summary>表头声明的总行数。</summary>
        public int TotalCount;
        /// <summary>列名列表：首列固定为 "key"，其余为生成 Item 类的公共属性（声明序）。</summary>
        public string[] Columns;
        /// <summary>行原始值（首列为 key），与 Columns 对齐。</summary>
        public List<object[]> Rows = new List<object[]>();
        /// <summary>行显示文本（已格式化），与 Columns 对齐，供网格直接渲染。</summary>
        public List<string[]> RowTexts = new List<string[]>();
        /// <summary>解码耗时（毫秒）。</summary>
        public double DecodeMs;
        /// <summary>非 null 表示解码失败，内容为失败原因。</summary>
        public string Error;
        /// <summary>失败时的 hex 预览。</summary>
        public string HexPreview;
        /// <summary>是否解码成功。</summary>
        public bool Ok => Error == null;
    }

    /// <summary>
    /// 配置表 bytes 反向解码服务。
    /// 复用运行时生成类（TableBaseTemp.SetByteBuf + Item 构造函数）解码，保证与 ConfigSystem 运行时行为一致；
    /// 共享缓存段由生成表类重写的 ReadHead 自动处理。多语言还原通过注入 ConfigSystem 语言表实现。仅编辑器使用。
    /// </summary>
    public static class ConfigBytesDecoder
    {
        private const string LanguagePrefix = "Language_";
        private const int HexPreviewBytes = 256;

        /// <summary>配置表 bytes 输出目录（与 ConfigBinaryWriter 同口径）。</summary>
        public static string GetTableBytesDir()
        {
            return Path.Combine(Application.dataPath, Settings.UpdateSetting.ConfigOutPutPath);
        }

        /// <summary>多语言 bytes 输出目录（与 LocalizationExporter 同口径：输出目录的同级 LanguageBytes）。</summary>
        public static string GetLanguageBytesDir()
        {
            var configDir = Path.GetDirectoryName(Settings.UpdateSetting.ConfigOutPutPath) ?? string.Empty;
            return Path.Combine(Application.dataPath, configDir, "LanguageBytes");
        }

        /// <summary>
        /// 扫描全部可查看的 bytes 文件（配置表 + 多语言表）。
        /// </summary>
        public static List<string> ScanTableFiles()
        {
            var files = new List<string>();
            var tableDir = GetTableBytesDir();
            if (Directory.Exists(tableDir))
            {
                files.AddRange(Directory.GetFiles(tableDir, "*.bytes"));
            }
            var langDir = GetLanguageBytesDir();
            if (Directory.Exists(langDir))
            {
                files.AddRange(Directory.GetFiles(langDir, "*.bytes"));
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>
        /// 扫描多语言 bytes 文件（Language_*.bytes）。
        /// </summary>
        public static List<string> ScanLanguageFiles()
        {
            var files = new List<string>();
            var langDir = GetLanguageBytesDir();
            if (Directory.Exists(langDir))
            {
                files.AddRange(Directory.GetFiles(langDir, "*.bytes"));
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>
        /// 语言文件的显示名（去 Language_ 前缀与扩展名，如 ChineseSimplified）。
        /// </summary>
        public static string GetLanguageDisplayName(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            return name.StartsWith(LanguagePrefix, StringComparison.Ordinal) ? name.Substring(LanguagePrefix.Length) : name;
        }

        /// <summary>
        /// 解码一个 bytes 文件为表格数据。
        /// </summary>
        public static DecodedTable Decode(string filePath)
        {
            return DecodeInternal(filePath, out _);
        }

        /// <summary>
        /// 解码语言文件并反射注入 ConfigSystem 的私有语言表，
        /// 使生成代码的 GetLanguageString 属性（如 desc）返回文本。仅影响编辑器会话内的单例。
        /// </summary>
        public static bool InjectLanguage(string languageFilePath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(languageFilePath) || !File.Exists(languageFilePath))
            {
                error = $"语言文件不存在: {languageFilePath}";
                return false;
            }
            var decoded = DecodeInternal(languageFilePath, out var configInstance);
            if (decoded.Error != null)
            {
                error = decoded.Error;
                return false;
            }
            try
            {
                var field = typeof(ConfigSystem).GetField("m_languageConfig", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    error = "未找到 ConfigSystem.m_languageConfig 字段（运行时代码可能已变更）";
                    return false;
                }
                field.SetValue(ConfigSystem.Instance, configInstance);
                return true;
            }
            catch (Exception e)
            {
                error = $"注入语言表失败: {e.Message}";
                return false;
            }
        }

        /// <summary>
        /// 值格式化为显示文本：基础类型按 InvariantCulture，容器/自定义对象序列化为 JSON。
        /// </summary>
        public static string FormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is string s)
            {
                return s;
            }
            if (value is bool b)
            {
                return b ? "true" : "false";
            }
            if (value is float f)
            {
                return f.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is double d)
            {
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            try
            {
                return JsonConvert.SerializeObject(value);
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }

        /// <summary>
        /// 全表导出为 JSON（对象数组，含 key 列）。
        /// </summary>
        public static string ToJson(DecodedTable table)
        {
            var array = new JArray();
            foreach (var row in table.Rows)
            {
                var obj = new JObject();
                for (var i = 0; i < table.Columns.Length; i++)
                {
                    var v = row[i];
                    try
                    {
                        obj[table.Columns[i]] = v == null ? JValue.CreateNull() : JToken.FromObject(v);
                    }
                    catch (Exception e)
                    {
                        obj[table.Columns[i]] = $"<err: {e.Message}>";
                    }
                }
                array.Add(obj);
            }
            return array.ToString(Formatting.Indented);
        }

        /// <summary>
        /// 全表导出为 CSV 文本（调用方负责写文件，建议 UTF-8 带 BOM 便于 Excel 识别中文）。
        /// </summary>
        public static string ToCsv(DecodedTable table)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", table.Columns.Select(EscapeCsvField)));
            foreach (var text in table.RowTexts)
            {
                sb.AppendLine(string.Join(",", text.Select(EscapeCsvField)));
            }
            return sb.ToString();
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return string.Empty;
            }
            var needQuote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            return needQuote ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
        }

        /// <summary>
        /// 解码核心：复用运行时读端（SetByteBuf → GetAllKeys × GetItem），反射 Item 属性逐格取值。
        /// configInstance 供语言表注入复用（已全量物化行缓存）。
        /// </summary>
        private static DecodedTable DecodeInternal(string filePath, out object configInstance)
        {
            var sw = Stopwatch.StartNew();
            var fileName = Path.GetFileName(filePath);
            var result = new DecodedTable
            {
                FilePath = filePath,
                FileName = fileName,
                TableName = Path.GetFileNameWithoutExtension(filePath),
                IsLanguageFile = fileName.StartsWith(LanguagePrefix, StringComparison.Ordinal),
            };
            configInstance = null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(filePath);
            }
            catch (Exception e)
            {
                result.Error = $"读取文件失败: {e.Message}";
                return result;
            }

            // 类型映射与 ConfigSystem.InitConfig 一致：TEngine.{文件名}
            var assembly = typeof(ConfigSystem).Assembly;
            var dataType = assembly.GetType("TEngine." + result.TableName);
            if (dataType == null && fileName.StartsWith(LanguagePrefix, StringComparison.Ordinal))
            {
                // 多语言文件名 ≠ 表类名：运行时 InitLanguage 固定 new LanguageConfig() 后喂数据
                dataType = assembly.GetType("TEngine.LanguageConfig");
            }
            if (dataType == null)
            {
                result.Error = $"未找到生成类 TEngine.{result.TableName}（生成代码与 bytes 不同步？请先执行 Tools/配置表工具/生成全部数据）";
                result.HexPreview = BuildHexPreview(bytes, HexPreviewBytes);
                return result;
            }

            try
            {
                var configObj = Activator.CreateInstance(dataType);
                // SetByteBuf 是 TableBaseTemp 的公开入口，不走 SetTextAsset（后者会经 ResourceModule 卸载，编辑器环境不可用）
                var setByteBuf = dataType.GetMethod("SetByteBuf");
                setByteBuf.Invoke(configObj, new object[] { new ByteBuf(bytes) });
                result.TotalCount = ((ConfigTableBase)configObj).TotalCount;

                var getAllKeys = dataType.GetMethod("GetAllKeys");
                var getItem = dataType.GetMethod("GetItem");
                var keys = (IEnumerable)getAllKeys.Invoke(configObj, null);

                var itemType = dataType.BaseType.GetGenericArguments()[1];
                var props = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                var columns = new string[props.Length + 1];
                columns[0] = "key";
                for (var i = 0; i < props.Length; i++)
                {
                    columns[i + 1] = props[i].Name;
                }
                result.Columns = columns;

                foreach (var key in keys)
                {
                    var item = getItem.Invoke(configObj, new[] { key, (object)true });
                    var row = new object[columns.Length];
                    var text = new string[columns.Length];
                    row[0] = key;
                    text[0] = FormatValue(key);
                    if (item != null)
                    {
                        for (var i = 0; i < props.Length; i++)
                        {
                            object value;
                            try
                            {
                                value = props[i].GetValue(item);
                            }
                            catch (Exception e)
                            {
                                value = $"<err: {e.InnerException?.Message ?? e.Message}>";
                            }
                            row[i + 1] = value;
                            text[i + 1] = FormatValue(value);
                        }
                    }
                    result.Rows.Add(row);
                    result.RowTexts.Add(text);
                }
                configInstance = configObj;
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                result.Error = $"解码失败: {inner.GetType().Name}: {inner.Message}";
                result.HexPreview = BuildHexPreview(bytes, HexPreviewBytes);
                return result;
            }

            result.DecodeMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// 生成前 maxBytes 字节的 hex 预览（兜底视图用）。
        /// </summary>
        private static string BuildHexPreview(byte[] data, int maxBytes)
        {
            var sb = new StringBuilder();
            var count = Math.Min(data.Length, maxBytes);
            for (var offset = 0; offset < count; offset += 16)
            {
                sb.AppendFormat("{0:X4}  ", offset);
                var lineEnd = Math.Min(offset + 16, count);
                for (var i = offset; i < offset + 16; i++)
                {
                    if (i < lineEnd)
                    {
                        sb.AppendFormat("{0:X2} ", data[i]);
                    }
                    else
                    {
                        sb.Append("   ");
                    }
                    if (i == offset + 7)
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(' ');
                for (var i = offset; i < lineEnd; i++)
                {
                    var c = (char)data[i];
                    sb.Append(c >= 0x20 && c < 0x7F ? c : '.');
                }
                sb.Append('\n');
            }
            sb.Append($"（共 {data.Length} 字节，预览前 {count} 字节）");
            return sb.ToString();
        }
    }
}
