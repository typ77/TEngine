# FancyScrollView TEngine 集成 — 任务分解

> 基于细化后的设计文档 v2，涵盖渐进式加载、混合事件传播、YooAsset 异步 Cell Prefab 加载。

## Phase 1: 源码移植（预估 1d）

### Task 1.1 — 复制 FancyScrollView Runtime 源码
- **目标**: 将 Runtime 源码复制到 HotFix 目录
- **文件**: Core/, Scroller/, ScrollRect/, GridView/ 下所有 .cs 文件
- **目标路径**: `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/{Core|Scroller|ScrollRect|GridView}/`
- **验证**: 文件复制完整，无遗漏
- **状态**: ⬜

### Task 1.2 — 删除 asmdef，改命名空间
- **目标**: 删除 `FancyScrollView.asmdef`，所有文件 namespace 改为 `GameLogic.UI.FancyScrollView`
- **注意**: using 语句同步更新；EasingCore namespace 保留或统一
- **验证**: 编译无错误
- **状态**: ⬜

### Task 1.3 — 编译通过，解决依赖
- **目标**: 所有移植文件在 HotFix 程序集内编译通过
- **依赖**: Task 1.1, 1.2
- **验证**: 无编译错误
- **状态**: ⬜

---

## Phase 2: 适配层核心（预估 3.5d）

### Task 2.1 — 定义 IFancyCell 接口
- **目标**: 从 FancyCell 抽象出接口
- **文件**: 新建 `Adapters/IFancyCell.cs`
- **接口**: Index, IsVisible, SetVisible, UpdateContent, UpdatePosition, SetContext, Initialize
- **验证**: 编译通过
- **状态**: ⬜

### Task 2.2 — 改造 FancyScrollView Cell 池为接口化
- **目标**: `IList<FancyCell<T,C>>` → `IList<IFancyCell<TItemData>>`，新增 `CellFactory` 委托
- **依赖**: Task 2.1
- **改造点**: pool 类型、ResizePool 改用 CellFactory、UpdateCells 改用接口方法
- **注意**: 同步改造 FancyScrollRect 和 FancyGridView 中的相关调用
- **验证**: 编译通过
- **状态**: ⬜

### Task 2.3 — 实现 FancyCellWidget 基类
- **目标**: `FancyCellWidget<TItemData> : UIWidget, IFancyCell<TItemData>`
- **文件**: 新建 `Adapters/FancyCellWidget.cs`
- **核心功能**:
  - 中间属性管理（CreateProperty + 自动 Dispose）
  - BindData / OnCellPosition 虚方法
  - OnCellVisible / OnCellInvisible 虚方法
  - IFancyCell 接口实现
  - ContentLoadState 状态机骨架（骨架/加载中/已加载/错误）
  - 事件回调：OnItemClicked, OnButtonClicked
- **依赖**: Task 2.1
- **验证**: 编译通过
- **状态**: ⬜

### Task 2.4 — 实现 CellWidgetPool（YooAsset 异步加载）
- **目标**: Cell Prefab 通过 YooAsset 异步加载，缓存后复用
- **文件**: 新建 `Adapters/CellWidgetPool.cs`
- **核心功能**:
  - `InitializeAsync(string assetLocation, IProgress<float>, CancellationToken)` 预加载 Prefab
  - 使用 `LoadAssetAsyncHandle` 获取加载进度
  - 缓存 Prefab 引用，后续通过 `UIWidget.CreateByPrefab` 创建 Cell
  - 返回 IFancyCell 接口
- **依赖**: Task 2.3
- **验证**: 编译通过
- **状态**: ⬜

### Task 2.5 — 实现 ContentLoadScheduler（渐进式加载调度）
- **目标**: 实现三种加载策略（Cascade / AutoTrigger / Manual）
- **文件**: 新建 `Adapters/ContentLoadScheduler.cs`
- **核心功能**:
  - Cascade：按 staggerDelay 依次触发可见 Cell 的 LoadContentAsync
  - AutoTrigger：OnCellVisible 时自动触发加载
  - Manual：外部显式调用
  - 统一 CancellationTokenSource 管理（ScrollViewWidget 销毁时取消所有）
  - `_isInitialized` 标记确保 Cascade 只触发一次
- **依赖**: Task 2.3
- **验证**: 编译通过
- **状态**: ⬜

### Task 2.6 — 实现 FancyScrollRectWidget 容器
- **目标**: 组合持有 FancyScrollRect + Scroller 引用，管理 Cell 池和事件
- **文件**: 新建 `Adapters/FancyScrollRectWidget.cs`
- **核心功能**:
  - ScriptGenerator 绑定 FancyScrollRect + Scroller 组件
  - InitializeAsync：预加载 Cell Prefab
  - UpdateContents：传入数据源
  - ScrollTo / JumpTo 导航
  - 事件中转：汇总 Cell 的 OnItemClicked / OnButtonClicked
  - 集成 ContentLoadScheduler
  - ContentLoadMode / StaggerDelay / PreloadCount 配置
- **依赖**: Task 2.2, 2.4, 2.5
- **验证**: 编译通过
- **状态**: ⬜

### Task 2.7 — 节点命名前缀扩展
- **目标**: ScriptGeneratorSetting.asset 新增前缀
- **前缀**: `m_fscroll_` → FancyScrollRect, `m_scroller_` → Scroller
- **验证**: Prefab 中使用新前缀的节点能正确生成绑定代码
- **状态**: ⬜

---

## Phase 3: 三个变体实现（预估 2d）

### Task 3.1 — 基础 ScrollView 变体（自定义动画）
- **目标**: FancyScrollViewWidget，支持无限滚动和自定义动画
- **特点**: loop 支持、normalized position 动画
- **依赖**: Task 2.6
- **验证**: Cell 随滚动位置做缩放/透明度变化
- **状态**: ⬜

### Task 3.2 — ScrollRect 变体（标准列表）
- **目标**: FancyScrollRectWidget，标准列表行为
- **特点**: padding / spacing / reuse margin / Scrollbar
- **依赖**: Task 2.6
- **验证**: 标准列表滚动，支持滚动条和惯性
- **状态**: ⬜

### Task 3.3 — GridView 变体（网格）
- **目标**: FancyGridViewWidget，网格布局
- **特点**: CellGroup 分组、startAxisCellCount
- **特殊处理**: CellGroup 内部子 Cell 适配
- **依赖**: Task 3.2
- **验证**: 网格正确排列
- **状态**: ⬜

---

## Phase 4: MVE 标杆示例（预估 1.5d）

### Task 4.1 — 完整 MVE 背包示例（ScrollRect 变体）
- **目标**: BagUI 完整四层架构 × ScrollView 集成
- **文件**: BagModel, BagService, BagDataContext, BagUI, BagItemCellWidget, Prefab
- **展示**: 中间 BindableProperty + BindText/BindSprite + 子 Widget 委托
- **验证**: 列表展示、增删数据后自动刷新、点击事件正确传播到 Service
- **状态**: ⬜

### Task 4.2 — 动画特效示例（ScrollView 变体）
- **目标**: Carousel / 3D 透视效果
- **验证**: Cell 随滚动位置做 3D 动画
- **状态**: ⬜

### Task 4.3 — 网格示例（GridView 变体）
- **目标**: 道具网格选择
- **验证**: 网格排列正确、滚动流畅
- **状态**: ⬜

### Task 4.4 — 渐进式加载示例
- **目标**: 展示三种加载策略
  - Cascade 首次级联入场动画
  - AutoTrigger 滚动自动加载二级内容
  - Manual TabBar 切换时手动加载
- **验证**: 加载进度可见、入场动画流畅、离开视口取消加载
- **状态**: ⬜

### Task 4.5 — 性能测试
- **目标**: 大数据量场景验证
- **指标**: 1000+ / 10000+ 数据项，FPS ≥ 55fps，GC Alloc 可接受
- **验证**: 无卡顿、无内存泄漏
- **状态**: ⬜

---

## Phase 5: 文档与规范（预估 1d）

### Task 5.1 — references 文档更新
- **目标**: 新增/更新 reference 文档
  - `ui-patterns.md` — CellWidget 使用模板、MVE 列表模式、渐进式加载
  - `naming-rules.md` — 新增 `m_fscroll_` / `m_scroller_` 前缀
  - `resource-api.md` — Cell Prefab 异步加载说明
- **状态**: ⬜

### Task 5.2 — troubleshooting 补充
- **目标**: 新增常见问题排查
  - Cell 不显示 / 位置错乱
  - BindData 不触发 / 二级内容加载失败
  - 子 Widget 内存泄漏
  - 级联动画不播放
- **状态**: ⬜

---

## 依赖关系

```
Phase 1 (1.1 → 1.2 → 1.3)
    │
    ▼
Phase 2
    2.1 ──→ 2.2 ──────────────→ 2.6 ──→ Phase 3
     │        │                    ▲
     └──→ 2.3 → 2.4 ─────────────┘
              │
              └──→ 2.5 ──────────┘
    
    2.7 (独立，可与 Phase 2 并行)
    │
    ▼
Phase 3 (3.1 ║ 3.2 → 3.3)     ← 3.1 和 3.2 可并行
    │
    ▼
Phase 4 (4.1 ║ 4.2 ║ 4.3 ║ 4.4) → 4.5
    │
    ▼
Phase 5 (5.1 ║ 5.2)
```

## 总计

- **任务数**: 22
- **预估**: 9~10 人天
- **关键路径**: Phase 1 → 2.1 → 2.3 → 2.4 → 2.6 → 3.2 → 4.1 → 4.5
- **最大风险**: Task 2.2（Cell 池接口化改造）、Task 2.5（ContentLoadScheduler 级联调度）
- **新增风险点**: Task 2.4（YooAsset 异步加载 Prefab 的进度回调需要正确处理 AssetHandle 生命周期）
