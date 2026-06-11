using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 响应式集合，支持脏标记和批量回调。
    /// 修改操作立即生效，但变更通知延迟到 FireCallback 调用（由 BatchScheduler 统一触发）。
    /// 同帧多次修改合并为一次 ReplaceAll 回调；单次修改保留原始事件类型。
    /// </summary>
    /// <typeparam name="T">元素类型，要求为 struct 并实现 IEquatable{T}</typeparam>
    public sealed class ObservableList<T> : IDisposable, IReadOnlyList<T>, IBatchDirtyTarget
        where T : struct, IEquatable<T>
    {
        private readonly List<T> _items;
        private int _operationCount;
        private ListChangedEventArgs<T> _firstEventArgs;
        private bool _isDirty;
        private bool _isDisposed;

        /// <summary>
        /// 集合变更事件。
        /// </summary>
        public event Action<ListChangedEventArgs<T>> OnChanged;

        public ObservableList() { _items = new List<T>(); }
        public ObservableList(int capacity) { _items = new List<T>(capacity); }
        public ObservableList(IEnumerable<T> collection) { _items = new List<T>(collection); }

        /// <summary>元素数量。</summary>
        public int Count => _items.Count;

        /// <summary>只读索引器。</summary>
        public T this[int index] => _items[index];

        /// <summary>返回只读视图。</summary>
        public IReadOnlyList<T> AsReadOnly() => _items.AsReadOnly();

        /// <summary>是否包含指定元素。</summary>
        public bool Contains(T item) => _items.Contains(item);

        /// <summary>查找元素索引。</summary>
        public int IndexOf(T item) => _items.IndexOf(item);

        /// <summary>获取枚举器。</summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <summary>
        /// 在末尾添加元素。
        /// </summary>
        public void Add(T item)
        {
            if (_isDisposed) return;
            _items.Add(item);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Add,
                Index = _items.Count - 1,
                Item = item
            });
        }

        /// <summary>
        /// 在指定位置插入元素。index 允许等于 Count（尾部插入）。
        /// </summary>
        public void Insert(int index, T item)
        {
            if (_isDisposed) return;
            if ((uint)index > (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _items.Insert(index, item);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Insert,
                Index = index,
                Item = item
            });
        }

        /// <summary>
        /// 移除指定元素。
        /// </summary>
        public bool Remove(T item)
        {
            if (_isDisposed) return false;
            int index = _items.IndexOf(item);
            if (index < 0) return false;
            _items.RemoveAt(index);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Remove,
                Index = index
            });
            return true;
        }

        /// <summary>
        /// 移除指定位置的元素。
        /// </summary>
        public void RemoveAt(int index)
        {
            if (_isDisposed) return;
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            _items.RemoveAt(index);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.RemoveAt,
                Index = index
            });
        }

        /// <summary>
        /// 替换指定位置的元素。
        /// </summary>
        public void Replace(int index, T newItem)
        {
            if (_isDisposed) return;
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var oldItem = _items[index];
            _items[index] = newItem;
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Replace,
                Index = index,
                Item = newItem,
                OldItem = oldItem
            });
        }

        /// <summary>
        /// 移动元素位置。
        /// </summary>
        public void Move(int fromIndex, int toIndex)
        {
            if (_isDisposed) return;
            if ((uint)fromIndex >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(fromIndex));
            if ((uint)toIndex >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(toIndex));
            var item = _items[fromIndex];
            _items.RemoveAt(fromIndex);
            _items.Insert(toIndex, item);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Move,
                Index = toIndex,
                OldIndex = fromIndex
            });
        }

        /// <summary>
        /// 清空集合。
        /// </summary>
        public void Clear()
        {
            if (_isDisposed) return;
            _items.Clear();
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Clear,
                Index = -1
            });
        }

        /// <summary>
        /// 批量添加元素。
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (_isDisposed) return;
            var list = items as IList<T> ?? items.ToList();
            _items.AddRange(list);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.AddRange,
                NewItems = (list is List<T> listT ? listT : list.ToList()).AsReadOnly()
            });
        }

        /// <summary>
        /// 替换所有元素。
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items)
        {
            if (_isDisposed) return;
            _items.Clear();
            _items.AddRange(items);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.ReplaceAll,
                NewItems = _items.ToList().AsReadOnly()
            });
        }

        /// <summary>
        /// 记录一次变更操作，由 BatchScheduler 在帧末统一触发回调。
        /// <para>
        /// 合并语义：同帧多次操作（_operationCount > 1）会合并为一次 ReplaceAll 事件，
        /// 丢弃各次操作的原始类型和参数。单次操作保留原始事件类型。
        /// Phase 3 引入 UIListWidget 时可能需要更细粒度的 Diff 模式。
        /// </para>
        /// </summary>
        private void NotifyChanged(in ListChangedEventArgs<T> args)
        {
            if (OnChanged == null) return;
            _operationCount++;
            if (_operationCount == 1) _firstEventArgs = args;
            _isDirty = true;
            BatchScheduler.SafeMarkDirty(this);
        }

        /// <summary>
        /// 由 BatchScheduler.Flush 调用。触发合并后的回调。
        /// <para>
        /// 合并规则：
        /// - 单次操作：保留原始事件类型（Add/Insert/Replace 等）
        /// - 多次操作：统一合并为 ReplaceAll，携带当前全量快照
        /// </para>
        /// </summary>
        void IBatchDirtyTarget.FireCallback()
        {
            if (!_isDirty || _isDisposed) return;
            _isDirty = false;
            if (_operationCount > 1)
            {
                OnChanged?.Invoke(new ListChangedEventArgs<T>
                {
                    Type = ListChangeType.ReplaceAll,
                    NewItems = _items.ToList().AsReadOnly()
                });
            }
            else
            {
                OnChanged?.Invoke(_firstEventArgs);
            }
            _operationCount = 0;
            _firstEventArgs = default;
        }

        /// <summary>是否已释放。</summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// 释放资源，取消所有订阅。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            OnChanged = null;
            _items.Clear();
        }
    }
}
