using System.Collections.Generic;
using System.Text;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 括号处理工具 — 统一全模块的括号剥离和字符串解析逻辑。
    /// </summary>
    public static class BracketUtils
    {
        /// <summary>
        /// 智能移除最外层括号：仅当内层包含括号时才剥离。
        /// </summary>
        public static string RemoveOuterBracketsSmart(string data)
        {
            if (string.IsNullOrEmpty(data)) return data;
            if (!((data.StartsWith("[") && data.EndsWith("]")) ||
                  (data.StartsWith("{") && data.EndsWith("}"))))
                return data;
            string inner = data.Substring(1, data.Length - 2);
            if (inner.Contains('[') || inner.Contains(']') ||
                inner.Contains('{') || inner.Contains('}'))
                return inner;
            return data;
        }

        /// <summary>
        /// 移除最外层括号（若括号配对且可安全删除）。
        /// 原 ReadExcel.RemoveOuterBracketsIfPaired 的完整迁移。
        /// </summary>
        public static string RemoveOuterBracketsAlways(string data)
        {
            if (string.IsNullOrEmpty(data))
                return data;

            if ((data.StartsWith("[") && data.EndsWith("]")) || (data.StartsWith("{") && data.EndsWith("}")))
            {
                Stack<char> stack = new Stack<char>();
                char openingBracket = data[0];
                char closingBracket = openingBracket == '[' ? ']' : '}';

                // 先假设最外层括号可删除
                bool canRemoveOuter = true;

                // 从第二个字符开始遍历，到倒数第二个字符结束
                for (int i = 1; i < data.Length - 1; i++)
                {
                    char c = data[i];
                    if (c == openingBracket)
                    {
                        stack.Push(c);
                    }
                    else if (c == closingBracket)
                    {
                        if (stack.Count == 0)
                        {
                            // 遇到右括号但栈为空，说明括号不匹配，不能删除最外层括号
                            canRemoveOuter = false;
                            break;
                        }
                        stack.Pop();
                    }
                }

                // 遍历结束后，若栈不为空，也不能删除最外层括号
                if (stack.Count > 0)
                {
                    canRemoveOuter = false;
                }

                if (canRemoveOuter)
                {
                    return data.Substring(1, data.Length - 2);
                }
            }

            return data;
        }

        /// <summary>
        /// 解析逗号分隔的元素列表，正确处理引号和括号嵌套。
        /// 原 ReadExcel.ParseElements 的完整迁移。
        /// </summary>
        public static List<string> ParseElements(string data)
        {
            List<string> elements = new List<string>();
            var currentElement = new StringBuilder();
            int bracketDepth = 0;
            bool inQuotes = false;

            foreach (char c in data)
            {
                if (c == '"')
                {
                    // 遇到引号，切换引号状态
                    inQuotes = !inQuotes;
                    currentElement.Append(c);
                }
                else if (c == '[')
                {
                    // 遇到左括号，增加括号深度
                    bracketDepth++;
                    currentElement.Append(c);
                }
                else if (c == ']')
                {
                    // 遇到右括号，减少括号深度
                    bracketDepth--;
                    currentElement.Append(c);
                }
                else if (c == ',' && !inQuotes && bracketDepth == 0)
                {
                    // 如果不在引号内且括号深度为 0，遇到逗号则分割元素
                    elements.Add(currentElement.ToString());
                    currentElement.Clear();
                }
                else
                {
                    // 其他字符直接添加到当前元素
                    currentElement.Append(c);
                }
            }

            // 添加最后一个元素
            if (currentElement.Length > 0)
            {
                elements.Add(currentElement.ToString());
            }

            return elements;
        }

        /// <summary>
        /// 按顶层冒号分割键值对，正确处理引号和括号嵌套。
        /// 原 ReadExcel.SplitKeyValue 的完整迁移。
        /// </summary>
        public static string[] SplitKeyValue(string element)
        {
            int bracketDepth = 0;
            bool inQuotes = false;

            for (int i = 0; i < element.Length; i++)
            {
                char c = element[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes; // 处理引号内的冒号不分割
                }
                else if (c == '[' || c == '{')
                {
                    bracketDepth++; // 处理括号内的冒号不分割
                }
                else if (c == ']' || c == '}')
                {
                    bracketDepth--; // 当前在括号外
                }
                else if (c == ':' && !inQuotes && bracketDepth == 0)
                {
                    // 在引号外且括号层级为0时，分割第一个冒号
                    string key = element.Substring(0, i).Trim();
                    string value = element.Substring(i + 1).Trim();
                    return new[] { key, value }; // 返回分割后的键和值
                }
            }
            return new[] { element, "" }; // 如果没有找到有效冒号，返回原始值和空值
        }

        /// <summary>
        /// 检查值是否为忽略标记。标记 "@ignore" 或 "##" 前缀表示忽略。
        /// </summary>
        public static bool CheckIgnore(string value)
        {
            return value.StartsWith("@ignore") || value.StartsWith("##");
        }
    }
}