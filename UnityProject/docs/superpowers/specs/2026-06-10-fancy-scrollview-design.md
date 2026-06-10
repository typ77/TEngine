# FancyScrollView TEngine 集成 — 细化设计 v2

> 基于 brainstorming 会话细化。涵盖：组合模式、TContext 保留、虚方法 CellFactory、
> 混合轻量 GridView 初始化、EasingCore+UniTask 动画、Inspector 配置组件、渐进式加载、事件传播。

## 设计决策汇总

| # | 决策 | 选择 | 原因 |
|---|------|------|------|
| 1 | TContext 泛型参数 | 保留 | 与 FancyScrollView 原始架构一致，Context 在 ScrollView 和所有 Cell 间共享状态（ScrollDirection、CalculateScrollSize、GetGroupCount 等） |
| 2 | Cell 创建管线 | 虚方法 CreateCell | 在创建边界对接 TEngine Widget 生命周期，不触碰核心布局逻辑 |
| 3 | Cell 池 | 保留 IList | FancyScrollView 的 pool 是位置索引循环缓冲区，不是传统 Acquire/Release 对象池；通过虚方法在创建/销毁边界对接 TEngine |
| 4 | GridView 子 Cell | 混合轻量初始化 | 支持 ScriptGenerator + BindMemberProperty + RegisterEvent，但不走完整 CreateImp（跳过 Inject、Parent.ListChild、OnRefresh） |
| 5 | 入场动画 | EasingCore + UniTask | 零额外依赖，复用 FancyScrollView 自带的 EasingCore 库 |
| 6 | FancyScrollRect 子类化 | Inspector 配置组件 | 业务用户通过 FancyScrollRectProxy 在 Inspector 配置参数，不需要写 abstract 子类 |
| 7 | CellPrefab 加载 | YooAsset 异步 + 缓存 | 首次 LoadAssetAsync 后缓存 Prefab 引用，后续通过 Instantiate 克隆 |
| 8 | 事件传播 | 回调 + 中转 | CellWidget 自治内部冒泡，ScrollViewWidget 汇总，UIWindow 按需转 GameEvent |

---

## 1. 架构总览

```
┌─────────────────────────────────────────────────────────────┐
│  HotFix 程序集内                                            │
│                                                             │
│  GameLogic.UI.FancyScrollView/                              │
│  ├── Core/                    ← 移植的原始核心（微改）      │
│  │   ├── FancyScrollView<T,C>.cs   （pool 改为 IFancyCell + CreateCell 虚方法）│
│  │   ├── FancyCell<T,C>.cs          （新增 : IFancyCell 接口）                │
│  │   └── NullContext.cs                                                       │
│  ├── Scroller/                ← 移植的滚动控制器            │
│  │   ├── Scroller.cs                                                          │
│  │   ├── EasingCore.cs       ← 动画曲线库（也用于入场动画）  │
│  │   └── Enums.cs                                                             │
│  ├── ScrollRect/              ← 移植的 ScrollRect 变体      │
│  │   ├── FancyScrollRect<T,C>.cs（CellSize 改为 Inspector 可配置）            │
│  │   ├── FancyScrollRectCell.cs                                               │
│  │   └── FancyScrollRectContext.cs                                            │
│  ├── GridView/                ← 移植的 GridView 变体        │
│  │   ├── FancyGridView<T,C>.cs （Setup 虚方法化）                             │
│  │   ├── FancyCellGroup.cs     （InstantiateCells 可 override）               │
│  │   └── FancyGridViewContext.cs                                              │
│  │                                                                             │
│  └── Adapters/                ← TEngine 适配层（新增）      │
│      ├── IFancyCell.cs                  ← Cell 接口（含 TContext）            │
│      ├── FancyCellWidget.cs             ← Cell Widget 基类                    │
│      ├── FancyScrollRectProxy.cs        ← Inspector 配置组件                  │
│      ├── FancyScrollViewWidget.cs       ← 基础容器 Widget（Carousel 等）     │
│      ├── FancyScrollRectWidget.cs       ← ScrollRect 容器 Widget              │
│      ├── FancyGridViewWidget.cs         ← GridView 容器 Widget                │
│      ├── CellGroupWrapper.cs            ← GridView CellGroup 适配             │
│      └── ContentLoadScheduler.cs        ← 渐进式加载调度                      │
│                                                             │
│  业务使用示例：                                              │
│  GameLogic/Module/UIModule/BagUI/                           │
│  ├── BagModel.cs          ← 数据模型                        │
│  ├── BagService.cs        ← 业务逻辑                        │
│  ├── BagDataContext.cs    ← Window 级 DataContext           │
│  ├── BagUI.cs             ← UIWindow                        │
│  ├── BagItemCellWidget.cs ← Cell Widget（继承 FancyCellWidget）│
│  └── BagItemCellWidget.prefab                              │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. FancyScrollView 源码改造清单

### 2.1 IFancyCell 接口（新增文件）

```csharp
// Adapters/IFancyCell.cs
namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// Cell 接口。保留 TContext 泛型参数，与 FancyScrollView 原始架构一致。
    /// FancyCell 改为实现此接口，FancyScrollView 的 pool 类型改为 IFancyCell。
    /// </summary>
    public interface IFancyCell<TItemData, TContext> where TContext : class, new()
    {
        int Index { get; set; }
        bool IsVisible { get; }
        void SetVisible(bool visible);
        void UpdateContent(TItemData itemData);
        void UpdatePosition(float position);
        void SetContext(TContext context);
        void Initialize();
    }
}
```

### 2.2 FancyCell.cs 改造

```csharp
// 原始：abstract class FancyCell<TItemData, TContext> : MonoBehaviour
// 改造后：新增接口实现（零行为变化）
public abstract class FancyCell<TItemData, TContext> : MonoBehaviour, IFancyCell<TItemData, TContext>
    where TContext : class, new()
{
    // ... 原有代码不变 ...
}
```

**改动量**：+1 行接口声明。

### 2.3 FancyScrollView.cs 改造

```csharp
// 改造 1：pool 类型改为接口
// 原：readonly IList<FancyCell<TItemData, TContext>> pool = new List<FancyCell<TItemData, TContext>>();
readonly IList<IFancyCell<TItemData, TContext>> pool = new List<IFancyCell<TItemData, TContext>>();

// 改造 2：ResizePool 改为调用虚方法
void ResizePool(float firstPosition)
{
    var addCount = Mathf.CeilToInt((1f - firstPosition) / cellInterval) - pool.Count;
    for (var i = 0; i < addCount; i++)
    {
        var cell = CreateCell(CellPrefab, cellContainer);
        cell.SetContext(Context);
        cell.Initialize();
        cell.SetVisible(false);
        pool.Add(cell);
    }
}

// 改造 3：新增虚方法（默认保持原始 Instantiate + GetComponent 行为）
protected virtual IFancyCell<TItemData, TContext> CreateCell(
    GameObject prefab, Transform container)
{
    var go = Object.Instantiate(prefab, container);
    var cell = go.GetComponent<IFancyCell<TItemData, TContext>>();
    if (cell == null)
    {
        throw new MissingComponentException(string.Format(
            "IFancyCell<{0}, {1}> component not found in {2}.",
            typeof(TItemData).FullName, typeof(TContext).FullName, prefab.name));
    }
    return cell;
}

// 改造 4：CellPrefab 从 abstract 改为 virtual + 可外部设置
// 原：protected abstract GameObject CellPrefab { get; }
[SerializeField] protected GameObject cellPrefab;
protected virtual GameObject CellPrefab => cellPrefab;
```

**改动量**：pool 类型 + ResizePool + CreateCell 虚方法 + CellPrefab virtual，约 20 行。

### 2.4 FancyScrollRect.cs 改造

```csharp
// 改造：CellSize 从 abstract 改为 Inspector 可配置
// 原：protected abstract float CellSize { get; }
[SerializeField] protected float cellSize = 100f;
protected virtual float CellSize => cellSize;
```

**改动量**：约 5 行。其他逻辑不变。

### 2.5 FancyGridView.cs 改造

```csharp
// 改造：Setup 方法改为 virtual，让 TEngine 适配层 override
protected virtual void Setup<TGroup>(FancyCell<TItemData, TContext> cellTemplate)
    where TGroup : FancyCell<TItemData[], TContext>
{
    // ... 原有逻辑不变 ...
}
```

**改动量**：+1 个 `virtual` 关键字。

### 2.6 不改动的文件

| 文件 | 原因 |
|------|------|
| FancyCellGroup.cs | InstantiateCells 已是 virtual，可 override |
| Scroller.cs | 独立滚动控制器，无需改动 |
| EasingCore.cs | 独立缓动库，入场动画也使用 |
| ScrollDirection 等枚举 | 无需改动 |
| FancyScrollRectCell.cs | 继承链不变 |
| FancyGridViewCell.cs | 继承链不变 |
| IFancyScrollRectContext.cs | 接口不变 |
| IFancyGridViewContext.cs | 接口不变 |

---

## 3. 适配层核心类

### 3.1 FancyCellWidget\<TItemData, TContext\> 基类

```csharp
namespace GameLogic.UI.FancyScrollView
{
    public enum ContentLoadState
    {
        Skeleton,   // 空壳/骨架（默认状态）
        Loading,    // 加载中
        Loaded,     // 已加载
        Error       // 加载失败
    }

    /// <summary>
    /// Cell Widget 基类。继承 UIWidget 走 TEngine 生命周期，实现 IFancyCell 对接 FancyScrollView。
    /// 保留 TContext 泛型参数，与 FancyScrollView 原始架构一致。
    /// </summary>
    public abstract class FancyCellWidget<TItemData, TContext> : UIWidget, IFancyCell<TItemData, TContext>
        where TContext : class, new()
    {
        // ── IFancyCell 实现 ──
        public int Index { get; set; } = -1;
        public bool IsVisible => gameObject != null && gameObject.activeSelf;

        public virtual void SetVisible(bool visible)
        {
            if (gameObject.activeSelf == visible) return;
            gameObject.SetActive(visible);
            if (visible) OnCellVisible();
            else OnCellInvisible();
        }

        public void UpdateContent(TItemData data) => BindData(data);
        public abstract void UpdatePosition(float normalizedPosition);
        public virtual void SetContext(TContext context) => Context = context;
        public virtual void Initialize() { }

        protected TContext Context { get; private set; }

        // ── MVE 中间属性管理 ──
        private readonly List<IDisposable> _cellProperties = new();

        protected BindableProperty<T> CreateProperty<T>(T initial = default)
        {
            var prop = new BindableProperty<T>(initial);
            _cellProperties.Add(prop);
            return prop;
        }

        // ── 轻量初始化（供 ScrollViewWidget.CreateCell 调用，不走 CreateImp）──
        internal void InitializeWidget(UIBase parentUI, GameObject widgetRoot)
        {
            CreateBase(widgetRoot, true);
            _parent = parentUI;
            Parent.ListChild.Add(this);
            ScriptGenerator();
            BindMemberProperty();
            RegisterEvent();
            OnCreate();
        }

        // ── 数据绑定（子类 override 的主入口）──
        protected abstract void BindData(TItemData data);

        // ── 动画（ScrollView 基础变体 override）──
        protected virtual void OnCellPosition(float normalizedPosition) { }

        // ── 可见性回调（可选 override）──
        protected virtual void OnCellVisible() { }
        protected virtual void OnCellInvisible() { }

        // ── 渐进式加载 ──
        public ContentLoadState LoadState => _loadState;
        private ContentLoadState _loadState = ContentLoadState.Skeleton;
        private CancellationTokenSource _loadCts;

        public async UniTask LoadContentAsync(
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (_loadState == ContentLoadState.Loaded) return;
            if (ContentAssetLocation == null) return;

            _loadState = ContentLoadState.Loading;
            OnContentLoading(0f);

            try
            {
                var handle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(ContentAssetLocation);
                if (progress != null)
                {
                    while (!handle.IsDone)
                    {
                        progress.Report(handle.Progress);
                        OnContentLoading(handle.Progress);
                        await UniTask.Yield(ct);
                    }
                }
                else
                {
                    await handle.Task;
                }
                ct.ThrowIfCancellationRequested();

                var prefab = handle.AssetObject as GameObject;
                if (prefab != null)
                {
                    // 创建子 Widget 并绑定到占位容器
                    // BindContentWidget(CreateSubWidget(prefab, _contentPlaceholder));
                }

                _loadState = ContentLoadState.Loaded;
                OnContentLoaded();
            }
            catch (OperationCanceledException)
            {
                _loadState = ContentLoadState.Skeleton;
            }
            catch (Exception e)
            {
                _loadState = ContentLoadState.Error;
                OnContentError(e.Message);
            }
        }

        public void CancelLoad()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
        }

        public void UnloadContent()
        {
            CancelLoad();
            _loadState = ContentLoadState.Skeleton;
            OnContentUnloaded();
        }

        // ── 子类可选 override ──
        protected virtual string ContentAssetLocation => null;
        protected virtual void BindContentWidget(UIWidget widget) { }
        protected virtual void OnContentLoading(float progress) { }
        protected virtual void OnContentLoaded() { }
        protected virtual void OnContentError(string error) { }
        protected virtual void OnContentUnloaded() { }

        // ── 事件回调 ──
        public event Action<int> OnItemClicked;
        public event Action<int> OnButtonClicked;

        protected void NotifyItemClicked() => OnItemClicked?.Invoke(Index);
        protected void NotifyButtonClicked() => OnButtonClicked?.Invoke(Index);

        // ── 入场动画（EasingCore + UniTask）──
        public async UniTask PlayFadeInAsync(float duration = 0.3f, CancellationToken ct = default)
        {
            var canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            var rect = rectTransform;
            var startY = rect.anchoredPosition.y + 30f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                float eased = EasingCore.Easing.Ease(t, Ease.OutCubic);

                canvasGroup.alpha = eased;
                rect.anchoredPosition = new Vector2(
                    rect.anchoredPosition.x,
                    Mathf.LerpUnclamped(startY, 0f, eased));
            }

            canvasGroup.alpha = 1f;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, 0f);
        }

        public async UniTask PlayScaleInAsync(float duration = 0.25f, CancellationToken ct = default)
        {
            var rect = rectTransform;
            var startScale = 0.8f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / duration);
                float eased = EasingCore.Easing.Ease(t, Ease.OutBack);

                float s = Mathf.LerpUnclamped(startScale, 1f, eased);
                rect.localScale = new Vector3(s, s, 1f);
            }

            rect.localScale = Vector3.one;
        }

        // ── 生命周期 ──
        protected override void OnDestroy()
        {
            CancelLoad();
            foreach (var prop in _cellProperties)
                prop.Dispose();
            _cellProperties.Clear();

            OnItemClicked = null;
            OnButtonClicked = null;

            base.OnDestroy();
        }
    }
}
```

### 3.2 FancyScrollRectProxy（Inspector 配置组件）

```csharp
namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 非泛型 Inspector 配置组件。
    /// 业务用户在 Prefab 上挂此组件配置 cellSize、spacing 等参数，
    /// 不需要为每个列表写 FancyScrollRect 子类。
    /// </summary>
    [DisallowMultipleComponent]
    public class FancyScrollRectProxy : MonoBehaviour
    {
        [Header("Cell Settings")]
        [SerializeField] internal float cellSize = 120f;
        [SerializeField] internal float spacing = 10f;
        [SerializeField] internal float paddingHead = 0f;
        [SerializeField] internal float paddingTail = 0f;
        [SerializeField] internal float reuseCellMarginCount = 0f;

        [Header("Scroll Settings")]
        [SerializeField] internal ScrollDirection scrollDirection = ScrollDirection.Vertical;

        /// <summary>运行时由 ScrollViewWidget 注入已加载的 Cell Prefab</summary>
        internal GameObject CellPrefab { get; set; }
    }
}
```

### 3.3 FancyScrollRectWidget\<TItemData, TContext\>（容器 Widget）

```csharp
namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// ScrollRect 容器 Widget。组合持有 FancyScrollRect + Scroller 组件引用，
    /// 管理 Cell 创建管线、事件中转、渐进式加载调度。
    /// </summary>
    public abstract class FancyScrollRectWidget<TItemData, TContext> : UIWidget
        where TContext : class, IFancyScrollRectContext, new()
    {
        // ── 组件引用（ScriptGenerator 绑定）──
        protected FancyScrollRectProxy _proxy;
        protected Scroller _scroller;
        private GameObject _cellPrefab;

        // 内部具体 FancyScrollRect 子类（桥接 Proxy 配置和 FancyScrollRect 抽象方法）
        // ScrollViewWidget 在 OnCreate 中动态创建此实例并注入 Proxy 参数
        private InternalScrollRect<TItemData, TContext> _scrollRect;

        protected override void ScriptGenerator()
        {
            _proxy = FindChildComponent<FancyScrollRectProxy>("m_fscroll_List");
            _scroller = FindChildComponent<Scroller>("m_scroller_Scroll");
        }

        protected override void OnCreate()
        {
            // 从 Proxy 配置创建内部 FancyScrollRect 实例
            _scrollRect = _proxy.gameObject.AddComponent<InternalScrollRect<TItemData, TContext>>();
            _scrollRect.Configure(_proxy);
        }

        // ── 初始化：异步加载 Cell Prefab ──
        public async UniTask InitializeAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            var handle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(cellAssetLocation);
            if (progress != null)
            {
                while (!handle.IsDone)
                {
                    progress.Report(handle.Progress);
                    await UniTask.Yield(ct);
                }
            }
            else
            {
                await handle.Task;
            }
            ct.ThrowIfCancellationRequested();
            _cellPrefab = handle.AssetObject as GameObject;
            _proxy.CellPrefab = _cellPrefab;
        }

        // ── 数据接口 ──
        public void UpdateContents(IList<TItemData> items)
            => _scrollRect.UpdateContents(items);
        public void ScrollTo(int index, float duration, float alignment = 0.5f)
            => _scrollRect.ScrollTo(index, duration, alignment);
        public void JumpTo(int index, float alignment = 0.5f)
            => _scrollRect.JumpTo(index, alignment);
        public int DataCount => _scrollRect.ItemsSource?.Count ?? 0;

        // ── 渐进式加载配置 ──
        public ContentLoadMode ContentLoadMode { get; set; } = ContentLoadMode.AutoTrigger;
        public float StaggerDelay { get; set; } = 0.08f;
        public int PreloadCount { get; set; } = 2;

        // ── 事件中转（汇总 Cell 回调）──
        public event Action<int> OnCellClicked;
        public event Action<int> OnCellButtonClicked;

        // ── 内部桥接类：将 Proxy Inspector 配置注入 FancyScrollRect ──
        private class InternalScrollRect<D, C> : FancyScrollRect<D, C>
            where C : class, IFancyScrollRectContext, new()
        {
            private FancyScrollRectProxy _proxyRef;

            public void Configure(FancyScrollRectProxy proxy)
            {
                _proxyRef = proxy;
                // 将 Proxy Inspector 参数同步到 FancyScrollRect 字段
                // cellSize = _proxyRef.cellSize; spacing = _proxyRef.spacing; ...
            }

            protected override float CellSize => _proxyRef?.cellSize ?? 100f;
            protected override GameObject CellPrefab => _proxyRef?.CellPrefab;
        }
    }
}
```

### 3.4 ContentLoadScheduler（渐进式加载调度器）

```csharp
namespace GameLogic.UI.FancyScrollView
{
    public enum ContentLoadMode
    {
        Cascade,      // 首次级联
        AutoTrigger,  // 滚动自动触发
        Manual        // 外部手动触发
    }

    /// <summary>
    /// 渐进式加载调度器。管理三种加载策略：
    /// - Cascade：首次初始化时按 staggerDelay 依次触发可见 Cell 加载
    /// - AutoTrigger：Cell 变为可见时自动触发加载
    /// - Manual：外部显式调用
    /// </summary>
    /// <summary>
    /// 渐进式加载调度器。泛型化以匹配具体的 TItemData/TContext。
    /// 由 ScrollViewWidget 在 OnCreate 时创建，持有同一泛型参数的 CellWidget 列表。
    /// </summary>
    public class ContentLoadScheduler<TItemData, TContext> : IDisposable
        where TContext : class, new()
    {
        private readonly List<FancyCellWidget<TItemData, TContext>> _visibleCells = new();
        private CancellationTokenSource _schedulerCts;
        private bool _isInitialized;

        public ContentLoadMode Mode { get; set; } = ContentLoadMode.AutoTrigger;
        public float StaggerDelay { get; set; } = 0.08f;
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Cascade 模式：首次级联加载。
        /// 按顺序间隔 staggerDelay 启动每个可见 Cell 的 LoadContentAsync。
        /// 仅触发一次（_isInitialized 标记）。
        /// </summary>
        public async UniTask TriggerCascadeAsync(CancellationToken ct = default)
        {
            if (_isInitialized) return;
            _isInitialized = true;

            for (int i = 0; i < _visibleCells.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var cell = _visibleCells[i];
                if (cell.LoadState == ContentLoadState.Skeleton
                    && cell.ContentAssetLocation != null)
                {
                    // 并行启动加载（不 await），间隔 staggerDelay
                    cell.LoadContentAsync(ct: ct).Forget();
                }

                if (i < _visibleCells.Count - 1)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(StaggerDelay),
                        cancellationToken: ct);
                }
            }
        }

        /// <summary>
        /// AutoTrigger 模式：Cell 可见时自动触发加载。
        /// </summary>
        public void OnCellBecameVisible(FancyCellWidget<TItemData, TContext> cell)
        {
            if (!_visibleCells.Contains(cell))
                _visibleCells.Add(cell);

            if (Mode != ContentLoadMode.AutoTrigger) return;
            if (cell.LoadState == ContentLoadState.Skeleton
                && cell.ContentAssetLocation != null)
            {
                cell.LoadContentAsync(ct: _schedulerCts.Token).Forget();
            }
        }

        public void OnCellBecameInvisible(FancyCellWidget<TItemData, TContext> cell)
        {
            _visibleCells.Remove(cell);
            // 不取消已发起的加载，让其自然完成
        }

        public void Dispose()
        {
            _schedulerCts?.Cancel();
            _schedulerCts?.Dispose();
            _schedulerCts = null;
            _visibleCells.Clear();
        }
    }
}
```

---

## 4. GridView 适配

### 4.1 CellGroupWrapper

```csharp
namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// GridView CellGroup 适配层。
    /// Override InstantiateCells 为子 Cell 走轻量 TEngine 初始化，
    /// 并转发子 Cell 事件到外部。
    /// </summary>
    public class CellGroupWrapper<TItemData, TContext> : FancyCellGroup<TItemData, TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        private UIBase _parentUI;

        public void SetParent(UIBase parentUI) => _parentUI = parentUI;

        /// <summary>
        /// 子 Cell 走轻量初始化：ScriptGenerator + BindMemberProperty + RegisterEvent + OnCreate。
        /// 跳过 Inject、Parent.ListChild 管理、OnRefresh。
        /// </summary>
        protected override FancyCell<TItemData, TContext>[] InstantiateCells()
        {
            int count = Context.GetGroupCount();
            var cells = new FancyCell<TItemData, TContext>[count];

            for (int i = 0; i < count; i++)
            {
                var go = Object.Instantiate(Context.CellTemplate, transform);
                var cellWidget = go.GetComponent<FancyCellWidget<TItemData, TContext>>();

                if (cellWidget != null)
                {
                    cellWidget.InitializeWidget(_parentUI, go);
                }

                cells[i] = cellWidget;
            }

            return cells;
        }

        public override void Initialize()
        {
            base.Initialize();

            // 订阅所有子 Cell 的回调，转发到外部
            foreach (var cell in Cells)
            {
                if (cell is FancyCellWidget<TItemData, TContext> widget)
                {
                    widget.OnItemClicked += index =>
                        CellItemClicked?.Invoke(index);
                    widget.OnButtonClicked += index =>
                        CellButtonClicked?.Invoke(index);
                }
            }
        }

        /// <summary>子 Cell 整行点击事件（转发）</summary>
        public event Action<int> CellItemClicked;
        /// <summary>子 Cell 操作按钮事件（转发）</summary>
        public event Action<int> CellButtonClicked;
    }
}
```

### 4.2 层级关系

```
FancyGridViewWidget (UIWidget)
  │
  │  FancyGridView 内部 ResizePool 创建 CellGroup
  │  通过虚方法 CreateCell → CellGroupWrapper
  │
  └── CellGroupWrapper (MonoBehaviour, 非 Widget)
        │
        │  InstantiateCells() 创建子 Cell（轻量初始化）
        │
        ├── GridCellWidget[0]  ← InitializeWidget → ScriptGenerator → Bind → RegisterEvent → OnCreate
        ├── GridCellWidget[1]  ← 同上
        └── GridCellWidget[2]  ← 同上
```

---

## 5. 事件传播

```
┌──────────────────────────────────────────────────────────────┐
│  CellWidget 层（通用、可复用）                                │
│                                                              │
│  // CellWidget 不知道业务逻辑                                │
│  // 只暴露"发生了什么交互"的回调                             │
│  public event Action<int> OnItemClicked;      // 整行点击    │
│  public event Action<int> OnButtonClicked;    // 操作按钮    │
│                                                              │
│  // 内部多层嵌套的冒泡是 CellWidget 的自治职责              │
│  RegisterEvent():                                            │
│    _actionPanel.OnUse +=                                     │
│      () => NotifyButtonClicked();  // → OnButtonClicked?.Invoke(Index) │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  ScrollViewWidget 层（事件中转）                              │
│                                                              │
│  // 汇总所有 Cell 的回调，向上提供统一事件                   │
│  public event Action<int> OnCellClicked;                     │
│  public event Action<int> OnCellButtonClicked;               │
│                                                              │
│  // Cell 创建时订阅                                          │
│  cell.OnItemClicked += i => OnCellClicked?.Invoke(i);        │
│  cell.OnButtonClicked += i => OnCellButtonClicked?.Invoke(i);│
│                                                              │
│  // GridView 额外步骤：CellGroupWrapper 转发子 Cell 回调     │
│  cellGroup.CellItemClicked += i => OnCellClicked?.Invoke(i); │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  UIWindow 层（业务感知）                                     │
│                                                              │
│  // 方式 1：直接调 Service（简单场景）                       │
│  _scrollView.OnCellButtonClicked += index =>                 │
│    BagService.UseItem(index);                                │
│                                                              │
│  // 方式 2：发 GameEvent（多订阅者场景）                     │
│  _scrollView.OnCellButtonClicked += index =>                 │
│    GameEvent.Send<int>(IBagEvent_Event.OnItemUse, index);   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

---

## 6. 数据流（MVE 四层 CellWidget）

```
Model 变更
    │
    ▼
DataContext.MapList / MapProperty
    │
    ▼
Window.OnRefresh()
    │ var dc = GetDataContext<BagDataContext>();
    │ _scrollView.UpdateContents(dc.DisplayItems);
    ▼
FancyScrollView.UpdateContents(items)
    │
    │ 只遍历可见 Cell（结构性保障，非可见 Cell 零开销）
    ▼
CellWidget.BindData(TItemData data)         ← 外部唯一入口
    │
    ├── _pName.Value = data.DisplayName     ← 推值到中间属性
    ├── _pIcon.Value = data.IconLocation    ← 推值到中间属性
    ├── _pCount.Value = data.CountText      ← 推值到中间属性
    ├── _progressWidget.SetData(...)        ← 委托给子 Widget
    └── _tagListWidget.BindData(...)        ← 委托给子 Widget
         │
         ▼ (BatchScheduler 延迟触发，同帧合并)
    ┌────┴────┬─────────┬──────────────┐
    ▼         ▼         ▼              ▼
 BindText  BindSprite  BindText    SubWidget
 各自刷新  各自刷新    各自刷新    各自刷新
```

### Cell 回收时的状态管理

当 Cell 被回收（FancyScrollView 为同一 Cell 分配不同的数据索引）：

1. `cell.Index = newIndex` — 更新索引
2. `cell.UpdateContent(newData)` → `BindData(newData)` — 推新值到中间属性
3. BindableProperty 只在值变化时触发回调 → 无变化的数据不刷新
4. 瞬态状态（选中高亮、按钮状态等）在 BindData 中重置

BindableProperty 在 OnCreate 时创建、OnDestroy 时 Dispose，整个 Cell 生命周期内复用。

---

## 7. 渐进式加载三级模型

### 7.1 三层结构

```
Layer 1：Cell 壳（同步，永远本地）
┌─────────────────────────────────┐
│  即时渲染，无加载延迟           │
│  名称、图标占位、数量等基础信息 │
└─────────────────────────────────┘

Layer 2：骨架/占位（同步，本地）
┌─────────────────────────────────┐
│  二级内容的"空座位"            │
│  骨架屏、Loading 转圈、进度条   │
│  或空白容器                     │
└─────────────────────────────────┘

Layer 3：实际内容（异步，可能远程）
┌─────────────────────────────────┐
│  子 Widget / 子 Prefab          │
│  通过 YooAsset 异步加载         │
│  支持进度回调、取消、超时       │
└─────────────────────────────────┘
```

### 7.2 三种加载策略

**Cascade（首次级联）**：
- ScrollView 首次 UpdateContents 时触发
- 按 staggerDelay（默认 80ms）间隔依次启动可见 Cell 加载
- 每个 Cell 加载完成后播放入场动画（PlayFadeInAsync）
- 仅触发一次，后续滚动用 AutoTrigger

**AutoTrigger（滚动自动触发）**：
- Cell 变为可见 → OnCellVisible → 自动启动加载
- Cell 变为不可见 → 保留已加载内容（不卸载）

**Manual（手动触发）**：
- 外部显式调用 cell.LoadContentAsync
- 适用于预加载、TabBar 切换等场景

---

## 8. 完整改造清单

| 改造区域 | 改动 | 文件 | 改动量 |
|---------|------|------|--------|
| **源码移植** | 复制 Runtime 源码到 HotFix | Core/, Scroller/, ScrollRect/, GridView/ | ~15 文件 |
| **命名空间** | `FancyScrollView` → `GameLogic.UI.FancyScrollView` | 所有移植文件 | ~15 文件 |
| **删除 asmdef** | 删除 FancyScrollView.asmdef | - | 1 文件 |
| **IFancyCell** | 新增接口文件（含 TContext） | Adapters/IFancyCell.cs | ~15 行 |
| **FancyCell** | 新增接口实现 | Core/FancyCell.cs | +1 行 |
| **FancyScrollView** | pool→IFancyCell + CreateCell 虚方法 + CellPrefab virtual | Core/FancyScrollView.cs | ~20 行 |
| **FancyScrollRect** | CellSize abstract → Inspector 可配置 | ScrollRect/FancyScrollRect.cs | ~5 行 |
| **FancyGridView** | Setup 虚方法化 | GridView/FancyGridView.cs | ~5 行 |
| **FancyScrollRectProxy** | 新增 Inspector 配置组件 | Adapters/FancyScrollRectProxy.cs | ~30 行 |
| **FancyCellWidget** | Cell Widget 基类（含渐进式加载、EasingCore 动画） | Adapters/FancyCellWidget.cs | ~200 行 |
| **FancyScrollViewWidget** | 基础容器 Widget（Carousel 等） | Adapters/FancyScrollViewWidget.cs | ~80 行 |
| **FancyScrollRectWidget** | ScrollRect 容器 Widget | Adapters/FancyScrollRectWidget.cs | ~100 行 |
| **FancyGridViewWidget** | GridView 容器 Widget | Adapters/FancyGridViewWidget.cs | ~100 行 |
| **CellGroupWrapper** | GridView 子 Cell 轻量初始化 + 事件转发 | Adapters/CellGroupWrapper.cs | ~60 行 |
| **ContentLoadScheduler** | 渐进式加载调度器 | Adapters/ContentLoadScheduler.cs | ~80 行 |
| **ScriptGeneratorSetting** | 新增 m_fscroll_ / m_scroller_ 前缀 | Editor/ | ~10 行 |
| **references 文档** | ui-patterns.md / naming-rules.md 更新 | .claude/skills/tengine-dev/references/ | ~200 行 |

**新增文件**：7 个（Adapters 目录）
**修改文件**：~20 个（移植文件 + 命名空间 + 接口改造 + Editor 配置）
**预估总代码量**：~1000 行新增 + ~50 行源码改造

---

## 9. 性能考量

| 场景 | 策略 |
|------|------|
| 大量 Cell（1000+ 数据项） | FancyScrollView 天然只创建可见数量的 Cell（通常 < 20），滚动时复用 |
| 快速滚动 | BatchScheduler 合并同帧多次 BindData，只触发最后一次回调 |
| Cell 动画 | OnCellPosition 每帧对可见 Cell 调用，非可见 Cell 不调用 |
| 中间属性 GC | BindableProperty 在 OnCreate 时创建、OnDestroy 时 Dispose，无运行时分配 |
| 子 Widget 复杂 Cell | 子 Widget 通过 InitializeWidget 创建一次，随 Cell 复用，不反复创建销毁 |
| 二级内容加载 | 滚出视口可取消加载（CancellationToken），避免加载不可见内容 |
| Cell Prefab 加载 | 首次 LoadAssetAsync 后缓存 Prefab 引用，后续直接 Instantiate 克隆 |
| 入场动画 | EasingCore + UniTask 帧级插值，零额外依赖，无反射 |
