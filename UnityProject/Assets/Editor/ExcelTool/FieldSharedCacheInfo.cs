using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 字段共享缓存信息
    /// </summary>
    public class FieldSharedCacheInfo
    {
        public string FieldName;
        public string FieldType;
        public bool EnableSharedCache;
        // 该字段在所有行中出现的总次数（含重复）
        public int TotalCount;
        // value -> index 映射
        public Dictionary<string, int> ValueToIndex = new Dictionary<string, int>();
        public List<object> IndexToValue = new List<object>();
        public int UniqueCount => IndexToValue.Count;
        /// <summary>重复率 = (TotalCount - UniqueCount) / TotalCount</summary>
        public double DuplicationRate => TotalCount > 0 ? (double)(TotalCount - UniqueCount) / TotalCount : 0;
    }
}