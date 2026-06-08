using System.Collections.Generic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 集合变更事件参数。
    /// </summary>
    public readonly struct ListChangedEventArgs<T>
    {
        /// <summary>变更类型。</summary>
        public ListChangeType Type { get; init; }

        /// <summary>受影响的索引。</summary>
        public int Index { get; init; }

        /// <summary>Move 操作的源索引。</summary>
        public int OldIndex { get; init; }

        /// <summary>新增/替换后的新值。</summary>
        public T Item { get; init; }

        /// <summary>替换前的旧值（仅 Replace 类型有效）。</summary>
        public T OldItem { get; init; }

        /// <summary>批量操作的新值列表（AddRange/ReplaceAll 有效）。</summary>
        public IReadOnlyList<T> NewItems { get; init; }
    }
}
