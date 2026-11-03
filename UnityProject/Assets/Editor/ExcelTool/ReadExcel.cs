using System.Collections.Generic;
using System;
using System.Linq;
using System.Collections.Concurrent;

namespace TEngine.Editor.ExcelTool
{
    public class ReadExcel
    {

        // 缓存类型转换结果，减少重复字符串处理
        private static readonly ConcurrentDictionary<string, string> _typeCache = new ConcurrentDictionary<string, string>();

        public static void ClearTypeCache()
        {
            _typeCache.Clear();
        }

        private static readonly ParserDispatcher _dispatcher = new();

        private static void DebugError(string type, string data, string classname,
                                         ExcelExportResult result = null,
                                         string filePath = null, string sheetName = null,
                                         int row = -1, int col = -1)
        {
            result?.Error($"Invalid value '{data}' for type '{type}'",
                           filePath, sheetName, row, col);
        }
        private static string[] SplitKeyValue(string element)
        {
            return BracketUtils.SplitKeyValue(element);
        }

        public static object ParseValue(string type, string data, string classname,
                                         ExcelExportResult result = null,
                                         string filePath = null, string sheetName = null,
                                         int row = -1, int col = -1)
        {
            var ctx = new ParseContext
            {
                ClassName = classname,
                Result = result,
                FilePath = filePath,
                SheetName = sheetName,
                Row = row,
                Column = col
            };
            try
            {
                return _dispatcher.Parse(type, data, ctx);
            }
            catch (ExcelExportException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result?.Error($"Parse error: {ex.Message}. Data: '{data}', Class: {classname}",
                               filePath, sheetName, row, col);
                throw new ExcelExportException($"解析失败: {ex.Message}", classname, filePath, row, col, ex);
            }
        }

        public static Type SystemGetType(string type)
        {
            return TypeRegistry.Instance.GetSystemType(type);
        }

        /// <summary>
        /// 将配置表类型字符串转换为 C# 类型名称（用于代码生成）。结果带缓存。
        /// 示例："int" → "int"，"list<int>" → "System.Collections.Generic.List<int>"
        /// </summary>
        public static string GetTrueType(string type, string className = "", string inputPath = "", bool writtenForm = true)
        {
            // 创建缓存key，包含所有影响结果的参数
            string cacheKey = $"{type}|{className}|{inputPath}|{writtenForm}";

            if (_typeCache.TryGetValue(cacheKey, out string cachedResult))
            {
                return cachedResult;
            }

            string result;

            if (type.EndsWith(SupportType.Array, StringComparison.OrdinalIgnoreCase))
            {
                string innerType = type.Substring(0, type.Length - 2);
                result = GetTrueType(innerType, className, inputPath, writtenForm) + "[]";
            }
            else if (type.StartsWith(SupportType.List, StringComparison.OrdinalIgnoreCase) && type.EndsWith(">"))
            {
                string innerType = type.Substring(5, type.Length - 6);
                if (writtenForm)
                {
                    result = "System.Collections.Generic.List<" + GetTrueType(innerType, className, inputPath, writtenForm) + ">";
                }
                else
                {
                    result = "System.Collections.Generic.List`1[" + GetTrueType(innerType, className, inputPath, writtenForm) + "]";
                }
            }
            else if ((type.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) || type.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase)) && type.EndsWith(">"))
            {
                int commaIndex = type.IndexOf(',');
                if (commaIndex == -1)
                {
                    throw new ValidationException("Dictionary 类型必须包含两个用逗号分隔的类型");
                }
                string keyType = null;
                if (type.StartsWith(SupportType.Dictionary))
                {
                    keyType = type.Substring(5, commaIndex - 5);
                }
                else if(type.StartsWith(SupportType.DictionaryFull))
                {
                    keyType = type.Substring(11, commaIndex - 11);
                }
                string valueType = type.Substring(commaIndex + 1, type.Length - commaIndex - 2);
                if (writtenForm)
                {
                    result = "System.Collections.Generic.Dictionary<" + GetTrueType(keyType, className, inputPath, writtenForm) + "," + GetTrueType(valueType, className, inputPath, writtenForm) + ">";
                }
                else
                {
                    result = "System.Collections.Generic.Dictionary`2[" + GetTrueType(keyType, className, inputPath, writtenForm) + "," + GetTrueType(valueType, className, inputPath, writtenForm) + "]";
                }
            }
            else if (type.StartsWith(SupportType.Enum, StringComparison.OrdinalIgnoreCase))
            {
                // 内联 ExtractGenericArg（原私有方法已随 ParseValue 重构删除）
                var start = type.IndexOf('<');
                var end = type.LastIndexOf('>');
                if (start < 0 || end < 0 || end <= start)
                {
                    throw new FormatException($"类型字符串 '{type}' 泛型参数格式错误：缺少 '<' 或 '>'");
                }
                string innerContent = type.Substring(start + 1, end - start - 1)
                    .Split(',')[0]
                    .Trim();
                if (writtenForm)
                {
                    if (!innerContent.Contains('.'))
                    {
                        innerContent = $"{className}.{innerContent}";
                    }
                }
                else
                {
                    if (innerContent.Contains('.'))
                    {
                        string[] parts = innerContent.Split('.');
                        innerContent = $"{TypeRegistry.CodeNamespace}.{parts[0]}+{parts[1]},{TypeRegistry.CodeNamespace}";
                    }
                    else
                    {
                        innerContent = $"{TypeRegistry.CodeNamespace}.{className.Substring(0, className.Length - 4)}+{innerContent},{TypeRegistry.CodeNamespace}";
                    }
                }
                result = innerContent;
            }
            else if (type.StartsWith(SupportType.ValueTuple, StringComparison.OrdinalIgnoreCase) && type.EndsWith(">"))
            {
                string innerTypes = type.Substring(SupportType.ValueTuple.Length, type.Length - SupportType.ValueTuple.Length - 1);
                string[] typeArgs = innerTypes.Split(',').Select(t => t.Trim()).ToArray();

                string processedTypeArgs = string.Join(",", typeArgs.Select(t => GetTrueType(t, className, inputPath, writtenForm)));

                if (writtenForm)
                {
                    result = $"System.ValueTuple<{processedTypeArgs}>";
                }
                else
                {
                    result = $"System.ValueTuple`{typeArgs.Length}[{processedTypeArgs}]";
                }
            }
            else if (type == SupportType.RewardObject)
            {
                result = "RewardObject";
            }
            else if (type == SupportType.ConditionObject)
            {
                result = "ConditionObject";
            }
            else if (type == SupportType.EffectCfgObject)
            {
                result = "EffectCfgObject";
            }
            else if (type == SupportType.GradedPriceObject)
            {
                result = "GradedPriceObject";
            }
            else
            {
                result = TypeRegistry.Instance.TryResolve(type, out var entry)
                    ? entry.SystemTypeName
                    : type;
            }

            _typeCache.TryAdd(cacheKey, result);
            return result;
        }

        public static string RemoveOuterBracketsIfPaired(string data)
        {
            return BracketUtils.RemoveOuterBracketsAlways(data);
        }

        public static List<string> ParseElements(string data)
        {
            return BracketUtils.ParseElements(data);
        }
    }
}