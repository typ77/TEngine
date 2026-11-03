using System;
using System.Collections;
using System.Linq;

namespace TEngine.Editor.ExcelTool
{
    public class ListParser : ITypeParser
    {
        private readonly ParserDispatcher _dispatcher;

        public ListParser(ParserDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            string innerType = type.Substring(5, type.Length - 6).Trim();
            data = BracketUtils.RemoveOuterBracketsAlways(data);
            var elements = BracketUtils.ParseElements(data).ToArray();
            string resolvedInnerType = ReadExcel.GetTrueType(innerType, ctx.ClassName, "", false);
            Type elementType = TypeRegistry.Instance.GetSystemType(resolvedInnerType);
            if (elementType == null)
                elementType = Type.GetType(resolvedInnerType);
            if (elementType == null)
                throw new TypeNotSupportedException(
                    $"List 元素类型 '{innerType}' 无法解析 (完整类型: '{type}')",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            var list = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(elementType), elements.Length);
            for (int i = 0; i < elements.Length; i++)
                list.Add(_dispatcher.Parse(innerType, elements[i], ctx.WithLocation(ctx.Row, ctx.Column), depth + 1));
            return list;
        }
    }
}