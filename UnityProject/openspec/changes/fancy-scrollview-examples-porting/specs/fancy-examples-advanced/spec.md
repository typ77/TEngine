## ADDED Requirements

### Requirement: Example06_LoopTabBarUI
系统 SHALL 提供 `Example06_LoopTabBarUI`（UIWindow），展示循环 Tab 导航和窗口切换。使用 `FancyScrollerWidget` 适配器。

#### Scenario: Tab 导航
- **WHEN** Example06 启动
- **THEN** MUST 显示 Tab ScrollView 和对应的 Window 内容区域，选择 Tab 时切换 Window 内容

#### Scenario: 选中状态变更回调
- **WHEN** 用户滑动选择不同 Tab
- **THEN** ScrollView.OnSelectionChanged MUST 触发，切换当前显示的 Window（Out 动画 → In 动画）

#### Scenario: 默认选中第一个 Tab
- **WHEN** Example06 首次显示
- **THEN** MUST 调用 SelectCell(0) 默认选中第一个 Tab

### Requirement: Example09_LoadTextureUI
系统 SHALL 提供 `Example09_LoadTextureUI`（UIWindow），展示异步纹理加载。使用 `FancyScrollerWidget` 适配器。

#### Scenario: 异步加载纹理
- **WHEN** Cell 变为可见
- **THEN** MUST 通过 TextureLoader 异步加载对应索引的纹理资源

#### Scenario: 加载中占位显示
- **WHEN** 纹理正在加载
- **THEN** Cell MUST 显示占位 UI（如灰色背景或 Loading 指示器）

#### Scenario: 加载完成显示纹理
- **WHEN** 纹理加载完成
- **THEN** MUST 将纹理设置到 Image 组件上显示

#### Scenario: Cell 回收时释放纹理
- **WHEN** Cell 变为不可见或被回收
- **THEN** MUST 取消正在进行的纹理加载请求，释放已加载的纹理资源

### Requirement: 高级系列 Context 模式
Example 06 MUST 使用带 Context 的 FancyScrollerWidget，Context 包含选中状态回调。Example 09 无需 Context（使用 NullContext）。

#### Scenario: Example06 Context 结构
- **WHEN** Example06 的 Context 被创建
- **THEN** MUST 包含 `Action<int, MovementDirection> OnSelectionChanged` 回调