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
        /// <summary>
        /// v2「槽0空值」布局开关（由 ConfigBinaryWriter.PrepareEmptySlots 在写行数据前预扫描决定）。
        /// true ：表头缓存段第 0 槽为该类型空值、条目数 = UniqueCount + 1；行内空引用写 0、有效值写 index + 1。
        /// false：沿用 v1 布局（空引用 -1、有效值 index）。读端生成代码对两种布局均兼容（idx&lt;0 走静态空实例，其余按下标取）。
        /// </summary>
        public bool ReserveEmptySlot;
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