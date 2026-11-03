using System;
using System.Linq;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 值元组解析器 — 解析 valuetuple<T1,T2,...> 格式的值元组。
    /// 限制：最多 7 个类型参数。
    /// </summary>
    public class ValueTupleParser : ITypeParser
    {
        private readonly ParserDispatcher _dispatcher;

        public ValueTupleParser(ParserDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            data = BracketUtils.RemoveOuterBracketsAlways(data);

            // 从 valuetuple<T1,T2,...> 中提取类型参数列表
            int startIdx = type.IndexOf('<');
            int endIdx = type.LastIndexOf('>');
            if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
                throw new ExcelExportException(
                    $"值元组类型字符串 '{type}' 格式错误：缺少 '<' 或 '>'",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);

            string[] typeArgs = type.Substring(startIdx + 1, endIdx - startIdx - 1)
                .Split(',')
                .Select(t => t.Trim())
                .ToArray();

            if (typeArgs.Length < 1 || typeArgs.Length > 7)
                throw new ExcelExportException(
                    $"限制值元组长度最大为7: {typeArgs.Length}",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);

            var elements = BracketUtils.ParseElements(data).ToArray();

            Type[] genericTypes = typeArgs.Select(t => TypeRegistry.Instance.GetSystemType(t)).ToArray();
            object[] values = new object[typeArgs.Length];

            for (int i = 0; i < typeArgs.Length; i++)
            {
                values[i] = _dispatcher.Parse(
                    typeArgs[i],
                    elements.Length > i ? elements[i] : "",
                    ctx.WithLocation(ctx.Row, ctx.Column),
                    depth + 1);
            }

            Type tupleType = Type.GetType($"System.ValueTuple`{typeArgs.Length}")?.MakeGenericType(genericTypes);

            if (tupleType == null)
                throw new InvalidOperationException("无法创建值元组类型");

            return Activator.CreateInstance(tupleType, values);
        }
    }
}