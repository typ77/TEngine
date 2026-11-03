using System;
using System.Collections;
using System.Linq;

namespace TEngine.Editor.ExcelTool
{
    public class DictionaryParser : ITypeParser
    {
        private readonly ParserDispatcher _dispatcher;

        public DictionaryParser(ParserDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            data = BracketUtils.RemoveOuterBracketsAlways(data);

            int commaIndex = type.IndexOf(',');
            if (commaIndex == -1)
            {
                throw new ExcelExportException(
                    "Dictionary 类型必须包含两个用逗号分隔的类型",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            }

            string keyType;
            if (type.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase))
            {
                keyType = type.Substring(5, commaIndex - 5).Trim();
            }
            else if (type.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase))
            {
                keyType = type.Substring(11, commaIndex - 11).Trim();
            }
            else
            {
                throw new TypeNotSupportedException(
                    $"不支持的 Dictionary 类型格式: '{type}'",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            }

            string valueType = type.Substring(commaIndex + 1, type.Length - commaIndex - 2).Trim();
            var elements = BracketUtils.ParseElements(data).ToArray();
            string resolvedKeyType = ReadExcel.GetTrueType(keyType, ctx.ClassName, "", false);
            string resolvedValueType = ReadExcel.GetTrueType(valueType, ctx.ClassName, "", false);
            Type keyElementType = TypeRegistry.Instance.GetSystemType(resolvedKeyType)
                ?? Type.GetType(resolvedKeyType);
            Type valueElementType = TypeRegistry.Instance.GetSystemType(resolvedValueType)
                ?? Type.GetType(resolvedValueType);
            if (keyElementType == null)
                throw new TypeNotSupportedException(
                    $"Dictionary key 类型 '{keyType}' 无法解析 (完整类型: '{type}')",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            if (valueElementType == null)
                throw new TypeNotSupportedException(
                    $"Dictionary value 类型 '{valueType}' 无法解析 (完整类型: '{type}')",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            var dictionary = (IDictionary)Activator.CreateInstance(
                typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(keyElementType, valueElementType));

            bool isColonFormat = elements.Length > 0 && elements.Any(e => e.Contains(":") && BracketUtils.SplitKeyValue(e).Length == 2);

            if (isColonFormat)
            {
                foreach (var elem in elements)
                {
                    var keyValue = BracketUtils.SplitKeyValue(elem);
                    if (keyValue.Length != 2) continue;

                    object key = _dispatcher.Parse(keyType, keyValue[0], ctx.WithLocation(ctx.Row, ctx.Column), depth + 1);
                    object value = _dispatcher.Parse(valueType, keyValue[1], ctx.WithLocation(ctx.Row, ctx.Column), depth + 1);
                    if (!dictionary.Contains(key))
                    {
                        dictionary.Add(key, value);
                    }
                }
            }
            else
            {
                // 逗号交替格式：需奇偶校验
                if (elements.Length % 2 != 0)
                {
                    throw new ExcelExportException(
                        $"Dictionary 逗号交替格式的元素数量必须为偶数，当前为 {elements.Length} 个。" +
                        $"类型: '{type}', 数据: '{data}'",
                        ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
                }

                for (int i = 0; i < elements.Length; i += 2)
                {
                    object key = _dispatcher.Parse(keyType, elements[i], ctx.WithLocation(ctx.Row, ctx.Column), depth + 1);
                    object value = _dispatcher.Parse(valueType, elements[i + 1], ctx.WithLocation(ctx.Row, ctx.Column), depth + 1);
                    if (!dictionary.Contains(key))
                    {
                        dictionary.Add(key, value);
                    }
                }
            }

            return dictionary;
        }
    }
}