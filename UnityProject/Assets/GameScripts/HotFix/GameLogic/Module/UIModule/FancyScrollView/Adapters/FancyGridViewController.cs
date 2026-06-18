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
    /// 纯 C# 的 GridView（网格）滚动控制器。
    /// <para>
    /// 每个池单元是一组 Cell（一行/列，含 <see cref="StartAxisCellCount"/> 个），组内 Cell 共享主轴 position，
    /// 各自按 <c>Index % groupCount</c> 计算交叉轴偏移（由 Cell.UpdatePosition 完成）。
    /// </para>
    /// <para>
    /// 位置算法（cellInterval/scrollOffset/MaxScrollPosition/ToScrollerPosition）与
    /// <see cref="FancyScrollRectController"/> 一致，但池单元按「组」管理。
    /// </para>
    /// </summary>
    public class FancyGridViewController : IDisposable
    {
        // ── 依赖 ──
        private UIBase _owner;
        private Scroller _scroller;
        private Transform _cellContainer;
        private Func<IFancyCell> _cellFactory;

        // ── Prefab ──
        private AssetHandle _cellPrefabHandle;
        private GameObject _cellPrefab;

        // ── Cell 池（每元素是一组子 Cell）──
        private readonly List<CellGroupUnit> _groupPool = new();
        private IList _itemsSource = new List<object>();
        private object _sharedContext;
        private float _currentPosition;
        private bool _initialized;

        private static readonly Dictionary<Type, MethodInfo> _initializeWidgetCache = new();

        // ── 事件 ──
        public event Action<int> OnCellItemClicked;
        public event Action<int> OnCellButtonClicked;
        public event Action<float> OnPositionChanged;

        // ── 配置 ──
        /// <summary>每行/列 Cell 数量。</summary>
        public int StartAxisCellCount { get; set; } = 4;

        /// <summary>交叉轴 Cell 间距。</summary>
        public float StartAxisSpacing { get; set; } = 0f;

        /// <summary>Cell 二维尺寸。</summary>
        public Vector2 CellSize2D { get; set; } = new Vector2(100f, 100f);

        /// <summary>主轴 Cell 间距。</summary>
        public float Spacing { get => _spacing; set { _spacing = value; Relayout(); } }
        private float _spacing = 0f;

        public float PaddingTop { get => _paddingHead; set { _paddingHead = value; Relayout(); } }
        private float _paddingHead = 0f;

        public float PaddingBottom { get => _paddingTail; set { _paddingTail = value; Relayout(); } }
        private float _paddingTail = 0f;

        public float ReuseCellMarginCount { get; set; } = 0f;

        public int DataCount => _itemsSource?.Count ?? 0;

        private float MainAxisCellSize => _scroller != null && _scroller.ScrollDirection == ScrollDirection.Horizontal
            ? CellSize2D.x : CellSize2D.y;

        // ── 内部位置算法状态 ──
        private float _cellInterval = 0.2f;
        private float _scrollOffset = 0.5f;

        public object SharedContext => _sharedContext;

        #region 生命周期

        public void Initialize(UIBase owner, Scroller scroller, Func<IFancyCell> cellFactory = null, object context = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _scroller = scroller ?? throw new ArgumentNullException(nameof(scroller));
            _cellContainer = (scroller as FancyScrollerView)?.CellContainer ?? scroller.transform;
            _cellFactory = cellFactory;
            _sharedContext = context;
            _scroller.OnValueChanged(OnScrollerPositionChanged);
            FillContextScrollInfo();
            FillContextGroupInfo();
        }

        public void SetCellFactory(Func<IFancyCell> cellFactory)
            => _cellFactory = cellFactory ?? throw new ArgumentNullException(nameof(cellFactory));

        public void SetContext(object context)
        {
            _sharedContext = context;
            FillContextScrollInfo();
            FillContextGroupInfo();
        }

        public async UniTask LoadCellPrefabAsync(
            string cellAssetLocation, IProgress<float> progress = null, System.Threading.CancellationToken ct = default)
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
                throw new InvalidOperationException($"Cell prefab load failed: {cellAssetLocation}, error: {_cellPrefabHandle.LastError}");
            }
            _cellPrefab = _cellPrefabHandle.AssetObject as GameObject;
            if (_cellPrefab == null)
            {
                throw new InvalidOperationException($"Cell prefab load failed (null): {cellAssetLocation}");
            }
        }

        public void Dispose()
        {
            for (var i = _groupPool.Count - 1; i >= 0; i--)
            {
                _groupPool[i].DestroyAll();
            }
            _groupPool.Clear();
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

        #region 数据与滚动

        public void UpdateData(IList items)
        {
            _itemsSource = items ?? new List<object>();
            if (_scroller != null)
            {
                // Scroller 的 totalCount = 组数（每组算一个滚动单元）
                int groupCount = Mathf.CeilToInt((float)Math.Max(_itemsSource.Count, 1) / StartAxisCellCount);
                _scroller.SetTotalCount(groupCount);
            }
            FillContextGroupInfo();
            Refresh();
        }

        public void ScrollTo(int index, float duration, float alignment = 0.5f, Action onComplete = null)
        {
            int groupIndex = index / StartAxisCellCount;
            _scroller?.ScrollTo(ToScrollerPosition(groupIndex, alignment), duration, onComplete);
        }

        public void ScrollTo(int index, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
        {
            int groupIndex = index / StartAxisCellCount;
            _scroller?.ScrollTo(ToScrollerPosition(groupIndex, alignment), duration, easing, onComplete);
        }

        public void JumpTo(int index, float alignment = 0.5f)
        {
            if (_scroller == null) return;
            int groupIndex = index / StartAxisCellCount;
            _scroller.Position = ToScrollerPosition(groupIndex, alignment);
        }

        public void Refresh()
        {
            UpdatePosition(_currentPosition, true);
            RefreshScrollbar();
        }

        /// <summary>更新滚动条：内容不足一屏（!Scrollable）时隐藏，否则按视口/内容比例设 size。
        /// scrollbar 的 value 由 Scroller 自动同步（Scroller.UpdatePosition 内）；GridView 只管 size/可见性。
        /// 在 Refresh/Relayout/UpdateData 后调用（contentLength 随 groupCount/padding 变化）。</summary>
        private void RefreshScrollbar()
        {
            if (_scroller == null || _scroller.Scrollbar == null) return;
            _scroller.Scrollbar.gameObject.SetActive(Scrollable);
            var contentLength = Mathf.Max(GroupCount + (_paddingHead + _paddingTail - _spacing) / (CellSize + _spacing), 1f);
            _scroller.Scrollbar.size = Scrollable ? Mathf.Clamp01(ViewportLength / contentLength) : 1f;
        }

        public void Relayout()
        {
            // padding/spacing 改变后，缓存的 _currentPosition（基于旧 padding 换算的归一化位置）已失效。
            // 必须用 Scroller 当前物理位置重新换算，padding 的留白偏移（PaddingHeadLength → 负 position）
            // 才能即时生效。与 FancyScrollRectController.Relayout 同源。
            if (_scroller != null)
            {
                UpdatePosition(ToFancyScrollViewPosition(Scrollable ? _scroller.Position : 0f), false);
            }
            else
            {
                UpdatePosition(_currentPosition, false);
            }
            RefreshScrollbar();
        }

        #endregion

        #region 位置算法（同 FancyScrollRectController）

        private float ViewportSize => _scroller != null ? _scroller.ViewportSize : 0f;
        private float CellSize => MainAxisCellSize;

        private float ScrollLength => 1f / Mathf.Max(_cellInterval, 1e-2f) - 1f;
        private float ViewportLength => ScrollLength - ReuseCellMarginCount * 2f;
        private float PaddingHeadLength => (_paddingHead - _spacing * 0.5f) / (CellSize + _spacing);

        private int GroupCount => Mathf.CeilToInt((float)Math.Max(_itemsSource.Count, 1) / StartAxisCellCount);

        private float MaxScrollPosition => GroupCount
            - ScrollLength
            + ReuseCellMarginCount * 2f
            + (_paddingHead + _paddingTail - _spacing) / (CellSize + _spacing);

        private bool Scrollable => MaxScrollPosition > 0f;

        private void AdjustCellIntervalAndScrollOffset()
        {
            var totalSize = ViewportSize + (CellSize + _spacing) * (1f + ReuseCellMarginCount * 2f);
            _cellInterval = (CellSize + _spacing) / totalSize;
            _scrollOffset = _cellInterval * (1f + ReuseCellMarginCount);
        }

        private float ToScrollerPosition(float position)
        {
            return (position + PaddingHeadLength) / MaxScrollPosition * Mathf.Max(GroupCount - 1, 1);
        }

        private float ToScrollerPosition(float position, float alignment = 0.5f)
        {
            var offset = alignment * (ScrollLength - (1f + ReuseCellMarginCount * 2f))
                + (1f - alignment - 0.5f) * _spacing / (CellSize + _spacing);
            return ToScrollerPosition(Mathf.Clamp(position - offset, 0f, MaxScrollPosition));
        }

        private float ToFancyScrollViewPosition(float position)
        {
            return position / Mathf.Max(GroupCount - 1, 1) * MaxScrollPosition - PaddingHeadLength;
        }

        #endregion

        #region 池化与位置更新

        private void OnScrollerPositionChanged(float position)
        {
            OnPositionChanged?.Invoke(position);
            UpdatePosition(ToFancyScrollViewPosition(Scrollable ? position : 0f), false);
        }

        private void UpdatePosition(float position, bool forceRefresh)
        {
            if (_cellPrefab == null || _cellContainer == null) return;
            AdjustCellIntervalAndScrollOffset();
            if (!_initialized) _initialized = true;
            _currentPosition = position;

            var p = position - _scrollOffset / _cellInterval;
            var firstGroupIndex = Mathf.CeilToInt(p);
            var firstPosition = (Mathf.Ceil(p) - p) * _cellInterval;

            if (firstPosition + _groupPool.Count * _cellInterval < 1f)
            {
                ResizePool(firstPosition);
            }

            UpdateGroups(firstPosition, firstGroupIndex, forceRefresh);
        }

        private void ResizePool(float firstPosition)
        {
            var addCount = Mathf.CeilToInt((1f - firstPosition) / _cellInterval) - _groupPool.Count;
            for (var i = 0; i < addCount; i++)
            {
                var group = CreateGroup();
                group.SetContextAll(_sharedContext);
                group.InitializeAll();
                group.SetVisibleAll(false);
                _groupPool.Add(group);
            }
        }

        private void UpdateGroups(float firstPosition, int firstGroupIndex, bool forceRefresh)
        {
            var interval = _cellInterval;
            var itemCount = _itemsSource.Count;
            var groupCount = GroupCount;

            for (var i = 0; i < _groupPool.Count; i++)
            {
                var groupIndex = firstGroupIndex + i;
                var position = firstPosition + i * interval;
                var group = _groupPool[i];

                if (groupIndex < 0 || groupIndex >= groupCount || position > 1f)
                {
                    group.SetVisibleAll(false);
                    continue;
                }

                // 该组的 item 范围
                int itemStart = groupIndex * StartAxisCellCount;
                int itemEnd = Math.Min(itemStart + StartAxisCellCount, itemCount);

                bool contentChanged = forceRefresh || group.GroupIndex != groupIndex || !group.IsVisible;
                group.GroupIndex = groupIndex;
                group.SetVisibleAll(true);

                if (contentChanged)
                {
                    // 分发该组的数据到子 Cell
                    for (int c = 0; c < group.Cells.Count; c++)
                    {
                        int itemIndex = itemStart + c;
                        var cell = group.Cells[c];
                        if (itemIndex < itemEnd)
                        {
                            cell.Index = itemIndex;
                            cell.SetVisible(true);
                            cell.UpdateContent(_itemsSource[itemIndex]);
                        }
                        else
                        {
                            cell.SetVisible(false);
                        }
                    }
                }

                // 广播 position 给所有子 Cell（共享主轴位置）
                group.UpdatePositionAll(position);
            }
        }

        /// <summary>创建一个组（含 StartAxisCellCount 个子 Cell，每个有独立 GameObject）。</summary>
        private CellGroupUnit CreateGroup()
        {
            if (_cellFactory == null)
            {
                throw new InvalidOperationException("Cell factory not set. Call SetCellFactory() first.");
            }

            var cells = new List<IFancyCell>(StartAxisCellCount);
            for (int i = 0; i < StartAxisCellCount; i++)
            {
                cells.Add(CreateSingleCell());
            }
            return new CellGroupUnit(cells);
        }

        /// <summary>创建单个带 GameObject 的 Cell（Instantiate prefab + InitializeWidget + 订阅事件）。</summary>
        private IFancyCell CreateSingleCell()
        {
            var go = UnityEngine.Object.Instantiate(_cellPrefab, _cellContainer);
            var cell = _cellFactory();

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

        #region Context 分组信息

        /// <summary>填充 Context 的 ScrollDirection 与 CalculateScrollSize（IFancyScrollRectContext，供 Cell.UpdatePosition）。</summary>
        private void FillContextScrollInfo()
        {
            if (_sharedContext == null || _scroller == null) return;
            var ctxType = _sharedContext.GetType();

            var sdProp = ctxType.GetProperty("ScrollDirection");
            if (sdProp != null && sdProp.CanWrite)
            {
                sdProp.SetValue(_sharedContext, _scroller.ScrollDirection);
            }

            var cssProp = ctxType.GetProperty("CalculateScrollSize");
            if (cssProp != null && cssProp.CanWrite)
            {
                cssProp.SetValue(_sharedContext, new Func<(float ScrollSize, float ReuseMargin)>(() =>
                {
                    var interval = MainAxisCellSize + _spacing;
                    var reuseMargin = interval * ReuseCellMarginCount;
                    var scrollSize = _scroller.ViewportSize + interval + reuseMargin * 2f;
                    return (scrollSize, reuseMargin);
                }));
            }
        }

        private void FillContextGroupInfo()
        {
            var ctx = _sharedContext;
            if (ctx == null) return;
            var ctxType = ctx.GetType();
            float crossSize = _scroller != null && _scroller.ScrollDirection == ScrollDirection.Horizontal
                ? CellSize2D.y : CellSize2D.x;
            SetFunc(ctxType, ctx, "GetGroupCount", () => StartAxisCellCount);
            SetFunc(ctxType, ctx, "GetStartAxisSpacing", () => StartAxisSpacing);
            SetFunc(ctxType, ctx, "GetCellSize", () => crossSize);
        }

        private static void SetFunc(Type t, object ctx, string name, Func<int> g)
        {
            var p = t.GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(ctx, g);
        }

        private static void SetFunc(Type t, object ctx, string name, Func<float> g)
        {
            var p = t.GetProperty(name);
            if (p != null && p.CanWrite) p.SetValue(ctx, g);
        }

        #endregion

        /// <summary>纯 C# Cell 组单元：聚合一组子 Cell，共享 position 与可见性。</summary>
        private sealed class CellGroupUnit
        {
            public readonly List<IFancyCell> Cells;
            public int GroupIndex = -1;
            public bool IsVisible { get; private set; }

            public CellGroupUnit(List<IFancyCell> cells) => Cells = cells;

            public void SetContextAll(object context)
            {
                foreach (var c in Cells) c.SetContext(context);
            }

            public void InitializeAll()
            {
                foreach (var c in Cells) c.Initialize();
            }

            public void SetVisibleAll(bool visible)
            {
                IsVisible = visible;
                if (!visible)
                {
                    // 真正隐藏子 Cell 的 GameObject：否则 datacount 减少或滚动到两端时，
                    // 超出范围的组仅设标记而不隐藏，残留旧内容导致重影。
                    foreach (var c in Cells) c.SetVisible(false);
                }
                // visible=true 时，子 Cell 可见性由 UpdateGroups 按 item 范围（cell.SetVisible）控制。
            }

            public void UpdatePositionAll(float position)
            {
                foreach (var c in Cells)
                {
                    if (c.IsVisible) c.UpdatePosition(position);
                }
            }

            public void DestroyAll()
            {
                foreach (var c in Cells)
                {
                    if (c is UIWidget w && w.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(w.gameObject);
                    }
                }
            }
        }
    }
}
