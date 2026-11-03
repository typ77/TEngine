using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    /// <summary>
    /// 组合辅助索引成员（单个字段）
    /// </summary>
    public class CompoundIndexMember
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    /// <summary>
    /// 组合辅助索引：由 ##lb-labels## 行同组 ~ 前缀列组合而成的联合索引。
    /// Members 顺序 = 字段在表中的定义顺序（= item 构造函数读取顺序）。
    /// </summary>
    public class CompoundIndex
    {
        public string GroupName { get; set; }
        public List<CompoundIndexMember> Members { get; set; } = new List<CompoundIndexMember>();
    }
}
