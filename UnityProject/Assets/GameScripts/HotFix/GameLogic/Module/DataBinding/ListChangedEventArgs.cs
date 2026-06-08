using System.Collections.Generic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 集合变更事件参数。
    /// </summary>
    public struct ListChangedEventArgs<T>
    {
        /// <summary>变更类型。</summary>
        public ListChangeType Type { get; set; }

        /// <summary>受影响的索引。</summary>
        public int Index { get; set; }

        /// <summary>Move 操作的源索引。</summary>
        public int OldIndex { get; set; }

        /// <summary>新增/替换后的新值。</summary>
        public T Item { get; set; }

        /// <summary>替换前的旧值（仅 Replace 类型有效）。</summary>
        public T OldItem { get; set; }

        /// <summary>批量操作的新值列表（AddRange/ReplaceAll 有效）。</summary>
        public IReadOnlyList<T> NewItems { get; set; }
    }
}
