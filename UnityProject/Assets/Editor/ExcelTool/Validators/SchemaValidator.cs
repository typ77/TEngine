using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    public class SchemaValidator
    {
        private static readonly HashSet<string> CSharpKeywords = new()
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum",
            "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
            "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
            "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
            "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        public void EnsureUniqueFieldName(HashSet<string> fieldNames, string fieldName,
                                            string excelFilePath, string sheetName, int column)
        {
            if (!fieldNames.Add(fieldName))
                UnityEngine.Debug.LogWarning(
                    $"Duplicate field name '{fieldName}'. File: {excelFilePath}, Sheet: {sheetName}, Column: {column}");
        }

        public void ValidateCSharpIdentifier(string identifier, string kind,
                                              string excelFilePath, string sheetName,
                                              int row = -1, int column = -1)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                UnityEngine.Debug.LogWarning(
                    $"Invalid empty {kind}. File: {excelFilePath}, Sheet: {sheetName}, Row: {row}, Column: {column}");
                return;
            }
            if (CSharpKeywords.Contains(identifier))
            {
                UnityEngine.Debug.LogWarning(
                    $"C# keyword '{identifier}' cannot be used as {kind}. File: {excelFilePath}, Sheet: {sheetName}, Row: {row}, Column: {column}");
                return;
            }
            if (!IsValidCSharpIdentifier(identifier))
            {
                UnityEngine.Debug.LogWarning(
                    $"Invalid C# identifier '{identifier}' for {kind}. File: {excelFilePath}, Sheet: {sheetName}, Row: {row}, Column: {column}");
            }
        }

        public static bool IsValidCSharpIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
            for (var i = 1; i < value.Length; i++)
                if (!(char.IsLetterOrDigit(value[i]) || value[i] == '_')) return false;
            return true;
        }

        public static bool IsKnownType(string type)
        {
            return TypeRegistry.Instance.IsKnownType(type);
        }

        public static bool IsKeyType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            try
            {
                var td = TypeDescriptor.Parse(type);
                if (td.Kind != TypeKind.Primitive) return false;
                return td.PrimitiveName switch
                {
                    "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "str" or "string" => true,
                    _ => false
                };
            }
            catch { return false; }
        }
    }
}
