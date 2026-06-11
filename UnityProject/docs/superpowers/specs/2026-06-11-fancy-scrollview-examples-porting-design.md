# FancyScrollView Examples Porting — 独立示例体系设计

基于 TEngine `UIWindow` / `UIWidget` 规范，将 FancyScrollView 官方示例移植为可独立运行、资源集中管理、故障隔离的示例体系。

## 1. 背景

TEngine 已完成 FancyScrollView 核心能力集成，并已有 `FancyScrollRectWidget`、`FancyGridViewWidget` 等适配器基础。当前缺少一套面向 TEngine 开发者的完整运行示例，用于展示如何在热更代码、`UIWindow`、`UIWidget`、YooAsset、UniTask 规范下使用 FancyScrollView。

原始 FancyScrollView 示例偏向 Unity `MonoBehaviour` 和场景直挂模式。如果直接复制，会绕过 TEngine UI 生命周期、资源管理和事件清理规范，无法作为后续业务 UI 的参考样板。因此本设计将示例移植定义为“TEngine 化独立示例体系建设”，而不是简单复刻官方场景。

本设计还明确两个关键约束：

1. 每个 Example 必须作为独立 `UIWindow` 单元运行，单个 Example 出错不得拖垮其他 Example。
2. Example 资源统一集中在模块级资源目录下管理，方便后续新增更多示例。

## 2. 目标与非目标

### 2.1 目标

- 将 9 个 FancyScrollView 官方 Example 移植为 TEngine `UIWindow` 示例。
- 每个 Example 都可以直接通过 `GameModule.UI.ShowUIAsync<T>()` 打开。
- 每个 Example 都可以从 `FancyTestHomeUI` 导航打开。
- 每个 Example 的代码、Prefab、资源、数据、Context、运行状态互相隔离。
- 资源集中存放于 `Assets/AssetRaw/UI/FancyScrollViewExamples/`。
- 每个 Example 必须符合 TEngine `UIWindow` / `UIWidget` 设计规范。
- 单个 Example 打开失败、资源缺失或运行异常时，不影响其他 Example 的直接打开和 HomeUI 导航。

### 2.2 非目标

- 不把 Example 设计成共享业务框架。
- 不复用 Example 之间的 `ItemData`、`Context`、`Cell`、`ScrollViewWidget`。
- 不要求每个 Example 拥有独立 Scene。
- 不修改主 `GameScene` 和主流程链。
- 不使用 `Resources.Load`。
- 不使用 Coroutine。
- 不绕过 UIModule 手动管理 `UIWindow` 生命周期。
- 不承诺视觉 Shader 在所有平台表现完全一致；视觉示例只要求当前目标环境基础显示并能隔离错误。

## 3. 已确认设计原则

### 3.1 每个 Example 是独立 UIWindow 单元

每个 Example 都是一个独立 `UIWindow`，必须能通过 `GameModule.UI.ShowUIAsync<ExampleXX_NameUI>()` 直接打开。Example 不依赖 `FancyTestHomeUI.UserData`、共享 Context 或其他 Example 初始化逻辑。

### 3.2 HomeUI 只是导航器

`FancyTestHomeUI` 只负责展示 Example 列表、打开目标 Example、关闭当前 Example、显示状态和错误。它不持有 Example 数据、Context、Cell 引用、资源句柄或运行状态。

### 3.3 共享基础设施最小化

共享层只包含通用 FancyScrollView 引擎、TEngine UI 适配器和导航工具。Example 的行为、数据、视觉状态、资源归属和运行时状态都保留在各自目录内。

### 3.4 资源集中管理

所有 FancyScrollView 示例资源集中到：

```text
Assets/AssetRaw/UI/FancyScrollViewExamples/
```

目录内部按资源类型分类，资源文件通过 `ExampleXX_` 前缀表达归属。

### 3.5 必须符合 TEngine UIWindow/UIWidget 规范

每个 Example 不只是“能跑”，还必须是 TEngine UI 标准写法的参考样板。原始 `MonoBehaviour` 逻辑必须适配到 `UIWindow` / `UIWidget` 生命周期中。

## 4. 总体架构

### 4.1 代码目录结构

```text
Assets/GameScripts/HotFix/GameLogic/
├── Module/
│   └── UIModule/
│       └── FancyScrollView/
│           ├── Core/
│           ├── Scroller/
│           ├── ScrollRect/
│           ├── GridView/
│           └── Adapters/
│               ├── FancyCellWidget.cs
│               ├── FancyScrollViewWidget.cs
│               ├── FancyScrollerWidget.cs
│               ├── FancyScrollRectWidget.cs
│               └── FancyGridViewWidget.cs
│
└── UI/
    └── FancyScrollViewExamples/
        ├── FancyTestHomeUI/
        ├── Common/
        ├── Example01_Basic/
        ├── Example02_FocusOn/
        ├── Example03_InfiniteScroll/
        ├── Example04_Metaball/
        ├── Example05_Voronoi/
        ├── Example06_LoopTabBar/
        ├── Example07_ScrollRect/
        ├── Example08_GridView/
        └── Example09_LoadTexture/
```

`Module/UIModule/FancyScrollView` 放通用引擎与适配层；`UI/FancyScrollViewExamples` 放示例窗口和示例逻辑。

### 4.2 资源目录结构

```text
Assets/AssetRaw/UI/FancyScrollViewExamples/
├── Prefabs/
│   ├── Windows/
│   └── Cells/
├── Materials/
├── Shaders/
├── Textures/
├── Animations/
└── Shared/
```

资源集中存放，但通过 `ExampleXX_` 前缀保持 Example 归属。

### 4.3 运行入口关系

```text
FancyScrollViewTestScene
  └── ProcedureTestSample
      └── 默认打开 FancyTestHomeUI

FancyTestHomeUI
  └── 通过显式 Entry 表打开各 Example

ExampleXX_NameUI
  └── 也必须支持直接 ShowUIAsync 打开
```

### 4.4 直接打开与 HomeUI 打开

每个 Example 必须同时支持两种路径：

```csharp
GameModule.UI.ShowUIAsync<Example01_BasicUI>();
```

以及从 `FancyTestHomeUI` 的 Dropdown 导航打开。HomeUI 只是调用同一套公开窗口入口，不做额外数据注入。

## 5. Example 隔离模型

### 5.1 Example 的独立边界

每个 Example 独立拥有：

- `UIWindow`
- `ScrollViewWidget`
- `Cell`
- `ItemData`
- `Context`（如需要）
- Window Prefab
- Cell Prefab
- 专属动画、材质、Shader、贴图
- 运行时状态
- 资源释放逻辑
- 错误日志前缀

### 5.2 禁止跨 Example 共享的内容

禁止跨 Example 共享：

- `ItemData`
- `Context`
- `Cell`
- `ScrollViewWidget`
- Window Prefab
- Cell Prefab
- 可变静态状态
- 示例专属资源
- 示例内部事件状态

即使两个 Example 的 UI 看起来相似，也优先复制独立文件和 Prefab，以保证故障范围可控。

### 5.3 允许共享的内容

允许共享：

- FancyScrollView 核心类
- `Scroller`
- `FancyCellWidget`
- `FancyScrollerWidget`
- `FancyScrollRectWidget`
- `FancyGridViewWidget`
- `FancyTestHomeUI`
- `FancyExampleEntry`
- 展示名称常量
- 真正通用且无状态的占位资源

### 5.4 共享可变静态状态禁用规则

禁止在共享层使用：

```csharp
public static int CurrentExampleIndex;
public static object CurrentContext;
public static List<object> CurrentItems;
public static CancellationTokenSource CurrentLoadCts;
```

允许共享不可变常量，例如 Example 展示名称。

## 6. HomeUI 导航与错误隔离

### 6.1 FancyTestHomeUI 职责

`FancyTestHomeUI` 是导航面板，不是 Example 宿主。它只负责：

1. 展示 Example 列表。
2. 接收 Dropdown 选择。
3. 关闭当前 Example。
4. 打开目标 Example。
5. 显示当前状态或错误。
6. 保持自身可交互。

它不负责创建 Example 数据、预加载 Example 资源、管理 Example 内部事件或释放 Example 专属资源。

### 6.2 FancyExampleEntry 显式入口表

HomeUI 使用显式 Entry 表，不通过反射扫描窗口类型：

```csharp
public sealed class FancyExampleEntry
{
    public string DisplayName { get; }
    public bool IsImplemented { get; }
    public Func<UniTask> OpenAsync { get; }
    public Action Close { get; }
}
```

显式表便于为每个 Example 独立配置 `IsImplemented`、打开逻辑和关闭逻辑。

### 6.3 打开流程

```text
用户选择 Example
  -> 如果正在切换，忽略本次请求
  -> best-effort 关闭当前 Example
  -> 如果目标未实现，显示 TODO 状态
  -> try 打开目标 Example
  -> 成功：记录 current，显示 Opened
  -> 失败：清空 current，显示 Failed，HomeUI 保持可操作
```

打开失败只影响当前 Example，不能禁用 Dropdown 或关闭 HomeUI。

### 6.4 关闭流程

关闭当前 Example 使用 best-effort：

```text
try close current
catch ex:
  log warning
  clear current
continue open target
```

关闭失败只记录 Warning，不阻止打开其他 Example。

### 6.5 未实现 Example 的处理

未实现 Example 可以出现在 Dropdown 中，但显示 `(TODO)`。选择后只更新状态文本，不调用空打开逻辑，也不记录为当前窗口。

### 6.6 快速切换保护

HomeUI 使用 `_isSwitching` 防止 Dropdown 连续触发导致多个 Example 同时打开。

### 6.7 HomeUI 验收标准

- Dropdown 显示全部 9 个 Example。
- 未实现 Example 可显示但不会触发异常。
- 已实现 Example 可从 Dropdown 打开。
- 切换 Example 时旧 Example 被关闭。
- 当前 Example 打开失败后 HomeUI 仍可操作。
- 当前 Example 关闭失败不会阻止打开其他 Example。
- 快速连续切换不会打开多个 Example。
- HomeUI 销毁时只尝试关闭当前 Example，不调用 `CloseAll`。
- 日志包含 Example 名称，错误可定位到具体 Example。

## 7. 资源集中管理方案

### 7.1 模块级资源根目录

所有 FancyScrollView 示例资源统一集中在：

```text
Assets/AssetRaw/UI/FancyScrollViewExamples/
```

这表示资源属于 FancyScrollView 示例模块，而不是普通业务 UI 资源。

### 7.2 按资源类型分类

推荐结构：

```text
Assets/AssetRaw/UI/FancyScrollViewExamples/
├── Prefabs/
│   ├── Windows/
│   │   ├── FancyTestHomeUI.prefab
│   │   ├── Example01_BasicUI.prefab
│   │   └── ...
│   └── Cells/
│       ├── Example01_BasicCell.prefab
│       ├── Example02_FocusOnCell.prefab
│       └── ...
├── Materials/
│   ├── Example04_MetaballBackground.mat
│   └── Example05_VoronoiBackground.mat
├── Shaders/
│   ├── Example04_Metaball.shader
│   ├── Example04_Metaball.hlsl
│   ├── Example05_Voronoi.shader
│   └── Example05_Voronoi.hlsl
├── Textures/
│   └── Example09_LoadTexture/
├── Animations/
│   ├── Example01_Basic/
│   ├── Example02_FocusOn/
│   └── Example06_LoopTabBar/
└── Shared/
```

### 7.3 ExampleXX_ 命名规则

所有 Example 专属资源使用 `ExampleXX_` 前缀，例如：

```text
Example01_BasicUI.prefab
Example01_BasicCell.prefab
Example04_MetaballBackground.mat
Example09_Texture_001.png
```

禁止使用 `Cell.prefab`、`Background.mat`、`Scroll.anim` 这类无法表达归属的名称。

### 7.4 YooAsset 资源加载规范

- Sprite 使用 `SetSprite` / `SetSubSprite`。
- GameObject 使用 `LoadGameObjectAsync`，或由 UIModule / Widget 现有流程加载。
- Material、TextAsset、AnimationClip 等非 GameObject Asset 使用 `LoadAssetAsync<T>`，并在 `OnDestroy` 中 `UnloadAsset`。
- Shader 优先通过 Material 引用，不建议运行时直接加载 Shader。
- 禁止 `Resources.Load`。
- 禁止 `LoadAssetAsync<Sprite>`。
- 禁止 `LoadAssetAsync<GameObject>` + `Instantiate` 组合。

### 7.5 资源隔离验收标准

- Window Prefab 位于 `Prefabs/Windows/`。
- Cell Prefab 位于 `Prefabs/Cells/`。
- Example 专属资源使用 `ExampleXX_` 前缀。
- Example 不引用其他 Example 前缀的专属资源。
- 运行时资源加载符合 YooAsset 规范。
- 单个 Example 资源缺失时，其他 Example 仍可打开。

## 8. TEngine UI 合规要求

### 8.1 UIWindow 规范

每个 Example 的窗口类必须使用 `[Window]`：

```csharp
[Window(UILayer.UI, location: "Example01_BasicUI")]
public class Example01_BasicUI : UIWindow
{
}
```

要求：

- 类名使用 `ExampleXX_NameUI`。
- `location` 与窗口 Prefab 文件名一致。
- 支持 `GameModule.UI.ShowUIAsync<T>()` 直接打开。
- 不依赖 HomeUI 的 `UserData`。
- 不绕过 UIModule 手动实例化窗口。
- 不调用 `GameModule.UI.CloseAll()`。

### 8.2 UIWidget 规范

ScrollView 组件必须作为 `UIWidget` 管理。它负责绑定 Scroller 或 FancyScrollRectProxy、持有当前 Example 的 Context、创建当前 Example 的 Cell、提供 `UpdateData`、中转 Cell 事件并清理自身注册的回调。

### 8.3 Cell Widget 规范

Cell 通过 `FancyCellWidget<TItemData, TContext>` 或等价 TEngine Widget 生命周期适配类接入：

- 使用 `ScriptGenerator` 绑定节点。
- 使用 `BindData` 刷新显示。
- 使用 `UpdatePosition` 处理滚动位置动画。
- Cell 复用时重置视觉状态。
- 不直接打开/关闭窗口。
- 不访问 HomeUI。

### 8.4 生命周期职责

- `ScriptGenerator`：绑定节点和创建子 Widget。
- `RegisterEvent`：注册当前 UI 的事件。
- `OnCreate`：一次性初始化。
- `OnRefresh`：每次打开或刷新时更新显示数据。
- `OnDestroy`：取消异步任务、释放本 Example 手动加载的资源、清理当前状态。

### 8.5 节点前缀规范

常用节点前缀：

```text
m_go_          GameObject
m_item_        UIWidget
m_tf_          Transform
m_rect_        RectTransform
m_text_        Text
m_btn_         Button
m_img_         Image
m_rimg_        RawImage
m_fscroll_     FancyScrollRectProxy
m_scroller_    Scroller
m_scroll_      ScrollRect
m_input_       InputField
m_dropdown_    Dropdown
m_canvasGroup_ CanvasGroup
```

不需要绑定的装饰节点无需加前缀。

### 8.6 事件规范

- UI 内部按钮事件使用 Unity UI 事件，并由窗口生命周期管理。
- 监听 TEngine 事件时使用 `AddUIEvent`。
- 禁止 UI 内直接使用未清理的 `GameEvent.AddEventListener`。
- Cell 到 Window 的事件路径为：Cell → Context / Widget event → ScrollViewWidget → UIWindow。

### 8.7 异步规范

- 使用 UniTask。
- 不使用 Coroutine。
- 不使用 `Task.Run` 处理 Unity 对象。
- 异步加载必须支持取消。
- 忽略 UniTask 返回值时必须 `.Forget()`，并保证异常可控。

### 8.8 资源规范

每个 Example 的资源加载必须遵守第 7 节，尤其禁止 `Resources.Load`、`LoadAssetAsync<Sprite>`、`LoadAssetAsync<GameObject>` + `Instantiate`。

## 9. 每个 Example 的代码模板

### 9.1 UIWindow 模板

```csharp
[Window(UILayer.UI, location: "Example01_BasicUI")]
public class Example01_BasicUI : UIWindow
{
    private Example01_BasicScrollViewWidget _scrollView;
    private readonly List<Example01_BasicItemData> _items = new();

    protected override void ScriptGenerator()
    {
        _scrollView = CreateWidget<Example01_BasicScrollViewWidget>("m_item_ScrollView");
    }

    protected override void OnRefresh()
    {
        BuildItems();
        _scrollView.UpdateData(_items);
    }

    protected override void OnDestroy()
    {
        _items.Clear();
        base.OnDestroy();
    }

    private void BuildItems()
    {
        _items.Clear();
        for (int i = 0; i < 20; i++)
        {
            _items.Add(new Example01_BasicItemData($"Cell {i}"));
        }
    }
}
```

### 9.2 ScrollViewWidget 模板

```csharp
public class Example01_BasicScrollViewWidget
    : FancyScrollerWidget<Example01_BasicItemData, NullContext>
{
    private const string CELL_PREFAB_LOCATION = "Example01_BasicCell";

    protected override string CellPrefabLocation => CELL_PREFAB_LOCATION;
}
```

实际实现以现有适配器 API 为准，但职责边界不变：Widget 子类声明自己的 Cell Prefab、创建自己的 Cell、处理中转和当前 Example 滚动逻辑。

### 9.3 Cell 模板

```csharp
public class Example01_BasicCell
    : FancyCellWidget<Example01_BasicItemData, NullContext>
{
    private Text _textMessage;
    private Animator _animator;

    protected override void ScriptGenerator()
    {
        _textMessage = FindChildComponent<Text>("m_text_Message");
        _animator = FindChildComponent<Animator>("m_go_Content");
    }

    protected override void BindData(Example01_BasicItemData data)
    {
        _textMessage.text = data.Message;
    }

    public override void UpdatePosition(float position)
    {
        if (_animator == null)
        {
            return;
        }

        _animator.Play("Example01_CellScroll", 0, position);
    }
}
```

### 9.4 ItemData 模板

```csharp
public sealed class Example01_BasicItemData
{
    public string Message { get; }

    public Example01_BasicItemData(string message)
    {
        Message = message;
    }
}
```

### 9.5 Context 模板

```csharp
public sealed class Example02_FocusOnContext
{
    public int SelectedIndex { get; set; } = -1;
    public Action<int> OnCellClicked { get; set; }
}
```

### 9.6 Prefab 节点模板

Window Prefab：

```text
Example01_BasicUI
├── m_text_Title
├── m_item_ScrollView
│   ├── m_scroller_List
│   └── m_tf_CellContainer
└── m_btn_Close
```

Cell Prefab：

```text
Example01_BasicCell
└── m_go_Content
    ├── m_text_Message
    └── m_img_Background
```

## 10. 9 个 Example 的独立范围

### 10.1 Example01_Basic

- 目标：基础列表、垂直滚动、Cell 复用、位置动画。
- 独立代码：`Example01_BasicUI`、`Example01_BasicScrollViewWidget`、`Example01_BasicCell`、`Example01_BasicItemData`。
- 独立资源：`Example01_BasicUI.prefab`、`Example01_BasicCell.prefab`、`Example01_CellScroll.anim`、`Example01_Cell.controller`。
- 验收：可直接打开、可从 HomeUI 打开、显示 20 个 Cell、滚动不串数据、重复打开关闭无错误。

### 10.2 Example02_FocusOn

- 目标：点击 Cell 后更新选中态并平滑滚动到目标位置。
- 独立代码：`Example02_FocusOnUI`、`Example02_FocusOnScrollViewWidget`、`Example02_FocusOnCell`、`Example02_FocusOnItemData`、`Example02_FocusOnContext`。
- 独立资源：`Example02_FocusOnUI.prefab`、`Example02_FocusOnCell.prefab`、Example02 专属动画。
- 验收：点击聚焦正确、选中态不串位、快速连续点击无异常。

### 10.3 Example03_InfiniteScroll

- 目标：无限循环滚动、首尾连续衔接、循环索引刷新。
- 独立代码：`Example03_InfiniteScrollUI`、`Example03_InfiniteScrollViewWidget`、`Example03_InfiniteScrollCell`、`Example03_InfiniteScrollItemData`、`Example03_InfiniteScrollContext`。
- 独立资源：Example03 专属 Window Prefab、Cell Prefab、动画。
- 验收：连续滚动自然、快速拖动不串数据、循环逻辑不影响其他示例。

### 10.4 Example04_Metaball

- 目标：Metaball 视觉背景与滚动 Cell 动画。
- 独立代码：`Example04_MetaballUI`、`Example04_MetaballScrollViewWidget`、`Example04_MetaballCell`、`Example04_MetaballItemData`、`Example04_MetaballBackground`。
- 独立资源：`Example04_MetaballBackground.mat`、`Example04_Metaball.shader`、`Example04_Metaball.hlsl`、Example04 专属 Prefab 和动画。
- 验收：背景正常显示，资源缺失日志带 `[Example04_Metaball]`，不影响 Example05。

### 10.5 Example05_Voronoi

- 目标：Voronoi 视觉背景与滚动 Cell 动画。
- 独立代码：`Example05_VoronoiUI`、`Example05_VoronoiScrollViewWidget`、`Example05_VoronoiCell`、`Example05_VoronoiItemData`、`Example05_VoronoiBackground`。
- 独立资源：`Example05_VoronoiBackground.mat`、`Example05_Voronoi.shader`、`Example05_Voronoi.hlsl`、Example05 专属 Prefab 和动画。
- 验收：不复用 Example04 资源，Shader 缺失只影响 Example05。

### 10.6 Example06_LoopTabBar

- 目标：循环 TabBar、选中态、内容 Panel 切换、入场/出场动画。
- 独立代码：`Example06_LoopTabBarUI`、`Example06_LoopTabBarWidget`、`Example06_TabCell`、`Example06_TabItemData`、`Example06_LoopTabBarContext`、`Example06_WindowPanel`、`Example06_SlideScreenTransition`。
- 独立资源：Example06 专属 Tab Cell Prefab、Window Panel Prefab、动画和控制器。
- 验收：Tab 切换正确，快速切换不串状态，动画缺失不影响其他 Example。

### 10.7 Example07_ScrollRect

- 目标：演示 `FancyScrollRectWidget`，支持 Padding、Spacing、DataCount、SelectIndex、Alignment 调整。
- 独立代码：`Example07_ScrollRectUI`、`Example07_ScrollRectViewWidget`、`Example07_ScrollRectCell`、`Example07_ScrollRectItemData`、`Example07_ScrollRectContext`、`Example07_Alignment`。
- 独立资源：Example07 专属 Window Prefab 和 Cell Prefab。
- 验收：非法输入不抛异常，不依赖 `FancyScrollerWidget`。

### 10.8 Example08_GridView

- 目标：演示 `FancyGridViewWidget`，支持网格布局、X/Y Spacing、DataCount、SelectIndex、Alignment。
- 独立代码：`Example08_GridViewUI`、`Example08_GridViewWidget`、`Example08_GridCell`、`Example08_GridItemData`、`Example08_GridViewContext`。
- 独立资源：Example08 专属 Window Prefab 和 Cell Prefab。
- 验收：不复用 Example07 的 Context 或 Cell，GridView 参数调整只影响 Example08。

### 10.9 Example09_LoadTexture

- 目标：滚动 Cell 中异步图片加载、复用安全、取消加载、loading/loaded/failed 状态。
- 独立代码：`Example09_LoadTextureUI`、`Example09_LoadTextureScrollViewWidget`、`Example09_LoadTextureCell`、`Example09_LoadTextureItemData`、`Example09_TextureLoadState`。
- 独立资源：Example09 专属 Window Prefab、Cell Prefab、`Textures/Example09_LoadTexture/` 下的图片。
- 验收：使用 `SetSprite`，不使用 `LoadAssetAsync<Sprite>`，快速滚动不串图，关闭窗口取消未完成加载。

## 11. OpenSpec 文档调整方案

### 11.1 proposal.md 调整

补充：

- `Isolation Strategy`
- `Asset Organization`
- `TEngine Compliance`

并在 `What Changes` 中明确：HomeUI 是导航器，每个 Example 是独立 `UIWindow`，资源集中管理且 Example 自带归属前缀。

### 11.2 design.md 调整

重写为独立 Example 体系设计，覆盖：

- Example 独立窗口模型
- HomeUI 失败隔离
- 共享基础设施边界
- 资源集中管理
- TEngine 合规要求
- 9 个 Example 的独立范围

### 11.3 tasks.md 调整

推荐任务结构：

```text
0. 文档与规范修正
1. 共享最小基础设施
2. FancyTestHomeUI — 导航器
3. FancyScrollerWidget — 基础 Scroller 适配器
4. Example01_Basic — 独立闭环
5. Example02_FocusOn — 独立闭环
6. Example03_InfiniteScroll — 独立闭环
7. Example04_Metaball — 独立闭环
8. Example05_Voronoi — 独立闭环
9. Example06_LoopTabBar — 独立闭环
10. Example07_ScrollRect — 独立闭环
11. Example08_GridView — 独立闭环
12. Example09_LoadTexture — 独立闭环
13. 集成与回归验证
14. 使用文档
```

每个 Example 使用统一任务模板：代码、资源、独立性、TEngine UI 规范、运行、验证。

### 11.4 specs/* 调整

现有 specs 增加以下场景：

- 每个 Example 可直接打开。
- HomeUI 打开失败后仍可操作。
- 资源集中存放且 Example 前缀命名。
- Visual Example 资源缺失只影响当前 Example。
- ScrollRect / GridView 不依赖 `FancyScrollerWidget`。
- 每个 Example 遵守 TEngine `UIWindow` / `UIWidget` 生命周期。

### 11.5 新增 fancy-examples-isolation spec

建议新增：

```text
openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-isolation/spec.md
```

作为总约束，覆盖 Example 独立窗口、失败隔离、资源归属、TEngine 合规。

## 12. 验收策略

### 12.1 单 Example 验收

- 可直接 `ShowUIAsync` 打开。
- 可从 HomeUI 打开。
- 重复打开/关闭 3 次无 Console Error。
- 资源清理正确。
- 事件清理正确。
- 状态不串到其他 Example。

### 12.2 HomeUI 验收

- 打开失败不影响其他 Example。
- 关闭失败不阻断新 Example。
- 快速切换有保护。
- 未实现 Example 不报错。
- HomeUI 销毁只关闭当前 Example。

### 12.3 资源验收

- 所有资源位于 `Assets/AssetRaw/UI/FancyScrollViewExamples/`。
- Example 专属资源使用 `ExampleXX_` 前缀。
- 不跨 Example 引用专属资源。
- 不使用 `Resources.Load`。

### 12.4 TEngine 合规验收

- `UIWindow` 使用 `[Window]`。
- `location` 与 Prefab 文件名一致。
- ScrollView 使用 `UIWidget`。
- Cell 使用 `FancyCellWidget` 或等价 Widget 生命周期适配。
- 节点前缀符合规范。
- 异步使用 UniTask。
- UI 事件随生命周期清理。
- 资源加载符合 YooAsset 规范。

### 12.5 回归验收

- 主 `GameScene` 不受影响。
- 主 Procedure 流程不受影响。
- EditMode 测试通过。
- 若某个 Example 被故意破坏，其它 Example 仍能直接验证。

## 13. 风险与应对

| 风险 | 应对 |
|---|---|
| 共享适配器出错影响多个 Scroller 示例 | 将其归类为基础设施问题；Example07/08 不依赖 `FancyScrollerWidget`，可独立验证 |
| HomeUI 出错影响导航 | 每个 Example 必须支持直接 `ShowUIAsync<T>()` 打开 |
| 某个 Example 资源缺失 | 错误日志带 Example 名称；不影响其他 Example |
| 资源集中后归属不清 | 强制 `ExampleXX_` 前缀 |
| 官方 MonoBehaviour 写法迁移不彻底 | 每个 Example 增加 TEngine 合规验收 |
| 异步图片加载串图 | Example09 使用 CancellationToken 和数据匹配校验 |
| Shader 平台差异 | 视觉示例错误隔离到单 Example |

## 14. 最终结论

本设计将 `fancy-scrollview-examples-porting` 定义为一套 TEngine 化独立示例体系，而不是简单复制官方 `MonoBehaviour` 示例。

每个 Example 都是独立 `UIWindow`，可直接打开，也可通过 HomeUI 导航打开；HomeUI 只负责导航；共享层保持最小；资源集中存放但通过 `ExampleXX_` 前缀保持归属；每个 Example 必须符合 TEngine `UIWindow` / `UIWidget`、YooAsset、UniTask 和事件生命周期规范。

最终目标是让任意单个 Example 的问题都能被限制在自身范围内，同时为后续新增 FancyScrollView 示例提供可复制的 TEngine 标准模板。