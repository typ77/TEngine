using System.Collections.Generic;
using System.Linq;

namespace TEngine
{
    public class EffectCfgItem
    {
        /// <summary>
        /// 效果名称/类型
        /// </summary>
        public string EffectType { get; set; }

        /// <summary>
        /// 参数列表1 - 二维整数列表
        /// 格式: [[attr_id, param, value], ...] 或 []
        /// </summary>
        public List<List<int>> ParamList { get; private set; } = new List<List<int>>();
        
        /// <summary>
        /// 参数列表2 - 一维整数列表
        /// 格式: [param1, param2, ...]
        /// </summary>
        public List<int> Param2List { get; private set; } = new List<int>();

        /// <summary>
        /// 参数列表3 - 嵌套二维整数列表
        /// 格式: [[attr_id, value], ...]
        /// </summary>
        public List<List<int>> Param3List { get; private set; } = new List<List<int>>();
        
        /// <summary>
        /// 原始数据
        /// </summary>
        public List<string> RawData { get; private set; }

        /// <summary>
        /// 私有构造函数
        /// </summary>
        private EffectCfgItem() { }
        
        public EffectCfgItem(string effectType, List<List<int>> paramList)
        {
            EffectType = effectType;
            ParamList = paramList ?? new List<List<int>>();
        }

        public static EffectCfgItem Of(List<string> list)
        {
            var item = new EffectCfgItem();
            item.RawData = list;

            if (list == null || list.Count == 0)
                return item;

            // 索引0: 效果类型/名称
            // 如果是 "[]" 空列表标记，则设置为空字符串
            string effectTypeStr = list.ElementAtOrDefault(0);
            item.EffectType = string.Equals(effectTypeStr, "[]", System.StringComparison.OrdinalIgnoreCase) ? string.Empty : effectTypeStr.Trim('"');
            
            // 索引1: ParamList - 解析二维整数列表字符串
            string paramListStr = list.ElementAtOrDefault(1);
            item.ParamList = ParseNestedIntList(paramListStr);

            // 索引2: Param2List - 解析一维整数列表字符串
            string param2Str = list.ElementAtOrDefault(2);
            item.Param2List = ParseIntList(param2Str);

            // 索引3: Param3List - 解析嵌套二维整数列表字符串
            string param3Str = list.ElementAtOrDefault(3);
            item.Param3List = ParseNestedIntList(param3Str);

            return item;
        }
        
        /// <summary>
        /// 解析嵌套整数列表字符串
        /// 输入: "[[1,2,3],[4,5,6]]" 或 "[]" 或 "[[1,10002,10],[2,10002,10]]"
        /// 输出: List<List<int>>
        /// </summary>
        private static List<List<int>> ParseNestedIntList(string str)
        {
            var result = new List<List<int>>();

            if (string.IsNullOrWhiteSpace(str))
                return result;

            // 去除首尾空白
            string trimmedStr = str.Trim();

            // 检查是否为空数组
            if (trimmedStr == "[]")
                return result;

            // 检查格式是否正确（必须以 [[ 开头，以 ]] 结尾）
            if (!trimmedStr.StartsWith("[[") || !trimmedStr.EndsWith("]]"))
                return result;

            // 只移除最外层的 [ 和 ]，保留内层的方括号结构
            // "[[1008,900]]" -> "[1008,900]"
            // "[[1,2,3],[4,5,6]]" -> "[1,2,3],[4,5,6]"
            string innerContent = trimmedStr.Substring(1, trimmedStr.Length - 2);

            // 如果内层为空，直接返回空列表
            if (string.IsNullOrWhiteSpace(innerContent))
                return result;

            // 按顶层逗号分割（处理嵌套括号）
            var innerArrayStrs = SplitByTopLevelComma(innerContent);

            foreach (var innerArrayStr in innerArrayStrs)
            {
                // 直接解析每个内层数组字符串（已经有 [] 包裹）
                List<int> intList = ParseIntList(innerArrayStr.Trim());
                if (intList.Any())
                {
                    result.Add(intList);
                }
            }

            return result;
        } 

        /// <summary>
        /// 解析整数列表字符串
        /// 输入: "[1,2,3]" 或 "[]"
        /// 输出: List<int>
        /// </summary>
        private static List<int> ParseIntList(string str)
        {
            var result = new List<int>();

            if (string.IsNullOrWhiteSpace(str))
                return result;

            // 去除首尾空白
            string trimmedStr = str.Trim();

            // 检查是否为空数组
            if (trimmedStr == "[]")
                return result;

            // 检查格式是否正确（必须以 [ 开头，以 ] 结尾）
            if (!trimmedStr.StartsWith("[") || !trimmedStr.EndsWith("]"))
                return result;

            // 移除外层的 [ 和 ]
            string innerContent = trimmedStr.Substring(1, trimmedStr.Length - 2);

            // 如果内层为空，直接返回空列表
            if (string.IsNullOrWhiteSpace(innerContent))
                return result;

            // 分割并转换为整数
            var parts = innerContent.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var part in parts)
            {
                if (int.TryParse(part, out int value))
                {
                    result.Add(value);
                }
            }

            return result;
        }
        
        /// <summary>
        /// 按顶层逗号分割字符串（处理嵌套括号）
        /// 例如: "[1,2,3],[4,5,6]" -> ["[1,2,3]", "[4,5,6]"]
        /// </summary>
        private static List<string> SplitByTopLevelComma(string str)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(str))
                return result;

            int bracketDepth = 0;
            int startIndex = 0;

            for (int i = 0; i < str.Length; i++)
            {
                switch (str[i])
                {
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        bracketDepth--;
                        break;
                    case ',':
                        if (bracketDepth == 0)
                        {
                            result.Add(str.Substring(startIndex, i - startIndex).Trim());
                            startIndex = i + 1;
                        }
                        break;
                }
            }

            // 添加最后一个元素
            if (startIndex < str.Length)
            {
                result.Add(str.Substring(startIndex).Trim());
            }

            return result;
        }

        public int GetIntParam(int groupIndex, int paramIndex)
        {
            if (groupIndex < 0 || groupIndex >= ParamList.Count)
            {
                return 0;
            }

            var group = ParamList[groupIndex];
            if (paramIndex < 0 || paramIndex >= group.Count)
            {
                return 0;
            }

            return group[paramIndex];
        }

        public int GetFirstParam(int paramIndex)
        {
            return GetIntParam(0, paramIndex);
        }
        
        /// <summary>
        /// 深拷贝
        /// </summary>
        public EffectCfgItem Clone()
        {
            var item = new EffectCfgItem { EffectType = this.EffectType };
            // 替代 Select(l => l.ToList()).ToList()/ToList() 拷贝链（效果配置合并/结算链），显式双层 for 深拷贝消迭代器装箱与中间集合分配
            var srcParamList = this.ParamList;
            var dstParamList = item.ParamList;
            for (int i = 0; i < srcParamList.Count; i++)
            {
                dstParamList.Add(CopyIntList(srcParamList[i]));
            }

            var srcParam2List = this.Param2List;
            var dstParam2List = item.Param2List;
            for (int i = 0; i < srcParam2List.Count; i++)
            {
                dstParam2List.Add(srcParam2List[i]);
            }

            var srcParam3List = this.Param3List;
            var dstParam3List = item.Param3List;
            for (int i = 0; i < srcParam3List.Count; i++)
            {
                dstParam3List.Add(CopyIntList(srcParam3List[i]));
            }

            if (this.RawData != null)
            {
                // RawData 属性无初始化器，克隆体默认 null，必须先建目标列表再填充（否则 Add 必然 NRE）
                var dstRawData = new List<string>(this.RawData.Count);
                for (int i = 0; i < this.RawData.Count; i++)
                {
                    dstRawData.Add(this.RawData[i]);
                }
                item.RawData = dstRawData;
            }

            return item;
        }

        /// <summary>
        /// 拷贝整数列表（Clone/Merge 共用，替代 ToList()/sourceItem.ToList() 的枚举器装箱）
        /// </summary>
        private static List<int> CopyIntList(List<int> source)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new List<int>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                copy.Add(source[i]);
            }
            return copy;
        }
        
        /// <summary>
        /// 合并另一个效果配置项
        /// </summary>
        public void Merge(EffectCfgItem other)
        {
            if (other == null || this.EffectType != other.EffectType)
                return;

            MergeNestedList(this.ParamList, other.ParamList);
            MergeSimpleList(this.Param2List, other.Param2List);
            MergeNestedList(this.Param3List, other.Param3List);
        }
        
        private void MergeNestedList(List<List<int>> target, List<List<int>> source)
        {
            // 替代 Where/FirstOrDefault LINQ 链与 sourceItem.ToList()（效果合并链），显式 for + 索引比对零分配
            for (int i = 0; i < source.Count; i++)
            {
                var sourceItem = source[i];
                if (sourceItem == null || sourceItem.Count == 0)
                {
                    continue;
                }

                int id = sourceItem[0];
                int value = sourceItem.Count > 1 ? sourceItem[1] : 0;

                int foundIndex = -1;
                for (int j = 0; j < target.Count; j++)
                {
                    var targetItem = target[j];
                    if (targetItem != null && targetItem.Count > 0 && targetItem[0] == id)
                    {
                        foundIndex = j;
                        break;
                    }
                }

                if (foundIndex >= 0 && target[foundIndex].Count > 1)
                {
                    target[foundIndex][1] += value;
                }
                else
                {
                    target.Add(CopyIntList(sourceItem));
                }
            }
        }
        
        private void MergeSimpleList(List<int> target, List<int> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (i < target.Count)
                    target[i] += source[i];
                else
                    target.Add(source[i]);
            }
        }
    }

    public class EffectCfgObject
    {
        private readonly List<EffectCfgItem> _itemList = new List<EffectCfgItem>();

        public List<EffectCfgItem> ItemList => _itemList;

        public EffectCfgItem GetItemByType(string effectType)
        {
            // 替代 FirstOrDefault(pred)（效果查询/合并链，消闭包与委托分配），显式 for + string 比较
            for (int i = 0; i < _itemList.Count; i++)
            {
                var item = _itemList[i];
                if (item != null && item.EffectType == effectType)
                {
                    return item;
                }
            }
            return null;
        }
        
        public void AddItem(EffectCfgItem item)
        {
            if (item != null && !_itemList.Contains(item))
            {
                _itemList.Add(item);
            }
        }
        
        public EffectCfgObject(Luban.ByteBuf byteBuf)
        {
            var listCount = byteBuf.ReadSize();
            for (var index = 0; index < listCount; index++)
            {
                List<string> rawTextList = new List<string>();
                var size = byteBuf.ReadSize();
                for (int j = 0; j < size; j++) 
                {
                    rawTextList.Add(byteBuf.ReadString());
                }

                EffectCfgItem item = EffectCfgItem.Of(rawTextList);
                if (item != null)
                {
                    _itemList.Add(item);
                }
            }
        }

        public static bool IsNullOrEmpty(EffectCfgObject effectCfgObject)
        {
            return (effectCfgObject?.ItemList?.Count ?? 0) == 0 || string.IsNullOrWhiteSpace(effectCfgObject.ItemList[0].EffectType);
        }

        public EffectCfgObject()
        {
        }
        
        public void MergeEffect(EffectCfgItem newItem)
        {
            if (newItem == null) return;

            var existingItem = GetItemByType(newItem.EffectType);
            if (existingItem == null)
            {
                _itemList.Add(newItem.Clone());
            }
            else
            {
                existingItem.Merge(newItem);
            }
        }
    }
}