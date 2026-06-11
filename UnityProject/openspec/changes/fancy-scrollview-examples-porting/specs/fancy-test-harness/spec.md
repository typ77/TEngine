## ADDED Requirements

### Requirement: FancyScrollViewTestScene
系统 SHALL 提供独立的 Unity Scene 文件 `FancyScrollViewTestScene.unity`，包含 Camera、Directional Light、UIRoot（Canvas + EventSystem）和 GameEntry 组件。

#### Scenario: 场景启动流程
- **WHEN** 在 Unity Editor 中打开 FancyScrollViewTestScene 并点击 Play
- **THEN** GameEntry.Awake() MUST 初始化 Module 系统，并通过独立的 ProcedureSetting 启动 ProcedureTestSample

### Requirement: ProcedureTestSample
系统 SHALL 提供 `ProcedureTestSample` 类继承 `ProcedureBase`，作为测试入口 Procedure。

#### Scenario: 直接启动测试 UI
- **WHEN** ProcedureTestSample.OnEnter() 被调用
- **THEN** MUST 调用 `GameModule.UI.ShowUIAsync<FancyTestHomeUI>()` 显示测试主界面，跳过所有 Splash/Download/HotFix 流程

#### Scenario: 无需热更加载
- **WHEN** ProcedureTestSample 运行
- **THEN** MUST NOT 触发 ProcedureLoadAssembly、ProcedureInitResources 等热更流程

### Requirement: 独立 ProcedureSetting
系统 SHALL 提供独立的 `ProcedureSetting` ScriptableObject（`TestProcedureSetting.asset`），仅包含 ProcedureTestSample。

#### Scenario: 配置隔离
- **WHEN** FancyScrollViewTestScene 中的 GameEntry 使用 TestProcedureSetting
- **THEN** availableProcedureTypeNames MUST 仅包含 `ProcedureTestSample`，entranceProcedureTypeName MUST 为 `ProcedureTestSample`

### Requirement: FancyTestHomeUI 主界面
系统 SHALL 提供 `FancyTestHomeUI` 类继承 `UIWindow`，带有 Dropdown 组件用于切换 9 个 Example。

#### Scenario: Dropdown 显示所有 Example
- **WHEN** FancyTestHomeUI 被打开
- **THEN** Dropdown MUST 包含以下选项：01_Basic、02_FocusOn、03_InfiniteScroll、04_Metaball、05_Voronoi、06_LoopTabBar、07_ScrollRect、08_GridView、09_LoadTexture

#### Scenario: 切换 Example 时关闭旧窗口打开新窗口
- **WHEN** 用户选择 Dropdown 中的某个 Example
- **THEN** 系统 MUST 先关闭当前打开的 Example UIWindow（如有），再通过 `GameModule.UI.ShowUIAsync` 打开选中的 Example UIWindow

#### Scenario: 默认显示第一个 Example
- **WHEN** FancyTestHomeUI 首次打开
- **THEN** MUST 自动选中并打开 01_Basic Example

#### Scenario: Window 特性标记
- **WHEN** FancyTestHomeUI 类定义
- **THEN** MUST 标记 `[Window(UILayer.UI, location: "FancyTestHomeUI")]` 特性