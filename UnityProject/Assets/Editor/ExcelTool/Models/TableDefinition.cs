using System.Collections.Generic;

namespace TEngine.Editor.ExcelTool
{
    public class ExcelCellLocation
    {
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public int Row { get; set; } = -1;
        public int Column { get; set; } = -1;
    }

    public class FieldDefinition
    {
        public string Name { get; set; }
        public TypeDescriptor Type { get; set; }
        public int ColumnIndex { get; set; }
        public ExcelCellLocation HeaderLocation { get; set; }
    }

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

    public class FieldValue
    {
        public string RawText { get; set; }
        public object ParsedValue { get; set; }
        public TypeDescriptor TypeDescriptor { get; set; }
        public ExcelCellLocation Location { get; set; }
    }

    public class TableRow
    {
        public string PrimaryKey { get; set; }
        public Dictionary<string, FieldValue> Fields { get; set; } = new();
    }

    public class TableDefinition
    {
        public string ClassName { get; set; }
        public string FilePath { get; set; }
        public string SheetName { get; set; }
        public FieldDefinition PrimaryKey { get; set; }
        public List<FieldDefinition> Fields { get; set; } = new();
        public List<CompoundIndex> CompoundIndexes { get; set; } = new List<CompoundIndex>();
        public List<TableRow> Rows { get; set; } = new();
    }
}
