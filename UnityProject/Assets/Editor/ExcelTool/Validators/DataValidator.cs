using System;
using System.Linq;
using UnityEngine;

namespace TEngine.Editor.ExcelTool.Validators
{
    /// <summary>
    /// 数据格式验证器 — 集中管理自定义对象格式验证
    /// </summary>
    public static class DataValidator
    {
        /// <summary>
        /// 验证 RewardObject 格式 - 每个奖励条目必须有 3 个元素
        /// </summary>
        public static bool ValidateRewardObjectFormat(string data, string className, string id, string tagName)
        {
            try
            {
                if (data.StartsWith("[") && data.EndsWith("]"))
                {
                    string innerContent = data.Substring(1, data.Length - 2);
                    if (innerContent.Contains("[") || innerContent.Contains("]"))
                    {
                        data = innerContent;
                    }
                }

                // 如果去掉外层后就是空，视为空数据
                if (string.IsNullOrWhiteSpace(data))
                {
                    return false;
                }

                var elements = ReadExcel.ParseElements(data).ToArray();

                // 如果只有一个元素且内容还是 []，说明是空列表 [[]]，视为空数据
                if (elements.Length == 1 && elements[0].Trim() == "[]")
                {
                    return false;
                }

                // 如果是空数据 ([] 或 [[]])，不加入缓存但不算格式错误
                if (elements.Length == 0 || (elements.Length == 1 && string.IsNullOrWhiteSpace(elements[0])))
                {
                    return false;
                }

                foreach (var elem in elements)
                {
                    if (string.IsNullOrWhiteSpace(elem))
                    {
                        continue;
                    }

                    string innerData = ReadExcel.RemoveOuterBracketsIfPaired(elem);
                    var innerElements = ReadExcel.ParseElements(innerData).ToArray();

                    // 空条目不算格式错误，直接跳过
                    if (innerElements.Length == 0 || (innerElements.Length == 1 && string.IsNullOrWhiteSpace(innerElements[0])))
                    {
                        continue;
                    }

                    if (innerElements.Length != 3)
                    {
                        Debug.LogError($"[RewardObject] 格式错误 - 表: {className}, id:{id} 项:{tagName} 数据: [{elem}], 期望每个奖励 3 个元素, 实际得到 {innerElements.Length} 个 - 已跳过不加入共享缓存");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"【Exception】[RewardObject] 格式验证异常 - 表: {className}, id:{id} 项:{tagName} 数据: {data}, 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证 ConditionObject 格式 - 每个条件必须至少 2 个元素
        /// </summary>
        public static bool ValidateConditionObjectFormat(string data, string className, string id, string tagName)
        {
            try
            {
                if (data.StartsWith("[") && data.EndsWith("]"))
                {
                    string innerContent = data.Substring(1, data.Length - 2);
                    if (innerContent.Contains("[") || innerContent.Contains("]"))
                    {
                        data = innerContent;
                    }
                }

                // 如果去掉外层后就是空，视为空数据
                if (string.IsNullOrWhiteSpace(data))
                {
                    return false;
                }

                var elements = ReadExcel.ParseElements(data).ToArray();

                // 如果只有一个元素且内容还是 []，说明是空列表 [[]]，视为空数据
                if (elements.Length == 1 && elements[0].Trim() == "[]")
                {
                    return false;
                }

                // 如果是空数据 ([] 或 [[]])，不加入缓存但不算格式错误
                if (elements.Length == 0 || (elements.Length == 1 && string.IsNullOrWhiteSpace(elements[0])))
                {
                    return false;
                }

                foreach (var elem in elements)
                {
                    if (string.IsNullOrWhiteSpace(elem))
                    {
                        continue;
                    }

                    string innerData = ReadExcel.RemoveOuterBracketsIfPaired(elem);
                    var innerElements = ReadExcel.ParseElements(innerData).ToArray();

                    // 空条目不算格式错误，直接跳过
                    if (innerElements.Length == 0 || (innerElements.Length == 1 && string.IsNullOrWhiteSpace(innerElements[0])))
                    {
                        continue;
                    }

                    // 条件至少需要 1 个元素(条件类型)，参数可选，例如:
                    //   ["clan_had_join"]                          纯状态判定，无参数
                    //   ["demon_role_max_evo_level", 16]           条件类型 + 单参数
                    //   ["mall_daily_buy_item_count", 100009, 1]   条件类型 + 多参数
                    // 与运行时 ConditionItem.Of 的 list.Count >= 1 约束保持一致
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"【Exception】[ConditionObject] 格式验证异常 - 表: {className}, id:{id} 项:{tagName} 数据: {data}, 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证 EffectCfgObject 格式 - 每个 EffectCfgItem 至少 1 个元素(EffectType)，其余参数可选
        /// 运行时 EffectCfgItem.Of 用 ElementAtOrDefault(0..3) 读取，对元素数量不敏感，缺失字段解析为空列表。
        /// 实际数据格式是变长的:
        ///   固定属性效果:  [EffectType, ParamList]
        ///     例: ["attr",[[10002,960]]]
        ///   特殊效果:     [EffectType, ParamList, Param2List, Param3List]
        ///     例: ["<name>",[],[2,767],[[1,320]]]
        /// </summary>
        public static bool ValidateEffectCfgObjectFormat(string data, string className, string id, string tagName)
        {
            try
            {
                if (data.StartsWith("[") && data.EndsWith("]"))
                {
                    string innerContent = data.Substring(1, data.Length - 2);
                    if (innerContent.Contains("[") || innerContent.Contains("]"))
                    {
                        data = innerContent;
                    }
                }

                // 如果去掉外层后就是空，视为空数据
                if (string.IsNullOrWhiteSpace(data))
                {
                    return false;
                }

                var elements = ReadExcel.ParseElements(data).ToArray();

                // 如果只有一个元素且内容还是 []，说明是空列表 [[]]，视为空数据
                if (elements.Length == 1 && elements[0].Trim() == "[]")
                {
                    return false;
                }

                // 如果是空数据 ([] 或 [])，不加入缓存但不算格式错误
                if (elements.Length == 0 || (elements.Length == 1 && string.IsNullOrWhiteSpace(elements[0])))
                {
                    return false;
                }

                // 过滤掉空元素后再检查
                var nonEmptyElements = elements.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();

                // 空数组 [] 视为默认，不算格式错误
                if (nonEmptyElements.Count == 0)
                {
                    return false;
                }

                // 与运行时 EffectCfgItem.Of 保持一致: 至少需要 EffectType(索引0)，其余字段可选。
                // 空条目已在前面过滤，到这里至少有 1 个非空元素，无需再卡数量。
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"【Exception】[EffectCfgObject] 格式验证异常 - 表: {className}, id:{id} 项:{tagName} 数据: {data}, 错误: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 验证 GradedPriceObject 格式 - 每个价格条目必须有 4 个元素
        /// </summary>
        public static bool ValidateGradedPriceObjectFormat(string data, string className, string id, string tagName)
        {
            try
            {
                if (data.StartsWith("[") && data.EndsWith("]"))
                {
                    string innerContent = data.Substring(1, data.Length - 2);
                    if (innerContent.Contains("[") || innerContent.Contains("]"))
                    {
                        data = innerContent;
                    }
                }

                // 如果去掉外层后就是空，视为空数据
                if (string.IsNullOrWhiteSpace(data))
                {
                    return false;
                }

                var elements = ReadExcel.ParseElements(data).ToArray();

                // 如果只有一个元素且内容还是 []，说明是空列表 [[]]，视为空数据
                if (elements.Length == 1 && elements[0].Trim() == "[]")
                {
                    return false;
                }

                // 如果是空数据 ([] 或 [[]])，不加入缓存但不算格式错误
                if (elements.Length == 0 || (elements.Length == 1 && string.IsNullOrWhiteSpace(elements[0])))
                {
                    return false;
                }

                foreach (var elem in elements)
                {
                    if (string.IsNullOrWhiteSpace(elem))
                    {
                        continue;
                    }

                    string innerData = ReadExcel.RemoveOuterBracketsIfPaired(elem);
                    var innerElements = ReadExcel.ParseElements(innerData).ToArray();

                    // 空条目不算格式错误，直接跳过
                    if (innerElements.Length == 0 || (innerElements.Length == 1 && string.IsNullOrWhiteSpace(innerElements[0])))
                    {
                        continue;
                    }

                    if (innerElements.Length != 4)
                    {
                        Debug.LogError($"[GradedPriceObject] 格式错误 - 表: {className}, id:{id} 项:{tagName} 数据: [{elem}], 期望每个价格条目 4 个元素, 实际得到 {innerElements.Length} 个 - 已跳过不加入共享缓存");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"【Exception】[GradedPriceObject] 格式验证异常 - 表: {className}, id:{id} 项:{tagName} 数据: {data}, 错误: {ex.Message}");
                return false;
            }
        }
    }
}