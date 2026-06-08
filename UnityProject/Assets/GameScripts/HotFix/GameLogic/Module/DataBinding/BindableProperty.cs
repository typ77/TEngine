using System;
using System.Collections.Generic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 响应式属性包装器，支持脏标记和批量回调。
    /// 赋值立即更新值，但回调延迟到 FireCallback 调用（由 BatchScheduler 统一触发）。
    /// 同帧多次赋值合并为一次回调，旧值保留为第一次赋值前的值。
    /// </summary>
    /// <typeparam name="T">属性类型</typeparam>
    public sealed class BindableProperty<T> : IDisposable, IBatchDirtyTarget
    {
        private T _value;
        private readonly IEqualityComparer<T> _comparer;
        private bool _isDirty;
        private T _oldValue;
        private bool _isDisposed;

        /// <summary>
        /// 值变化事件，参数为 (旧值, 新值)。
        /// </summary>
        public event Action<T, T> OnValueChanged;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="initialValue">初始值</param>
        /// <param name="comparer">自定义相等比较器，null 使用默认</param>
        public BindableProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            _value = initialValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        /// <summary>
        /// 当前值，赋值立即生效，回调延迟触发。
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                if (_isDisposed) return;
                if (_comparer.Equals(_value, value)) return;

                if (!_isDirty)
                {
                    _oldValue = _value;
                    _isDirty = true;
                }
                _value = value;

                if (OnValueChanged != null)
                    BatchScheduler.Instance.MarkDirty(this);
            }
        }

        /// <summary>
        /// 是否已释放。
        /// </summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// 显式接口实现，外部通过 IBatchDirtyTarget 调用。
        /// </summary>
        void IBatchDirtyTarget.FireCallback()
        {
            if (!_isDirty || _isDisposed) return;
            _isDirty = false;
            var old = _oldValue;
            var current = _value;
            OnValueChanged?.Invoke(old, current);
        }

        /// <summary>
        /// 强制触发回调，即使值未变化。
        /// </summary>
        public void ForceNotify()
        {
            if (_isDisposed) return;
            _isDirty = true;
            if (OnValueChanged != null)
                BatchScheduler.Instance.MarkDirty(this);
        }

        /// <summary>
        /// 静默设置值，不触发回调。
        /// </summary>
        public void SetValueSilently(T value)
        {
            if (_isDisposed) return;
            _value = value;
        }

        /// <summary>
        /// 释放资源，取消所有订阅。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            OnValueChanged = null;
        }
    }
}
