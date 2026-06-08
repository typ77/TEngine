using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 数据上下文基类。
    /// 聚合多个 BindableProperty 和 ObservableList，提供 MapProperty / MapList 映射方法。
    /// Dispose 时自动取消所有订阅并释放持有的属性资源。
    /// </summary>
    public abstract class DataContext : IDisposable
    {
        private readonly List<Action> _unsubscribers = new();
        private readonly List<IDisposable> _ownedProperties = new();
        private bool _isDisposed;

        /// <summary>
        /// 是否已释放。
        /// </summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// 单源标量映射：source → target（经 converter 转换）。
        /// 订阅后立即用 source 当前值初始化 target。
        /// </summary>
        protected void MapProperty<TSource, TTarget>(
            BindableProperty<TSource> source,
            BindableProperty<TTarget> target,
            Func<TSource, TTarget> converter)
        {
            target.SetValueSilently(converter(source.Value));
            Action<TSource, TSource> handler = (_, _) =>
            {
                target.Value = converter(source.Value);
            };
            source.OnValueChanged += handler;
            _unsubscribers.Add(() => source.OnValueChanged -= handler);
        }

        /// <summary>
        /// 双源标量映射：source1 + source2 → target（经 converter 转换）。
        /// 任一 source 变化时重新计算 target。
        /// </summary>
        protected void MapProperty<T1, T2, TTarget>(
            BindableProperty<T1> source1,
            BindableProperty<T2> source2,
            BindableProperty<TTarget> target,
            Func<T1, T2, TTarget> converter)
        {
            target.SetValueSilently(converter(source1.Value, source2.Value));
            Action<T1, T1> h1 = (_, _) => target.Value = converter(source1.Value, source2.Value);
            Action<T2, T2> h2 = (_, _) => target.Value = converter(source1.Value, source2.Value);
            source1.OnValueChanged += h1;
            source2.OnValueChanged += h2;
            _unsubscribers.Add(() => source1.OnValueChanged -= h1);
            _unsubscribers.Add(() => source2.OnValueChanged -= h2);
        }

        /// <summary>
        /// 三源标量映射：source1 + source2 + source3 → target（经 converter 转换）。
        /// 任一 source 变化时重新计算 target。
        /// </summary>
        protected void MapProperty<T1, T2, T3, TTarget>(
            BindableProperty<T1> source1,
            BindableProperty<T2> source2,
            BindableProperty<T3> source3,
            BindableProperty<TTarget> target,
            Func<T1, T2, T3, TTarget> converter)
        {
            target.SetValueSilently(converter(source1.Value, source2.Value, source3.Value));
            Action<T1, T1> h1 = (_, _) => target.Value = converter(source1.Value, source2.Value, source3.Value);
            Action<T2, T2> h2 = (_, _) => target.Value = converter(source1.Value, source2.Value, source3.Value);
            Action<T3, T3> h3 = (_, _) => target.Value = converter(source1.Value, source2.Value, source3.Value);
            source1.OnValueChanged += h1;
            source2.OnValueChanged += h2;
            source3.OnValueChanged += h3;
            _unsubscribers.Add(() => source1.OnValueChanged -= h1);
            _unsubscribers.Add(() => source2.OnValueChanged -= h2);
            _unsubscribers.Add(() => source3.OnValueChanged -= h3);
        }

        /// <summary>
        /// 列表同步映射：source list → target list（经 converter 转换）。
        /// 初始同步后，增量跟踪 source 的变更事件并同步到 target。
        /// </summary>
        protected void MapList<TSource, TTarget>(
            ObservableList<TSource> source,
            ObservableList<TTarget> target,
            Func<TSource, TTarget> converter)
            where TSource : struct, IEquatable<TSource>
            where TTarget : struct, IEquatable<TTarget>
        {
            target.ReplaceAll(source.Select(converter));
            Action<ListChangedEventArgs<TSource>> handler = args =>
            {
                switch (args.Type)
                {
                    case ListChangeType.Add: target.Add(converter(args.Item)); break;
                    case ListChangeType.Insert: target.Insert(args.Index, converter(args.Item)); break;
                    case ListChangeType.RemoveAt: target.RemoveAt(args.Index); break;
                    case ListChangeType.Replace: target.Replace(args.Index, converter(args.Item)); break;
                    case ListChangeType.Move: target.Move(args.OldIndex, args.Index); break;
                    case ListChangeType.Clear: target.Clear(); break;
                    default: target.ReplaceAll(source.Select(converter)); break;
                }
            };
            source.OnChanged += handler;
            _unsubscribers.Add(() => source.OnChanged -= handler);
        }

        /// <summary>
        /// 释放资源，取消所有订阅。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            foreach (var unsub in _unsubscribers) unsub();
            _unsubscribers.Clear();
            foreach (var prop in _ownedProperties) prop.Dispose();
            _ownedProperties.Clear();
        }
    }

    /// <summary>
    /// 泛型 DataContext 基类，关联特定的 View 类型。
    /// </summary>
    /// <typeparam name="TView">关联的 View 类型（UIBase 子类）</typeparam>
    public abstract class DataContext<TView> : DataContext where TView : UIBase
    {
    }
}
