## Why

TEngine 已集成 FancyScrollView 核心引擎和部分 UIWidget 适配器，但缺少符合 TEngine UIWindow/UIWidget 规范的完整示例。官方 setchi/FancyScrollView 示例主要使用 MonoBehaviour 和场景直挂模式，不能直接作为 TEngine 热更 UI、YooAsset 资源管理和 UI 生命周期的参考。

本变更要把 9 个 FancyScrollView 示例建设为一套 TEngine 化独立示例体系。每个 Example 都是独立 UIWindow 单元，可直接通过 `GameModule.UI.ShowUIAsync<T>()` 打开，也可从 `FancyTestHomeUI` 导航打开。任意单个 Example 的错误必须限制在自身范围内，不影响其他 Example 验证。

## What Changes

- 新增 `FancyTestHomeUI` 导航窗口，只负责列出、打开、关闭 Example，不持有 Example 数据、Context、Cell、资源句柄或运行状态
- 移植 9 个 Example 为独立 UIWindow 单元，每个 Example 均可直接通过 `GameModule.UI.ShowUIAsync<T>()` 打开
  - 01_Basic — 基础列表
  - 02_FocusOn — 聚焦滚动
  - 03_InfiniteScroll — 无限循环
  - 04_Metaball — Shader Metaball 特效
  - 05_Voronoi — Shader Voronoi 特效
  - 06_LoopTabBar — Tab 导航切换
  - 07_ScrollRect — ScrollRect 模式（已有适配器）
  - 08_GridView — 网格布局（已有适配器）
  - 09_LoadTexture — 异步纹理加载
- 每个 Example 独立拥有 ItemData、Context、Cell、ScrollViewWidget、Window Prefab、Cell Prefab 和专属资源
- 新增 `FancyScrollerWidget` 基础 Scroller 适配器，但该适配器只承担通用 Scroller 桥接职责，不包含 Example 专属逻辑
- 统一集中管理 Example 资源到 `Assets/AssetRaw/UI/FancyScrollViewExamples/`
- 每个 Example 的专属资源使用 `ExampleXX_` 前缀表达归属
- 每个 Example 增加 TEngine UIWindow/UIWidget 合规验收标准

## 独立运行策略

每个 Example 都是独立的 UIWindow 级别演示单元。`FancyTestHomeUI` 仅作为导航聚合器，不得持有 Example 数据、Context、Cell 引用、资源句柄或运行状态。

每个 Example 必须能通过 `GameModule.UI.ShowUIAsync<T>()` 直接打开。Example 特有的代码、Prefab、资源、ItemData、Context、Cell Widget 和运行状态必须在 Example 内部隔离。任意单个 Example 失败不得阻止其他 Example 打开。

## 资源组织策略

所有 FancyScrollView 示例资源集中在以下目录：

`Assets/AssetRaw/UI/FancyScrollViewExamples/`

按资源类型分组：

- `Prefabs/Windows/`
- `Prefabs/Cells/`
- `Materials/`
- `Shaders/`
- `Textures/`
- `Animations/`
- `Shared/`

每个 Example 专属资源必须使用 `ExampleXX_` 前缀。集中存储表达模块归属，前缀保持每个 Example 的独立所有权和故障隔离。

## TEngine 合规要求

每个 Example 必须实现为 TEngine 合规的 UIWindow/UIWidget 参考。原始 MonoBehaviour 示例必须适配为 TEngine UI 生命周期，不得作为场景直挂脚本复制。

每个 Example 必须遵循：

- `[Window]` 注册
- `GameModule.UI.ShowUIAsync<T>()` 打开
- 基于 UIWidget 的 ScrollView 和 Cell 生命周期
- TEngine 节点前缀命名规范
- YooAsset / SetSprite / LoadGameObjectAsync 资源规范
- UniTask 异步规范
- AddUIEvent 生命周期清理规范（在使用 GameEvent 时）

## Capabilities

### New Capabilities

- `fancy-scroller-widget`: 基础 Scroller 引擎适配器，桥接 FancyScrollView + Scroller 到 UIWidget 生命周期，不包含 Example 专属逻辑
- `fancy-test-harness`: 独立测试场景基础设施（TestScene + ProcedureTestSample + FancyTestHomeUI 导航窗口）
- `fancy-examples-basic`: 基础滚动系列 Example（01-03）的独立 UIWindow 移植
- `fancy-examples-visual`: 视觉特效系列 Example（04-05）的独立 UIWindow 移植，含 Shader 资源
- `fancy-examples-advanced`: 高级交互系列 Example（06、09）的独立 UIWindow 移植，含 Tab 导航和异步加载模式
- `fancy-examples-scrollrect`: ScrollRect/GridView 系列 Example（07-08）的独立 UIWindow 移植
- `fancy-examples-isolation`: 所有 Example 的独立运行、故障隔离、资源归属和 TEngine 生命周期总约束

### Modified Capabilities

（无已有 spec 需要修改）

## Impact

- **新增文件**：约 50-70 个文件（.cs 代码、.prefab、.unity Scene、.asset、Shader 资源、动画资源）
- **代码位置**：`Assets/GameScripts/HotFix/GameLogic/` 下的 UI 和 Module 目录
- **程序集**：全部在 HotFix 程序集内（不需要修改 Main 或 Runtime 程序集）
- **不影响主流程**：Procedure 配置独立，TestScene 与主 GameScene 完全隔离
- **依赖**：已有的 FancyScrollViewWidget、FancyScrollRectWidget、FancyGridViewWidget、FancyCellWidget 适配器
- **Shader 资源**：Example 04/05 需要 Metaball.shader 和 Voronoi.shader 及配套 .hlsl/.mat
- **动画资源**：Example 01-06 需要迁移 Animator Controller 和 Animation Clip
- 每个 Example 的代码和资源互相隔离，单个 Example 故障不应影响其他 Example
- HomeUI 失败时，Example 仍应可通过直接 `ShowUIAsync<T>()` 方式验证
- 资源集中在 FancyScrollViewExamples 模块目录下，便于后续新增 Example
- 本变更不修改主 GameScene 和主流程链