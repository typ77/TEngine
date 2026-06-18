# Scroller Unification 设计文档

> 日期：2026-06-13
> 状态：已批准
> 范围：FancyScrollView 适配层重构——消除 Controller/Widget 双路径，统一为非泛型 Controller

---

## 1. 动机

FancyScrollView 适配层存在两条并行路径：

| 路径 | 使用者 | 用户代码量 | 问题 |
|------|--------|-----------|------|
| 继承 Widget（FancyScrollerWidget\<T,C\>） | Example02/03 | ~15 行子类 | 3 层继承链、手动 BindScroller |
| 组合 Controller（FancyScrollerController\<T,C\>） | Example01 | ~15 行样板 | 手动 new/Initialize/Dispose、双字段冗余 |

两条路径的 Cell 池算法完全重复（~44 行），且 Controller 的泛型参数 `FancyScrollerController<TItemData, TContext>` 使得 ScriptGenerator 无法自动生成。

**目标**：让 ScriptGenerator 把 Scroller 节点自动生成为非泛型 `FancyScrollerController` 字段，用户只需 `SetCellFactory` + `LoadCellPrefabAsync` + `UpdateData`，三个 API 完成全部操作。

---

## 2. 核心设计决策

### D1：非泛型 Controller

将 `FancyScrollerController<TItemData, TContext>` 改为 `FancyScrollerController`（非泛型）。

**理由**：Controller 内部不关心 TItemData 和 TContext 的具体类型——它只是索引数据、分发给 Cell、管理可见性。类型安全由 Cell 端保证。去掉泛型后，ScriptGenerator 可以像对待 Button/Text 一样自动生成该字段。

**代价**：
- `UpdateData(IList)` 代替 `UpdateData(IList<TItemData>)`——提供泛型便捷方法 `UpdateData<T>(IList<T>)` 保留类型推断
- `SharedContext` 变为 `object`——使用处需转型
- Cell 内部有一次引用类型转型 `(TItemData)itemData`——性能影响可忽略

### D2：双接口体系

新增非泛型 `IFancyCell` 基接口，现有泛型 `IFancyCell<TItemData, TContext>` 继承之。

```
IFancyCell (非泛型)           ← Controller 只依赖这个
  └── IFancyCell<T, C> (泛型)  ← Cell 层使用，类型安全
        └── FancyCellWidget<T, C>  ← 显式接口实现桥接转型
```

FancyCellWidget 通过显式接口实现 `void IFancyCell.UpdateContent(object data) => UpdateContent((TItemData)data)` 桥接两层。

**理由**：用户写的 Cell 代码完全不变——仍 override 泛型的 `UpdateContent(TItemData)`。

### D3：FancyScrollerWidget 合并吸收基类

`FancyScrollViewWidget<T, C>` 被吸收进非泛型的 `FancyScrollerWidget`。继承链从 3 层变 1 层：

```
Before:  UIWidget → FancyScrollViewWidget<T,C> → FancyScrollerWidget<T,C> → 子类
After:   UIWidget → FancyScrollerWidget → 子类（可选）
```

`FancyScrollerWidget` 内嵌非泛型 `FancyScrollerController`，转发所有 API。

### D4：Controller 保持纯 C#

Controller 继续作为纯 C# 类（IDisposable），不变成 MonoBehaviour。

ScriptGenerator 在遇到 `UIComponentName.FancyScroller` 时走特殊生成路径：生成 `new FancyScrollerController()` + `Initialize(...)` 而非 `GetComponent<T>()`。

### D5：CellFactory 由用户在 OnCreate 中设置

ScriptGenerator 生成的 Initialize 传 `null` 作为 cellFactory。用户在 OnCreate 中调用 `m_scroller.SetCellFactory(() => new MyCell())`。

**理由**：Cell 类型是业务逻辑，ScriptGenerator 不应知道。

---

## 3. 架构变更

### 3.1 接口层

```
┌───────────────────────────────────────────────────────┐
│ IFancyCell (新增，非泛型)                               │
│                                                       │
│ int Index { get; set; }                               │
│ bool IsVisible { get; }                               │
│ void Initialize();                                    │
│ void SetVisible(bool visible);                        │
│ void UpdateContent(object itemData);                  │
│ void UpdatePosition(float position);                  │
│ void SetContext(object context);                      │
└───────────────────────────────────────────────────────┘
          ▲
          │ extends
┌───────────────────────────────────────────────────────┐
│ IFancyCell<TItemData, TContext> : IFancyCell (改造)    │
│                                                       │
│ // 泛型版本保留，供 Cell 层使用                          │
│ void UpdateContent(TItemData itemData);               │
│ void SetContext(TContext context);                     │
└───────────────────────────────────────────────────────┘
```

### 3.2 Controller 层

```
FancyScrollerController (非泛型，纯 C#, IDisposable)
├── 字段
│   ├── UIBase _owner
│   ├── Scroller _scroller
│   ├── Transform _cellContainer
│   ├── Func<IFancyCell> _cellFactory
│   ├── object _sharedContext               (原 TContext)
│   ├── IList _itemsSource                  (原 IList<TItemData>)
│   ├── List<IFancyCell> _cellPool          (原 List<IFancyCell<T,C>>)
│   └── AssetHandle _cellPrefabHandle
│
├── 生命周期
│   ├── Initialize(owner, scroller, container, cellFactory, context)
│   ├── LoadCellPrefabAsync(path)
│   └── Dispose()
│
├── 公开 API
│   ├── SetCellFactory(Func<IFancyCell>)
│   ├── UpdateData(IList) / UpdateData<T>(IList<T>)
│   ├── ScrollTo(index, duration) / JumpTo(index)
│   ├── Refresh() / Relayout()
│   └── GetCenterIndex()
│
├── 配置属性
│   ├── CellInterval { get; set; }
│   ├── ScrollOffset { get; set; }
│   └── Loop { get; set; }
│
├── 事件
│   ├── event Action<int> OnCellItemClicked
│   └── event Action<int> OnCellButtonClicked
│
└── Cell 池算法（不变）
    ├── UpdatePosition / ResizePool / UpdateCells / CircularIndex
    └── 唯一实现源
```

### 3.3 Widget 层

```
FancyScrollerWidget : UIWidget (非泛型)
├── 内嵌 _controller (FancyScrollerController)
├── 自发现：OnCreate 中从子节点自动找到 Scroller + cellContainer
├── 虚方法
│   ├── CreateCell() → IFancyCell        (子类 override)
│   ├── CellInterval / ScrollOffset / Loop (子类 override)
│   └── OnCellClicked / OnCellButtonClicked (子类 override)
├── 公开 API（转发到 _controller）
│   ├── InitializeAsync / LoadCellPrefabAsync
│   ├── UpdateData / ScrollTo / JumpTo / Refresh / Relayout
│   └── SharedContext (object)
└── OnDestroy → _controller.Dispose()
```

### 3.4 删除的文件/类

- `FancyScrollViewWidget<T, C>` —— 被 FancyScrollerWidget 吸收
- `[ScrollView]` Attribute —— 不需要了

---

## 4. ScriptGenerator 集成

### 4.1 Ruler 改造

`ScriptGenerateRuler` 新增 `isCustomInit` 标志：

```csharp
[SerializeField] public bool isCustomInit = false;
```

当 `isCustomInit = true` 时，ScriptGenerator 走自定义初始化路径。

### 4.2 ScriptAutoGenerator 改造

`WriteAutoScript` 方法中新增 Scroller 特殊处理分支：

```csharp
if (rule.componentName == UIComponentName.FancyScroller)
{
    // 字段生成
    strVar.AppendLine($"\t\tprivate FancyScrollerController {varName};");

    // 初始化代码生成
    strBind.AppendLine($"\t\t\t{varName} = new FancyScrollerController();");
    strBind.AppendLine($"\t\t\t{varName}.Initialize(this, ");
    strBind.AppendLine($"\t\t\t    m_bindComponent.GetComponent<Scroller>({scrollerBindIndex}), ");
    strBind.AppendLine($"\t\t\t    FindChild(\"{childPath}/m_cellContainer\"), ");
    strBind.AppendLine($"\t\t\t    null);");
}
```

### 4.3 OnDestroy 自动生成

ScriptGenerator 新增 OnDestroy 区域，为所有 `FancyScrollerController` 字段生成 Dispose：

```csharp
protected override void OnDestroy()
{
    m_scroller?.Dispose();
    m_scroller = null;
    base.OnDestroy();
}
```

### 4.4 组件名映射更新

```csharp
UIComponentName.FancyScroller → GetComponentTypeName 仍返回 "Scroller"
// 但 WriteAutoScript 中识别此枚举后走特殊路径，不使用 GetComponentTypeName
```

---

## 5. 用户体验对比

### 5.1 Example01（直接在 UIWindow 中使用 ScrollView）

```
Before:
═══════════════════════════════════════════════════════════
private FancyScrollerController<Example01_BasicItemData, NullContext> _scroller;
private List<Example01_BasicItemData> _items;

// ScriptGenerator (手动):
_scroller = new FancyScrollerController<Example01_BasicItemData, NullContext>();
_scroller.Initialize(this, m_scroller, m_tfCellContainer,
    () => new Example01_BasicCell());

// OnCreate:
await _scroller.LoadCellPrefabAsync("Example01_BasicCell");
_items = Service.BuildItems();
_scroller.UpdateData(_items);

// OnDestroy:
_scroller?.Dispose();
_scroller = null;


After:
═══════════════════════════════════════════════════════════
// m_scroller 由 _Gen.g.cs 自动生成（FancyScrollerController 类型）
// Initialize + Dispose 自动生成
private List<Example01_BasicItemData> _items;

// OnCreate（全部用户代码）:
m_scroller.SetCellFactory(() => new Example01_BasicCell());
await m_scroller.LoadCellPrefabAsync("Example01_BasicCell");
_items = Service.BuildItems();
m_scroller.UpdateData(_items);

// OnDestroy：自动，不用写
```

**样板代码：15 行 → 3 行。**

### 5.2 Example02（Widget 子类）

```
Before:
═══════════════════════════════════════════════════════════
public class Example02_FocusOnScrollViewWidget
    : FancyScrollerWidget<Example02_FocusOnItemData, Example02_FocusOnContext>
{
    protected override void ScriptGenerator()
    {
        var scroller = FindChildComponent<Scroller>("m_scroller");
        var cellContainer = FindChild("m_scroller/m_cellContainer");
        BindScroller(scroller, cellContainer);
    }

    protected override FancyCellWidget<...> CreateCellWidget() => new Example02_FocusOnCell();

    protected override void OnCellClicked(int index)
    {
        SharedContext.SelectedIndex = index;
        ScrollTo(index, 0.3f);
        Refresh();
    }
}


After:
═══════════════════════════════════════════════════════════
public class Example02_FocusOnScrollViewWidget : FancyScrollerWidget
{
    protected override IFancyCell CreateCell() => new Example02_FocusOnCell();

    protected override void OnCellClicked(int index)
    {
        ((Example02_FocusOnContext)SharedContext).SelectedIndex = index;
        ScrollTo(index, 0.3f);
        Refresh();
    }
}
```

**简化：删除 ScriptGenerator override + BindScroller 调用。**

### 5.3 Example03（最简场景）

```
Before:
═══════════════════════════════════════════════════════════
public class Example03_InfiniteScrollViewWidget
    : FancyScrollerWidget<Example03_InfiniteScrollItemData, NullContext>
{
    protected override float CellInterval => 0.2f;
    protected override float ScrollOffset => 0.5f;
    protected override bool Loop => true;

    protected override void ScriptGenerator() { BindScroller(...); }

    protected override FancyCellWidget<...> CreateCellWidget() => new Example03_InfiniteScrollCell();
}


After:
═══════════════════════════════════════════════════════════
public class Example03_InfiniteScrollViewWidget : FancyScrollerWidget
{
    protected override float CellInterval => 0.2f;
    protected override float ScrollOffset => 0.5f;
    protected override bool Loop => true;

    protected override IFancyCell CreateCell() => new Example03_InfiniteScrollCell();
}
```

**简化：删除 ScriptGenerator override。**

---

## 6. 风险与缓解

| 风险 | 影响 | 缓解 |
|------|------|------|
| Cell 内 `(TItemData)object` 转型 | 如果传入错误类型会 InvalidCastException | Cell 的 override 方法签名明确，编译期可发现类型不匹配；运行时报错信息清晰（在具体 Cell 类中） |
| `SharedContext` 变 object | 使用时需转型 | 子类可定义 `private MyContext Ctx => (MyContext)SharedContext;` 便捷属性 |
| ScriptGenerator 改造范围 | 改动 WriteAutoScript 核心方法 | 用 `if (rule.componentName == FancyScroller)` 条件分支隔离，不影响其他组件的生成逻辑 |
| 向后兼容 | FancyScrollerWidget 签名变化 | Example02/03 需要迁移（去泛型 + 去 ScriptGenerator override），但改动量小且机械 |
| 现有 IFancyCell\<T,C\> 实现者 | 需要适配非泛型基接口 | FancyCellWidget 基类统一处理显式接口实现，子类无感知 |

---

## 7. 文件变动清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `IFancyCell.cs` | 改造 | 拆分非泛型基接口 + 泛型子接口 |
| `FancyCellWidget.cs` | 改造 | 加显式接口实现（IFancyCell.UpdateContent/SetContext） |
| `FancyScrollerController.cs` | 重大重构 | 去泛型、字段类型改为 object/IList/IFancyCell |
| `FancyScrollerWidget.cs` | 重大重构 | 去泛型、吸收 FancyScrollViewWidget、内嵌 Controller |
| `FancyScrollViewWidget.cs` | 删除 | 被 FancyScrollerWidget 吸收 |
| `ScriptGenerateRuler.cs` | 微调 | 新增 isCustomInit 字段 |
| `ScriptAutoGenerator.cs` | 改造 | Scroller 特殊生成路径 + OnDestroy 区域 |
| `ScriptGeneratorSetting.cs` | 微调 | 组件名映射更新 |
| `Example01_BasicUI.cs` | 简化 | 删除手动样板代码 |
| `Example02_FocusOnScrollViewWidget.cs` | 简化 | 去泛型 + 删 ScriptGenerator override |
| `Example03_InfiniteScrollViewWidget.cs` | 简化 | 同上 |

---

## 8. 开放问题

1. **Widget 自发现的约定**：FancyScrollerWidget 是通过 `GetComponentInChildren<Scroller>()` 自动发现 Scroller，还是仍由外部传入？前者更简洁，后者更灵活（多 Scroller 场景）。
2. **Context 创建时机**：非泛型 Controller 的 context 由谁创建？选项 A：用户在 Initialize 中传入；选项 B：用户在 SetCellFactory 中传入；选项 C：默认 null（不使用 Context）。

---

## 9. 实现变更备注（2026-06-13 迭代）

> 本节决策在实现阶段修正了正文 §3（cellContainer 自发现）与 §4（ScriptGenerator 生成路径），正文相关描述已过时，以本节为准。

**CellContainer 改由 Scroller 子类持有（取代命名约定查找）：**

- 正文 §3/§4 中 Controller 通过遍历子节点「命名含 CellContainer」查找 `_cellContainer`（`FindCellContainer`）的方案**已废弃**。
- 最终方案：新增 `FancyScrollerView : Scroller`（`Assets/.../FancyScrollView/Scroller/FancyScrollerView.cs`），携带 `[SerializeField] RectTransform cellContainer`。`FancyScrollerController.Initialize` 改为 `_cellContainer = (scroller as FancyScrollerView)?.CellContainer`，彻底消除命名约定。
- `FancyScrollerView` 继承 `Scroller`，ScriptGenerator 仍生成 `m_scroller = GetComponent<Scroller>(0)`（子类兼容，**ScriptGeneratorSetting / GetComponentTypeName 无需改动**）。

**右键菜单 GameObject/UI/Scroller：**

- 新增 `Assets/Editor/FancyScrollView/FancyScrollerMenu.cs`，一键创建标准结构：
  ```
  m_scroller  [RectTransform, Image, Mask, FancyScrollerView]  ← viewport 指向自身
    └─ CellContainer  [RectTransform]   ← cellContainer 字段自动赋值
  ```
- `CellContainer` 节点**无 `m_` 前缀**：它不作为 ScriptGenerator 独立绑定字段（`WriteScriptUIComponent` 按命名规则匹配会跳过），而由 Scroller 组件持有。`_Gen` 不再生成 `m_tfCellContainer` 字段。

**字段组织：** ScriptGenerator 为每个 Scroller 节点生成配对两字段——`private Scroller m_scroller` + `private FancyScrollerController m_scrollerController`，后者在 `ScriptGenerator()` 中 `new` + `Initialize(this, m_scroller, null)`，在 `OnDestroy()` 中 `Dispose()`。业务代码（如 Example01）只使用 `m_scrollerController`。

**迁移 prefab 的坑：** 替换 prefab 上的组件后重新生成 `_Gen`，必须用 `PrefabUtility.LoadPrefabContents` 加载实例 → `GenerateCSharpScript` → `SaveAsPrefabAsset` 回写，否则 `UIBindComponent.m_components` 会残留指向已销毁组件的悬空引用（运行时 `GetComponent<Scroller>(0)` 返回 null → `Initialize` 抛 ArgumentNullException）。详见 memory `uibindcomponent-rebuild-needs-prefab-contents`。
