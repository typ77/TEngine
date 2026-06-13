using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 纯 C# 的 ScrollView 控制器（组合模式，不继承 UIWidget）。
    /// <para>
    /// 非泛型版本，内部使用 <see cref="IList"/> 存储数据、<c>object</c> 存储 Context、
    /// <see cref="IFancyCell"/>（非泛型接口）管理 Cell 池。
    /// 类型安全由 Cell 端的显式接口实现保证
    /// （<see cref="IFancyCell.UpdateContent(object)"/> 在 Cell 内部转型为具体类型）。
    /// </para>
    /// <para>
    /// 使用方式（在 UIWindow 中）：
    /// <code>
    /// _scroller = new FancyScrollerController();
    /// _scrollerController.Initialize(this, m_scroller, () => new MyCell());
    /// await _scroller.LoadCellPrefabAsync("MyCellPrefab");
    /// _scroller.UpdateData(items);
    /// </code>
    /// </para>
    /// <para>
    /// Cell 创建时以 owner（UIWindow）为父 UIBase，直接挂在 Window 的 ListChild 下，
    /// 无需中间 ScrollViewWidget 层。Window 关闭时调用 <see cref="Dispose"/> 清理 Cell 池。
    /// </para>
    /// </summary>
    public class FancyScrollerController : IDisposable
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
        private object _sharedContext;
        private IList _itemsSource = new List<object>();
        private float _currentPosition;
        private bool _initialized;

        // ── 反射缓存 ─────────────────────────────────────────────
        private static readonly Dictionary<Type, MethodInfo> _initializeWidgetCache = new();

        // ── 事件 ─────────────────────────────────────────────────

        /// <summary>Cell 被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellItemClicked;

        /// <summary>Cell 内按钮被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellButtonClicked;

        // ── 配置属性（取代旧版虚方法）────────────────────────────

        /// <summary>Cell 间隔（0.01 ~ 1），默认 0.2。</summary>
        public float CellInterval { get; set; } = 0.2f;

        /// <summary>滚动偏移（0 ~ 1），默认 0.5。</summary>
        public float ScrollOffset { get; set; } = 0.5f;

        /// <summary>是否启用循环滚动，默认 false。</summary>
        public bool Loop { get; set; } = false;

        /// <summary>当前滚动位置（Scroller 报告的原始值）。</summary>
        public float CurrentPosition => _currentPosition;

        /// <summary>与所有 Cell 共享的 Context 对象。</summary>
        public object SharedContext => _sharedContext;

        #region 生命周期

        /// <summary>
        /// 初始化控制器，注入所有依赖。
        /// <para>在 UIWindow.ScriptGenerator() 末尾调用。</para>
        /// </summary>
        /// <param name="owner">拥有此控制器的 UIWindow（或其他 UIBase），作为 Cell 的父。</param>
        /// <param name="scroller">Scroller 组件引用（通常由 UIBindComponent 自动生成）。</param>
        /// <param name="cellContainer">Cell 实例的父容器 Transform。</param>
        /// <param name="cellFactory">Cell 工厂委托，返回 <see cref="IFancyCell"/> 实例。</param>
        /// <param name="context">共享上下文对象（可选）。</param>
        public void Initialize(
            UIBase owner,
            Scroller scroller,
            Func<IFancyCell> cellFactory = null,
            object context = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _scroller = scroller ?? throw new ArgumentNullException(nameof(scroller));
            // cellContainer 由 Scroller 的子节点提供（约定命名含 "CellContainer"），无需外部传入路径。
            _cellContainer = FindCellContainer(scroller);
            if (_cellContainer == null)
            {
                throw new InvalidOperationException(
                    $"CellContainer not found under Scroller '{scroller.name}'. " +
                    "Expected a child node whose name contains 'CellContainer' (e.g. m_tfCellContainer / m_cellContainer).");
            }
            // cellFactory 允许为 null：ScriptGenerator 在 ScriptGenerator() 阶段生成 Initialize 时还不持有 Cell 工厂，
            // 由使用方在 OnCreate() 中调用 SetCellFactory() 延后注入。实际创建 Cell 时做空检查。
            _cellFactory = cellFactory;
            _sharedContext = context;

            _scroller.OnValueChanged(OnScrollerPositionChanged);
        }

        /// <summary>
        /// 从 Scroller 的子节点中查找 CellContainer（命名含 "CellContainer" 的直接子节点）。
        /// <para>cellContainer 与 Scroller 配对，由 Scroller 提供，调用方无需关心其节点名或路径。</para>
        /// </summary>
        private static Transform FindCellContainer(Scroller scroller)
        {
            var t = scroller.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child.name.IndexOf("CellContainer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>
        /// 更新 Cell 工厂委托。
        /// </summary>
        /// <param name="cellFactory">Cell 工厂委托，返回 <see cref="IFancyCell"/> 实例。</param>
        public void SetCellFactory(Func<IFancyCell> cellFactory)
        {
            _cellFactory = cellFactory ?? throw new ArgumentNullException(nameof(cellFactory));
        }

        /// <summary>
        /// 异步加载 Cell Prefab 并缓存。
        /// <para>多次调用会释放前一次的 AssetHandle。</para>
        /// </summary>
        /// <param name="cellAssetLocation">YooAsset 资源定位路径。</param>
        /// <param name="progress">加载进度回调（可选）。</param>
        /// <param name="ct">取消令牌。</param>
        public async UniTask LoadCellPrefabAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
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
                throw new InvalidOperationException(
                    $"Cell prefab load failed (asset is null): {cellAssetLocation}");
            }
        }

        /// <summary>
        /// 释放所有资源：销毁 Cell 池 GameObject、释放 AssetHandle、清除事件。
        /// <para>在 UIWindow.OnDestroy() 中调用。</para>
        /// </summary>
        public void Dispose()
        {
            // 销毁 Cell 池
            for (var i = _cellPool.Count - 1; i >= 0; i--)
            {
                var cell = _cellPool[i];
                if (cell is UIWidget widget && widget.gameObject != null)
                {
                    UnityEngine.Object.Destroy(widget.gameObject);
                }
            }

            _cellPool.Clear();

            // 释放资源
            ReleaseCellPrefabHandle();

            // 清除事件
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

        /// <summary>
        /// 更新列表数据并刷新显示。
        /// <para>接受任意 IList（List&lt;T&gt;、T[] 等均可），类型安全由 Cell 端保证。</para>
        /// </summary>
        /// <param name="items">数据列表。</param>
        public void UpdateData(IList items)
        {
            _itemsSource = items ?? new List<object>();
            if (_scroller != null) _scroller.SetTotalCount(_itemsSource.Count);
            Refresh();
        }

        /// <summary>
        /// 滚动到指定索引位置。
        /// </summary>
        public void ScrollTo(int index, float duration = 0.3f)
        {
            _scroller?.ScrollTo(index, duration);
        }

        /// <summary>
        /// 立即跳转到指定索引位置（无动画）。
        /// </summary>
        public void JumpTo(int index)
        {
            _scroller?.JumpTo(index);
        }

        /// <summary>
        /// 强制刷新所有可见 Cell 的内容。
        /// </summary>
        public void Refresh()
        {
            UpdatePosition(_currentPosition, true);
        }

        /// <summary>
        /// 重新布局（修改 padding/spacing 后调用）。
        /// </summary>
        public void Relayout()
        {
            UpdatePosition(_currentPosition, false);
        }

        /// <summary>
        /// 获取当前居中显示的 Cell 索引。
        /// <para>基于 Scroller 的原始位置值四舍五入得到。</para>
        /// </summary>
        public int GetCenterIndex()
        {
            return Mathf.RoundToInt(_currentPosition);
        }

        #endregion

        #region Cell 创建

        /// <summary>
        /// 创建一个 Cell 实例：Instantiate Prefab → Cell Widget 初始化（反射）→ 订阅事件。
        /// <para>
        /// Cell 以 _owner（UIWindow）为父 UIBase，直接挂在 Window 的 ListChild 下。
        /// InitializeWidget 是 internal 方法，通过反射调用。
        /// </para>
        /// </summary>
        private IFancyCell CreateCell()
        {
            if (_cellFactory == null)
            {
                throw new InvalidOperationException(
                    "Cell factory not set. Call SetCellFactory() before binding data (e.g. in OnCreate).");
            }

            var go = UnityEngine.Object.Instantiate(_cellPrefab, _cellContainer);
            var cell = _cellFactory();

            // 通过反射调用 internal InitializeWidget(UIBase, GameObject, bool)
            var cellType = cell.GetType();
            if (!_initializeWidgetCache.TryGetValue(cellType, out var method))
            {
                method = cellType.GetMethod(
                    "InitializeWidget",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(UIBase), typeof(GameObject), typeof(bool) },
                    null);

                if (method == null)
                {
                    // 回退：尝试两参数版本
                    method = cellType.GetMethod(
                        "InitializeWidget",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(UIBase), typeof(GameObject) },
                        null);
                }

                if (method == null)
                {
                    throw new InvalidOperationException(
                        $"Cell type {cellType.Name} does not have an InitializeWidget method.");
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

            // 通过反射订阅事件
            var onItemClickedEvent = cellType.GetEvent("OnItemClicked",
                BindingFlags.Instance | BindingFlags.Public);
            var onButtonClickedEvent = cellType.GetEvent("OnButtonClicked",
                BindingFlags.Instance | BindingFlags.Public);

            if (onItemClickedEvent != null)
            {
                var itemHandler = (Action<int>)HandleCellClicked;
                onItemClickedEvent.AddEventHandler(cell, itemHandler);
            }

            if (onButtonClickedEvent != null)
            {
                var buttonHandler = (Action<int>)HandleCellButtonClicked;
                onButtonClickedEvent.AddEventHandler(cell, buttonHandler);
            }

            return cell;
        }

        private void HandleCellClicked(int index)
        {
            OnCellItemClicked?.Invoke(index);
        }

        private void HandleCellButtonClicked(int index)
        {
            OnCellButtonClicked?.Invoke(index);
        }

        #endregion

        #region Cell 池管理

        private void OnScrollerPositionChanged(float position)
        {
            UpdatePosition(position, false);
        }

        private void UpdatePosition(float position, bool forceRefresh)
        {
            if (_cellPrefab == null || _cellContainer == null) return;

            if (!_initialized)
            {
                _initialized = true;
            }

            _currentPosition = position;

            var interval = CellInterval;
            var offset = ScrollOffset;
            var p = position - offset / interval;
            var firstIndex = Mathf.CeilToInt(p);
            var firstPosition = (Mathf.Ceil(p) - p) * interval;

            if (firstPosition + _cellPool.Count * interval < 1f)
            {
                ResizePool(firstPosition);
            }

            UpdateCells(firstPosition, firstIndex, forceRefresh);
        }

        private void ResizePool(float firstPosition)
        {
            var interval = CellInterval;
            var addCount = Mathf.CeilToInt((1f - firstPosition) / interval) - _cellPool.Count;
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
            var interval = CellInterval;
            var useLoop = Loop;
            var items = _itemsSource;
            var itemCount = items.Count;

            for (var i = 0; i < _cellPool.Count; i++)
            {
                var index = firstIndex + i;
                var position = firstPosition + i * interval;
                var cell = _cellPool[CircularIndex(index, _cellPool.Count)];

                if (useLoop)
                {
                    index = CircularIndex(index, itemCount);
                }

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

        private static int CircularIndex(int i, int size)
        {
            return size < 1 ? 0 : i < 0 ? size - 1 + (i + 1) % size : i % size;
        }

        #endregion
    }
}
