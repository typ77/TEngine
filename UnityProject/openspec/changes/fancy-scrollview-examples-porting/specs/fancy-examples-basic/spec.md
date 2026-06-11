## ADDED Requirements

### Requirement: Example01_BasicUI
系统 SHALL 提供 `Example01_BasicUI`（UIWindow），展示基础 FancyScrollView 列表。使用 `FancyScrollerWidget` 适配器。

#### Scenario: 显示 20 个 Cell
- **WHEN** Example01_BasicUI 打开
- **THEN** MUST 加载 Cell Prefab 并创建 20 个 ItemData（"Cell 0" ~ "Cell 19"），通过 UpdateData 传入 ScrollView

#### Scenario: Cell 显示消息文本
- **WHEN** Cell 的 BindData 被调用
- **THEN** MUST 将 ItemData.Message 显示到 Text 组件上

#### Scenario: Cell 位置动画
- **WHEN** Cell 的 UpdatePosition 被调用
- **THEN** MUST 通过 Animator 播放 scroll 动画，Animator.speed = 0 实现位置控制

### Requirement: Example02_FocusOnUI
系统 SHALL 提供 `Example02_FocusOnUI`（UIWindow），展示聚焦滚动功能。使用 `FancyScrollerWidget` 适配器。

#### Scenario: 点击 Cell 聚焦居中
- **WHEN** 用户点击某个 Cell
- **THEN** Context.OnCellClicked MUST 被触发，ScrollView MUST 平滑滚动到选中 Cell 并居中

#### Scenario: 选中态高亮
- **WHEN** Cell 的 UpdateContent 被调用
- **THEN** MUST 根据 Context.SelectedIndex == Index 设置选中/未选中样式

### Requirement: Example03_InfiniteScrollUI
系统 SHALL 提供 `Example03_InfiniteScrollUI`（UIWindow），展示无限循环滚动。使用 `FancyScrollerWidget` 适配器。

#### Scenario: 循环滚动
- **WHEN** 滚动到列表末尾
- **THEN** MUST 循环回到列表开头，数据不重复但位置连续

#### Scenario: 默认选中第一个
- **WHEN** Example03 启动
- **THEN** MUST 调用 SelectCell(0) 默认选中第一个 Cell

### Requirement: Basic Examples SHALL be independent TEngine UIWindows
Example01_BasicUI、Example02_FocusOnUI 和 Example03_InfiniteScrollUI SHALL 各自拥有独立的 UIWindow、Widget、Cell、ItemData、Context（如需）、Window Prefab、Cell Prefab 和 Example 专属资源。

#### Scenario: Example02 fails
- **WHEN** Example02_FocusOnUI 打开失败
- **THEN** Example01_BasicUI SHALL 仍可通过 `GameModule.UI.ShowUIAsync<T>()` 直接打开
- **AND** Example03_InfiniteScrollUI SHALL 仍可直接打开

#### Scenario: ItemData 数据模型
- **WHEN** 创建各 Example 的 ItemData
- **THEN** 每个 Example SHALL 拥有自己的 ItemData 类（如 Example01_BasicItemData）
- **AND** ItemData SHALL 包含 `string Message` 属性，构造函数接受 message 参数