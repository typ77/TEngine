using System.Collections.Generic;
using System.Linq;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 自定义对象解析器 — 统一处理 RewardObject、ConditionObject、EffectCfgObject、GradedPriceObject。
    /// 使用 BracketUtils.RemoveOuterBracketsSmart 智能处理括号。
    /// </summary>
    public class ObjectParser : ITypeParser
    {
        public ObjectParser(ParserDispatcher dispatcher)
        {
            // dispatcher 保留以备将来可能的嵌套解析需求
        }

        public object Parse(string type, string data, ParseContext ctx, int depth)
        {
            // 智能移除外层括号：仅当内层包含括号时才剥离
            data = BracketUtils.RemoveOuterBracketsSmart(data);

            var elements = BracketUtils.ParseElements(data).ToArray();
            var lower = type.ToLowerInvariant();

            if (lower == SupportType.ConditionObject.ToLowerInvariant())
            {
                // ConditionObject: List<List<string>>
                var condList = new List<List<string>>();
                foreach (var elem in elements)
                {
                    var innerData = BracketUtils.RemoveOuterBracketsAlways(elem);
                    var innerElements = BracketUtils.ParseElements(innerData).ToArray();
                    condList.Add(innerElements.ToList());
                }
                return condList;
            }

            if (lower == SupportType.EffectCfgObject.ToLowerInvariant())
            {
                // EffectCfgObject: 单个 EffectCfgItem
                var effectList = new List<List<string>>();
                var innerList = new List<string>();
                foreach (var elem in elements)
                {
                    innerList.Add(elem);
                }
                effectList.Add(innerList);
                return effectList;
            }

            if (lower == SupportType.GradedPriceObject.ToLowerInvariant())
            {
                // GradedPriceObject: List<List<long>>
                var rewardList = new List<List<long>>();
                foreach (var elem in elements)
                {
                    var innerData = BracketUtils.RemoveOuterBracketsAlways(elem);
                    var innerElements = BracketUtils.ParseElements(innerData).ToArray();
                    var innerList = new List<long>();
                    foreach (var innerElem in innerElements)
                    {
                        if (long.TryParse(innerElem, out var value))
                        {
                            innerList.Add(value);
                        }
                        else
                        {
                            ctx.Result?.Error($"Invalid value '{innerElem}' for type 'long'",
                                ctx.FilePath, ctx.SheetName, ctx.Row, ctx.Column);
                            innerList.Add(0);
                        }
                    }
                    rewardList.Add(innerList);
                }
                return rewardList;
            }

            if (lower == SupportType.RewardObject.ToLowerInvariant())
            {
                // RewardObject: List<List<long>>
                var rewardList = new List<List<long>>();
                foreach (var elem in elements)
                {
                    var innerData = BracketUtils.RemoveOuterBracketsAlways(elem);
                    var innerElements = BracketUtils.ParseElements(innerData).ToArray();
                    var innerList = new List<long>();
                    foreach (var innerElem in innerElements)
                    {
                        if (long.TryParse(innerElem, out var value))
                        {
                            innerList.Add(value);
                        }
                        else
                        {
                            ctx.Result?.Error($"Invalid value '{innerElem}' for type 'long'",
                                ctx.FilePath, ctx.SheetName, ctx.Row, ctx.Column);
                            innerList.Add(0);
                        }
                    }
                    rewardList.Add(innerList);
                }
                return rewardList;
            }

            throw new TypeNotSupportedException(
                $"不支持的自定义对象类型: '{type}'",
                ctx.ClassName, ctx.FilePath, ctx.Row, ctx.Column);
        }
    }
}