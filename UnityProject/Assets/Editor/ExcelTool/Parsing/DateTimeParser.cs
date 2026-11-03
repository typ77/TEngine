using System;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 日期时间解析器 — D19 修复。
    /// 旧方案: 按字符串位数判断 (length >= 19 → 纳秒, >= 16 → 微秒, >= 13 → 毫秒, >= 10 → 秒)
    /// 新方案: 按数值范围 switch 判断, 避免前导零导致的误判。
    /// </summary>
    public class DateTimeParser : ITypeParser
    {
        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            data = BracketUtils.RemoveOuterBracketsAlways(data);

            if (string.IsNullOrEmpty(data))
                return DateTime.MinValue;

            if (long.TryParse(data, out long timestamp))
            {
                // D19: 位数判断 → 数值范围 switch
                // 纳秒: >= 10^18 (19位)
                // 微秒: >= 10^15 (16位)
                // 毫秒: >= 10^12 (13位)
                // 秒:   >= 10^9  (10位)
                var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

                if (timestamp >= 1_000_000_000_000_000_000L)
                {
                    // 纳秒级: 1 纳秒 = 100 纳秒刻 (ticks), 所以 timestamp / 100
                    return epoch.AddTicks(timestamp / 100).DateTime;
                }

                if (timestamp >= 1_000_000_000_000_000L)
                {
                    // 微秒级: 1 微秒 = 10 ticks
                    return epoch.AddTicks(timestamp * 10).DateTime;
                }

                if (timestamp >= 1_000_000_000_000L)
                {
                    // 毫秒级
                    return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
                }

                if (timestamp >= 1_000_000_000L)
                {
                    // 秒级
                    return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                }
            }

            if (DateTime.TryParse(data, out DateTime defaultResult))
            {
                return defaultResult;
            }

            return DateTime.MinValue;
        }
    }
}