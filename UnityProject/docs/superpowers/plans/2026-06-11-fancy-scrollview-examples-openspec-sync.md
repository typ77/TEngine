# FancyScrollView Examples OpenSpec 同步实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将 `openspec/changes/fancy-scrollview-examples-porting/` 下的 OpenSpec 文档同步到已确认并提交的“独立示例体系设计”。

**架构：** 本计划只修改文档，不改 Unity 代码和资源。同步顺序为：先改提案与设计，明确独立窗口、HomeUI 导航、资源集中和 TEngine 合规原则；再重写任务清单；最后补齐 spec 场景，并新增 `fancy-examples-isolation` 总约束。

**技术栈：** Markdown、OpenSpec change artifacts、TEngine 设计规范、Git。

---

## 文件结构与职责

### 需要修改的文件

- 修改：`openspec/changes/fancy-scrollview-examples-porting/proposal.md`
  - 职责：说明为什么要做、做什么、影响范围；补充独立运行、资源集中、TEngine 合规三个核心目标。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/design.md`
  - 职责：作为 OpenSpec 内的技术设计主文档；与 `docs/superpowers/specs/2026-06-11-fancy-scrollview-examples-porting-design.md` 的设计原则保持一致，但更聚焦于实施约束。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/tasks.md`
  - 职责：把原线性移植任务重写为“共享最小基础设施 + HomeUI 导航器 + 每个 Example 独立闭环 + 集成验收”。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-scroller-widget/spec.md`
  - 职责：约束 `FancyScrollerWidget` 是通用适配器，不包含 Example 专属逻辑。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-test-harness/spec.md`
  - 职责：约束 TestScene、Procedure、HomeUI 导航和失败隔离。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-basic/spec.md`
  - 职责：约束 Example01-03 的独立窗口、独立数据模型、TEngine 合规与失败隔离。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-visual/spec.md`
  - 职责：约束 Example04-05 的集中资源、Example 前缀、视觉资源失败隔离。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-advanced/spec.md`
  - 职责：约束 Example06、Example09 的独立状态、Tab 状态隔离、异步图片加载规范。

- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-scrollrect/spec.md`
  - 职责：约束 Example07-08 不依赖 `FancyScrollerWidget`，各自使用独立 Context / Cell / Widget。

### 需要创建的文件

- 创建：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-isolation/spec.md`
  - 职责：新增总约束，集中表达所有 Example 必须独立运行、故障隔离、资源集中、TEngine UI 合规。

### 不应修改的文件

- 不修改：`Assets/**`
- 不修改：`UserSettings/**`
- 不修改：`docs/superpowers/specs/2026-06-11-fancy-scrollview-examples-porting-design.md`
- 不处理：当前工作区中已有的 `.meta` 删除/新增和 `bash.exe.stackdump`

---

## 任务 1：同步 `proposal.md`

**文件：**
- 修改：`openspec/changes/fancy-scrollview-examples-porting/proposal.md`

- [ ] **步骤 1：查看现有 proposal 内容**

运行：

```bash
git diff -- openspec/changes/fancy-scrollview-examples-porting/proposal.md
```

预期：如果尚未修改，命令无输出；如果已有未提交修改，先阅读输出，确认是否与本计划一致。

- [ ] **步骤 2：重写 Why 段落**

将 `## Why` 改为包含以下要点：

```md
TEngine 已集成 FancyScrollView 核心引擎和部分 UIWidget 适配器，但缺少符合 TEngine UIWindow/UIWidget 规范的完整示例。官方 setchi/FancyScrollView 示例主要使用 MonoBehaviour 和场景直挂模式，不能直接作为 TEngine 热更 UI、YooAsset 资源管理和 UI 生命周期的参考。

本变更要把 9 个 FancyScrollView 示例建设为一套 TEngine 化独立示例体系。每个 Example 都是独立 UIWindow 单元，可直接通过 `GameModule.UI.ShowUIAsync<T>()` 打开，也可从 `FancyTestHomeUI` 导航打开。任意单个 Example 的错误必须限制在自身范围内，不影响其他 Example 验证。
```

- [ ] **步骤 3：重写 What Changes 列表**

保留原有 9 个 Example 清单，并新增或替换为以下核心条目：

```md
- 新增 `FancyTestHomeUI` 导航窗口，只负责列出、打开、关闭 Example，不持有 Example 数据、Context、Cell、资源句柄或运行状态
- 移植 9 个 Example 为独立 UIWindow 单元，每个 Example 均可直接通过 `GameModule.UI.ShowUIAsync<T>()` 打开
- 每个 Example 独立拥有 ItemData、Context、Cell、ScrollViewWidget、Window Prefab、Cell Prefab 和专属资源
- 新增 `FancyScrollerWidget` 基础 Scroller 适配器，但该适配器只承担通用 Scroller 桥接职责，不包含 Example 专属逻辑
- 统一集中管理 Example 资源到 `Assets/AssetRaw/UI/FancyScrollViewExamples/`
- 每个 Example 的专属资源使用 `ExampleXX_` 前缀表达归属
- 每个 Example 增加 TEngine UIWindow/UIWidget 合规验收标准
```

- [ ] **步骤 4：新增 Isolation Strategy 小节**

在 `## Capabilities` 前插入：

```md
## Isolation Strategy

Each Example is an independent UIWindow-level demo unit. `FancyTestHomeUI` is only a navigation aggregator and must not own Example data, Context, Cell references, resource handles, or runtime state.

Every Example must be directly openable through `GameModule.UI.ShowUIAsync<T>()`. Example-specific code, Prefabs, resources, ItemData, Context, Cell widgets, and runtime state must remain isolated by Example. A failure in one Example must not prevent other Examples from opening.
```

- [ ] **步骤 5：新增 Asset Organization 小节**

在 `Isolation Strategy` 后插入：

```md
## Asset Organization

All FancyScrollView example assets are centrally stored under:

`Assets/AssetRaw/UI/FancyScrollViewExamples/`

Assets are grouped by resource type:

- `Prefabs/Windows/`
- `Prefabs/Cells/`
- `Materials/`
- `Shaders/`
- `Textures/`
- `Animations/`
- `Shared/`

Every Example-specific asset must use an `ExampleXX_` prefix. Centralized storage expresses module ownership, while the prefix preserves per-Example ownership and failure isolation.
```

- [ ] **步骤 6：新增 TEngine Compliance 小节**

在 `Asset Organization` 后插入：

```md
## TEngine Compliance

Every Example must be implemented as a TEngine-compliant UIWindow/UIWidget reference. The original MonoBehaviour examples must be adapted into TEngine UI lifecycle instead of being copied as scene-attached scripts.

Each Example must follow:

- `[Window]` registration
- `GameModule.UI.ShowUIAsync<T>()` opening
- UIWidget-based ScrollView and Cell lifecycle
- TEngine node prefix naming
- YooAsset / SetSprite / LoadGameObjectAsync resource rules
- UniTask async rules
- AddUIEvent lifecycle cleanup rules where GameEvent is used
```

- [ ] **步骤 7：调整 Impact 小节**

在 `## Impact` 中加入：

```md
- 每个 Example 的代码和资源互相隔离，单个 Example 故障不应影响其他 Example
- HomeUI 失败时，Example 仍应可通过直接 `ShowUIAsync<T>()` 方式验证
- 资源集中在 FancyScrollViewExamples 模块目录下，便于后续新增 Example
- 本变更不修改主 GameScene 和主流程链
```

- [ ] **步骤 8：验证 proposal 没有旧方向冲突**

运行：

```bash
rg -n "共享相同的 ItemData|Resources 加载|Resources.Load|HomeUI.*数据|统一管理.*资源释放" openspec/changes/fancy-scrollview-examples-porting/proposal.md
```

预期：无输出。如果有输出，逐条确认是否为禁止语境；不是禁止语境时改写。

---

## 任务 2：同步 `design.md`

**文件：**
- 修改：`openspec/changes/fancy-scrollview-examples-porting/design.md`

- [ ] **步骤 1：用新结构重写 design 主体**

将 `design.md` 重写为以下章节结构：

```md
## Context
## Goals / Non-Goals
## Design Principles
### Independent Example Windows
### Navigation-Only HomeUI
### Minimal Shared Infrastructure
### Centralized Example Assets
### TEngine UIWindow/UIWidget Compliance
## Architecture
### Code Layout
### Asset Layout
### Runtime Entry Flow
## Shared Infrastructure Boundary
### Allowed Shared Code
### Forbidden Shared Code
### Shared Static State Rules
## HomeUI Failure Isolation
### Entry Table
### Open Flow
### Close Flow
### Unimplemented Example Flow
### Fast Switching Guard
## Example Template
### UIWindow Template
### ScrollViewWidget Template
### Cell Template
### ItemData Template
### Context Template
## Example Scope
### Example01_Basic
### Example02_FocusOn
### Example03_InfiniteScroll
### Example04_Metaball
### Example05_Voronoi
### Example06_LoopTabBar
### Example07_ScrollRect
### Example08_GridView
### Example09_LoadTexture
## Resource Policy
## Event Policy
## Async Policy
## Verification Strategy
## Risks / Trade-offs
```

- [ ] **步骤 2：写入设计原则**

在 `## Design Principles` 中写入：

```md
### Independent Example Windows
Every Example is an independent UIWindow and must be directly openable through `GameModule.UI.ShowUIAsync<T>()`.

### Navigation-Only HomeUI
`FancyTestHomeUI` is a navigation-only UIWindow. It maintains only an ExampleEntry table and the current selected entry. It does not hold Example data, Context, Cell references, resource handles, or runtime state.

### Minimal Shared Infrastructure
Shared infrastructure is limited to generic FancyScrollView engines, TEngine UI adapters, and navigation utilities. Example-specific behavior must stay inside each Example directory.

### Centralized Example Assets
All Example assets are stored under `Assets/AssetRaw/UI/FancyScrollViewExamples/` and grouped by resource type. Example-specific assets use `ExampleXX_` prefixes.

### TEngine UIWindow/UIWidget Compliance
Every Example is a TEngine UI reference implementation. It must adapt original MonoBehaviour logic into UIWindow/UIWidget lifecycle and TEngine resource/event/async conventions.
```

- [ ] **步骤 3：写入代码与资源结构**

在 `## Architecture` 中加入代码结构和资源结构。代码结构使用：

```text
Assets/GameScripts/HotFix/GameLogic/
├── Module/UIModule/FancyScrollView/
└── UI/FancyScrollViewExamples/
    ├── FancyTestHomeUI/
    ├── Common/
    ├── Example01_Basic/
    └── ...
```

资源结构使用：

```text
Assets/AssetRaw/UI/FancyScrollViewExamples/
├── Prefabs/Windows/
├── Prefabs/Cells/
├── Materials/
├── Shaders/
├── Textures/
├── Animations/
└── Shared/
```

- [ ] **步骤 4：写入 HomeUI 失败隔离流程**

在 `## HomeUI Failure Isolation` 中加入：

```text
用户选择 Example
  -> 如果正在切换，忽略本次请求
  -> best-effort 关闭当前 Example
  -> 如果目标未实现，显示未实现状态
  -> try 打开目标 Example
  -> 成功：记录 current，显示 Opened
  -> 失败：清空 current，显示 Failed，HomeUI 保持可操作
```

并明确：关闭失败不阻止打开其他 Example；打开失败不禁用 Dropdown。

- [ ] **步骤 5：写入 Example 代码模板约束**

在 `## Example Template` 中说明每个 Example 必须有：

```md
- `ExampleXX_NameUI`
- `ExampleXX_NameScrollViewWidget`
- `ExampleXX_NameCell`
- `ExampleXX_NameItemData`
- `ExampleXX_NameContext`（如需要）
```

并说明：`UIWindow` 负责直接打开和构造测试数据；`ScrollViewWidget` 负责滚动控制；`Cell` 负责显示与交互；`ItemData` 不持有 UI 组件或资源句柄；`Context` 只保存当前 Example 的运行状态。

- [ ] **步骤 6：写入 9 个 Example 范围摘要**

在 `## Example Scope` 中为每个 Example 写 4 行摘要：目标、独立代码、独立资源、验收重点。必须包含：

```md
Example01_Basic: 基础列表、20 个 Cell、Cell 复用和位置动画。
Example02_FocusOn: 点击聚焦、SelectedIndex、选中态不串位。
Example03_InfiniteScroll: 无限循环、首尾连续、循环索引不影响普通列表。
Example04_Metaball: Metaball 视觉资源独立，资源缺失只影响 Example04。
Example05_Voronoi: Voronoi 视觉资源独立，不复用 Example04。
Example06_LoopTabBar: Tab 状态和内容切换状态只在 Example06 内部保存。
Example07_ScrollRect: 使用 FancyScrollRectWidget，不依赖 FancyScrollerWidget。
Example08_GridView: 使用 FancyGridViewWidget，不复用 Example07 的 Context / Cell。
Example09_LoadTexture: 使用 SetSprite，快速滚动不串图，关闭时取消加载。
```

- [ ] **步骤 7：写入资源、事件、异步策略**

在对应小节写入：

```md
Resource Policy:
- Sprite uses SetSprite / SetSubSprite.
- GameObject uses LoadGameObjectAsync or existing UIModule/Widget flow.
- Non-GameObject assets use LoadAssetAsync<T> and UnloadAsset.
- Resources.Load is forbidden.

Event Policy:
- UI event listeners are registered in RegisterEvent.
- GameEvent listeners inside UI use AddUIEvent.
- Cell events flow through Context or Widget event to UIWindow.

Async Policy:
- UniTask is required.
- Coroutine is forbidden.
- Async loading must support cancellation when the Example is destroyed or the Cell is reused.
```

- [ ] **步骤 8：验证 design 没有冲突内容**

运行：

```bash
rg -n "共享.*ItemData|共享.*Context|Resources.Load|Resources 加载|HomeUI.*Context|HomeUI.*资源句柄" openspec/changes/fancy-scrollview-examples-porting/design.md
```

预期：无输出，或只出现在“禁止”语境中。

---

## 任务 3：重写 `tasks.md`

**文件：**
- 修改：`openspec/changes/fancy-scrollview-examples-porting/tasks.md`

- [ ] **步骤 1：替换为新任务结构**

将 `tasks.md` 改为以下一级结构：

```md
## 0. 文档与规范修正
## 1. 共享最小基础设施
## 2. FancyTestHomeUI — 导航器
## 3. FancyScrollerWidget — 基础 Scroller 适配器
## 4. Example01_Basic — 独立闭环
## 5. Example02_FocusOn — 独立闭环
## 6. Example03_InfiniteScroll — 独立闭环
## 7. Example04_Metaball — 独立闭环
## 8. Example05_Voronoi — 独立闭环
## 9. Example06_LoopTabBar — 独立闭环
## 10. Example07_ScrollRect — 独立闭环
## 11. Example08_GridView — 独立闭环
## 12. Example09_LoadTexture — 独立闭环
## 13. 集成与回归验证
## 14. 使用文档
```

- [ ] **步骤 2：写入文档与规范修正任务**

在 `## 0. 文档与规范修正` 下写入：

```md
- [ ] 更新 proposal.md，补充 Example 独立运行、资源集中管理、TEngine 合规要求
- [ ] 更新 design.md，重写为独立 Example 体系设计
- [ ] 更新 tasks.md，统一每个 Example 的独立闭环验收模板
- [ ] 更新 specs/*，补充故障隔离、资源集中、TEngine 合规场景
- [ ] 新增 specs/fancy-examples-isolation/spec.md 总约束
```

- [ ] **步骤 3：写入 HomeUI 任务**

在 `## 2. FancyTestHomeUI — 导航器` 下写入：

```md
- [ ] 创建 FancyTestHomeUI.cs，标记 `[Window]`
- [ ] 创建 FancyExampleEntry，包含 DisplayName / IsImplemented / OpenAsync / Close
- [ ] 建立 9 个 Example 的显式 Entry 表
- [ ] Dropdown 从 Entry 表生成
- [ ] 未实现 Example 显示未实现状态，不触发异常
- [ ] 打开 Example 使用 try/catch，失败只显示当前 Example 错误
- [ ] 关闭当前 Example 使用 best-effort，不阻塞打开其他 Example
- [ ] 加入 `_isSwitching` 防止快速切换并发
- [ ] HomeUI 不持有 Example 数据、Context、Cell、资源句柄
- [ ] HomeUI 销毁时只关闭当前 Example，不调用 CloseAll
- [ ] 验证 Example 打开失败后仍可继续打开其他 Example
```

- [ ] **步骤 4：写入 FancyScrollerWidget 任务**

在 `## 3. FancyScrollerWidget — 基础 Scroller 适配器` 下写入：

```md
- [ ] 创建 FancyScrollerWidget<TItemData, TContext>
- [ ] 绑定 Scroller 与 CellContainer
- [ ] 提供 UpdateData / ScrollTo / JumpTo 基础 API
- [ ] 通过虚方法创建当前 Example 的 Cell Widget
- [ ] 中转 Cell 点击事件
- [ ] 销毁时清理自身事件
- [ ] 不生成 Example 测试数据
- [ ] 不持有 Example 资源状态
- [ ] 不处理 FocusOn / Infinite / LoopTabBar 等具体业务逻辑
- [ ] 编写基础生命周期测试
```

- [ ] **步骤 5：为每个 Example 写统一模板**

每个 `ExampleXX` 章节都包含以下小节和检查项；根据 Example 名称替换 `ExampleXX_Name`：

```md
### 代码
- [ ] 创建 `ExampleXX_NameUI.cs`
- [ ] 创建 `ExampleXX_NameScrollViewWidget.cs`
- [ ] 创建 `ExampleXX_NameCell.cs`
- [ ] 创建 `ExampleXX_NameItemData.cs`
- [ ] 创建 `ExampleXX_NameContext.cs`（如需要）

### 资源
- [ ] 创建 `ExampleXX_NameUI.prefab`
- [ ] 创建 `ExampleXX_NameCell.prefab`
- [ ] 迁移本 Example 专属动画 / 材质 / 贴图 / Shader（如需要）
- [ ] 所有资源位于 `Assets/AssetRaw/UI/FancyScrollViewExamples/`
- [ ] 所有资源名使用 `ExampleXX_` 前缀

### 独立性
- [ ] 不复用其他 Example 的 ItemData / Context / Cell / Widget
- [ ] 不复用其他 Example 的 Window Prefab / Cell Prefab
- [ ] 不依赖 HomeUI 的 UserData 或共享状态
- [ ] 不使用共享可变静态状态
- [ ] 当前 Example 失败不影响其他 Example

### TEngine UI 规范
- [ ] UIWindow 使用 `[Window]` 标记，location 与窗口 Prefab 文件名一致
- [ ] UIWindow 可通过 `GameModule.UI.ShowUIAsync<T>()` 直接打开
- [ ] UIWindow 生命周期职责清晰：ScriptGenerator / RegisterEvent / OnCreate / OnRefresh / OnDestroy
- [ ] ScrollView 使用 UIWidget / FancyScrollView Widget 适配器管理
- [ ] Cell 使用 FancyCellWidget 或等价 TEngine Widget 生命周期适配
- [ ] Prefab 节点命名符合 TEngine 前缀规范
- [ ] UI 内事件使用 Unity UI 事件或 AddUIEvent，并随生命周期清理
- [ ] 资源加载符合 YooAsset / SetSprite / LoadGameObjectAsync 规范
- [ ] 不使用 Resources.Load
- [ ] 不使用 Coroutine
- [ ] 不绕过 UIModule 手动 Instantiate UIWindow

### 运行
- [ ] 支持 `GameModule.UI.ShowUIAsync<ExampleXX_NameUI>()` 直接打开
- [ ] 支持从 `FancyTestHomeUI` 打开
- [ ] 关闭当前 Example 后可打开其他 Example

### 验证
- [ ] 重复打开/关闭 3 次无 Console Error
- [ ] 当前 Example 打开失败不影响 HomeUI 和其他 Example
- [ ] OnDestroy 清理当前 Example 的事件、资源和取消令牌
```

- [ ] **步骤 6：为特殊 Example 增加专项检查**

在对应章节额外加入：

```md
Example04_Metaball:
- [ ] Metaball Material / Shader / HLSL 位于集中资源目录并使用 Example04_ 前缀
- [ ] 缺失视觉资源时日志包含 `[Example04_Metaball]`

Example05_Voronoi:
- [ ] Voronoi Material / Shader / HLSL 位于集中资源目录并使用 Example05_ 前缀
- [ ] Example05 不复用 Example04 的视觉资源

Example06_LoopTabBar:
- [ ] Tab 选中状态和 Window 切换状态只保存在 Example06 内部
- [ ] 快速切换 Tab 不影响其他 Example

Example07_ScrollRect:
- [ ] 使用 FancyScrollRectWidget，不依赖 FancyScrollerWidget
- [ ] 非法输入在 Example07 内部兜底

Example08_GridView:
- [ ] 使用 FancyGridViewWidget，不复用 Example07 的 Context / Cell
- [ ] 网格参数调整只影响 Example08

Example09_LoadTexture:
- [ ] 使用 Image.SetSprite 加载 Sprite
- [ ] 不使用 LoadAssetAsync<Sprite>
- [ ] 快速滚动不串图
- [ ] 关闭窗口时取消未完成加载
```

- [ ] **步骤 7：写入集成与回归验证任务**

在 `## 13. 集成与回归验证` 下写入：

```md
- [ ] 逐个直接打开 9 个 Example UIWindow
- [ ] 逐个从 FancyTestHomeUI 打开 9 个 Example
- [ ] 任意一个 Example 打开失败时 HomeUI 仍可切换到其他 Example
- [ ] 破坏 Example04 的 Material 后，Example05 仍可打开
- [ ] 破坏 Example02 的 Prefab 后，Example01 / Example03 仍可打开
- [ ] HomeUI 销毁时只关闭当前 Example
- [ ] 主 GameScene 与主 Procedure 流程不受影响
- [ ] 全部 Example 无 Resources.Load / Coroutine / LoadAssetAsync<Sprite>
- [ ] 全部 Example 的 Window / Cell Prefab 位于集中资源目录
- [ ] 全部 Example 的资源使用 ExampleXX_ 前缀
- [ ] 运行 EditMode 测试
```

- [ ] **步骤 8：验证 tasks 没有旧共享模型**

运行：

```bash
rg -n "共享相同|共享 Context|Resources.Load|LoadAssetAsync<Sprite>|HomeUI.*数据" openspec/changes/fancy-scrollview-examples-porting/tasks.md
```

预期：只允许 `Resources.Load` 和 `LoadAssetAsync<Sprite>` 出现在禁止项或验收检查中；不允许出现“共享相同 ItemData”这类旧模型。

---

## 任务 4：同步 `fancy-scroller-widget/spec.md`

**文件：**
- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-scroller-widget/spec.md`

- [ ] **步骤 1：保留现有适配器基础 Requirement**

保留现有关于初始化、InternalScrollView、Scroller 事件绑定、`UpdateData` API 的 Requirement。

- [ ] **步骤 2：新增 Example-agnostic Requirement**

追加：

```md
### Requirement: FancyScrollerWidget SHALL remain Example-agnostic
FancyScrollerWidget SHALL provide only generic Scroller integration and SHALL NOT contain Example-specific behavior, data generation, visual state, resource ownership, or Example selection logic.

#### Scenario: Example-specific FocusOn logic
- **WHEN** Example02 implements click-to-focus behavior
- **THEN** the behavior SHALL be implemented in Example02_FocusOnScrollViewWidget or Example02_FocusOnContext
- **AND** FancyScrollerWidget SHALL NOT contain Example02-specific logic

#### Scenario: Example-specific visual resources
- **WHEN** Example04 or Example05 uses Shader or Material resources
- **THEN** those resources SHALL be owned by the Example-specific UIWindow / Widget / Prefab
- **AND** FancyScrollerWidget SHALL NOT load or cache those resources
```

- [ ] **步骤 3：新增 TEngine lifecycle Requirement**

追加：

```md
### Requirement: FancyScrollerWidget SHALL create Cells through TEngine Widget lifecycle
FancyScrollerWidget SHALL create Example Cells through a Widget-compatible creation path so Cell ScriptGenerator, RegisterEvent, OnCreate, BindData, UpdatePosition, and OnDestroy responsibilities remain valid.

#### Scenario: Cell created for Example01
- **WHEN** Example01_BasicScrollViewWidget creates a Cell
- **THEN** the Cell SHALL be an Example01_BasicCell
- **AND** the Cell SHALL be initialized through the FancyCellWidget lifecycle adapter
```

- [ ] **步骤 4：验证 spec 无示例业务泄漏**

运行：

```bash
rg -n "生成.*ItemData|Metaball|Voronoi|LoopTabBar|LoadTexture" openspec/changes/fancy-scrollview-examples-porting/specs/fancy-scroller-widget/spec.md
```

预期：如果出现 `Metaball` / `Voronoi` / `LoopTabBar` / `LoadTexture`，只能在“不得包含 Example 逻辑”的场景中出现。

---

## 任务 5：同步 `fancy-test-harness/spec.md`

**文件：**
- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-test-harness/spec.md`

- [ ] **步骤 1：保留 TestScene / Procedure / ProcedureSetting Requirement**

保留现有关于 `FancyScrollViewTestScene`、`ProcedureTestSample`、`TestProcedureSetting.asset` 的 Requirement。

- [ ] **步骤 2：调整 FancyTestHomeUI Requirement**

将 HomeUI 描述改为：

```md
### Requirement: FancyTestHomeUI navigation window
系统 SHALL 提供 `FancyTestHomeUI` 类继承 `UIWindow`，作为导航窗口列出 9 个 Example。FancyTestHomeUI SHALL NOT own Example data, Context, Cell references, resource handles, or runtime state.
```

- [ ] **步骤 3：新增 Entry 表场景**

追加：

```md
#### Scenario: Explicit ExampleEntry table
- **WHEN** FancyTestHomeUI initializes
- **THEN** it SHALL build an explicit Entry table for all 9 Examples
- **AND** each Entry SHALL contain DisplayName, IsImplemented, OpenAsync, and Close behavior
```

- [ ] **步骤 4：新增失败隔离 Requirement**

追加：

```md
### Requirement: HomeUI navigation SHALL isolate Example failures
FancyTestHomeUI SHALL remain interactive when opening or closing an Example fails.

#### Scenario: Example open fails
- **WHEN** the user selects an Example whose open operation throws
- **THEN** HomeUI SHALL show a failure status for that Example
- **AND** HomeUI SHALL keep the Dropdown interactive
- **AND** the user SHALL be able to open another Example

#### Scenario: Example close fails
- **WHEN** HomeUI fails to close the current Example
- **THEN** HomeUI SHALL log a warning
- **AND** HomeUI SHALL still attempt to open the selected Example
```

- [ ] **步骤 5：新增直接打开 Requirement**

追加：

```md
### Requirement: Each Example SHALL support direct opening
Each Example UIWindow SHALL be openable directly without FancyTestHomeUI.

#### Scenario: Direct open
- **WHEN** `GameModule.UI.ShowUIAsync<ExampleXX_NameUI>()` is called
- **THEN** the Example SHALL initialize its own data and display normally
- **AND** it SHALL NOT require UserData from FancyTestHomeUI
```

- [ ] **步骤 6：验证 HomeUI 不再是运行核心**

运行：

```bash
rg -n "HomeUI.*数据|HomeUI.*Context|HomeUI.*资源|预加载.*Example" openspec/changes/fancy-scrollview-examples-porting/specs/fancy-test-harness/spec.md
```

预期：无输出，或只出现在否定约束中。

---

## 任务 6：同步 Basic / Visual / Advanced / ScrollRect specs

**文件：**
- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-basic/spec.md`
- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-visual/spec.md`
- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-advanced/spec.md`
- 修改：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-scrollrect/spec.md`

- [ ] **步骤 1：修正 Basic spec 中旧共享 ItemData 模型**

删除或替换以下旧 Requirement：

```md
### Requirement: 基础系列共享 ItemData
Example 01-03 SHALL 共享相同的 ItemData 结构（包含 string Message 字段），但各自独立的 Context（如需）。
```

替换为：

```md
### Requirement: Basic Examples SHALL be independent TEngine UIWindows
Example01_BasicUI, Example02_FocusOnUI, and Example03_InfiniteScrollUI SHALL each own their UIWindow, Widget, Cell, ItemData, Context when needed, Window Prefab, Cell Prefab, and Example-specific resources.

#### Scenario: Example02 fails
- **WHEN** Example02_FocusOnUI fails to open
- **THEN** Example01_BasicUI SHALL still be openable directly
- **AND** Example03_InfiniteScrollUI SHALL still be openable directly
```

- [ ] **步骤 2：补充 Visual spec 资源集中与隔离 Requirement**

追加：

```md
### Requirement: Visual Example assets SHALL be centrally stored but Example-prefixed
Visual Example assets SHALL be stored under `Assets/AssetRaw/UI/FancyScrollViewExamples/` and use Example-specific prefixes.

#### Scenario: Metaball material missing
- **WHEN** Example04_MetaballBackground is missing
- **THEN** Example04_MetaballUI SHALL report an Example04-specific error
- **AND** Example05_VoronoiUI SHALL remain openable

#### Scenario: Voronoi shader missing
- **WHEN** Example05_Voronoi shader assets are missing
- **THEN** Example05_VoronoiUI SHALL report an Example05-specific error
- **AND** Example04_MetaballUI SHALL remain openable if its own resources are valid
```

- [ ] **步骤 3：修正 Visual spec 中共享 Context 模式**

将：

```md
Example 04-05 SHALL 使用带 Context 的 FancyScrollerWidget，Context 包含选中索引回调。
```

替换为：

```md
Example04 and Example05 SHALL define their own Example-specific Context when Context is needed. They SHALL NOT share Context, Background component, Material, Shader, Cell, or Widget classes.
```

- [ ] **步骤 4：补充 Advanced spec 独立状态 Requirement**

追加：

```md
### Requirement: Advanced Examples SHALL own runtime state
Advanced Examples SHALL keep their runtime state inside their own UIWindow, Widget, Cell, or Context.

#### Scenario: LoopTabBar state changes
- **WHEN** Example06 changes selected tab
- **THEN** no other Example SHALL observe or depend on that state

#### Scenario: LoadTexture cell is reused
- **WHEN** Example09_LoadTextureCell is reused for another ItemData
- **THEN** it SHALL reset loading state
- **AND** it SHALL cancel or ignore obsolete image requests
```

- [ ] **步骤 5：补充 ScrollRect spec 与 Scroller 隔离 Requirement**

追加：

```md
### Requirement: ScrollRect and GridView Examples SHALL not depend on FancyScrollerWidget
Example07 and Example08 SHALL use their dedicated adapters and SHALL remain verifiable independently of Scroller Examples.

#### Scenario: FancyScrollerWidget unavailable
- **WHEN** FancyScrollerWidget has a regression
- **THEN** Example07_ScrollRectUI and Example08_GridViewUI SHALL still be directly openable if their own adapters are valid
```

- [ ] **步骤 6：验证旧共享要求被移除**

运行：

```bash
rg -n "共享相同|共享 Context|共享 ItemData|共享 Context 模式" openspec/changes/fancy-scrollview-examples-porting/specs
```

预期：无输出。如果需要描述“不共享”，使用 “SHALL NOT share” 或中文“不得共享”。

---

## 任务 7：新增 `fancy-examples-isolation/spec.md`

**文件：**
- 创建：`openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-isolation/spec.md`

- [ ] **步骤 1：创建 spec 目录和文件**

运行：

```bash
mkdir -p openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-isolation
```

创建文件内容：

```md
## ADDED Requirements

### Requirement: Examples SHALL be isolated UIWindow units
Each Example SHALL be implemented as an independent UIWindow unit with its own code, Prefabs, resources, data, Context when needed, and runtime state.

#### Scenario: One Example fails
- **WHEN** any single Example fails to open
- **THEN** other Examples SHALL remain directly openable
- **AND** FancyTestHomeUI SHALL remain interactive

### Requirement: Examples SHALL comply with TEngine UI lifecycle
Each Example SHALL follow TEngine UIWindow/UIWidget lifecycle and resource conventions.

#### Scenario: Example UIWindow opens
- **WHEN** an Example UIWindow is opened through `GameModule.UI.ShowUIAsync<T>()`
- **THEN** it SHALL initialize through ScriptGenerator, RegisterEvent, OnCreate, and OnRefresh
- **AND** it SHALL clean up its own resources through OnDestroy

### Requirement: Example assets SHALL be centralized and prefixed
Each Example-specific asset SHALL be stored under `Assets/AssetRaw/UI/FancyScrollViewExamples/` and use an `ExampleXX_` prefix.

#### Scenario: Example-specific asset lookup
- **WHEN** a Window Prefab, Cell Prefab, Material, Shader, Texture, or Animation belongs to Example04
- **THEN** its asset name SHALL begin with `Example04_`
- **AND** it SHALL NOT be referenced by another Example as a required resource

### Requirement: Shared mutable Example state SHALL be forbidden
Shared infrastructure SHALL NOT store mutable runtime state for the current Example, current Context, current Items, current Cell, or current resource handles.

#### Scenario: Switching between Examples
- **WHEN** the user switches from Example02 to Example03
- **THEN** Example02 runtime state SHALL NOT be reused by Example03
- **AND** Example03 SHALL initialize its own runtime state
```

- [ ] **步骤 2：验证新 spec 文件存在**

运行：

```bash
test -f openspec/changes/fancy-scrollview-examples-porting/specs/fancy-examples-isolation/spec.md && echo exists
```

预期输出：

```text
exists
```

---

## 任务 8：全量文档验证与提交

**文件：**
- 验证：`openspec/changes/fancy-scrollview-examples-porting/**/*.md`

- [ ] **步骤 1：扫描占位符和旧方向**

运行：

```bash
rg -n "待定|TBD|TODO|后续实现|共享相同|共享 ItemData|共享 Context 模式" openspec/changes/fancy-scrollview-examples-porting
```

预期：无输出。若 `TODO` 仅用于 HomeUI UI 文案，改为中文“未实现”避免误判。

- [ ] **步骤 2：扫描禁止 API 是否只在禁止语境中出现**

运行：

```bash
rg -n "Resources\.Load|Coroutine|LoadAssetAsync<Sprite>|LoadAssetAsync<GameObject>" openspec/changes/fancy-scrollview-examples-porting
```

预期：这些词只出现在“禁止使用”“不得使用”“无 Resources.Load”这类约束语境中。

- [ ] **步骤 3：确认 OpenSpec 变更文件列表**

运行：

```bash
git status --short openspec/changes/fancy-scrollview-examples-porting
```

预期：只显示 `openspec/changes/fancy-scrollview-examples-porting/` 下的修改和新增文件。

- [ ] **步骤 4：查看 diff**

运行：

```bash
git diff -- openspec/changes/fancy-scrollview-examples-porting
```

预期：diff 仅包含 OpenSpec 文档同步，不包含 Unity 代码、Prefab、Scene、UserSettings 或 `.meta` 修复。

- [ ] **步骤 5：提交 OpenSpec 文档同步**

运行：

```bash
git add openspec/changes/fancy-scrollview-examples-porting
git commit -m "docs: sync FancyScrollView examples OpenSpec" -m "Co-Authored-By: Claude <noreply@anthropic.com>"
```

预期：提交成功，提交内容只包含 OpenSpec 文档。

- [ ] **步骤 6：提交后验证**

运行：

```bash
git log -1 --oneline -- openspec/changes/fancy-scrollview-examples-porting
git status --short
```

预期：最新提交为 `docs: sync FancyScrollView examples OpenSpec`。`git status --short` 中仍可能显示本计划执行前已存在的无关工作区变更，但不应出现已暂存的 OpenSpec 变更。

---

## 自检清单

- [ ] `proposal.md` 覆盖独立窗口、HomeUI 导航、资源集中、TEngine 合规。
- [ ] `design.md` 不再推荐 `Resources` 加载，也不再把 HomeUI 作为 Example 运行核心。
- [ ] `tasks.md` 中每个 Example 都有“代码、资源、独立性、TEngine UI 规范、运行、验证”检查项。
- [ ] `fancy-examples-basic/spec.md` 不再要求 Example01-03 共享 ItemData。
- [ ] `fancy-examples-visual/spec.md` 不再要求 Example04-05 共享 Context。
- [ ] `fancy-examples-isolation/spec.md` 已新增，并覆盖直接打开、失败隔离、资源归属、TEngine 生命周期。
- [ ] 所有文档中的禁止 API 只出现在禁止或验收语境中。
- [ ] 本同步不修改 `Assets/**`、`UserSettings/**` 或 `docs/superpowers/specs/2026-06-11-fancy-scrollview-examples-porting-design.md`。
