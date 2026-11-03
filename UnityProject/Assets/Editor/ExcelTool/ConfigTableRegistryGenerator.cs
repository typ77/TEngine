using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// ConfigTableRegistry.cs 生成器：表名（bytes 文件名）→ 表类工厂，
    /// 供 ConfigSystem.InitConfig 免反射建表（config-system-reflection-analysis P3）。
    /// 合并式生成：与既有注册表条目取并集后排序输出，单表导出不会丢掉其他表的注册。
    /// </summary>
    internal static class ConfigTableRegistryGenerator
    {
        /// <summary>
        /// 生成文件名。文件名不以 Config/Item 结尾，天然规避
        /// DeleteManagedGeneratedCodeFiles 的托管生成文件清理（只删 *Config.cs/*Item.cs/*Uncompressed.cs）。
        /// </summary>
        public const string FileName = "ConfigTableRegistry.cs";

        // 匹配既有条目行：{ "XxxConfig", static () => new XxxConfig() },
        private static readonly Regex EntryRegex = new Regex(
            @"^\s*\{\s*""(?<name>[A-Za-z_][A-Za-z0-9_]*)""\s*,",
            RegexOptions.Compiled);

        public static string GetRegistryPath(string scriptFolder)
            => Path.Combine(scriptFolder, FileName);

        /// <summary>
        /// 构建注册表源码。selectedClassNames 为本次导出的 sheet 类名（不带 Config 后缀），
        /// 与既有文件中的条目合并去重，按名称排序输出。
        /// </summary>
        public static string Build(string scriptFolder, IEnumerable<string> selectedClassNames)
        {
            var names = new HashSet<string>();

            // 1. 既有条目：单表导出/部分导出时保住其余表的注册。
            //    仅当该表的类文件仍存在才保留——删表后 isDelete 全量重导时类文件已被清理，
            //    残留条目会指向不存在的类导致编译错（isDelete=false 时孤儿类文件尚在，条目随文件保留）。
            var existingPath = GetRegistryPath(scriptFolder);
            if (File.Exists(existingPath))
            {
                foreach (var line in File.ReadAllLines(existingPath))
                {
                    var match = EntryRegex.Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }
                    var name = match.Groups["name"].Value;
                    var stem = name.EndsWith("Config", StringComparison.Ordinal)
                        ? name.Substring(0, name.Length - "Config".Length)
                        : name;
                    if (File.Exists(Path.Combine(scriptFolder, stem + "Item.cs")))
                    {
                        names.Add(name);
                    }
                }
            }

            // 2. 本次导出的表：sheet 类名 + Config 后缀 = 表类名 = bytes 文件名
            foreach (var className in selectedClassNames)
            {
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }
                names.Add(className + "Config");
            }

            var sb = new StringBuilder(4096 + names.Count * 96);
            sb.Append("// ").AppendLine(ScriptGenerator.GeneratedCodeMarker);
            sb.AppendLine("/*配置表工厂注册表：表名(bytes文件名) -> 表实例。由导表工具自动生成，勿手改。*/");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace TEngine");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 配置表工厂注册表：替代 InitConfig 的反射建表（Assembly.GetType + Activator.CreateInstance）。");
            sb.AppendLine("    /// 与表类文件由导表工具同步维护；未命中时 InitConfig 回落反射并告警。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class ConfigTableRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        private static readonly Dictionary<string, Func<ConfigTableBase>> s_factories = new Dictionary<string, Func<ConfigTableBase>>");
            sb.AppendLine("        {");
            foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
            {
                sb.AppendLine("            { \"" + name + "\", static () => new " + name + "() },");
            }
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 按表名（bytes 文件名，如 AchivementConfig）创建表实例。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public static bool TryCreate(string name, out ConfigTableBase table)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (s_factories.TryGetValue(name, out var factory))");
            sb.AppendLine("            {");
            sb.AppendLine("                table = factory();");
            sb.AppendLine("                return table != null;");
            sb.AppendLine("            }");
            sb.AppendLine("            table = null;");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
