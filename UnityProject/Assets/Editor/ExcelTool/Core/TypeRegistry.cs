using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 类型注册表 — 集中管理所有受支持类型的别名、系统类型映射、序列化动作和默认值。
    /// 单例模式，全局唯一实例 <see cref="Instance"/>。
    /// </summary>
    public class TypeRegistry
    {
        /// <summary>由表生成的 C# 数据类型所在命名空间。</summary>
        public const string CodeNamespace = "F8Framework.F8ExcelDataClass";

        // ── TypeEntry 层级 ──────────────────────────────────────────

        public abstract class TypeEntry
        {
            public string[] Aliases { get; }
            public string SystemTypeName { get; }
            public string AssemblyHint { get; }
            public string CsTypeName { get; }
            public string WriteAction { get; }
            public object DefaultValue { get; }

            protected TypeEntry(string[] aliases, string systemTypeName, string assemblyHint,
                string csTypeName, string writeAction, object defaultValue)
            {
                Aliases = aliases;
                SystemTypeName = systemTypeName;
                AssemblyHint = assemblyHint;
                CsTypeName = csTypeName;
                WriteAction = writeAction;
                DefaultValue = defaultValue;
            }
        }

        public class PrimitiveEntry : TypeEntry
        {
            public PrimitiveEntry(string[] aliases, string systemTypeName, string assemblyHint,
                string csTypeName, string writeAction, object defaultValue)
                : base(aliases, systemTypeName, assemblyHint, csTypeName, writeAction, defaultValue) { }
        }

        public class CustomObjectEntry : TypeEntry
        {
            public CustomObjectEntry(string alias, string fullClrTypeName)
                : base(new[] { alias }, fullClrTypeName, null, alias, null, null) { }
        }

        // ── 数据 ────────────────────────────────────────────────────

        private readonly Dictionary<string, TypeEntry> _byAlias =
            new Dictionary<string, TypeEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] ContainerPatterns =
            { "list<", "dict<", "dictionary<", "valuetuple<" };

        private static readonly HashSet<string> KeyTypeNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "int", "uint", "long", "ulong", "short", "ushort", "str", "string" };

        private static readonly PrimitiveEntry[] BaseTypes =
        {
            new(new[] { SupportType.Bool },
                "System.Boolean", "mscorlib", "bool", "WriteBool", false),
            new(new[] { SupportType.Byte },
                "System.Byte", "mscorlib", "byte", "WriteByte", (byte)0),
            new(new[] { SupportType.SByte },
                "System.SByte", "mscorlib", "sbyte", null, (sbyte)0),
            new(new[] { SupportType.Short },
                "System.Int16", "mscorlib", "short", "WriteShort", (short)0),
            new(new[] { SupportType.UShort },
                "System.UInt16", "mscorlib", "ushort", "WriteShort", (ushort)0),
            new(new[] { SupportType.Int },
                "System.Int32", "mscorlib", "int", "WriteInt", 0),
            new(new[] { SupportType.UInt },
                "System.UInt32", "mscorlib", "uint", "WriteInt", 0u),
            new(new[] { SupportType.Long },
                "System.Int64", "mscorlib", "long", "WriteLong", 0L),
            new(new[] { SupportType.ULong },
                "System.UInt64", "mscorlib", "ulong", "WriteLong", 0UL),
            new(new[] { SupportType.Float },
                "System.Single", "mscorlib", "float", "WriteFloat", 0f),
            new(new[] { SupportType.Double },
                "System.Double", "mscorlib", "double", "WriteDouble", 0d),
            new(new[] { SupportType.Decimal },
                "System.Decimal", "mscorlib", "decimal", null, 0m),
            new(new[] { SupportType.String },
                "System.String", "mscorlib", "string", "WriteString", ""),
            new(new[] { SupportType.StringFull },
                "System.String", "mscorlib", "string", "WriteString", ""),
            new(new[] { SupportType.Obj },
                "System.Object", "mscorlib", "object", null, null),
            new(new[] { SupportType.ObjFull },
                "System.Object", "mscorlib", "object", null, null),
            new(new[] { SupportType.Vector2, SupportType.Vector2Full },
                "UnityEngine.Vector2", "UnityEngine", "UnityEngine.Vector2", "WriteVector2", default(Vector2)),
            new(new[] { SupportType.Vector3, SupportType.Vector3Full },
                "UnityEngine.Vector3", "UnityEngine", "UnityEngine.Vector3", "WriteVector3", default(Vector3)),
            new(new[] { SupportType.Vector4, SupportType.Vector4Full },
                "UnityEngine.Vector4", "UnityEngine", "UnityEngine.Vector4", null, default(Vector4)),
            new(new[] { SupportType.Vector2Int, SupportType.Vector2IntFull },
                "UnityEngine.Vector2Int", "UnityEngine.CoreModule", "UnityEngine.Vector2Int", null, default(Vector2Int)),
            new(new[] { SupportType.Vector3Int, SupportType.Vector3IntFull },
                "UnityEngine.Vector3Int", "UnityEngine.CoreModule", "UnityEngine.Vector3Int", null, default(Vector3Int)),
            new(new[] { SupportType.Quaternion, SupportType.QuaternionFull },
                "UnityEngine.Quaternion", "UnityEngine", "UnityEngine.Quaternion", null, default(Quaternion)),
            new(new[] { SupportType.Color },
                "UnityEngine.Color", "UnityEngine", "UnityEngine.Color", null, default(Color)),
            new(new[] { SupportType.DateTime },
                "System.DateTime", "mscorlib", "System.DateTime", null, default(DateTime)),
            new(new[] { SupportType.LanguageType },
                "System.String", "mscorlib", "string", "WriteInt", 0),
        };

        private static readonly CustomObjectEntry[] CustomObjectTypes =
        {
            new(SupportType.RewardObject,
                "System.Collections.Generic.List`1[[System.Collections.Generic.List`1[[System.Int64,mscorlib]],mscorlib]]"),
            new(SupportType.ConditionObject,
                "System.Collections.Generic.List`1[[System.Collections.Generic.List`1[[System.String,mscorlib]],mscorlib]]"),
            new(SupportType.EffectCfgObject,
                "System.Collections.Generic.List`1[[System.Collections.Generic.List`1[[System.String,mscorlib]],mscorlib]]"),
            new(SupportType.GradedPriceObject,
                "System.Collections.Generic.List`1[[System.Collections.Generic.List`1[[System.Int64,mscorlib]],mscorlib]]"),
        };

        public static TypeRegistry Instance { get; } = new TypeRegistry();

        // ── 构造函数 ────────────────────────────────────────────────

        private TypeRegistry()
        {
            foreach (var entry in BaseTypes)
                foreach (var alias in entry.Aliases)
                    _byAlias[alias] = entry;
            foreach (var entry in CustomObjectTypes)
                foreach (var alias in entry.Aliases)
                    _byAlias[alias] = entry;
        }

        // ── 公开 API ────────────────────────────────────────────────

        public bool TryResolve(string alias, out TypeEntry entry)
            => _byAlias.TryGetValue(alias, out entry);

        /// <summary>判断类型字符串是否为已知/受支持的类型。</summary>
        public bool IsKnownType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            if (_byAlias.ContainsKey(type)) return true;
            var lower = type.ToLowerInvariant();
            if (lower.EndsWith(SupportType.Array)) return true;
            if (lower.StartsWith(SupportType.List) && type.EndsWith(">")) return true;
            if ((lower.StartsWith(SupportType.Dictionary) || lower.StartsWith(SupportType.DictionaryFull)) && type.EndsWith(">")) return true;
            if (lower.StartsWith(SupportType.ValueTuple) && type.EndsWith(">")) return true;
            if (lower.StartsWith(SupportType.Enum)) return true;
            return false;
        }

        /// <summary>判断类型是否为容器类型（数组、List、Dictionary、ValueTuple）。</summary>
        public bool IsContainerType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            var lower = type.ToLowerInvariant();
            if (lower.EndsWith(SupportType.Array)) return true;
            foreach (var pattern in ContainerPatterns)
                if (lower.StartsWith(pattern) && type.EndsWith(">"))
                    return true;
            return false;
        }

        /// <summary>判断类型是否可用作主键/字典键。</summary>
        public bool IsKeyType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return KeyTypeNames.Contains(type);
        }

        /// <summary>获取类型的 System.Type 对象。</summary>
        public Type GetSystemType(string type)
        {
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));

            // 简单类型 — 从注册表中直接查找
            if (_byAlias.TryGetValue(type, out var entry))
            {
                if (entry is CustomObjectEntry)
                    return Type.GetType(entry.SystemTypeName);
                var hint = entry.AssemblyHint;
                var name = entry.SystemTypeName;
                if (name.StartsWith("UnityEngine.Vector2Int") || name.StartsWith("UnityEngine.Vector3Int"))
                    return Type.GetType($"{name},{hint}");
                if (name.StartsWith("UnityEngine."))
                    return Type.GetType($"{name},{hint}");
                if (name.StartsWith("System."))
                    return Type.GetType($"{name},{hint}");
                return Type.GetType(name);
            }

            // 容器类型/自定义对象 — 委托给 TypeDescriptor
            try { return Type.GetType(TypeDescriptor.Parse(type).ToSystemTypeName()); }
            catch { return null; }
        }

        /// <summary>获取类型的默认值。</summary>
        public object GetDefaultValue(string type)
        {
            if (_byAlias.TryGetValue(type, out var entry))
                return entry.DefaultValue;
            return null;
        }

        /// <summary>获取类型的二元序列化动作标识。</summary>
        public string GetWriteAction(string type)
        {
            if (_byAlias.TryGetValue(type, out var entry))
                return entry.WriteAction;
            return null;
        }

        /// <summary>获取 C# 类型名称（用于代码生成）。</summary>
        public string GetCsTypeName(string type)
        {
            if (_byAlias.TryGetValue(type, out var entry))
                return entry.CsTypeName;
            return type;
        }
    }
}