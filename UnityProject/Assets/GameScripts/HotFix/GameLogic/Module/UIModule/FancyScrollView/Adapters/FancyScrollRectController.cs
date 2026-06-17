using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;
using GameLogic.UI.FancyScrollView.EasingCore;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 纯 C# 的 ScrollRect 风格滚动控制器（组合模式，不继承 MonoBehaviour / 不依赖泛型 MonoBehaviour 引擎）。
    /// <para>
    /// 解决 <see cref="FancyScrollRect{TItemData, TContext}"/>（泛型 MonoBehaviour）无法 AddComponent 的问题：
    /// 将位置计算（cellInterval/scrollOffset/MaxScrollPosition）、Cell 池、Padding/Spacing/Alignment 配置
    /// 全部搬入本纯 C# 类，通过非泛型 <see cref="IFancyCell"/> 接口管理 <see cref="FancyCellWidget{TItemData, TContext}"/> Cell。
    /// </para>
    /// <para>
    /// 使用方式（在 UIWindow 中，同 <see cref="FancyScrollerController"/> 范式）：
    /// <code>
    /// _controller = new FancyScrollRectController();
    /// _controller.Initialize(this, m_scroller, () => new MyCell());
    /// await _controller.LoadCellPrefabAsync("MyCellPrefab");
    /// _controller.PaddingTop = 10f;       // ScrollRect 特性：可调 Padding/Spacing
    /// _controller.UpdateData(items);
    /// _controller.ScrollTo(index, 0.3f, Ease.InOutQuint, Alignment.Middle);
    /// </code>
    /// </para>
    /// </summary>
    public class FancyScrollRectController : IDisposable
    {
        // ── 依赖（通过 Initialize 注入）───────────────────────────
        private UIBase _owner;
        private Scroller _scroller;
        private Transform _cellContainer;
        private Func<IFancyCell> _cellFactory;

        // ── Cell Prefab 资源管理 ─────────────────────────────────
        private AssetHandle _cellPrefabHandle;
        private GameObject _cellPrefab;

        // ── Cell 池 ──────────────────────────────────────────────
        private readonly List<IFancyCell> _cellPool = new();
        private IList _itemsSource = new List<object>();
        private object _sharedContext;
        private float _currentPosition;
        private bool _initialized;

        // ── 反射缓存（InitializeWidget）──────────────────────────
        private static readonly Dictionary<Type, MethodInfo> _initializeWidgetCache = new();

        // ── 事件 ─────────────────────────────────────────────────

        /// <summary>Cell 被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellItemClicked;

        /// <summary>Cell 内按钮被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellButtonClicked;

        /// <summary>滚动位置变化时触发（Scroller 原始值）。</summary>
        public event Action<float> OnPositionChanged;

        // ── ScrollRect 布局配置（移植自 FancyScrollRect）─────────

        /// <summary>Cell 尺寸（沿主轴，像素）。</summary>
        public float CellSize { get; set; } = 100f;

        /// <summary>Cell 间距（像素）。</summary>
        public float Spacing
        {
            get => _spacing;
            set { _spacing = value; Relayout(); }
        }
        private float _spacing = 0f;

        /// <summary>内容顶部内边距（像素）。</summary>
        public float PaddingTop
        {
            get => _paddingHead;
            set { _paddingHead = value; Relayout(); }
        }
        private float _paddingHead = 0f;

        /// <summary>内容底部内边距（像素）。</summary>
        public float PaddingBottom
        {
            get => _paddingTail;
            set { _paddingTail = value; Relayout(); }
        }
        private float _paddingTail = 0f;

        /// <summary>Cell 复用余白（Cell 数量单位）。</summary>
        public float ReuseCellMarginCount { get; set; } = 0f;

        /// <summary>数据条目数。</summary>
        public int DataCount => _itemsSource?.Count ?? 0;

        // ── FancyScrollView 内部状态（移植）──────────────────────
        private float _cellInterval = 0.2f;  // 归一化 Cell 间隔（由布局参数推导）
        private float _scrollOffset = 0.5f;  // 归一化滚动偏移（由布局参数推导）

        /// <summary>与所有 Cell 共享的 Context 对象。</summary>
        public object SharedContext => _sharedContext;

        #region 生命周期

        /// <summary>
        /// 初始化控制器。
        /// </summary>
        /// <param name="owner">拥有此控制器的 UIBase（作为 Cell 的父）。</param>
        /// <param name="scroller">Scroller 组件引用。</param>
        /// <param name="cellFactory">Cell 工厂委托。</param>
        /// <param name="context">共享上下文（可选）。</param>
        public void Initialize(
            UIBase owner,
            Scroller scroller,
            Func<IFancyCell> cellFactory = null,
            object context = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _scroller = scroller ?? throw new ArgumentNullException(nameof(scroller));
            _cellContainer = (scroller as FancyScrollerView)?.CellContainer ?? scroller.transform;
            _cellFactory = cellFactory;
            _sharedContext = context;

            _scroller.OnValueChanged(OnScrollerPositionChanged);

            // 填充 Context 的 IFancyScrollRectContext 成员（移植自 FancyScrollRect.Initialize）
            // Cell.UpdatePosition 依赖 CalculateScrollSize/ScrollDirection。
            FillContextScrollInfo();
        }

        /// <summary>填充 Context 的 ScrollDirection 与 CalculateScrollSize（供 Cell.UpdatePosition 使用）。</summary>
        private void FillContextScrollInfo()
        {
            if (_sharedContext == null || _scroller == null) return;
            var ctxType = _sharedContext.GetType();

            // ScrollDirection
            var sdProp = ctxType.GetProperty("ScrollDirection");
            if (sdProp != null && sdProp.CanWrite)
            {
                sdProp.SetValue(_sharedContext, _scroller.ScrollDirection);
            }

            // CalculateScrollSize：返回 (scrollSize, reuseMargin)，移植自 FancyScrollRect.Initialize
            var cssProp = ctxType.GetProperty("CalculateScrollSize");
            if (cssProp != null && cssProp.CanWrite)
            {
                cssProp.SetValue(_sharedContext, new Func<(float ScrollSize, float ReuseMargin)>(() =>
                {
                    var interval = CellSize + _spacing;
                    var reuseMargin = interval * ReuseCellMarginCount;
                    var scrollSize = _scroller.ViewportSize + interval + reuseMargin * 2f;
                    return (scrollSize, reuseMargin);
                }));
            }
        }

        /// <summary>更新 Cell 工厂。</summary>
        public void SetCellFactory(Func<IFancyCell> cellFactory)
            => _cellFactory = cellFactory ?? throw new ArgumentNullException(nameof(cellFactory));

        /// <summary>设置共享上下文（自动填充 ScrollDirection/CalculateScrollSize）。</summary>
        public void SetContext(object context)
        {
            _sharedContext = context;
            FillContextScrollInfo();
        }

        /// <summary>异步加载 Cell Prefab。</summary>
        public async UniTask LoadCellPrefabAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            System.Threading.CancellationToken ct = default)
        {
            ReleaseCellPrefabHandle();

            _cellPrefabHandle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(cellAssetLocation);
            while (!_cellPrefabHandle.IsDone)
            {
                progress?.Report(_cellPrefabHandle.Progress);
                await UniTask.Yield(ct);
            }
            ct.ThrowIfCancellationRequested();

            if (_cellPrefabHandle.Status == EOperationStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Cell prefab load failed: {cellAssetLocation}, error: {_cellPrefabHandle.LastError}");
            }

            _cellPrefab = _cellPrefabHandle.AssetObject as GameObject;
            if (_cellPrefab == null)
            {
                throw new InvalidOperationException($"Cell prefab load failed (null): {cellAssetLocation}");
            }
        }

        /// <summary>释放所有资源。</summary>
        public void Dispose()
        {
            for (var i = _cellPool.Count - 1; i >= 0; i--)
            {
                var cell = _cellPool[i];
                if (cell is UIWidget widget && widget.gameObject != null)
                {
                    UnityEngine.Object.Destroy(widget.gameObject);
                }
            }
            _cellPool.Clear();
            ReleaseCellPrefabHandle();
            OnCellItemClicked = null;
            OnCellButtonClicked = null;
        }

        private void ReleaseCellPrefabHandle()
        {
            _cellPrefab = null;
            _cellPrefabHandle?.Dispose();
            _cellPrefabHandle = null;
        }

        #endregion

        #region 公开 API

        /// <summary>更新列表数据并刷新。</summary>
        public void UpdateData(IList items)
        {
            _itemsSource = items ?? new List<object>();
            if (_scroller != null) _scroller.SetTotalCount(_itemsSource.Count);
            Refresh();
        }

        /// <summary>滚动到指定索引（带对齐）。</summary>
        public void ScrollTo(int index, float duration, float alignment = 0.5f, Action onComplete = null)
        {
            _scroller?.ScrollTo(ToScrollerPosition(index, alignment), duration, onComplete);
        }

        /// <summary>滚动到指定索引（带对齐与缓动）。</summary>
        public void ScrollTo(int index, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
        {
            _scroller?.ScrollTo(ToScrollerPosition(index, alignment), duration, easing, onComplete);
        }

        /// <summary>立即跳转到指定索引（带对齐，无动画）。</summary>
        public void JumpTo(int index, float alignment = 0.5f)
        {
            if (_scroller == null) return;
            _scroller.Position = ToScrollerPosition(index, alignment);
        }

        /// <summary>强制刷新所有可见 Cell。</summary>
        public void Refresh() => UpdatePosition(_currentPosition, true);

        /// <summary>重新布局（改 Padding/Spacing 后调用）。</summary>
        public void Relayout() => UpdatePosition(_currentPosition, false);

        #endregion

        #region ScrollRect 位置算法（移植自 FancyScrollRect.cs）

        // Viewport 沿主轴的像素尺寸
        private float ViewportSize => _scroller != null ? _scroller.ViewportSize : 0f;

        // 可滚动的归一化区间长度（FancyScrollRect.ScrollLength）
        private float ScrollLength => 1f / Mathf.Max(_cellInterval, 1e-2f) - 1f;

        // 可滚动区间（扣除复用余白）
        private float ViewportLength => ScrollLength - ReuseCellMarginCount * 2f;

        // 顶部 Padding 对应的归一化偏移（FancyScrollRect.PaddingHeadLength）
        private float PaddingHeadLength => (_paddingHead - _spacing * 0.5f) / (CellSize + _spacing);

        // 最大滚动位置（FancyScrollRect.MaxScrollPosition）
        private float MaxScrollPosition => _itemsSource.Count
            - ScrollLength
            + ReuseCellMarginCount * 2f
            + (_paddingHead + _paddingTail - _spacing) / (CellSize + _spacing);

        private bool Scrollable => MaxScrollPosition > 0f;

        /// <summary>
        /// 根据当前布局参数重新计算 cellInterval / scrollOffset（FancyScrollRect.AdjustCellIntervalAndScrollOffset）。
        /// </summary>
        private void AdjustCellIntervalAndScrollOffset()
        {
            var totalSize = ViewportSize + (CellSize + _spacing) * (1f + ReuseCellMarginCount * 2f);
            _cellInterval = (CellSize + _spacing) / totalSize;
            _scrollOffset = _cellInterval * (1f + ReuseCellMarginCount);
        }

        /// <summary>FancyScrollView 位置 → Scroller 位置（FancyScrollRect.ToScrollerPosition）。</summary>
        private float ToScrollerPosition(float position)
        {
            return (position + PaddingHeadLength) / MaxScrollPosition * Mathf.Max(_itemsSource.Count - 1, 1);
        }

        /// <summary>FancyScrollView 位置 → Scroller 位置（带对齐，FancyScrollRect.ToScrollerPosition(pos, alignment)）。</summary>
        private float ToScrollerPosition(float position, float alignment = 0.5f)
        {
            var offset = alignment * (ScrollLength - (1f + ReuseCellMarginCount * 2f))
                + (1f - alignment - 0.5f) * _spacing / (CellSize + _spacing);
            return ToScrollerPosition(Mathf.Clamp(position - offset, 0f, MaxScrollPosition));
        }

        /// <summary>Scroller 位置 → FancyScrollView 位置（FancyScrollRect.ToFancyScrollViewPosition）。</summary>
        private float ToFancyScrollViewPosition(float position)
        {
            return position / Mathf.Max(_itemsSource.Count - 1, 1) * MaxScrollPosition - PaddingHeadLength;
        }

        #endregion

        #region Cell 池与位置更新（移植自 FancyScrollView + FancyScrollRect）

        private void OnScrollerPositionChanged(float position)
        {
            OnPositionChanged?.Invoke(position);
            // FancyScrollRect.OnScrollerValueChanged: UpdatePosition(ToFancyScrollViewPosition(Scrollable ? p : 0))
            UpdatePosition(ToFancyScrollViewPosition(Scrollable ? position : 0f), false);
        }

        private void UpdatePosition(float position, bool forceRefresh)
        {
            if (_cellPrefab == null || _cellContainer == null) return;

            AdjustCellIntervalAndScrollOffset();

            if (!_initialized) _initialized = true;

            _currentPosition = position;

            // FancyScrollView.UpdatePosition 的池化逻辑
            var p = position - _scrollOffset / _cellInterval;
            var firstIndex = Mathf.CeilToInt(p);
            var firstPosition = (Mathf.Ceil(p) - p) * _cellInterval;

            if (firstPosition + _cellPool.Count * _cellInterval < 1f)
            {
                ResizePool(firstPosition);
            }

            UpdateCells(firstPosition, firstIndex, forceRefresh);
        }

        private void ResizePool(float firstPosition)
        {
            var addCount = Mathf.CeilToInt((1f - firstPosition) / _cellInterval) - _cellPool.Count;
            for (var i = 0; i < addCount; i++)
            {
                var cell = CreateCell();
                cell.SetContext(_sharedContext);
                cell.Initialize();
                cell.SetVisible(false);
                _cellPool.Add(cell);
            }
        }

        private void UpdateCells(float firstPosition, int firstIndex, bool forceRefresh)
        {
            var interval = _cellInterval;
            var items = _itemsSource;
            var itemCount = items.Count;

            for (var i = 0; i < _cellPool.Count; i++)
            {
                var index = firstIndex + i;
                var position = firstPosition + i * interval;
                var cell = _cellPool[i];

                if (index < 0 || index >= itemCount || position > 1f)
                {
                    cell.SetVisible(false);
                    continue;
                }

                if (forceRefresh || cell.Index != index || !cell.IsVisible)
                {
                    cell.Index = index;
                    cell.SetVisible(true);
                    cell.UpdateContent(_itemsSource[index]);
                }

                cell.UpdatePosition(position);
            }
        }

        private IFancyCell CreateCell()
        {
            if (_cellFactory == null)
            {
                throw new InvalidOperationException("Cell factory not set. Call SetCellFactory() first.");
            }

            var go = UnityEngine.Object.Instantiate(_cellPrefab, _cellContainer);
            var cell = _cellFactory();

            // 反射调用 internal InitializeWidget(UIBase, GameObject, bool)
            var cellType = cell.GetType();
            if (!_initializeWidgetCache.TryGetValue(cellType, out var method))
            {
                method = cellType.GetMethod("InitializeWidget",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(UIBase), typeof(GameObject), typeof(bool) }, null);

                if (method == null)
                {
                    method = cellType.GetMethod("InitializeWidget",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(UIBase), typeof(GameObject) }, null);
                }

                if (method == null)
                {
                    throw new InvalidOperationException($"Cell type {cellType.Name} has no InitializeWidget method.");
                }
                _initializeWidgetCache[cellType] = method;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 3)
            {
                method.Invoke(cell, new object[] { _owner, go, true });
            }
            else
            {
                method.Invoke(cell, new object[] { _owner, go });
            }

            // 订阅事件
            var onItemClickedEvent = cellType.GetEvent("OnItemClicked", BindingFlags.Instance | BindingFlags.Public);
            var onButtonClickedEvent = cellType.GetEvent("OnButtonClicked", BindingFlags.Instance | BindingFlags.Public);
            if (onItemClickedEvent != null)
            {
                onItemClickedEvent.AddEventHandler(cell, (Action<int>)HandleCellClicked);
            }
            if (onButtonClickedEvent != null)
            {
                onButtonClickedEvent.AddEventHandler(cell, (Action<int>)HandleCellButtonClicked);
            }

            return cell;
        }

        private void HandleCellClicked(int index) => OnCellItemClicked?.Invoke(index);
        private void HandleCellButtonClicked(int index) => OnCellButtonClicked?.Invoke(index);

        #endregion
    }
}
