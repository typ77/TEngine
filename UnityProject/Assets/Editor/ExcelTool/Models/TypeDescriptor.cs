using System;
using System.Collections.Generic;
using System.Linq;

namespace TEngine.Editor.ExcelTool
{
    public enum TypeKind
    {
        Primitive, Array, List, Dict, ValueTuple, CustomObject
    }

    public class TypeDescriptor
    {
        public TypeKind Kind { get; set; }
        public string PrimitiveName { get; set; }
        public TypeDescriptor ElementType { get; set; }
        public TypeDescriptor KeyType { get; set; }
        public TypeDescriptor ValueType { get; set; }
        public List<TypeDescriptor> TupleElements { get; set; }
        public string CustomObjectClassName { get; set; }

        private static readonly Dictionary<string, string> PrimitiveTypes = new()
        {
            {"bool","System.Boolean"}, {"byte","System.Byte"}, {"sbyte","System.SByte"},
            {"short","System.Int16"}, {"ushort","System.UInt16"},
            {"int","System.Int32"}, {"uint","System.UInt32"},
            {"long","System.Int64"}, {"ulong","System.UInt64"},
            {"float","System.Single"}, {"double","System.Double"}, {"decimal","System.Decimal"},
            {"str","System.String"}, {"string","System.String"},
            {"obj","System.Object"}, {"object","System.Object"},
            {"vec2","UnityEngine.Vector2"}, {"vector2","UnityEngine.Vector2"},
            {"vec3","UnityEngine.Vector3"}, {"vector3","UnityEngine.Vector3"},
            {"vec4","UnityEngine.Vector4"}, {"vector4","UnityEngine.Vector4"},
            {"vec2int","UnityEngine.Vector2Int"}, {"vector2int","UnityEngine.Vector2Int"},
            {"vec3int","UnityEngine.Vector3Int"}, {"vector3int","UnityEngine.Vector3Int"},
            {"quat","UnityEngine.Quaternion"}, {"quaternion","UnityEngine.Quaternion"},
            {"color","UnityEngine.Color"}, {"datetime","System.DateTime"}, {"l","System.String"}
        };

        private static readonly HashSet<string> CustomObjects = new(StringComparer.OrdinalIgnoreCase)
            { "RewardObject", "ConditionObject", "EffectCfgObject", "GradedPriceObject" };

        public static TypeDescriptor Parse(string type)
        {
            if (string.IsNullOrEmpty(type))
                throw new ArgumentException("Type cannot be null or empty");
            type = type.Trim();
            var lower = type.ToLower();

            if (lower.EndsWith("[]"))
                return new TypeDescriptor { Kind = TypeKind.Array,
                    ElementType = Parse(type.Substring(0, type.Length - 2)) };

            if (lower.StartsWith("list<") && type.EndsWith(">"))
                return new TypeDescriptor { Kind = TypeKind.List,
                    ElementType = Parse(InnerGeneric(type)) };

            if ((lower.StartsWith("dict<") || lower.StartsWith("dictionary<")) && type.EndsWith(">"))
            {
                var (k, v) = SplitTopLevel(InnerGeneric(type));
                return new TypeDescriptor { Kind = TypeKind.Dict,
                    KeyType = Parse(k), ValueType = Parse(v) };
            }

            if (lower.StartsWith("valuetuple<") && type.EndsWith(">"))
            {
                var parts = SplitTopLevelAll(InnerGeneric(type));
                return new TypeDescriptor { Kind = TypeKind.ValueTuple,
                    TupleElements = parts.Select(Parse).ToList() };
            }

            if (CustomObjects.Contains(type))
                return new TypeDescriptor { Kind = TypeKind.CustomObject,
                    CustomObjectClassName = type };

            if (PrimitiveTypes.ContainsKey(lower))
                return new TypeDescriptor { Kind = TypeKind.Primitive,
                    PrimitiveName = lower };

            throw new ArgumentException($"Unknown type: {type}");
        }

        public string ToSystemTypeName()
        {
            return Kind switch
            {
                TypeKind.Primitive => PrimitiveTypes.TryGetValue(PrimitiveName, out var t) ? t : PrimitiveName,
                TypeKind.Array => ElementType.ToSystemTypeName() + "[]",
                TypeKind.List => $"System.Collections.Generic.List`1[{ElementType.ToSystemTypeName()}]",
                TypeKind.Dict => $"System.Collections.Generic.Dictionary`2[{KeyType.ToSystemTypeName()},{ValueType.ToSystemTypeName()}]",
                TypeKind.ValueTuple => $"System.ValueTuple`{TupleElements.Count}[{string.Join(",", TupleElements.Select(e => e.ToSystemTypeName()))}]",
                TypeKind.CustomObject => CustomObjectClassName,
                _ => throw new NotSupportedException(Kind.ToString())
            };
        }

        private static string InnerGeneric(string type)
        {
            int start = type.IndexOf('<') + 1;
            return type.Substring(start, type.Length - start - 1);
        }

        private static (string, string) SplitTopLevel(string inner)
        {
            int idx = FindTopLevelComma(inner);
            if (idx < 0) throw new ArgumentException($"Cannot find top-level comma in: {inner}");
            return (inner.Substring(0, idx).Trim(), inner.Substring(idx + 1).Trim());
        }

        private static List<string> SplitTopLevelAll(string inner)
        {
            var result = new List<string>();
            int depth = 0, last = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<') depth++;
                else if (inner[i] == '>') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    result.Add(inner.Substring(last, i - last).Trim());
                    last = i + 1;
                }
            }
            result.Add(inner.Substring(last).Trim());
            return result;
        }

        private static int FindTopLevelComma(string s)
        {
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>') depth--;
                else if (s[i] == ',' && depth == 0) return i;
            }
            return -1;
        }
    }
}
