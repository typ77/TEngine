using System.Collections.Generic;

namespace TEngine
{
    public class ConditionItem
    {
        private List<string> _textList;

        public string ConditionType { get; private set; }
        public List<string> ParamList { get; private set; }

        public ConditionItem(string conditionType, List<string> paramList)
        {
            ConditionType = conditionType;
            ParamList = paramList;
        }

        public static ConditionItem Of(List<string> list)
        {
            if (list.Count < 1)
            {
                return null;
            }

            string conditionType = list[0].Trim().Replace("\"", "");

            List<string> paramList = new();
            for (int i = 1; i < list.Count; i++)
            {
                // 与 ConditionType 一致：去掉导表残留引号，否则副本 GetItem("\"3011\"") 会取不到配置
                paramList.Add(list[i].Trim().Replace("\"", ""));
            }

            return new ConditionItem(conditionType, paramList);
        }

        public string GetStringParam(int index)
        {
            if (index < 0 || index >= ParamList.Count)
            {
                return null;
            }

            return ParamList[index];
        }

        /// <summary>
        /// 最后一个参数
        /// </summary>
        /// <returns></returns>
        public int GetIntLastParam()
        {
            return GetIntParam(ParamList.Count - 1);
        }
        
        public int GetIntParam(int index)
        {
            if (index < 0 || index >= ParamList.Count)
            {
                return 0;
            }

            return int.Parse(ParamList[index].Trim());
        }

        /// <summary>
        /// 获取整数列表参数，"[10000]"
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public List<int> GetIntListParam(int index)
        {
            if (index < 0 || index >= ParamList.Count)
            {
                return new List<int>();
            }
            string str = ParamList[index].Trim();
            if (!str.StartsWith("[") || !str.EndsWith("]"))
            {
                return new List<int>();
            }
            str = str.Substring(1, str.Length - 2);
            string[] strList = str.Split(',');
            List<int> intList = new();
            foreach (string item in strList)
            {
                intList.Add(int.Parse(item.Trim()));
            }
            return intList;
        }

        public long GetLongParam(int index)
        {
            if (index < 0 || index >= ParamList.Count)
            {
                return 0;
            }

            return long.Parse(ParamList[index].Trim());
        }

        // 源工程此处还有 GetConditionConfigItem/GetDesc_1 便捷方法，依赖业务表 ConditionConfig（生成类）。
        // 本项目未移植该业务表，条件文案格式化应由业务层封装，不在通用复合对象内耦合具体表。
    }

    public class ConditionObject
    {
        private readonly List<ConditionItem> _itemList = new List<ConditionItem>();

        public List<ConditionItem> ItemList => _itemList;

        public ConditionObject(Luban.ByteBuf byteBuf)
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

                ConditionItem item = ConditionItem.Of(rawTextList);
                if (item != null)
                {
                    _itemList.Add(item);
                }
            }
        }

        public static bool IsNullOrEmpty(ConditionObject conditionObject)
        {
            return (conditionObject?.ItemList?.Count ?? 0) == 0; ;
        }

        // 添加无参构造函数
        public ConditionObject()
        {
            // 无需额外逻辑，_itemList 已通过字段初始化器初始化
        }

        /// <summary>
        /// 从字符串列表生成ConditionObject
        /// </summary>
        /// <param name="conditionLists">外层列表为条件项集合，内层列表为单个条件项参数</param>
        /// <returns>生成的ConditionObject实例</returns>
        public static ConditionObject CreateFromList(List<List<string>> conditionLists)
        {
            var conditionObject = new ConditionObject();
            if (conditionLists == null) return conditionObject;

            foreach (var paramList in conditionLists)
            {
                var item = ConditionItem.Of(paramList);
                if (item != null)
                {
                    conditionObject._itemList.Add(item);
                }
            }
            return conditionObject;
        }
    }
}