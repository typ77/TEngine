## ADDED Requirements

### Requirement: Example07_ScrollRectUI
系统 SHALL 提供 `Example07_ScrollRectUI`（UIWindow），展示 FancyScrollRect 模式。使用 `FancyScrollRectWidget` 适配器。

#### Scenario: 显示可配置列表
- **WHEN** Example07 打开
- **THEN** MUST 显示 ScrollView + 输入控件（PaddingTop、PaddingBottom、Spacing、DataCount、SelectIndex）+ Alignment Dropdown

#### Scenario: 实时调整 Padding/Spacing
- **WHEN** 用户修改 Padding 或 Spacing 输入框
- **THEN** ScrollView MUST 实时重新布局（Relayout）

#### Scenario: 调整数据数量
- **WHEN** 用户修改 DataCount 输入框
- **THEN** MUST 重新生成指定数量的 ItemData 并更新 ScrollView 内容

#### Scenario: 定位到指定 Cell
- **WHEN** 用户修改 SelectIndex 或切换 Alignment
- **THEN** MUST 平滑滚动到指定索引位置，使用对应的 Alignment 对齐方式

#### Scenario: Cell 点击回调
- **WHEN** 用户点击某个 Cell
- **THEN** Context.OnCellClicked MUST 被触发，SelectIndex 输入框 MUST 更新为点击的索引值

#### Scenario: Cell 正弦波动画
- **WHEN** Cell 的 UpdatePosition 被调用
- **THEN** MUST 在基础位置上叠加正弦波水平偏移 `sin(normalizedPosition * PI * 2) * 65`

#### Scenario: Cell 选中态样式
- **WHEN** Cell 的 BindData 被调用
- **THEN** MUST 根据 Context.SelectedIndex == Index 设置选中（青色半透明）或未选中（白色半透明）样式

### Requirement: Example08_GridViewUI
系统 SHALL 提供 `Example08_GridViewUI`（UIWindow），展示 FancyGridView 网格布局。使用 `FancyGridViewWidget` 适配器。

#### Scenario: 显示网格列表
- **WHEN** Example08 打开
- **THEN** MUST 显示 GridView + 输入控件（PaddingTop、PaddingBottom、XSpacing、YSpacing、DataCount、SelectIndex）+ Alignment Dropdown

#### Scenario: 网格参数配置
- **WHEN** 用户修改 XSpacing/YSpacing
- **THEN** GridView MUST 实时更新交叉轴和主轴间距

#### Scenario: 网格数据数量调整
- **WHEN** 用户修改 DataCount 输入框
- **THEN** MUST 重新生成指定数量的 ItemData 并更新 GridView 内容

#### Scenario: 网格 Cell 定位
- **WHEN** 用户修改 SelectIndex 或切换 Alignment
- **THEN** MUST 平滑滚动到指定索引，使用对应的 Alignment 对齐方式

### Requirement: ScrollRect and GridView Examples SHALL be independent
Example07 和 Example08 SHALL 各自拥有独立的 Context、Cell、Widget、ItemData 和 Prefab。它们 SHALL NOT 共享 Context、Cell 或 Widget 类。

#### Scenario: FancyScrollerWidget unavailable
- **WHEN** FancyScrollerWidget 出现回归问题
- **THEN** Example07_ScrollRectUI 和 Example08_GridViewUI SHALL 在自身适配器有效时仍可直接打开

#### Scenario: Context 结构
- **WHEN** Example07 或 Example08 的 Context 被创建
- **THEN** 各 Example SHALL 拥有自己的 Context 类
- **AND** Context SHALL 包含 `Action<int> OnCellClicked` 和 `int SelectedIndex` 字段
- **AND** Example07 的 Context SHALL 实现 `IFancyScrollRectContext`，Example08 的 Context SHALL 实现 `IFancyGridViewContext`

### Requirement: Alignment 枚举
Example 07-08 SHALL 支持 Alignment 枚举用于定位对齐。

#### Scenario: Alignment 选项
- **WHEN** Alignment Dropdown 显示
- **THEN** MUST 包含 Upper、Middle、Lower 三个选项，映射到 0.0f、0.5f、1.0f 的 alignment 值