using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TEngine.Editor.ExcelTool
{
    public class PrimitiveParser : ITypeParser
    {
        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            switch (type)
            {
                case SupportType.Bool:
                    return data.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                           data.Equals("1", StringComparison.OrdinalIgnoreCase);

                case SupportType.Byte:
                    return byte.TryParse(data, out var b) ? b : LogAndDefault(ctx, type, data, (byte)0);
                case SupportType.SByte:
                    return sbyte.TryParse(data, out var sb) ? sb : LogAndDefault(ctx, type, data, (sbyte)0);
                case SupportType.Short:
                    return short.TryParse(data, out var s) ? s : LogAndDefault(ctx, type, data, (short)0);
                case SupportType.UShort:
                    return ushort.TryParse(data, out var us) ? us : LogAndDefault(ctx, type, data, (ushort)0);
                case SupportType.Int:
                case SupportType.LanguageType:
                    return int.TryParse(data, out var i) ? i : LogAndDefault(ctx, type, data, 0);
                case SupportType.UInt:
                    return uint.TryParse(data, out var ui) ? ui : LogAndDefault(ctx, type, data, 0u);
                case SupportType.Long:
                    return long.TryParse(data, out var l) ? l : LogAndDefault(ctx, type, data, 0L);
                case SupportType.ULong:
                    return ulong.TryParse(data, out var ul) ? ul : LogAndDefault(ctx, type, data, 0UL);
                case SupportType.Float:
                    return float.TryParse(data, out var f) ? f : LogAndDefault(ctx, type, data, 0f);
                case SupportType.Double:
                    return double.TryParse(data, out var d) ? d : LogAndDefault(ctx, type, data, 0d);
                case SupportType.Decimal:
                    return decimal.TryParse(data, out var dec) ? dec : LogAndDefault(ctx, type, data, 0m);

                case SupportType.String:
                case SupportType.StringFull:
                    return ProcessString(data);

                case SupportType.Obj:
                case SupportType.ObjFull:
                    return ProcessObject(data);

                default:
                    throw new TypeNotSupportedException($"未知基础类型: '{type}'",
                        ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
            }
        }

        private static readonly string[] AutoStripExtensions = { ".jpg", ".jpeg", ".png", ".prefab" };

        private static string ProcessString(string data)
        {
            string processed = Regex.Replace(data, @"(?<!\\)""", "");
            processed = processed.Replace("\\\"", "\"");
            processed = processed.Replace("\\n", "\n");

            // Assets/ 开头的路径 → 只要文件名（不含扩展名）
            if (processed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileNameWithoutExtension(processed);

            // 资源后缀 → 只要文件名（不含扩展名）
            foreach (var ext in AutoStripExtensions)
                if (processed.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileNameWithoutExtension(processed);

            return processed;
        }

        private static object ProcessObject(string data)
        {
            if (data.StartsWith("\"") && data.EndsWith("\""))
                return data.Trim('"');
            if (Regex.IsMatch(data, @"[.eE]"))
            {
                if (float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
                if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                if (decimal.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)) return dec;
                return data;
            }
            if (int.TryParse(data, out var iv)) return iv;
            if (long.TryParse(data, out var lv)) return lv;
            if (DateTime.TryParse(data, out var dt)) return dt;
            return data;
        }

        private static T LogAndDefault<T>(ParseContext ctx, string type, string data, T defaultValue)
        {
            ctx.Result?.Error($"Invalid value '{data}' for type '{type}'",
                ctx.FilePath, ctx.SheetName, ctx.Row, ctx.Column);
            return defaultValue;
        }
    }
}