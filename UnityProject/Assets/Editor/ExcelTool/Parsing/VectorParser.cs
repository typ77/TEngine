using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 向量/颜色类型解析器 — 统一 Vector2/3/4、Vector2Int/3Int、Quaternion、Color。
    /// 共用逗号正则分割逻辑。
    /// </summary>
    public class VectorParser : ITypeParser
    {
        private readonly ParserDispatcher _dispatcher;

        public VectorParser(ParserDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            data = BracketUtils.RemoveOuterBracketsAlways(data);

            // 共用正则分割: 逗号分隔，正确处理引号
            var parts = Regex.Matches(data, "(?:\"(?:[^\"]|\"\")*\"|[^,]+)")
                .Cast<Match>()
                .Select(m => m.Value)
                .ToArray();

            var lower = type.ToLowerInvariant();

            if (lower == SupportType.Vector2 || lower == SupportType.Vector2Full)
            {
                var v = new Vector2();
                v.x = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                v.y = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                return v;
            }

            if (lower == SupportType.Vector3 || lower == SupportType.Vector3Full)
            {
                var v = new Vector3();
                v.x = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                v.y = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                v.z = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 3 ? parts[2] : "0", ctx, depth + 1);
                return v;
            }

            if (lower == SupportType.Vector4 || lower == SupportType.Vector4Full)
            {
                var v = new Vector4();
                v.x = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                v.y = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                v.z = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 3 ? parts[2] : "0", ctx, depth + 1);
                v.w = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 4 ? parts[3] : "0", ctx, depth + 1);
                return v;
            }

            if (lower == SupportType.Vector2Int || lower == SupportType.Vector2IntFull)
            {
                var v = new Vector2Int();
                v.x = (int)_dispatcher.Parse(SupportType.Int, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                v.y = (int)_dispatcher.Parse(SupportType.Int, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                return v;
            }

            if (lower == SupportType.Vector3Int || lower == SupportType.Vector3IntFull)
            {
                var v = new Vector3Int();
                v.x = (int)_dispatcher.Parse(SupportType.Int, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                v.y = (int)_dispatcher.Parse(SupportType.Int, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                v.z = (int)_dispatcher.Parse(SupportType.Int, parts.Length >= 3 ? parts[2] : "0", ctx, depth + 1);
                return v;
            }

            if (lower == SupportType.Quaternion || lower == SupportType.QuaternionFull)
            {
                var q = new Quaternion();
                q.x = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                q.y = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                q.z = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 3 ? parts[2] : "0", ctx, depth + 1);
                q.w = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 4 ? parts[3] : "0", ctx, depth + 1);
                return q;
            }

            if (lower == SupportType.Color)
            {
                var c = new Color();
                c.r = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 1 ? parts[0] : "0", ctx, depth + 1);
                c.g = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 2 ? parts[1] : "0", ctx, depth + 1);
                c.b = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 3 ? parts[2] : "0", ctx, depth + 1);
                c.a = (float)_dispatcher.Parse(SupportType.Float, parts.Length >= 4 ? parts[3] : "0", ctx, depth + 1);
                return c;
            }

            throw new TypeNotSupportedException(
                $"不支持的向量类型: '{type}'",
                ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
        }
    }
}