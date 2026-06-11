## 0. 文档与规范修正

- [ ] 0.1 更新 proposal.md，补充独立运行、资源集中管理、TEngine 合规要求
- [ ] 0.2 更新 design.md，重写为独立 Example 体系设计
- [ ] 0.3 更新 tasks.md，统一每个 Example 的独立闭环验收模板
- [ ] 0.4 更新 specs/*，补充故障隔离、资源集中、TEngine 合规场景
- [ ] 0.5 新增 specs/fancy-examples-isolation/spec.md 总约束

## 1. 基础设施 — 测试 Scene 和 Procedure

- [ ] 1.1 创建 `ProcedureTestSample` 类（继承 ProcedureBase），OnEnter 中调用 `GameModule.UI.ShowUIAsync<FancyTestHomeUI>()`
- [ ] 1.2 创建 `TestProcedureSetting.asset`（ScriptableObject），仅包含 ProcedureTestSample
- [ ] 1.3 创建 `FancyScrollViewTestScene.unity`，包含 Camera、Directional Light、UIRoot、GameEntry（挂载 TestProcedureSetting）
- [ ] 1.4 验证：在 Editor 中打开 TestScene 并 Play，确认 ProcedureTestSample 能启动且无报错

## 2. FancyTestHomeUI — 测试主界面

- [ ] 2.1 创建 `FancyTestHomeUI` 类（UIWindow），标记 `[Window(UILayer.UI, location: "FancyTestHomeUI")]`
- [ ] 2.2 创建 `FancyTestHomeUI.prefab`，包含 Dropdown + 标题 Text
- [ ] 2.3 实现 Dropdown 初始化：填充 9 个 Example 选项
- [ ] 2.4 实现 onValueChanged 切换逻辑：关闭旧 Example Window → ShowUIAsync 新 Example Window
- [ ] 2.5 验证：Play 后 Dropdown 显示所有选项，切换时无报错

## 3. FancyScrollerWidget 适配器

- [ ] 3.1 创建 `FancyScrollerWidget<TItemData, TContext>` 抽象类，继承 `FancyScrollViewWidget`
- [ ] 3.2 实现 ScriptGenerator：查找 `m_scroller`（Scroller）和 `m_cellContainer`（Transform）
- [ ] 3.3 实现内部类 `InternalScrollView : FancyScrollView<TItemData, TContext>`，桥接 CellPrefab 和 CreateCell
- [ ] 3.4 实现 OnCreate：创建 InternalScrollView，绑定 Scroller.OnValueChanged → InternalScrollView.UpdatePosition
- [ ] 3.5 实现 `UpdateData(IList<TItemData>)` 公开 API
- [ ] 3.6 编写 EditMode 单元测试验证 FancyScrollerWidget 基本生命周期
- [ ] 3.7 验证：测试通过

## 4. Example01_Basic — 基础列表

- [ ] 4.1 创建 `ItemData.cs`（string Message）
- [ ] 4.2 创建 `BasicCell.cs`（FancyCellWidget），实现 BindData + UpdatePosition（Animator 动画）
- [ ] 4.3 迁移 Cell Prefab 动画资源（CellScroll.anim、CellScroll.controller）从原项目
- [ ] 4.4 创建 `BasicCell.prefab`，配置 Animator 和 UI 组件（Text）
- [ ] 4.5 创建 `BasicScrollViewWidget.cs`（FancyScrollerWidget 子类）
- [ ] 4.6 创建 `Example01_BasicUI.cs`（UIWindow），OnCreate 中初始化 ScrollViewWidget 并生成 20 个 ItemData
- [ ] 4.7 创建 `Example01_BasicUI.prefab`，包含 Scroller + CellContainer
- [ ] 4.8 验证：通过 FancyTestHomeUI 选择 01_Basic，显示 20 个带动画的 Cell

## 5. Example02_FocusOn — 聚焦滚动

- [ ] 5.1 创建 Context 类（OnCellClicked、SelectedIndex）
- [ ] 5.2 创建 FocusOnCell.cs，实现选中态高亮和点击回调
- [ ] 5.3 迁移并创建 Cell Prefab + 动画资源
- [ ] 5.4 创建 FocusOnScrollViewWidget.cs（FancyScrollerWidget 子类），实现 ScrollTo 聚焦
- [ ] 5.5 创建 `Example02_FocusOnUI.cs`（UIWindow）+ Prefab
- [ ] 5.6 验证：点击 Cell 后平滑滚动到选中位置

## 6. Example03_InfiniteScroll — 无限循环

- [ ] 6.1 创建 InfiniteCell.cs，实现循环滚动位置计算
- [ ] 6.2 迁移并创建 Cell Prefab + 动画资源
- [ ] 6.3 创建 InfiniteScrollViewWidget.cs，配置 Scroller MovementType = Elastic/Clamped
- [ ] 6.4 创建 `Example03_InfiniteScrollUI.cs`（UIWindow）+ Prefab，默认 SelectCell(0)
- [ ] 6.5 验证：循环滚动到末尾后连续回到开头

## 7. Example04_Metaball — Shader 特效

- [ ] 7.1 迁移 Shader 资源：Metaball.shader、Metaball.hlsl、Metaball.mat
- [ ] 7.2 创建 Background 组件（使用 Metaball Shader 渲染）
- [ ] 7.3 创建 MetaballCell.cs（FancyCellWidget）
- [ ] 7.4 迁移并创建 Cell Prefab + 动画资源
- [ ] 7.5 创建 MetaballScrollViewWidget.cs + Example04_MetaballUI.cs + Prefab
- [ ] 7.6 验证：Metaball 背景特效正常渲染，Cell 滚动动画流畅

## 8. Example05_Voronoi — Shader 特效

- [ ] 8.1 迁移 Shader 资源：Voronoi.shader、Voronoi.hlsl、Voronoi.mat
- [ ] 8.2 创建 Background 组件（使用 Voronoi Shader 渲染）
- [ ] 8.3 创建 VoronoiCell.cs（FancyCellWidget）
- [ ] 8.4 迁移并创建 Cell Prefab + 动画资源
- [ ] 8.5 创建 VoronoiScrollViewWidget.cs + Example05_VoronoiUI.cs + Prefab
- [ ] 8.6 验证：Voronoi 背景特效正常渲染

## 9. Example06_LoopTabBar — Tab 导航

- [ ] 9.1 创建 Context 类（OnSelectionChanged 回调 + MovementDirection）
- [ ] 9.2 创建 Tab.cs（FancyCellWidget），实现 Tab 选中态动画
- [ ] 9.3 创建 Window.cs（简单内容面板），实现 In/Out 切换动画
- [ ] 9.4 迁移并创建 Tab Prefab + Window Prefab + 动画资源
- [ ] 9.5 创建 LoopTabBarScrollViewWidget.cs + SlideScreenTransition
- [ ] 9.6 创建 `Example06_LoopTabBarUI.cs`（UIWindow）+ Prefab
- [ ] 9.7 验证：滑动 Tab 切换 Window 内容，Out/In 动画流畅

## 10. Example07_ScrollRect — ScrollRect 模式

- [ ] 10.1 创建 Context 类（OnCellClicked、SelectedIndex，实现 IFancyScrollRectContext）
- [ ] 10.2 创建 Alignment 枚举（Upper、Middle、Lower）
- [ ] 10.3 创建 ScrollRectCell.cs（FancyCellWidget），实现正弦波位置动画 + 选中态样式
- [ ] 10.4 创建 ScrollRectCell.prefab（Text + Image + Button）
- [ ] 10.5 创建 ScrollRectViewWidget.cs（FancyScrollRectWidget 子类）
- [ ] 10.6 创建 `Example07_ScrollRectUI.cs`（UIWindow）+ Prefab（含 InputField 控件 + Dropdown + FancyScrollRectProxy）
- [ ] 10.7 实现控件交互：Padding/Spacing 实时调整、DataCount 重新生成、SelectIndex + Alignment 定位
- [ ] 10.8 验证：所有 InputField 和 Dropdown 交互正常，Cell 正弦波动画和选中态正确

## 11. Example08_GridView — 网格布局

- [ ] 11.1 创建 Context 类（实现 IFancyGridViewContext）
- [ ] 11.2 创建 GridCell.cs（FancyCellWidget），实现网格 Cell 显示
- [ ] 11.3 创建 GridCell.prefab
- [ ] 11.4 创建 GridViewWidget.cs（FancyGridViewWidget 子类），配置 StartAxisCellCount
- [ ] 11.5 创建 `Example08_GridViewUI.cs`（UIWindow）+ Prefab（含 XSpacing/YSpacing 控件 + FancyScrollRectProxy）
- [ ] 11.6 实现网格参数交互：Padding、Spacing、DataCount、SelectIndex + Alignment
- [ ] 11.7 验证：网格布局正确，交叉轴和主轴间距可调

## 12. Example09_LoadTexture — 异步加载

- [ ] 12.1 创建 TextureLoader 类（异步加载纹理资源，支持取消和释放）
- [ ] 12.2 创建 LoadTextureCell.cs（FancyCellWidget），集成 TextureLoader，实现加载中/完成/失败状态
- [ ] 12.3 创建 LoadTextureCell.prefab（Image + 占位背景）
- [ ] 12.4 创建 LoadTextureScrollViewWidget.cs（FancyScrollerWidget 子类）
- [ ] 12.5 创建 `Example09_LoadTextureUI.cs`（UIWindow）+ Prefab
- [ ] 12.6 验证：纹理异步加载正常，滚动时加载和回收无内存泄漏

## 13. 集成验证

- [ ] 13.1 在 Editor 中完整运行 FancyScrollViewTestScene，逐个切换 9 个 Example
- [ ] 13.2 确认所有 Example 切换时无报错、无内存泄漏（Profiler 验证）
- [ ] 13.3 确认主流程（GameScene + ProcedureLaunch 链路）不受影响
- [ ] 13.4 运行全部 EditMode 测试确保无回归

## 14. 使用文档

- [ ] 14.1 编写 FancyScrollViewExamples README：架构概览、目录结构、如何运行
- [ ] 14.2 编写 FancyScrollerWidget 使用指南：继承模式、API 说明、注意事项
- [ ] 14.3 编写新增 Example 扩展指南：命名规范、文件模板、验收检查清单
- [ ] 14.4 编写故障排查指南：常见编译错误、运行时问题、资源缺失处理