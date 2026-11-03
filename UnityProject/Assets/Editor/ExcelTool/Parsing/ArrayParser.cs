using System;
using System.Linq;

namespace TEngine.Editor.ExcelTool
{
    public class ArrayParser : ITypeParser
    {
        private readonly ParserDispatcher _dispatcher;

        public ArrayParser(ParserDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            string innerType = type.Substring(0, type.Length - 2).Trim();
            data = BracketUtils.RemoveOuterBracketsAlways(data);
            var elements = BracketUtils.ParseElements(data).ToArray();
            string resolvedInnerType = ReadExcel.GetTrueType(innerType, ctx.ClassName, "", false);
            var systemType = TypeRegistry.Instance.GetSystemType(resolvedInnerType + "[]")
                ?? Type.GetType(resolvedInnerType + "[]");
            if (systemType == null)
                throw new TypeNotSupportedException(
                    $"Array 元素类型 '{innerType}' 无法解析 (完整类型: '{type}')",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            var array = (Array)Activator.CreateInstance(systemType, elements.Length);
            for (int i = 0; i < elements.Length; i++)
                array.SetValue(_dispatcher.Parse(innerType, elements[i], ctx.WithLocation(ctx.Row, ctx.Column), depth + 1), i);
            return array;
        }
    }
}