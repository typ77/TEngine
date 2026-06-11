## ADDED Requirements

### Requirement: FancyScrollerWidget 适配器基类
系统 SHALL 提供 `FancyScrollerWidget<TItemData, TContext>` 抽象类，继承 `FancyScrollViewWidget<TItemData, TContext>`，桥接 `FancyScrollView` + `Scroller` 引擎到 UIWidget 生命周期。TContext MUST 有无参构造函数。

#### Scenario: 初始化并显示列表
- **WHEN** 子类调用 `InitializeAsync(cellAssetLocation)` 加载 Cell Prefab 成功后，调用 `UpdateData(items)` 传入数据
- **THEN** InternalScrollView MUST 调用 `UpdateContents(items)` 刷新内容，Scroller MUST 调用 `SetTotalCount(items.Count)` 设置总数

#### Scenario: ScriptGenerator 查找 Scroller 组件
- **WHEN** FancyScrollerWidget 执行 ScriptGenerator
- **THEN** MUST 通过 `FindChildComponent<Scroller>("m_scroller")` 查找 Scroller 组件，通过 `FindChildReference<Transform>("m_cellContainer")` 查找 Cell 容器

### Requirement: InternalScrollView 内部桥接
系统 SHALL 提供内部类 `InternalScrollView` 继承 `FancyScrollView<TItemData, TContext>`，作为引擎桥接。

#### Scenario: CellPrefab 委托到 Owner
- **WHEN** InternalScrollView 需要 Cell Prefab
- **THEN** MUST 返回 Owner Widget 的 `CellPrefab` 属性（由 `InitializeAsync` 加载）

#### Scenario: CreateCell 委托到 Owner
- **WHEN** InternalScrollView 需要创建 Cell
- **THEN** MUST 委托到 Owner Widget 的 `CreateCellForInternal()` 方法，确保 UIWidget 生命周期管理

### Requirement: Scroller 事件绑定
FancyScrollerWidget MUST 在 OnCreate 中将 Scroller 的 OnValueChanged 事件绑定到 InternalScrollView 的 UpdatePosition。

#### Scenario: 滚动位置同步
- **WHEN** 用户拖拽 Scroller
- **THEN** Scroller.OnValueChanged MUST 触发 InternalScrollView.UpdatePosition，刷新所有可见 Cell 的位置

### Requirement: UpdateData 公开 API
FancyScrollerWidget SHALL 提供 `UpdateData(IList<TItemData> items)` 方法，同时更新内容和 Scroller 总数。

#### Scenario: 更新数据源
- **WHEN** 外部调用 `UpdateData(items)`
- **THEN** MUST 先调用 InternalScrollView.UpdateContents(items) 再调用 Scroller.SetTotalCount(items.Count)