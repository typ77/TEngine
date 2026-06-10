## Why

TEngine 当前的列表管理依赖 `AdjustIconNum` + `UIWidget` 手动创建/销毁，存在以下问题：

1. **无 Cell 复用** — 列表项数量多时全部实例化，内存和 CPU 开销随数据量线性增长
2. **无 Cell 动画** — 列表项无法根据滚动位置做缩放/透明度/3D 变换等视觉效果
3. **无无限滚动** — 列表不能循环展示，首尾无法衔接
4. **缺少标准网格布局** — 网格列表需要业务层自行计算排列

FancyScrollView（https://github.com/setchi/FancyScrollView）是一个成熟的 Unity ScrollView 组件，支持 Cell 复用、无限滚动、自定义动画、网格布局，MIT 协议。但它的原始实现与 TEngine 的架构有显著差异：独立程序集、独立的 Cell 生命周期、直接 `Instantiate` 创建、不遵循 MVE 四层架构。

需要将 FancyScrollView **移植并改造**为符合 TEngine 规范的 UI 控件，作为通用 UI 基础设施供业务层使用。

## What Changes

### 源码移植

- **复制 FancyScrollView Runtime 源码到 HotFix 目录** — 放入 `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/`，作为热更代码的一部分
- **删除独立 asmdef** — 不使用 FancyScrollView.asmdef，代码随 HotFix 程序集编译
- **统一命名空间** — 从 `FancyScrollView` 改为 `GameLogic.UI.FancyScrollView`
- **保留 EasingCore 依赖** — 单文件 `EasingCore.cs` 一并带入

### TEngine 适配层

- **新增 `FancyCellWidget<TItemData>` 基类** — 继承 `UIWidget`，通过组合模式实现 `IFancyCell` 接口，支持 TEngine 完整生命周期（ScriptGenerator → SetupBindings → RegisterEvent → OnCreate → OnRefresh → OnDestroy）
- **新增 `FancyScrollViewWidget` / `FancyScrollRectWidget` / `FancyGridViewWidget`** — 三个容器 Widget，组合持有对应的 FancyScrollView MonoBehaviour 组件引用，对外暴露 `UpdateContents()` 接口
- **改造 Cell 创建流程** — 从 `Instantiate(CellPrefab)` 改为通过 `CellWidgetPool` 异步加载 Prefab（YooAsset）+ `UIWidget.CreateByPrefab()` 走 TEngine Widget 创建管线
- **新增节点命名前缀** — `m_fscroll_` 映射到 FancyScrollView 组件，`m_scroller_` 映射到 Scroller 组件

### 渐进式加载

- **三级加载模型** — Cell 壳（同步本地）→ 骨架/占位（同步本地）→ 实际子内容（异步，可能远程）
- **三种加载策略** — Cascade（首次级联入场动画）、AutoTrigger（滚动自动触发）、Manual（外部手动触发）
- **ContentLoadState 状态机** — Skeleton → Loading → Loaded / Error，支持 Unload 回到 Skeleton
- **进度感知** — CellWidget 可选实现 `ContentAssetLocation` 返回二级内容资源路径，加载过程通过 `IProgress<float>` 回调上报进度
- **级联动画** — 首次初始化时按 staggerDelay 间隔从首个可见 Cell 向末尾依次启动加载，每个 Cell 完成后播放入场动画

### 混合事件传播

- **CellWidget 暴露回调** — `OnItemClicked` / `OnButtonClicked` 等，Cell 内部消化多层嵌套冒泡
- **ScrollViewWidget 中转** — 汇总所有 Cell 回调，向上提供统一事件
- **UIWindow/Service 处理** — 直接调 Service 或发 GameEvent，取决于业务复杂度

### MVE 四层 CellWidget 架构

- **CellWidget 内部 ViewModel** — 每个 CellWidget 持有一组 `BindableProperty<T>` 中间属性，作为 Cell 粒度的 ViewModel
- **SetupBindings 绑定子组件** — 通过 `BindText`/`BindSprite` 等将中间属性绑定到 UI 子组件
- **BindData 推值** — ScrollView 只对可见 Cell 调用 `BindData(TItemData)`，推值到中间属性 → BatchScheduler 合并触发回调 → 各子组件各自刷新
- **子 Widget 自治** — 复合 Cell 内嵌的子 Widget 通过公开方法（如 `SetData`/`BindData`）接收数据，内部完全自治
- **可见性感知** — FancyScrollView 天然只对可见 Cell 调用 UpdateContent，非可见 Cell 零刷新开销

### 三个变体全部实现

- **FancyScrollView 基础版** — 自定义 Cell 动画布局（Carousel、Metaball 等特效场景）
- **FancyScrollRect 版** — 标准 ScrollRect 风格列表（背包、排行榜、邮件列表等）
- **FancyGridView 版** — 网格布局（道具格子、头像选择等）

## Capabilities

### New Capabilities

- `fancy-cell-widget`: CellWidget 基类 — 继承 UIWidget，支持中间 BindableProperty ViewModel、BindData 推值刷新、OnCellPosition 动画、可见性生命周期
- `fancy-scrollview-widget`: ScrollView 容器 Widget — 组合持有 FancyScrollRect 组件，管理 CellWidget 池化复用，提供 UpdateContents 接口
- `fancy-gridview-widget`: GridView 容器 Widget — 在 ScrollViewWidget 基础上增加网格布局支持
- `fancy-scroll-adapter`: Cell 创建适配层 — 桥接 FancyScrollView 的 Cell 池化机制与 TEngine 的 UIWidget.CreateByPrefab 创建管线

### Modified Capabilities

- `script-generator-setting`: ScriptGeneratorSetting 新增 `m_fscroll_` 前缀映射（用于节点自动绑定）
- `naming-rules`: 命名规范新增 FancyScrollView 相关前缀
- `ui-patterns`: UI 开发模式文档新增 CellWidget 使用模板和 MVE 列表模式指南

## Scope

### In Scope

- FancyScrollView Runtime 源码移植（Core + Scroller + ScrollRect + GridView + EasingCore）
- TEngine 适配层（CellWidget / ScrollViewWidget / GridViewWidget 基类）
- CellWidget MVE 刷新架构（中间属性 + Bind + 子 Widget 自治）
- ScriptGeneratorSetting 前缀扩展
- 三个变体各自的业务示例
- references 文档更新

### Out of Scope

- FancyScrollView Editor 工具改造（ScrollerEditor 暂保持原样）
- FancyScrollView Examples 的完整移植（仅移植核心运行时）
- 替换现有 AdjustIconNum 的存量代码（新控件与旧模式共存）
- async Cell 加载（初版 Cell Prefab 全部通过 Inspector 引用或同步加载）

## Risks

| 风险 | 影响 | 缓解 |
|------|------|------|
| 组合模式导致 CellWidget 与 FancyScrollView 之间适配层复杂 | 中 | 先实现最简路径（ScrollViewWidget 持有 MonoBehaviour 引用），逐步优化 |
| GridView 的 CellGroup 机制与 UIWidget 创建管线冲突 | 中 | CellGroup 内部子 Cell 不走 UIWidget 管线，仅最外层 Cell 走 Widget 生命周期 |
| Cell 中间 BindableProperty 增加内存开销 | 低 | 每个 Cell 约 4~6 个 BindableProperty（数百字节），可接受；复杂 Cell 可退化为直接赋值模式 |
| Scroller 组件与 TEngine 的 Input 系统冲突 | 低 | Scroller 使用标准 Unity EventSystem，与 TEngine 无冲突 |

## Dependencies

- TEngine UIBase / UIWidget / UIModule（已有）
- TEngine DataBinding 系统（BindableProperty / DataContext / BatchScheduler）（已有）
- UniTask（已有）
- FancyScrollView 源码（https://github.com/setchi/FancyScrollView，MIT）
