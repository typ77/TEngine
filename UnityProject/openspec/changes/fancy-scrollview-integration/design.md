# FancyScrollView TEngine 集成 — 技术设计

> 基于 brainstorming 会话细化的完整设计。涵盖：组合模式、MVE 刷新架构、渐进式加载、事件传播、三种变体。

## 1. 架构总览

```
┌─────────────────────────────────────────────────────────────┐
│  HotFix 程序集内                                            │
│                                                             │
│  GameLogic.UI.FancyScrollView/                              │
│  ├── Core/                    ← 移植的原始核心（微改）      │
│  │   ├── FancyScrollView<T,C>.cs                           │
│  │   ├── FancyCell<T,C>.cs                                 │
│  │   └── NullContext.cs                                    │
│  ├── Scroller/                ← 移植的滚动控制器            │
│  │   ├── Scroller.cs                                       │
│  │   ├── EasingCore.cs                                     │
│  │   └── Enums.cs                                          │
│  ├── ScrollRect/              ← 移植的 ScrollRect 变体      │
│  │   ├── FancyScrollRect<T,C>.cs                           │
│  │   └── IFancyScrollRectContext.cs                        │
│  ├── GridView/                ← 移植的 GridView 变体        │
│  │   ├── FancyGridView<T,C>.cs                             │
│  │   ├── FancyCellGroup<T,C>.cs                            │
│  │   └── IFancyGridViewContext.cs                          │
│  │                                                          │
│  └── Adapters/                ← TEngine 适配层（新增）      │
│      ├── IFancyCell.cs                  ← Cell 接口         │
│      ├── FancyCellWidget.cs             ← Cell Widget 基类  │
│      ├── FancyScrollViewWidget.cs       ← 容器 Widget 基类  │
│      ├── FancyScrollRectWidget.cs       ← ScrollRect 容器   │
│      ├── FancyGridViewWidget.cs         ← GridView 容器     │
│      ├── CellWidgetPool.cs              ← Cell 池工厂       │
│      └── ContentLoadScheduler.cs        ← 渐进式加载调度    │
│                                                             │
│  业务使用示例：                                              │
│  GameLogic/Module/UIModule/BagUI/                           │
│  ├── BagModel.cs                                             │
│  ├── BagService.cs                                           │
│  ├── BagDataContext.cs                                       │
│  ├── BagUI.cs                                                │
│  ├── BagItemCellWidget.cs                                    │
│  └── BagItemCellWidget.prefab                               │
└─────────────────────────────────────────────────────────────┘
```

## 2. 组合模式详解

### 2.1 核心问题

FancyCell 是 MonoBehaviour，UIWidget 也是 MonoBehaviour。C# 不支持多继承。

### 2.2 解决方案：接口化 + 组合持有

```
FancyCellWidget<TItemData> : UIWidget, IFancyCell<TItemData>
├── 实现 IFancyCell 接口（让 FancyScrollView 能统一调用 Cell 操作）
├── 继承 UIWidget（走 TEngine 生命周期）
└── 不继承 FancyCell（通过组合持有 Cell 行为）
```

FancyScrollView 侧的改造：

```csharp
// 原始：基于 FancyCell 抽象类
readonly IList<FancyCell<TItemData, TContext>> pool = new List<...>();
var cell = Instantiate(CellPrefab, cellContainer).GetComponent<FancyCell<...>>();

// 改造后：基于 IFancyCell 接口 + 工厂委托
readonly IList<IFancyCell<TItemData>> pool = new List<IFancyCell<TItemData>>();
Func<GameObject, Transform, IFancyCell<TItemData>> CellFactory { get; set; }
var cell = CellFactory.Invoke(CellPrefab, cellContainer);
```

## 3. CellWidget MVE 刷新架构

### 3.1 数据流

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

### 3.2 中间属性生命周期管理

```csharp
// FancyCellWidget 基类提供统一创建方法
private readonly List<IDisposable> _cellProperties = new();

protected BindableProperty<T> CreateProperty<T>(T initialValue = default)
{
    var prop = new BindableProperty<T>(initialValue);
    _cellProperties.Add(prop);
    return prop;
}

// OnDestroy 自动 Dispose 所有中间属性
protected override void OnDestroy()
{
    foreach (var prop in _cellProperties)
        prop.Dispose();
    _cellProperties.Clear();
    base.OnDestroy();
}
```

### 3.3 可见性感知

```csharp
public virtual void SetVisible(bool visible)
{
    gameObject.SetActive(visible);
    if (visible) OnCellVisible();
    else OnCellInvisible();
}

protected virtual void OnCellVisible() { }    // 可选：启动实时数据订阅、触发内容加载
protected virtual void OnCellInvisible() { }  // 可选：取消订阅、暂停加载
```

### 3.4 子 Widget 两种模式

**模式 A：直接赋值（简单子 Widget，90% 场景）**
```csharp
public class ProgressWidget : UIWidget
{
    private Slider _slider;
    private Text _textHp;

    protected override void ScriptGenerator()
    {
        _slider = FindChildComponent<Slider>("m_slider_Hp");
        _textHp = FindChildComponent<Text>("m_text_Hp");
    }

    public void SetData(float current, float max)
    {
        _slider.value = current / Mathf.Max(max, 1);
        _textHp.text = $"{(int)current}/{(int)max}";
    }
}
```

**模式 B：BindableProperty 引用（需要响应式，如实时血条）**
```csharp
public class LiveProgressWidget : UIWidget
{
    private Slider _slider;
    public void Bind(BindableProperty<float> ratioProp)
    {
        Bind(ratioProp, value => _slider.value = value);
    }
}
```

## 4. 事件传播：混合模式

### 设计决策

CellWidget 内部用回调 + 外部转 GameEvent/直接调 Service。

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
│  // 外部看到的只有简洁的回调接口                             │
│  RegisterEvent():                                            │
│    _actionPanel.OnUse +=                                     │
│      () => OnButtonClicked?.Invoke(Index);                   │
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

### 多层嵌套按钮冒泡原则

CellWidget 是事件边界。无论内部嵌套多深（CellWidget → ItemInfoWidget → ActionPanelWidget → m_btnUse），CellWidget 自己消化内部冒泡逻辑，对外只暴露简洁的回调。

## 5. Cell Prefab 加载：YooAsset 异步

### 设计决策

Cell Prefab 通过 YooAsset 资源路径加载，不使用 Inspector 拖入。

### 加载流程

```csharp
// ScrollViewWidget 提供初始化接口
public async UniTask InitializeAsync(
    string cellAssetLocation,
    IProgress<float> progress = null,
    CancellationToken ct = default)
{
    // 1. 预加载 Cell Prefab
    // 使用 GameModule.Resource.LoadAssetAsyncHandle 获取进度
    var handle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(cellAssetLocation);
    
    // 2. 轮询进度（可选）
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
    
    // 3. 缓存 Prefab 引用
    _cellPrefab = handle.AssetObject as GameObject;
}

// 后续 Cell 创建使用缓存的 Prefab + UIWidget.CreateByPrefab
```

### 资源下载与实例化的分层

```
阶段 1：资源包下载（由全局热更下载流程处理，非 ScrollView 职责）
  → ResourceDownloaderOperation 已有完整下载进度
  → 在打开 UI 窗口之前确保所需资源包已下载

阶段 2：资源实例化（ScrollViewWidget 内处理）
  → AssetBundle 已在本地，只需反序列化 + Instantiate
  → 通过 LoadAssetAsyncHandle.Progress 可获取加载进度
  → 首次加载完成后缓存 Prefab，后续复用
```

## 6. 渐进式加载（三级加载模型）

### 6.1 三层结构

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

### 6.2 状态机

```
┌──────────┐   LoadAsync   ┌──────────┐  完成  ┌───────┐
│ Skeleton │──────────────▶│ Loading  │───────▶│Loaded │
│ (空壳)   │               │(加载中)  │        │(已加载)│
└──────────┘               └─────┬────┘        └───┬───┘
     ▲                          │ 失败             │     │
     │                          ▼                  │     │
     │                    ┌──────────┐             │     │
     │                    │  Error   │             │     │
     │                    │(加载失败)│             │     │
     │                    └────┬─────┘             │     │
     │         重试           │                   │     │
     └───────────────────────┘    Unload           │     │
     ◄─────────────────────────────────────────────┘     │
```

### 6.3 三种加载策略

```
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│  策略 1：Cascade（级联）— 首次初始化时使用                   │
│                                                              │
│  时间轴 ──────────────────────────────────▶                  │
│                                                              │
│  Cell[0] ████████░░░░░░░░ ───完成                           │
│  Cell[1]   ┌80ms┌████████░░░░ ───完成                      │
│  Cell[2]       ┌160ms┌████████░░ ───完成                    │
│  Cell[3]           ┌240ms┌████████░ ───完成                 │
│  Cell[4]               ┌320ms┌██████ ───完成                │
│                                                              │
│  - 从第一个可见 Cell 向最后一个依次启动                      │
│  - 每个间隔 staggerDelay（可配置，默认 80ms）                │
│  - 每个 Cell 完成后播放入场动画（fade in / slide in）        │
│  - 方向跟随滚动方向（垂直=上到下，水平=左到右）              │
│  - 仅触发一次（_isInitialized 标记），后续滚动用 AutoTrigger │
│  - 适用场景：窗口首次打开、Tab 首次切换                      │
│                                                              │
│  策略 2：AutoTrigger（自动触发）— 滚动时使用                 │
│                                                              │
│  Cell 变为可见 → OnCellVisible() → 自动启动加载             │
│  Cell 变为不可见 → OnCellInvisible() → 可选取消加载         │
│                                                              │
│  - 无间隔，变为可见立即开始                                  │
│  - 如果内容已在本地缓存，瞬间完成                            │
│  - 离开视口后可保留已加载内容（不卸载）                      │
│  - 适用场景：InfiniteScroll 日常滚动                         │
│                                                              │
│  策略 3：Manual（手动触发）— 外部控制时使用                  │
│                                                              │
│  外部调用 cell.LoadContentAsync() 显式触发                  │
│                                                              │
│  - 完全由调用方决定何时加载                                  │
│  - 可控制并发数（一次最多加载 N 个 Cell 的内容）             │
│  - 可预加载（当前可见 + 上下各 N 个）                        │
│  - 适用场景：LoopTabBar、预加载策略、加载优先级控制           │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 6.4 级联加载的初始化流程

```
时刻 T=0：ScrollView 首次 UpdateContents

Step 1: 渲染所有可见 Cell 的壳（同步，立即）
  Cell[0~4]: 显示名称+数量+骨架占位 ← 立即可见

Step 2: 按级联顺序启动二级内容加载
  T=0ms:    Cell[0].LoadContentAsync()
  T=80ms:   Cell[1].LoadContentAsync()
  T=160ms:  Cell[2].LoadContentAsync()
  T=240ms:  Cell[3].LoadContentAsync()
  T=320ms:  Cell[4].LoadContentAsync()

Step 3: 每个 Cell 加载完成后播放入场动画
  Cell[0] 加载完成 → OnContentLoaded() → FadeIn + SlideUp
  Cell[1] 加载完成 → OnContentLoaded() → FadeIn + SlideUp
  （谁先完成谁先播，不严格按顺序）

关键细节：
  - 级联仅触发一次（_isInitialized 标记）
  - 后续滚动进入的 Cell 用 AutoTrigger 模式
  - 快速滚动时：Cell 还没开始加载就已滚出视口，直接跳过
  - ScrollViewWidget 销毁时统一取消所有加载（CancellationTokenSource）
```

### 6.5 策略配置

```csharp
enum ContentLoadMode
{
    Cascade,      // 首次级联
    AutoTrigger,  // 滚动自动触发
    Manual        // 外部手动触发
}

// ScrollViewWidget 配置
ContentLoadMode contentLoadMode = AutoTrigger;
float staggerDelay = 0.08f;   // Cascade 模式间隔
int preloadCount = 2;          // Auto 预加载额外数量
```

## 7. 三个变体的差异

```
┌──────────────────────────────────────────────────────────────┐
│  变体          │ 布局方式     │ 无限滚动 │ 网格 │ 适用场景   │
├────────────────┼──────────────┼──────────┼──────┼───────────┤
│ ScrollView     │ 自定义动画   │ ✅       │ ❌   │ Carousel  │
│ (基础版)       │ normalizedPos│ loop     │      │ 3D 特效   │
├────────────────┼──────────────┼──────────┼──────┼───────────┤
│ ScrollRect     │ 标准 Scroll  │ ❌       │ ❌   │ 背包      │
│ (列表版)       │ padding/     │          │      │ 排行榜    │
│                │ spacing      │          │      │ 邮件列表  │
├────────────────┼──────────────┼──────────┼──────┼───────────┤
│ GridView       │ 网格分组     │ ❌       │ ✅   │ 道具格子  │
│ (网格版)       │ CellGroup    │          │      │ 头像选择  │
│                │ startAxis    │          │      │ 技能面板  │
└────────────────┴──────────────┴──────────┴──────┴───────────┘
```

### GridView 的 CellGroup 特殊处理

FancyGridView 通过 CellGroup 管理网格列。CellGroup 内部的子 Cell 不走完整 UIWidget 生命周期，仅最外层 Cell 走 Widget 管线。

```
GridView Widget
  └── CellGroup (MonoBehaviour，非 Widget)
        ├── CellWidget[0] (UIWidget)
        ├── CellWidget[1] (UIWidget)
        └── CellWidget[2] (UIWidget)
```

## 8. 文件放置

```
Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/
├── Core/
│   ├── FancyScrollView.cs         ← 微改：pool 类型改为 IFancyCell + CellFactory 委托
│   ├── FancyCell.cs               ← 微改：提取接口 IFancyCell
│   └── NullContext.cs
├── Scroller/
│   ├── Scroller.cs
│   ├── EasingCore.cs
│   └── Enums/
├── ScrollRect/
│   ├── FancyScrollRect.cs
│   ├── FancyScrollRectCell.cs
│   └── FancyScrollRectContext.cs
├── GridView/
│   ├── FancyGridView.cs
│   ├── FancyCellGroup.cs
│   └── FancyGridViewContext.cs
└── Adapters/
    ├── IFancyCell.cs              ← 新增：Cell 接口
    ├── FancyCellWidget.cs         ← 新增：Cell Widget 基类（含渐进式加载）
    ├── FancyScrollViewWidget.cs   ← 新增：容器 Widget 基类
    ├── FancyScrollRectWidget.cs   ← 新增：ScrollRect 容器 Widget
    ├── FancyGridViewWidget.cs     ← 新增：GridView 容器 Widget
    ├── CellWidgetPool.cs          ← 新增：Cell 池工厂（YooAsset 异步加载 Prefab）
    └── ContentLoadScheduler.cs    ← 新增：渐进式加载调度（Cascade/Auto/Manual）
```

## 9. 关键接口定义

### 9.1 IFancyCell

```csharp
public interface IFancyCell<TItemData>
{
    int Index { get; set; }
    bool IsVisible { get; }
    void SetVisible(bool visible);
    void UpdateContent(TItemData data);
    void UpdatePosition(float normalizedPosition);
    void SetContext(object context);
    void Initialize();
}
```

### 9.2 FancyCellWidget（核心骨架）

```csharp
public abstract class FancyCellWidget<TItemData> : UIWidget, IFancyCell<TItemData>
{
    // ── IFancyCell 实现 ──
    public int Index { get; set; } = -1;
    public bool IsVisible => gameObject != null && gameObject.activeSelf;
    public virtual void SetVisible(bool visible) { /* 触发 OnCellVisible/OnCellInvisible */ }
    public void UpdateContent(TItemData data) => BindData(data);
    public abstract void UpdatePosition(float normalizedPosition);
    public virtual void SetContext(object context) { }
    public virtual void Initialize() { }

    // ── MVE 中间属性 ──
    protected BindableProperty<T> CreateProperty<T>(T initial = default);

    // ── 数据绑定（子类 override）──
    protected virtual void BindData(TItemData data) { }

    // ── 动画（子类 override）──
    protected virtual void OnCellPosition(float normalizedPosition) { }

    // ── 可见性回调（可选 override）──
    protected virtual void OnCellVisible() { }
    protected virtual void OnCellInvisible() { }

    // ── 渐进式加载 ──
    public ContentLoadState LoadState { get; }   // Skeleton/Loading/Loaded/Error
    public float LoadProgress { get; }           // 0.0~1.0
    public async UniTask LoadContentAsync(
        IProgress<float> progress = null,
        CancellationToken ct = default);
    public void CancelLoad();
    public void UnloadContent();

    // ── 加载状态回调（可选 override）──
    protected virtual void OnContentLoading(float progress) { }
    protected virtual void OnContentLoaded() { }      // 可播入场动画
    protected virtual void OnContentError(string error) { }
    protected virtual void OnContentUnloaded() { }

    // ── 子类实现：二级内容加载配置 ──
    protected virtual string ContentAssetLocation => null; // 返回 null 表示无二级内容
    protected virtual void BindContentWidget(UIWidget widget) { }

    // ── 事件回调 ──
    public event Action<int> OnItemClicked;
    public event Action<int> OnButtonClicked;

    // ── 生命周期 ──
    protected override void OnDestroy() { /* Dispose properties + CancelLoad */ }
}

public enum ContentLoadState
{
    Skeleton,   // 空壳/骨架
    Loading,    // 加载中
    Loaded,     // 已加载
    Error       // 加载失败
}
```

### 9.3 容器 Widget

```csharp
public abstract class FancyScrollRectWidget<TItemData> : UIWidget
{
    // ── 初始化（YooAsset 异步加载 Cell Prefab）──
    public async UniTask InitializeAsync(
        string cellAssetLocation,
        IProgress<float> progress = null,
        CancellationToken ct = default);

    // ── 数据接口 ──
    public void UpdateContents(IList<TItemData> items);
    public void ScrollTo(int index, float duration, float alignment = 0.5f);
    public void JumpTo(int index, float alignment = 0.5f);
    public int DataCount { get; }

    // ── 渐进式加载配置 ──
    public ContentLoadMode ContentLoadMode { get; set; } = ContentLoadMode.AutoTrigger;
    public float StaggerDelay { get; set; } = 0.08f;
    public int PreloadCount { get; set; } = 2;

    // ── 事件中转（汇总 Cell 回调）──
    public event Action<int> OnCellClicked;
    public event Action<int> OnCellButtonClicked;
}
```

## 10. 性能考量

| 场景 | 策略 |
|------|------|
| 大量 Cell（1000+ 数据项） | FancyScrollView 天然只创建可见数量的 Cell，滚动时复用 |
| 快速滚动 | BatchScheduler 合并同帧多次 BindData，只触发最后一次回调 |
| Cell 动画 | OnCellPosition 在每帧对可见 Cell 调用，非可见 Cell 不调用 |
| 中间属性 GC | BindableProperty 在 OnCreate 时创建、OnDestroy 时 Dispose，无运行时分配 |
| 子 Widget 复杂 Cell | 子 Widget 通过 CreateWidget 创建一次，随 Cell 复用，不反复创建销毁 |
| 二级内容加载 | 滚出视口可取消加载（CancellationToken），避免加载不可见内容 |
| Cell Prefab 加载 | 首次 LoadAssetAsync 后缓存 Prefab 引用，后续 CreateByPrefab 直接克隆 |
