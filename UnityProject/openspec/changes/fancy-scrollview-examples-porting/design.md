## Context

TEngine 项目已集成 FancyScrollView 核心引擎并完成了 `FancyScrollRectWidget`（ScrollRect 模式）和 `FancyGridViewWidget`（网格模式）的适配。这两个适配器将原始 MonoBehaviour 模式转换为 UIWidget 生命周期，支持异步 Prefab 加载、事件冒泡和 MVE 数据绑定。

当前缺失：
1. **基础 Scroller 引擎适配器**：原项目 9 个例子中有 7 个（01-06、09）使用 `FancyScrollView` + `Scroller` 引擎，我们只有 ScrollRect 和 GridView 的适配器
2. **完整使用示例**：开发者无法直观了解如何在 TEngine UIWindow 架构中使用这些组件
3. **独立调试入口**：当前主流程必须走完 Splash → Download → HotFix 链路，调试 UI 效率低

约束：
- 所有代码位于 HotFix 程序集（`GameScripts/HotFix/`）
- 使用 `UIWindow` + `[Window]` 特性注册窗口
- 使用 `UIWidget` 作为嵌入式组件
- 资源通过 YooAsset `LoadAssetAsync` 异步加载
- 测试 Scene 与主 GameScene 完全隔离

## Goals / Non-Goals

**Goals:**
- 完整移植原项目 9 个 Example，保持视觉效果和交互体验一致
- 新增 `FancyScrollerWidget` 适配器，补齐基础滚动引擎的 UIWidget 桥接
- 提供独立测试 Scene + Procedure，可一键启动调试
- 每个 Example 作为独立 UIWindow，通过 FancyTestHomeUI 的 Dropdown 切换
- 代码风格遵循 TEngine 规范：异步优先、GameModule 访问、资源释放

**Non-Goals:**
- 不修改已有 FancyScrollView 核心引擎代码（FancyScrollView.cs、FancyScrollRect.cs、FancyGridView.cs）
- 不修改已有适配器（FancyScrollRectWidget、FancyGridViewWidget、FancyCellWidget）的 API
- 不修改主流程（GameEntry、ProcedureSplash 等）
- 不做 Editor 工具或自动化测试（纯运行时示例）

## Design Principles

### Independent Example Windows

每个 Example 是独立的 UIWindow，必须能通过 `GameModule.UI.ShowUIAsync<T>()` 直接打开。Example 特有的代码、Prefab、资源、ItemData、Context、Cell Widget 和运行状态必须在 Example 内部隔离。

### Navigation-Only HomeUI

`FancyTestHomeUI` 是纯导航窗口。它只维护 ExampleEntry 表和当前选中的条目。它不持有 Example 数据、Context、Cell 引用、资源句柄或运行状态。

### Minimal Shared Infrastructure

共享基础设施仅限于通用 FancyScrollView 引擎、TEngine UI 适配器和导航工具。Example 专属行为必须留在各自 Example 目录内。

### Centralized Example Assets

所有 Example 资源存储在 `Assets/AssetRaw/UI/FancyScrollViewExamples/` 下，按资源类型分组。Example 专属资源使用 `ExampleXX_` 前缀。

### TEngine UIWindow/UIWidget Compliance

每个 Example 是 TEngine UI 参考实现。必须将原始 MonoBehaviour 逻辑适配为 UIWindow/UIWidget 生命周期和 TEngine 资源/事件/异步规范。

## Architecture

### Code Layout

```text
Assets/GameScripts/HotFix/GameLogic/
├── Module/UIModule/FancyScrollView/       ← 引擎 + 通用适配器
│   ├── FancyScrollViewWidget.cs
│   ├── FancyScrollRectWidget.cs
│   ├── FancyGridViewWidget.cs
│   ├── FancyScrollerWidget.cs             ← 新增：基础 Scroller 适配器
│   └── FancyCellWidget.cs
└── UI/FancyScrollViewExamples/            ← Example 实现
    ├── FancyTestHomeUI/
    │   └── FancyTestHomeUI.cs
    ├── Common/                            ← 仅放纯工具类
    ├── Example01_Basic/
    │   ├── Example01_BasicUI.cs           (UIWindow)
    │   ├── Example01_BasicScrollViewWidget.cs
    │   ├── Example01_BasicCell.cs
    │   └── Example01_BasicItemData.cs
    ├── Example02_FocusOn/
    │   ├── Example02_FocusOnUI.cs
    │   ├── Example02_FocusOnScrollViewWidget.cs
    │   ├── Example02_FocusOnCell.cs
    │   ├── Example02_FocusOnItemData.cs
    │   └── Example02_FocusOnContext.cs
    └── ...（每个 Example 同结构）
```

### Asset Layout

```text
Assets/AssetRaw/UI/FancyScrollViewExamples/
├── Prefabs/Windows/                       ← Window Prefabs
├── Prefabs/Cells/                         ← Cell Prefabs
├── Materials/                             ← Material 资源
├── Shaders/                               ← Shader + HLSL
├── Textures/                              ← 贴图
├── Animations/                            ← Animator Controller + Clip
└── Shared/                                ← 多 Example 共用的只读资源
```

### Runtime Entry Flow

```text
TestScene
  → GameEntry (挂载 TestProcedureSetting)
    → ProcedureTestSample.OnEnter()
      → GameModule.UI.ShowUIAsync<FancyTestHomeUI>()
        → Dropdown.onValueChanged(index)
          → Close current Example (best-effort)
          → ShowUIAsync<ExampleXX_NameUI>()
```

## Decisions

### D1: 测试入口架构 — 新 Scene + 新 ProcedureSetting

**选择**：新建 `FancyScrollViewTestScene`，配置独立的 `ProcedureSetting` 仅含 `ProcedureTestSample`。

**备选方案**：
- A) 复用主 Scene，通过编译开关切换入口 → 仍需走完 Procedure 链，调试慢
- B) 纯 MonoBehaviour 入口脚本 → 需手动初始化 Module 系统，与架构脱节

**理由**：方案最干净，ProcedureSetting 是 ScriptableObject 可多实例，TestScene 与主流程零耦合。GameEntry 已经在场景中，只需换一个 ProcedureSetting asset 指向新的入口 Procedure。

### D2: FancyScrollerWidget 设计 — 沿用 FancyScrollRectWidget 模式

**选择**：`FancyScrollerWidget<TItemData, TContext>` 继承 `FancyScrollViewWidget`，内嵌 `InternalScrollView` 桥接 `FancyScrollView` + `Scroller`。

**模式**：
```
FancyScrollerWidget<TItemData, TContext> : FancyScrollViewWidget
  ├── ScriptGenerator → 查找 "m_scroller" (Scroller) + "m_cellContainer" (Transform)
  ├── OnCreate → 创建 InternalScrollView，注入 Scroller + CellPrefab
  ├── UpdateData(items) → InternalScrollView.UpdateContents() + Scroller.SetTotalCount()
  └── InternalScrollView : FancyScrollView<TItemData, TContext>
        ├── CellPrefab → owner.CellPrefab
        └── CreateCell → owner.CreateCellForInternal()
```

**理由**：与 FancyScrollRectWidget 保持一致的适配模式，降低学习成本。Scroller 组件在 Prefab 上由 Inspector 配置，运行时由 Widget 统一初始化。

### D3: Cell 创建策略 — FancyCellWidget + BindData 模式

**选择**：所有 Cell 继承 `FancyCellWidget<TItemData, TContext>`，通过 `BindData(TItemData)` 绑定数据，`UpdatePosition(float)` 处理位置动画。

**与原项目差异**：
- 原项目：Cell 是 MonoBehaviour，通过 `[SerializeField]` 引用 UI 组件
- 我们：Cell 是 UIWidget，通过 `FindChild` / ScriptGenerator 引用 UI 组件
- 原项目 Cell Prefab 上挂 Cell 脚本 → 我们 Cell Prefab 不挂脚本，运行时由 Widget 动态创建

**理由**：UIWidget 模式统一生命周期管理，支持 MVE 数据绑定，且与现有 FancyCellWidget 基类一致。

### D4: 每个 Example 的文件组织

**选择**：每个 Example 独立目录，包含所有类型，使用 `ExampleXX_` 前缀。

**理由**：每个 Example 自包含，开发者可单独参考某个目录学习对应模式。前缀确保类型名唯一，避免跨 Example 冲突。

### D5: FancyTestHomeUI — Dropdown 切换，Close Old + Open New

**选择**：FancyTestHomeUI 维护当前打开的 Example Window 引用，Dropdown `onValueChanged` 时关闭旧窗口并打开新窗口。

**理由**：与用户需求一致（新老切换，关闭老的开启新的），简单直接。

### D6: Shader 资源处理（Example 04/05）

**选择**：将 Metaball 和 Voronoi 的 Shader（.shader + .hlsl + .mat）复制到集中资源目录 `Assets/AssetRaw/UI/FancyScrollViewExamples/Shaders/`，使用 `Example04_` / `Example05_` 前缀。

**理由**：Shader 通过 YooAsset 标准流程加载，不使用 Resources.Load。集中存储 + 前缀保证资源归属清晰。

## Shared Infrastructure Boundary

### Allowed Shared Code

- FancyScrollView 核心引擎（FancyScrollView / FancyScrollRect / FancyGridView）
- TEngine UI 适配器（FancyScrollViewWidget / FancyScrollRectWidget / FancyGridViewWidget / FancyScrollerWidget / FancyCellWidget）
- FancyTestHomeUI 导航逻辑

### Forbidden Shared Code

- 不得在 Example 之间共享 ItemData、Context、Cell、ScrollViewWidget 类
- 不得在共享代码中包含任何 Example 专属的业务逻辑
- FancyScrollerWidget 不得生成测试数据或持有 Example 运行状态

### Shared Static State Rules

- 共享基础设施不得存储可变的 Example 运行时状态（当前 Example、当前 Context、当前 Items、当前 Cell、当前资源句柄）
- Example 之间的状态隔离由各自 UIWindow 的实例状态保证

## HomeUI Failure Isolation

### Entry Table

FancyTestHomeUI 初始化时建立显式 Entry 表，每个 Entry 包含：
- `DisplayName`：Dropdown 显示名
- `IsImplemented`：是否已实现
- `OpenAsync()`：打开对应 Example
- `Close()`：关闭对应 Example

### Open Flow

```text
用户选择 Example
  → 如果正在切换（_isSwitching），忽略本次请求
  → best-effort 关闭当前 Example
  → 如果目标未实现，显示未实现状态
  → try 打开目标 Example
  → 成功：记录 current，显示 Opened
  → 失败：清空 current，显示 Failed，HomeUI 保持可操作
```

### Close Flow

关闭失败不阻止打开其他 Example。关闭使用 best-effort 策略：捕获异常并记录警告，不中断后续流程。

### Unimplemented Example Flow

未实现的 Example 在 Dropdown 中仍可见但标记为不可用。选择时显示"未实现"状态，不触发异常。

### Fast Switching Guard

使用 `_isSwitching` 标志防止快速切换并发。切换完成（无论成功失败）后重置标志。

## Example Template

每个 Example 必须包含以下类型（`ExampleXX_Name` 为具体替换值）：

### UIWindow Template

```csharp
[Window(UILayer.UI, location: "ExampleXX_NameUI")]
public class ExampleXX_NameUI : UIWindow
{
    // ScriptGenerator 引用 ScrollViewWidget
    // OnCreate → 构造测试数据
    // OnRefresh → 更新 ScrollViewWidget 数据
    // OnDestroy → 清理事件、资源、取消令牌
}
```

### ScrollViewWidget Template

```csharp
public class ExampleXX_NameScrollViewWidget : FancyScrollerWidget<ExampleXX_NameItemData, ...>
{
    // 或继承 FancyScrollRectWidget / FancyGridViewWidget（视 Example 而定）
    // 负责滚动控制
    // 通过虚方法创建 Example 专属 Cell
}
```

### Cell Template

```csharp
public class ExampleXX_NameCell : FancyCellWidget<ExampleXX_NameItemData, ...>
{
    // BindData → 绑定数据到 UI 组件
    // UpdatePosition → 处理位置动画
    // 点击事件通过 Context 或 Widget 事件传递到 UIWindow
}
```

### ItemData Template

```csharp
public class ExampleXX_NameItemData
{
    // 纯数据模型
    // 不持有 UI 组件或资源句柄
}
```

### Context Template

```csharp
public class ExampleXX_NameContext
{
    // 只保存当前 Example 的运行状态
    // 如：选中索引回调、滚动状态
}
```

## Example Scope

### Example01_Basic

- **目标**：基础列表
- **独立代码**：Example01_BasicUI / BasicScrollViewWidget / BasicCell / BasicItemData
- **独立资源**：Window Prefab + Cell Prefab + Animation
- **验收重点**：20 个 Cell、Cell 复用和位置动画

### Example02_FocusOn

- **目标**：点击聚焦
- **独立代码**：Example02_FocusOnUI / FocusOnScrollViewWidget / FocusOnCell / FocusOnItemData / FocusOnContext
- **独立资源**：Window Prefab + Cell Prefab + Animation
- **验收重点**：SelectedIndex、选中态不串位

### Example03_InfiniteScroll

- **目标**：无限循环
- **独立代码**：Example03_InfiniteScrollUI / InfiniteScrollViewWidget / InfiniteScrollCell / InfiniteScrollItemData
- **独立资源**：Window Prefab + Cell Prefab
- **验收重点**：首尾连续、循环索引不影响普通列表

### Example04_Metaball

- **目标**：Shader 特效
- **独立代码**：Example04_MetaballUI / MetaballScrollViewWidget / MetaballCell / MetaballItemData / MetaballContext
- **独立资源**：Window Prefab + Cell Prefab + Example04_MetaballBackground Material / Shader / HLSL
- **验收重点**：视觉资源独立，资源缺失只影响 Example04

### Example05_Voronoi

- **目标**：Shader 特效
- **独立代码**：Example05_VoronoiUI / VoronoiScrollViewWidget / VoronoiCell / VoronoiItemData / VoronoiContext
- **独立资源**：Window Prefab + Cell Prefab + Example05_VoronoiBackground Material / Shader / HLSL
- **验收重点**：视觉资源独立，不复用 Example04

### Example06_LoopTabBar

- **目标**：Tab 导航切换
- **独立代码**：Example06_LoopTabBarUI / LoopTabBarScrollViewWidget / LoopTabBarCell / LoopTabBarItemData / LoopTabBarContext
- **独立资源**：Window Prefab + Cell Prefab + Animation
- **验收重点**：Tab 状态和内容切换状态只在 Example06 内部保存

### Example07_ScrollRect

- **目标**：ScrollRect 模式
- **独立代码**：Example07_ScrollRectUI / ScrollRectViewWidget（继承 FancyScrollRectWidget）/ ScrollRectCell / ScrollRectItemData / ScrollRectContext
- **独立资源**：Window Prefab + Cell Prefab + Animation
- **验收重点**：使用 FancyScrollRectWidget，不依赖 FancyScrollerWidget

### Example08_GridView

- **目标**：网格布局
- **独立代码**：Example08_GridViewUI / GridViewWidget（继承 FancyGridViewWidget）/ GridViewCell / GridItemData / GridContext
- **独立资源**：Window Prefab + Cell Prefab
- **验收重点**：使用 FancyGridViewWidget，不复用 Example07 的 Context / Cell

### Example09_LoadTexture

- **目标**：异步纹理加载
- **独立代码**：Example09_LoadTextureUI / LoadTextureScrollViewWidget / LoadTextureCell / LoadTextureItemData
- **独立资源**：Window Prefab + Cell Prefab
- **验收重点**：使用 SetSprite、快速滚动不串图、关闭时取消加载

## Resource Policy

- Sprite 使用 `SetSprite` / `SetSubSprite`
- GameObject 使用 `LoadGameObjectAsync` 或现有 UIModule/Widget 流程
- 非 GameObject 资源使用 `LoadAssetAsync<T>` + `UnloadAsset`
- Resources.Load 禁止使用
- 所有 Example 资源位于 `Assets/AssetRaw/UI/FancyScrollViewExamples/`
- 所有 Example 资源名使用 `ExampleXX_` 前缀

## Event Policy

- UI 事件监听器在 `RegisterEvent` 中注册
- UI 内部 GameEvent 监听使用 `AddUIEvent`
- Cell 事件通过 Context 或 Widget 事件传递到 UIWindow
- 事件监听器随 UIWindow/UIWidget 生命周期清理

## Async Policy

- 必须使用 UniTask
- 禁止使用 Coroutine
- 异步加载必须支持 Example 销毁或 Cell 复用时的取消操作

## Verification Strategy

- 每个 Example 可通过 `GameModule.UI.ShowUIAsync<T>()` 直接打开
- 每个 Example 可从 FancyTestHomeUI 导航打开
- 重复打开/关闭 3 次无 Console Error
- 单个 Example 失败不影响 HomeUI 和其他 Example
- 主 GameScene 和主 Procedure 流程不受影响

## Risks / Trade-offs

- **[Scroller 适配器复杂度]** → 原项目 Scroller 是独立的滚动计算组件，与 ScrollRect 模式差异较大。缓解：先实现 FancyScrollerWidget 骨架，用 Example01 验证后再批量移植 02-06
- **[Prefab 创建工作量]** → 9 个 Example 各需独立 Prefab（Window Prefab + Cell Prefab），约 18+ 个 Prefab。缓解：基础结构可复制复用，利用 Unity Editor 批量创建
- **[动画资源迁移]** → Cell 的 Animator Controller + Animation Clip 需从原项目迁移。缓解：直接从 GitHub 下载原项目资源，保持原始动画参数
- **[ScrollView vs UIWidget 命名冲突]** → 原项目类名 `ScrollView` 与 UnityEngine.UI.ScrollView 冲突。缓解：使用带前缀的类名如 `Example01_BasicScrollViewWidget`
- **[04/05 Shader 平台兼容]** → Metaball/Voronoi Shader 可能在某些平台表现不同。缓解：保持原始 Shader 代码不变，作为纯视觉演示