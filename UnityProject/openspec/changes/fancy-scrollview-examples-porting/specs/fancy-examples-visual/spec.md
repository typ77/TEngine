## ADDED Requirements

### Requirement: Example04_MetaballUI
系统 SHALL 提供 `Example04_MetaballUI`（UIWindow），展示 Metaball Shader 视觉特效。使用 `FancyScrollerWidget` 适配器。

#### Scenario: Cell 带位置动画
- **WHEN** Cell 的 UpdatePosition 被调用
- **THEN** MUST 通过 Animator 播放 scroll 动画控制位置

#### Scenario: Metaball 背景特效
- **WHEN** Example04 场景运行
- **THEN** MUST 显示 Background 组件，使用 Metaball Shader 渲染动态背景

### Requirement: Example05_VoronoiUI
系统 SHALL 提供 `Example05_VoronoiUI`（UIWindow），展示 Voronoi Shader 视觉特效。使用 `FancyScrollerWidget` 适配器。

#### Scenario: Cell 带位置动画
- **WHEN** Cell 的 UpdatePosition 被调用
- **THEN** MUST 通过 Animator 播放 scroll 动画控制位置

#### Scenario: Voronoi 背景特效
- **WHEN** Example05 场景运行
- **THEN** MUST 显示 Background 组件，使用 Voronoi Shader 渲染动态背景

### Requirement: Shader 资源迁移
系统 MUST 包含原项目的 Shader 资源文件。

#### Scenario: Metaball Shader 文件
- **WHEN** 构建 Example04
- **THEN** MUST 包含 Metaball.shader、Metaball.hlsl、Metaball.mat 资源

#### Scenario: Voronoi Shader 文件
- **WHEN** 构建 Example05
- **THEN** MUST 包含 Voronoi.shader、Voronoi.hlsl、Voronoi.mat 资源

### Requirement: Visual Examples SHALL be independent with Example-prefixed assets
Example04 和 Example05 SHALL 各自拥有独立的 Context、Background 组件、Material、Shader、Cell 和 Widget 类。资源集中存储在 `Assets/AssetRaw/UI/FancyScrollViewExamples/` 下，使用 Example 专属前缀。

#### Scenario: Metaball material missing
- **WHEN** Example04_MetaballBackground 资源缺失
- **THEN** Example04_MetaballUI SHALL 报告 Example04 专属错误
- **AND** Example05_VoronoiUI SHALL 保持可打开

#### Scenario: Voronoi shader missing
- **WHEN** Example05_Voronoi shader 资源缺失
- **THEN** Example05_VoronoiUI SHALL 报告 Example05 专属错误
- **AND** Example04_MetaballUI SHALL 在自身资源有效时保持可打开

#### Scenario: Context 结构
- **WHEN** Example04 或 Example05 的 Context 被创建
- **THEN** 各 Example SHALL 拥有自己的 Context 类（如 Example04_MetaballContext）
- **AND** Context SHALL 包含 `Action<int> OnCellClicked` 和 `int SelectedIndex` 字段