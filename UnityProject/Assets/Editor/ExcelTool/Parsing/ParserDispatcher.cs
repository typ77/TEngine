using System;
using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    public class ParserDispatcher
    {
        private const int MaxRecursionDepth = 32;
        private readonly Dictionary<string, ITypeParser> _parsers;

        public ParserDispatcher()
        {
            _parsers = new Dictionary<string, ITypeParser>(StringComparer.OrdinalIgnoreCase)
            {
                // Phase 2a: 先注册前4个 Parser
                ["primitive"]  = new PrimitiveParser(),
                ["array"]      = new ArrayParser(this),
                ["list"]       = new ListParser(this),
                ["dictionary"] = new DictionaryParser(this),
                // Phase 2b: 注册 enum, valuetuple, vector, datetime, object
                ["enum"]       = new EnumParser(),
                ["valuetuple"] = new ValueTupleParser(this),
                ["vector"]     = new VectorParser(this),
                ["datetime"]   = new DateTimeParser(),
                ["object"]     = new ObjectParser(this),
            };
        }

        public object Parse(string type, string data, ParseContext ctx, int depth = 0)
        {
            if (depth > MaxRecursionDepth)
                throw new ExcelExportException(
                    $"类型嵌套超过最大深度 {MaxRecursionDepth}: '{type}'",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);

            var parserName = ResolveParserName(type);
            if (!_parsers.TryGetValue(parserName, out var parser))
                throw new TypeNotSupportedException(
                    $"不支持的数据类型: '{type}'",
                    ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);

            return parser.Parse(type, data, ctx, depth);
        }

        private static string ResolveParserName(string type)
        {
            if (type.EndsWith(SupportType.Array, StringComparison.OrdinalIgnoreCase)) return "array";
            if (type.StartsWith(SupportType.List, StringComparison.OrdinalIgnoreCase)) return "list";
            if (type.StartsWith(SupportType.Dictionary, StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith(SupportType.DictionaryFull, StringComparison.OrdinalIgnoreCase)) return "dictionary";
            if (type.StartsWith(SupportType.Enum, StringComparison.OrdinalIgnoreCase)) return "enum";
            if (type.StartsWith(SupportType.ValueTuple, StringComparison.OrdinalIgnoreCase)) return "valuetuple";
            if (IsVectorType(type)) return "vector";
            if (type.Equals(SupportType.DateTime, StringComparison.OrdinalIgnoreCase)) return "datetime";
            if (IsCustomObject(type)) return "object";
            return "primitive";
        }

        private static bool IsVectorType(string type)
        {
            var lower = type.ToLowerInvariant();
            return lower == SupportType.Vector2 || lower == SupportType.Vector2Full ||
                   lower == SupportType.Vector3 || lower == SupportType.Vector3Full ||
                   lower == SupportType.Vector4 || lower == SupportType.Vector4Full ||
                   lower == SupportType.Vector2Int || lower == SupportType.Vector2IntFull ||
                   lower == SupportType.Vector3Int || lower == SupportType.Vector3IntFull ||
                   lower == SupportType.Quaternion || lower == SupportType.QuaternionFull ||
                   lower == SupportType.Color;
        }

        private static bool IsCustomObject(string type)
            => type.Equals(SupportType.RewardObject, StringComparison.OrdinalIgnoreCase) ||
               type.Equals(SupportType.ConditionObject, StringComparison.OrdinalIgnoreCase) ||
               type.Equals(SupportType.EffectCfgObject, StringComparison.OrdinalIgnoreCase) ||
               type.Equals(SupportType.GradedPriceObject, StringComparison.OrdinalIgnoreCase);
    }
}