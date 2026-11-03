using System;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 枚举类型解析器 — D18 修复: 扁平化三层 try/catch 为两层条件判断。
    /// 策略：先 TryParse 按名称匹配，失败则尝试 int.TryParse 按数值转换。
    /// 两者均失败则抛出异常。
    /// </summary>
    public class EnumParser : ITypeParser
    {
        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            data = BracketUtils.RemoveOuterBracketsAlways(data);

            // 从 enum<XXX> 中提取泛型参数
            int startIdx = type.IndexOf('<');
            int endIdx = type.LastIndexOf('>');
            if (startIdx < 0 || endIdx < 0 || endIdx <= startIdx)
                throw new ExcelExportException(
                    $"枚举类型字符串 '{type}' 格式错误：缺少 '<' 或 '>'",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);

            string innerContent = type.Substring(startIdx + 1, endIdx - startIdx - 1)
                .Split(',')[0]
                .Trim();

            // 构建完整的程序集限定枚举类型名
            string fullEnumTypeName;
            if (innerContent.Contains('.'))
            {
                string[] parts = innerContent.Split('.');
                fullEnumTypeName = $"{TypeRegistry.CodeNamespace}.{parts[0]}+{parts[1]},{TypeRegistry.CodeNamespace}";
            }
            else
            {
                string className = ctx.ClassName;
                fullEnumTypeName = $"{TypeRegistry.CodeNamespace}.{className.Substring(0, className.Length - 4)}+{innerContent},{TypeRegistry.CodeNamespace}";
            }

            Type enumType = Type.GetType(fullEnumTypeName);

            if (enumType == null || !enumType.IsEnum)
            {
                throw new ExcelExportException(
                    $"枚举类型不存在，请检查定义！尝试加载的类型名: {fullEnumTypeName}",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            }

            // D18: 扁平化三层 try/catch → 两层条件判断
            // 第一层: 按名称匹配
            if (Enum.TryParse(enumType, data, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            // 第二层: 按数值转换
            if (int.TryParse(data, out int enumValue) && Enum.IsDefined(enumType, enumValue))
            {
                return Enum.ToObject(enumType, enumValue);
            }

            // 两者均失败 → 报错并返回默认值
            ctx.Result?.Error($"Invalid value '{data}' for type '{type}'",
                ctx.FilePath, ctx.SheetName, ctx.Row, ctx.Column);
            return Activator.CreateInstance(enumType);
        }
    }
}