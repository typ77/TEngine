# UI 数据绑定系统设计文档 — Phase 1

> **版本**: v1.0  
> **日期**: 2026-06-08  
> **状态**: 设计评审  
> **范围**: 标量数据绑定 + DataContext 自动聚合 + 帧级批次合并

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [设计目标与约束](#2-设计目标与约束)
3. [架构总览](#3-架构总览)
4. [组件详细设计](#4-组件详细设计)
   - 4.1 [BindableProperty\<T\>](#41-bindablepropertyt)
   - 4.2 [ObservableList\<T\>](#42-observablelistt)
   - 4.3 [BatchScheduler](#43-batchscheduler)
   - 4.4 [DataContext\<TView\>](#44-datacontexttview)
   - 4.5 [DataContextAttribute](#45-datacontextattribute)
   - 4.6 [UIBase 扩展](#46-uibase-扩展)
5. [接口定义（完整 API）](#5-接口定义完整-api)
6. [类图](#6-类图)
7. [时序图](#7-时序图)
8. [示例代码](#8-示例代码)
9. [边界场景与约束](#9-边界场景与约束)
10. [文件结构规划](#10-文件结构规划)
11. [与现有系统的兼容性](#11-与现有系统的兼容性)
12. [Phase 2 展望](#12-phase-2-展望)

---

## 1. 背景与动机

### 1.1 现状

TEngine 的 UI 系统基于 `UIWindow` / `UIWidget` / `UIBase` 三层结构，提供窗口堆栈管理、层级排序、全屏遮挡、资源加载等基础能力。但数据刷新完全依赖开发者手动调用 `OnRefresh()`：

```
业务逻辑修改数据 → 手动传 Object[] → OnRefresh() 里手动赋值控件
```

### 1.2 痛点

| 痛点 | 举例 |
|------|------|
| 数据变了 UI 不知道 | 玩家升级了，主界面等级没刷新 |
| 同一数据多处展示 | 金币在主界面、商店、背包都有显示，改一处要改三处 |
| OnRefresh 粒度太粗 | 道具数量变了要刷新整个列表 |
| 手动调 OnRefresh 容易遗漏 | 业务逻辑改了数据忘记通知 UI |
| 多数据源组合复杂 | 背包面板需要背包+角色+VIP三组数据 |

### 1.3 设计方向

基于 MVE（Model-View-EventHandler）架构，引入轻量级响应式数据绑定：

- **Model** 持有 `BindableProperty<T>` / `ObservableList<T>`，纯数据，不知道 UI 存在
- **DataContext** 自动聚合多 Model 数据，转换为 View 友好格式
- **View** 通过 `Bind()` 订阅 DataContext 属性，数据变化自动刷新

---

## 2. 设计目标与约束

### 2.1 目标

1. **一处赋值，多处自动更新** — Model 变更自动传播到所有订阅的 View
2. **多数据源透明聚合** — View 只关心 DataContext，不直接依赖 Model
3. **Widget 级绑定粒度** — 标量 Push 到控件，列表按 Item 级刷新
4. **零外部依赖** — 纯 C# 实现，不引入第三方库
5. **生命周期自动管理** — Bind/Unbind 自动配对，无泄漏风险
6. **与现有系统共存** — 不替代 OnRefresh / GameEvent，渐进式采用

### 2.2 约束

1. **帧级批次合并** — 同帧内多次赋值合并为一次通知，对业务透明
2. **DataContext 转换函数必须无副作用** — 纯函数，不改 Model
3. **ObservableList Item 必须不可变** — struct / record，修改必须 Replace
4. **Phase 1 不含虚拟化列表 UI** — 数据层就绪，UIListWidget 放 Phase 2

### 2.3 设计决策记录

| # | 决策点 | 结论 | 理由 |
|---|--------|------|------|
| D1 | BindableProperty 泛型约束 | 无约束 + 内部 EqualityComparer | 99% 场景足够，特殊情况可传比较器 |
| D2 | 赋值触发时机 | 值立即变 + 回调帧末统一触发 | 业务代码读到最新值，View 看到一致状态 |
| D3 | 赋值语义 | 所有类型统一：赋值 = 通知 | 引用类型内部修改不触发，必须替换对象 |
| D4 | ObservableList Item 类型 | struct / record 不可变 | 语义清晰、无 GC、和 D3 一致 |
| D5 | Item 修改方式 | 强制 Replace(int, T) | 避免隐藏 setter 导致的误用 bug |
| D6 | DataContext 创建方式 | Attribute 声明 + 自动创建 | 风格与 WindowAttribute 一致，开闭原则 |
| D7 | DataContext 依赖获取 | 无参构造函数 + 直接 Singleton\<T\>.Instance | 避免 MakeGenericType 的 HybridCLR 兼容风险 |
| D8 | Widget 获取 DataContext | 向上遍历 Parent 链到 UIWindow | Widget 共享 Window 的 DataContext |
| D9 | 批次合并策略 | 同帧同属性只保留最新值 | 高频变更不造成多次 View 刷新 |
| D10 | 与 OnRefresh 关系 | 共存 | Bind 处理数据驱动，OnRefresh 处理命令式逻辑 |

---

## 3. 架构总览

```
┌─────────────────────────────────────────────────────────────────────┐
│                           整体架构                                   │
│                                                                     │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐                          │
│   │ Model A │  │ Model B │  │ Model C │    纯数据，持有            │
│   │         │  │         │  │         │    BindableProperty<T>     │
│   │ Gold*   │  │ Items*  │  │ VipLv*  │    & ObservableList<T>    │
│   └────┬────┘  └────┬────┘  └────┬────┘                            │
│        │            │            │                                  │
│        └──────┬─────┴────────────┘                                 │
│               │  自动订阅                                           │
│               ▼                                                     │
│   ┌──────────────────────────────┐                                 │
│   │      DataContext<TView>      │    聚合多源                     │
│   │                              │    格式转换                     │
│   │  MapProperty → 标量映射       │    暴露 View 友好属性          │
│   │  MapList     → 列表映射       │                                │
│   │                              │                                │
│   │  BatchScheduler 帧级合并      │    对业务透明                  │
│   └──────────────┬───────────────┘                                 │
│                  │                                                  │
│        ┌─────────┴──────────┐                                      │
│        │ Push(标量)  Pull(列表)│    Phase 1 标量 Push               │
│        ▼                  ▼        Phase 2 列表 Pull               │
│   ┌──────────┐    ┌────────────────┐                              │
│   │ UIWindow │    │ UIWidget       │                              │
│   │          │    │                │                              │
│   │ Bind()   │    │ Bind()         │                              │
│   │ 自动刷新  │    │ 通过 Parent 链  │                              │
│   │          │    │ 获取 DataContext│                              │
│   └──────────┘    └────────────────┘                              │
│                                                                     │
│   共存:                                                              │
│   OnRefresh()   ← 命令式业务逻辑（Tab切换、网络请求、首次进入）       │
│   GameEvent     ← 跨模块事件通信                                     │
└─────────────────────────────────────────────────────────────────────┘
```

数据流方向：

```
Model(s) ──赋值──▶ BatchScheduler ──帧末──▶ DataContext ──转换──▶ View
                         │                                      │
                    对业务透明                              Bind() 自动刷新
                  开发者只需:                               开发者只需:
                  gold.Value = 100;                         Bind(ctx.Gold, cb);
```

---

## 4. 组件详细设计

### 4.1 BindableProperty\<T\>

#### 4.1.1 职责

值包装器。赋值时检测值是否变化，变化则标记为脏，由 BatchScheduler 在帧末统一触发回调。

#### 4.1.2 核心语义

```
赋值 = 值立即变化 + 回调延迟到帧末

gold.Value = 100;       // 值立即变成 100
Debug.Log(gold.Value);  // 输出 100 ✓
// 但 OnValueChanged 回调还没触发，要等 LateUpdate
```

#### 4.1.3 值比较策略

- 默认使用 `EqualityComparer<T>.Default` 进行比较
- 构造时可传入自定义 `IEqualityComparer<T>`
- 值不变不触发通知（`gold.Value = 100; gold.Value = 100;` → 只触发一次）
- 引用类型：引用比较（同一对象赋值不触发，必须赋新对象）

#### 4.1.4 回调设计

```
event Action<T, T> OnValueChanged   // (oldValue, newValue)

- 支持多个订阅者
- 订阅时不会立即触发（不会拿到当前值）
- 批次合并后只触发一次，传递 (旧值, 最终新值)
- 帧内多次赋值: gold=100, gold=200, gold=50 → 回调 (100, 50)
```

#### 4.1.5 边界行为

| 场景 | 行为 |
|------|------|
| 构造后无赋值 | 无回调触发 |
| 赋相同值 | 不触发（比较器判断相等） |
| 赋值后立刻读 | 读到新值（值立即变） |
| 一帧内赋值多次 | 回调合并，只触发一次 |
| 赋值 null | 支持（引用类型），与旧 null 比较不触发 |
| Dispose 后赋值 | 不再标记脏，不触发回调 |

---

### 4.2 ObservableList\<T\>

#### 4.2.1 职责

带变更通知的有序集合。增删改操作触发对应类型的变更事件，同样通过 BatchScheduler 帧级合并。

#### 4.2.2 泛型约束

```csharp
public class ObservableList<T> where T : struct, IEquatable<T>
```

- 约束 `struct` — 不可变语义，修改必须 Replace
- 约束 `IEquatable<T>` — 确保 Item 级别的值比较可靠
- 使用 `record struct` 更佳（自动生成 Equals/ToString/with 表达式）

#### 4.2.3 变更事件类型

```csharp
public enum ListChangeType
{
    Add,           // 尾部添加
    Insert,        // 指定位置插入
    Remove,        // 按 Item 移除
    RemoveAt,      // 按索引移除
    Replace,       // 替换指定位置的 Item（核心操作）
    Clear,         // 清空
    Move,          // 移动位置
    ReplaceAll,    // 整体替换（批量操作）
    AddRange,      // 批量添加
}
```

#### 4.2.4 事件参数

```csharp
public readonly struct ListChangedEventArgs<T>
{
    public ListChangeType Type { get; }
    public int Index { get; }         // 受影响的索引
    public int OldIndex { get; }      // Move 时的源索引
    public T Item { get; }            // 新值（Add/Insert/Replace）
    public T OldItem { get; }         // 旧值（Replace）
    public IReadOnlyList<T> NewItems { get; }   // 批量操作的新值
}
```

#### 4.2.5 API 设计要点

- **隐藏 indexer setter**：`T this[int]` 只有 get，强制使用 `Replace(int, T)`
- **批量操作一次通知**：`AddRange` / `ReplaceAll` 触发单次 `ReplaceAll` 事件
- **只读视图**：`AsReadOnly()` 返回 `IReadOnlyList<T>`，防止外部直接修改内部数组
- **线程安全**：不做线程同步，限制主线程使用（Unity 约定）

#### 4.2.6 ReplaceAll 的语义

```
list.ReplaceAll(newItems);
// 内部: 清空旧数据 → 添加新数据 → 触发一次 ReplaceAll 事件
// 不触发多次 Add/Remove
// View 侧收到 ReplaceAll 后全量刷新（Phase 2 由 UIListWidget 处理）
```

---

### 4.3 BatchScheduler

#### 4.3.1 职责

收集一帧内所有的属性/列表变更，在帧末统一触发回调。对业务代码完全透明。

#### 4.3.2 工作原理

```
┌─────────────────────────────────────────────────────────────┐
│                    帧 N 的执行流程                            │
│                                                             │
│  Update:                                                    │
│    业务代码执行:                                             │
│      gold.Value = 100;        → 脏标记: gold 入 _dirtySet   │
│      items.Replace(3, item);  → 脏标记: items 入 _dirtySet  │
│      gold.Value = 50;         → 已在 _dirtySet，不重复      │
│      level.Value = 10;        → 脏标记: level 入 _dirtySet  │
│                                                             │
│  LateUpdate (BatchScheduler.Flush):                        │
│    1. 遍历 _dirtyProps，每个属性触发一次 OnValueChanged     │
│       gold:    (旧值, 50)     ← 最终值                     │
│       level:   (旧值, 10)                                  │
│    2. 遍历 _dirtyLists，每个列表触发一次 OnChanged          │
│       items:   Replace(index=3, newItem)                   │
│    3. 清空 _dirtyProps 和 _dirtyLists                      │
│                                                             │
│  结果:                                                      │
│    - 业务代码赋值 4 次                                      │
│    - View 收到 3 次更新通知（gold 合并为 1 次）             │
│    - DataContext 收到后进一步合并，View 实际可能只刷新 1 次  │
└─────────────────────────────────────────────────────────────┘
```

#### 4.3.3 数据结构

```
内部状态:
  HashSet<BindablePropertyCore>  _dirtyProps    // 本帧变化的属性
  HashSet<ObservableListCore>    _dirtyLists    // 本帧变化的列表
  bool                           _isFlushing    // 防止重入

BindableProperty 赋值时:
  if (!_isFlushing)              // 非刷新期间才入队
      BatchScheduler.Instance.MarkDirty(this);
```

#### 4.3.4 防重入

```
Flush 期间如果回调里又改了 Model 属性？

void OnGoldChanged(long oldVal, long newVal)
{
    // 如果这里又改了属性？
    Level.Value = newVal > 1000 ? 10 : 1;
}

处理: 
  - Flush 期间标记 _isFlushing = true
  - 新的脏标记仍然入队（但本轮不处理）
  - 下一帧 Flush 再处理
  - 但建议: DataContext 转换函数不应有副作用（设计约束）
```

#### 4.3.5 集成方式

```
方案: 注册到 ModuleSystem 的 LateUpdate 阶段

public sealed partial class BatchScheduler : ISingleton, IUpdate
{
    public void OnUpdate()  // 实际是 LateUpdate 时机
    {
        Flush();
    }
}

或者: 使用自定义 PlayerLoopTiming

UniTask.PlayerLoopTiming.LastLateUpdate
```

#### 4.3.6 无订阅者优化

```
如果一个 BindableProperty 没有任何订阅者（OnValueChanged == null），
赋值时不需要标记脏，跳过 BatchScheduler。

BindableProperty 赋值伪码:
  if (!EqualityComparer.Equals(_value, value))
  {
      _value = value;
      if (OnValueChanged != null)  // 有订阅者才入队
          BatchScheduler.MarkDirty(this);
  }

这确保无人关心的属性变更零开销。
```

---

### 4.4 DataContext\<TView\>

#### 4.4.1 职责

聚合多个 Model 的数据，转换为 View 友好的格式，自动管理订阅生命周期。

#### 4.4.2 核心方法

```
MapProperty<TSource, TTarget>(
    BindableProperty<TSource> source,
    BindableProperty<TTarget> target,
    Func<TSource, TTarget> converter
)

功能:
  - 订阅 source 的 OnValueChanged
  - 源变化时，用 converter 转换后赋给 target
  - target 也是 BindableProperty，赋值会走 BatchScheduler
  - 框架自动管理订阅，Dispose 时全部清理

多源映射:
MapProperty<T1, T2, TTarget>(
    BindableProperty<T1> source1,
    BindableProperty<T2> source2,
    BindableProperty<TTarget> target,
    Func<T1, T2, TTarget> converter
)

支持 2~4 个源的组合转换。
任一源变化都触发 converter 重算。
BatchScheduler 保证同帧内多源变化只算一次。
```

#### 4.4.3 列表映射

```
MapList<TSource, TTarget>(
    ObservableList<TSource> source,
    ObservableList<TTarget> target,
    Func<TSource, TTarget> converter
)

功能:
  - source 的变更事件同步到 target
  - Add → 转换后 Add 到 target
  - Replace → 转换后 Replace 到 target
  - RemoveAt → RemoveAt 到 target
  - Clear → Clear target
  - ReplaceAll → 全量重算 ReplaceAll

转换在 DataContext.Flush 阶段执行，同帧内合并。
```

#### 4.4.4 生命周期

```
创建时机:
  UIModule.OnWindowPrepare() 中，在 window.InternalCreate() 之前
  框架根据 [DataContextAttribute] 自动创建

销毁时机:
  UIWindow.InternalDestroy() 中，在 OnDestroy() 之前
  框架自动调用 DataContext.Dispose()

DataContext.Dispose():
  1. 取消所有 MapProperty/MapList 的源订阅
  2. 清理内部状态
  3. 释放输出属性（BindableProperty/ObservableList）的订阅者
```

#### 4.4.5 DataContext 无参构造函数（HybridCLR 安全）

```
DataContext 使用无参构造函数，内部直接访问 Singleton<T>.Instance。

为什么不用有参构造函数 + 反射解析？
  ❌ typeof(Singleton<>).MakeGenericType(hotfixType) 在 HybridCLR 中有兼容风险
  ❌ PropertyInfo.GetValue 在解释模式下性能不佳
  ✅ 直接 Singleton<T>.Instance 是项目已有惯例，零反射

[DataContext(typeof(BagDataContext))]
class BagUI : UIWindow { }

class BagDataContext : DataContext<BagUI>
{
    public BagDataContext()  // 无参构造函数
    {
        // 直接获取 Singleton（零反射，HybridCLR 安全）
        var player = PlayerModel.Instance;
        var bag = BagModel.Instance;
        var vip = VipModel.Instance;

        MapProperty(player.Gold, GoldText, gold => FormatGold(gold));
        // ...
    }
}

工厂实现（与 UIModule.CreateInstance 模式一致）:
  Activator.CreateInstance(contextType)  ← 同 UIModule 创建窗口的方式
  Attribute.GetCustomAttribute(type)     ← 同 UIModule 读取 WindowAttribute
  缓存工厂委托，首次反射后续零开销
```

#### 4.4.6 纯函数约束

```
DataContext 的转换 lambda 必须是纯函数：
  ✗ 不修改 Model（无副作用）
  ✗ 不发起网络请求
  ✗ 不调用 GameEvent.Send
  ✓ 只做数据格式转换

理由:
  转换函数在 BatchScheduler.Flush 中被调用，
  时序不确定（帧末），副作用会导致不可预测的行为。

  命令式逻辑应放在 OnRefresh / EventHandler 中。

执行机制:
  框架不做运行时检测（成本太高），通过文档和编码规范约束。
  在 DataContext 基类 XML 注释中明确标注。
```

---

### 4.5 DataContextAttribute

#### 4.5.1 定义

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class DataContextAttribute : Attribute
{
    public Type DataContextType { get; }

    public DataContextAttribute(Type dataContextType)
    {
        if (dataContextType == null)
            throw new ArgumentNullException(nameof(dataContextType));
        if (!typeof(DataContext).IsAssignableFrom(dataContextType))
            throw new ArgumentException(
                $"Type must derive from DataContext, got {dataContextType.FullName}");
        DataContextType = dataContextType;
    }
}
```

#### 4.5.2 用法

```csharp
[Window(UILayer.UI)]
[DataContext(typeof(BagDataContext))]
class BagUI : UIWindow
{
}
```

#### 4.5.3 缺失时的行为

```
如果 UIWindow 没有 [DataContext] 特性:
  → 不创建 DataContext
  → DataContext 属性返回 null
  → GetDataContext<T>() 返回 default
  → 现有窗口零影响
```

---

### 4.6 UIBase 扩展

#### 4.6.1 新增属性和方法

```
UIBase 扩展:
┌────────────────────────────────────────────────────────────┐
│                                                            │
│  // DataContext 访问（UIWindow 直接持有，UIWidget 向上查找） │
│  internal DataContext _dataContext;                         │
│  public DataContext DataContext => ...                      │
│                                                            │
│  // 泛型访问                                               │
│  public T GetDataContext<T>() where T : DataContext         │
│                                                            │
│  // 标量绑定 (Push 模式)                                   │
│  public void Bind<T>(                                      │
│      BindableProperty<T> property,                         │
│      Action<T> onChanged)                                  │
│                                                            │
│  // 标量绑定 (带旧值)                                      │
│  public void Bind<T>(                                      │
│      BindableProperty<T> property,                         │
│      Action<T, T> onChanged)   // (oldValue, newValue)     │
│                                                            │
│  // 内部: 绑定列表（自动管理生命周期）                       │
│  private List<Binding> _bindings;                          │
│                                                            │
│  // 自动清理（在 InternalDestroy 时调用）                   │
│  internal void RemoveAllBindings()                         │
│                                                            │
│  // 绑定设置虚方法（子类 override 声明绑定）                 │
│  protected virtual void SetupBindings() { }                │
│                                                            │
└────────────────────────────────────────────────────────────┘
```

#### 4.6.2 Bind 方法内部实现

```
Bind<T>(property, onChanged) 内部逻辑:

1. 创建 Binding 记录 { property, onChanged }
2. 添加到 _bindings 列表
3. 订阅 property.OnValueChanged
4. 立即用当前值调用一次 onChanged（首次同步）
   ← 这确保 Bind 后 UI 立即显示当前数据

RemoveAllBindings() 内部逻辑:
1. 遍历 _bindings
2. 取消每个 property 的 OnValueChanged 订阅
3. 清空 _bindings
4. 在 UIWindow.InternalDestroy 和 UIWidget.OnDestroyWidget 中调用
```

#### 4.6.3 Widget 获取 DataContext

```
UIWidget.GetDataContext<T>() 实现:

public T GetDataContext<T>() where T : DataContext
{
    // 向上遍历 Parent 链
    UIBase current = this;
    while (current != null)
    {
        if (current._dataContext != null)
        {
            return current._dataContext as T;
        }
        current = current.Parent;
    }
    return null;
}

UIWindow.DataContext 实现:
  直接返回 _dataContext（框架在 InternalCreate 前注入）

UIWidget.DataContext 实现:
  return Parent?.DataContext;  // 向上一级
  （或者用上面的遍历方式，效果相同因为 Parent 链最终到 UIWindow）
```

#### 4.6.4 SetupBindings 调用时机

```
UIWindow.InternalCreate() 内部调用顺序（修改后）:

void InternalCreate()
{
    if (_isCreate == false)
    {
        _isCreate = true;
        Inject();              // 现有: 依赖注入钩子
        ScriptGenerator();     // 现有: 代码生成绑定
        BindMemberProperty();  // 现有: 成员属性绑定
        SetupBindings();       // ← 新增: 数据绑定声明
        RegisterEvent();       // 现有: 事件注册
        OnCreate();            // 现有: 用户初始化
    }
}

注意: SetupBindings 在 RegisterEvent 之前，
      确保 Bind 在事件注册前就生效。
      OnRefresh 在 InternalCreate 之后被调用，
      首次数据由 Bind 的立即回调提供，
      命令式初始化逻辑放 OnRefresh。
```

---

## 5. 接口定义（完整 API）

### 5.1 BindableProperty\<T\>

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 响应式属性包装器。
    /// 赋值时检测值变化，变化则通过 BatchScheduler 在帧末触发回调。
    /// <remarks>
    /// 语义：赋值 = 值立即变化 + 回调延迟到帧末。
    /// 引用类型需替换整个对象，内部修改不触发通知。
    /// </remarks>
    /// </summary>
    public sealed class BindableProperty<T> : IDisposable
    {
        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="initialValue">初始值（默认 default）。</param>
        /// <param name="comparer">自定义比较器（默认 EqualityComparer&lt;T&gt;.Default）。</param>
        public BindableProperty(T initialValue = default, IEqualityComparer<T> comparer = null);

        /// <summary>
        /// 当前值。赋值时自动检测变化并标记脏。
        /// 值立即变化，回调延迟到帧末 BatchScheduler.Flush。
        /// </summary>
        public T Value { get; set; }

        /// <summary>
        /// 值变化回调。在 BatchScheduler.Flush（帧末）中触发。
        /// 参数: (oldValue, newValue)
        /// </summary>
        public event Action<T, T> OnValueChanged;

        /// <summary>
        /// 是否已释放。
        /// </summary>
        public bool IsDisposed { get; }

        /// <summary>
        /// 释放资源，清除所有订阅者。
        /// </summary>
        public void Dispose();

        /// <summary>
        /// 手动标记脏（触发回调）。通常不需要手动调用。
        /// </summary>
        public void ForceNotify();

        /// <summary>
        /// 静默赋值（不触发通知）。
        /// 用于初始化或批量操作中间状态。
        /// </summary>
        public void SetValueSilently(T value);
    }
}
```

### 5.2 ObservableList\<T\>

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 带变更通知的有序集合。
    /// Item 类型约束为 struct（不可变），修改必须通过 Replace。
    /// </summary>
    public sealed class ObservableList<T> : IDisposable, IReadOnlyList<T>
        where T : struct, IEquatable<T>
    {
        /// <summary>
        /// 构造函数。
        /// </summary>
        public ObservableList();
        public ObservableList(int capacity);
        public ObservableList(IEnumerable<T> collection);

        // ──── 读操作 ────

        /// <summary>元素数量。</summary>
        public int Count { get; }

        /// <summary>
        /// 按索引读取元素。只读，修改请使用 Replace。
        /// </summary>
        public T this[int index] { get; }

        /// <summary>
        /// 返回只读视图。不产生拷贝。
        /// </summary>
        public IReadOnlyList<T> AsReadOnly();

        /// <summary>是否包含指定元素。</summary>
        public bool Contains(T item);

        /// <summary>查找元素索引。未找到返回 -1。</summary>
        public int IndexOf(T item);

        // ──── 写操作（触发变更通知）────

        /// <summary>尾部添加元素。</summary>
        public void Add(T item);

        /// <summary>指定位置插入元素。</summary>
        public void Insert(int index, T item);

        /// <summary>移除指定元素。</summary>
        public bool Remove(T item);

        /// <summary>移除指定位置的元素。</summary>
        public void RemoveAt(int index);

        /// <summary>
        /// 替换指定位置的元素。这是修改 Item 的唯一方式。
        /// </summary>
        public void Replace(int index, T newItem);

        /// <summary>移动元素从 fromIndex 到 toIndex。</summary>
        public void Move(int fromIndex, int toIndex);

        /// <summary>清空所有元素。</summary>
        public void Clear();

        // ──── 批量操作（一次通知）────

        /// <summary>批量添加元素。触发一次 AddRange 通知。</summary>
        public void AddRange(IEnumerable<T> items);

        /// <summary>
        /// 整体替换。清空后添加新元素，触发一次 ReplaceAll 通知。
        /// 适用于全量刷新场景。
        /// </summary>
        public void ReplaceAll(IEnumerable<T> items);

        // ──── 事件 ────

        /// <summary>
        /// 集合变更事件。在 BatchScheduler.Flush 中触发。
        /// </summary>
        public event Action<ListChangedEventArgs<T>> OnChanged;

        // ──── 生命周期 ────

        /// <summary>是否已释放。</summary>
        public bool IsDisposed { get; }

        /// <summary>释放资源，清除所有订阅者。</summary>
        public void Dispose();
    }
}
```

### 5.3 ListChangedEventArgs\<T\>

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 集合变更事件参数。
    /// </summary>
    public readonly struct ListChangedEventArgs<T>
    {
        /// <summary>变更类型。</summary>
        public ListChangeType Type { get; init; }

        /// <summary>受影响的索引。</summary>
        public int Index { get; init; }

        /// <summary>Move 操作的源索引。</summary>
        public int OldIndex { get; init; }

        /// <summary>新增/替换后的新值。</summary>
        public T Item { get; init; }

        /// <summary>替换前的旧值（仅 Replace 类型有效）。</summary>
        public T OldItem { get; init; }

        /// <summary>批量操作的新值列表（AddRange/ReplaceAll 有效）。</summary>
        public IReadOnlyList<T> NewItems { get; init; }
    }

    /// <summary>
    /// 集合变更类型。
    /// </summary>
    public enum ListChangeType
    {
        Add,
        Insert,
        Remove,
        RemoveAt,
        Replace,
        Clear,
        Move,
        ReplaceAll,
        AddRange,
    }
}
```

### 5.4 BatchScheduler

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 帧级批次合并调度器。
    /// 收集一帧内所有属性/列表变更，在帧末统一触发回调。
    /// 对业务代码完全透明。
    /// </summary>
    public sealed class BatchScheduler : Singleton<BatchScheduler>
    {
        /// <summary>
        /// 标记属性为脏。由 BindableProperty 赋值时内部调用。
        /// </summary>
        internal void MarkDirty(BindablePropertyCore property);

        /// <summary>
        /// 标记列表为脏。由 ObservableList 变更时内部调用。
        /// </summary>
        internal void MarkDirty(ObservableListCore list);

        /// <summary>
        /// 刷新所有脏属性和脏列表。在 LateUpdate 中自动调用。
        /// </summary>
        internal void Flush();

        /// <summary>
        /// 当前帧是否有待处理的变更。调试用。
        /// </summary>
        public bool HasPendingChanges { get; }
    }
}
```

### 5.5 DataContext

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// DataContext 抽象基类。
    /// 聚合多个 Model 数据，转换为 View 友好格式。
    /// 框架自动管理订阅生命周期。
    /// </summary>
    public abstract class DataContext : IDisposable
    {
        /// <summary>是否已释放。</summary>
        public bool IsDisposed { get; }

        /// <summary>释放所有订阅和内部状态。</summary>
        public void Dispose();

        // ──── 子类使用的映射方法（protected）────

        /// <summary>
        /// 单源标量映射。
        /// source 变化时自动用 converter 转换后赋给 target。
        /// </summary>
        protected void MapProperty<TSource, TTarget>(
            BindableProperty<TSource> source,
            BindableProperty<TTarget> target,
            Func<TSource, TTarget> converter);

        /// <summary>
        /// 双源标量映射。
        /// 任一 source 变化时自动重算。
        /// </summary>
        protected void MapProperty<T1, T2, TTarget>(
            BindableProperty<T1> source1,
            BindableProperty<T2> source2,
            BindableProperty<TTarget> target,
            Func<T1, T2, TTarget> converter);

        /// <summary>
        /// 三源标量映射。
        /// </summary>
        protected void MapProperty<T1, T2, T3, TTarget>(
            BindableProperty<T1> source1,
            BindableProperty<T2> source2,
            BindableProperty<T3> source3,
            BindableProperty<TTarget> target,
            Func<T1, T2, T3, TTarget> converter);

        /// <summary>
        /// 列表映射。
        /// source 的变更同步到 target，每条元素通过 converter 转换。
        /// </summary>
        protected void MapList<TSource, TTarget>(
            ObservableList<TSource> source,
            ObservableList<TTarget> target,
            Func<TSource, TTarget> converter)
            where TSource : struct, IEquatable<TSource>
            where TTarget : struct, IEquatable<TTarget>;
    }

    /// <summary>
    /// 泛型 DataContext 基类，关联到具体 View 类型。
    /// </summary>
    public abstract class DataContext<TView> : DataContext where TView : UIBase
    {
    }
}
```

### 5.6 DataContextAttribute

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 标记 UIWindow 使用的 DataContext 类型。
    /// 框架在窗口创建时自动实例化 DataContext 并注入依赖。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DataContextAttribute : Attribute
    {
        public Type DataContextType { get; }

        public DataContextAttribute(Type dataContextType);
    }
}
```

### 5.7 UIBase 扩展方法

```csharp
// 在 UIBase 类中新增的成员:

public partial class UIBase
{
    // ──── DataContext 访问 ────

    /// <summary>
    /// 数据上下文。UIWindow 直接持有，UIWidget 向上查找。
    /// 无 DataContext 时返回 null。
    /// </summary>
    public DataContext DataContext { get; }

    /// <summary>
    /// 获取强类型的 DataContext。
    /// UIWindow 直接返回，UIWidget 向上遍历 Parent 链。
    /// </summary>
    public T GetDataContext<T>() where T : DataContext;

    // ──── 标量绑定 ────

    /// <summary>
    /// 绑定到 BindableProperty，值变化时自动回调。
    /// 首次绑定时用当前值立即回调一次（首次同步）。
    /// OnDestroy 时自动解绑。
    /// </summary>
    /// <param name="property">数据源属性。</param>
    /// <param name="onChanged">值变化回调。(newValue)</param>
    public void Bind<T>(BindableProperty<T> property, Action<T> onChanged);

    /// <summary>
    /// 绑定到 BindableProperty（带旧值）。
    /// </summary>
    /// <param name="property">数据源属性。</param>
    /// <param name="onChanged">值变化回调。(oldValue, newValue)</param>
    public void Bind<T>(BindableProperty<T> property, Action<T, T> onChanged);

    // ──── 生命周期 ────

    /// <summary>
    /// 声明数据绑定。子类 override 在这里调用 Bind()。
    /// 在 InternalCreate 流程中自动调用。
    /// </summary>
    protected virtual void SetupBindings();

    /// <summary>
    /// 移除所有绑定。在销毁时自动调用。
    /// </summary>
    internal void RemoveAllBindings();
}
```

---

## 6. 类图

```
┌─────────────────────────────────────────────────────────────────────┐
│                           类图                                      │
│                                                                     │
│  ┌──────────────────────────┐     ┌──────────────────────────────┐ │
│  │  «abstract» UIBase       │     │  BindableProperty<T>         │ │
│  ├──────────────────────────┤     ├──────────────────────────────┤ │
│  │ + DataContext             │     │ + Value: T { get; set; }     │ │
│  │ + GetDataContext<T>()     │────▶│ + OnValueChanged: event     │ │
│  │ + Bind<T>(prop, cb)       │     │ + Dispose()                  │ │
│  │ # SetupBindings()         │     │ + SetValueSilently(v)        │ │
│  │ # RemoveAllBindings()     │     │ + ForceNotify()              │ │
│  └──────────┬───────────────┘     └──────────────────────────────┘ │
│             │                            ▲                         │
│             │ extends                    │ marks dirty              │
│  ┌──────────┴───────────┐     ┌──────────┴──────────────┐         │
│  │  «abstract» UIWindow │     │  BatchScheduler         │         │
│  ├──────────────────────┤     │  «Singleton»            │         │
│  │ - _dataContext        │     ├─────────────────────────┤         │
│  │ + DataContext         │     │ # MarkDirty(prop)       │         │
│  │ + Init(...)           │     │ # MarkDirty(list)       │         │
│  │ + InternalLoad(...)   │     │ # Flush()               │         │
│  │ + InternalCreate()    │     └─────────────────────────┘         │
│  │ + InternalDestroy()   │              │                          │
│  └──────────────────────┘              │ triggers Flush            │
│             │                           │                          │
│             │ [DataContext]             ▼                          │
│  ┌──────────┴───────────┐     ┌─────────────────────────┐         │
│  │  DataContextAttribute│     │  «abstract» DataContext  │         │
│  ├──────────────────────┤     ├─────────────────────────┤         │
│  │ + DataContextType    │────▶│ # MapProperty<...>()     │         │
│  └──────────────────────┘     │ # MapList<...>()         │         │
│                               │ + Dispose()              │         │
│                               └───────────┬─────────────┘         │
│                                           │ extends               │
│                               ┌───────────┴─────────────┐         │
│                               │ DataContext<TView>       │         │
│                               └─────────────────────────┘         │
│                                                                   │
│  ┌──────────────────────────┐     ┌──────────────────────────┐   │
│  │  ObservableList<T>       │     │  ListChangedEventArgs<T> │   │
│  │  where T: struct         │     │  «readonly struct»       │   │
│  ├──────────────────────────┤     ├──────────────────────────┤   │
│  │ + Count                  │     │ + Type: ListChangeType   │   │
│  │ + this[int]: T { get }   │     │ + Index: int             │   │
│  │ + Add/Insert/Remove...   │────▶│ + Item: T                │   │
│  │ + Replace(int, T)        │     │ + OldItem: T             │   │
│  │ + ReplaceAll(IEnumerable)│     │ + OldIndex: int          │   │
│  │ + OnChanged: event       │     │ + NewItems: IReadOnlyList│   │
│  │ + Dispose()              │     └──────────────────────────┘   │
│  └──────────────────────────┘                                     │
│                                                                   │
│  ┌──────────────────────────┐                                     │
│  │  UIWidget                │                                     │
│  ├──────────────────────────┤                                     │
│  │ + DataContext → 向上查找  │──────── Parent 链 ──────▶ UIBase   │
│  │ + GetDataContext<T>()    │                                     │
│  │ + Bind<T>(prop, cb)      │                                     │
│  └──────────────────────────┘                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 7. 时序图

### 7.1 窗口打开全流程

```
┌──────┐  ┌──────────┐  ┌────────────┐  ┌────────────────┐  ┌──────────┐  ┌─────────────┐
│Caller│  │ UIModule │  │ UIWindow   │  │ BatchScheduler │  │DataContext│  │ Model(s)    │
└──┬───┘  └────┬─────┘  └─────┬──────┘  └───────┬────────┘  └────┬─────┘  └──────┬──────┘
   │           │              │                 │                 │              │
   │ ShowUI<BagUI>()          │                 │                 │              │
   │──────────▶│              │                 │                 │              │
   │           │              │                 │                 │              │
   │           │ CreateInstance<BagUI>()         │                 │              │
   │           │──────────────▶│                 │                 │              │
   │           │              │                 │                 │              │
   │           │              │ Init(name,layer,│...)              │              │
   │           │              │◀────────────────│                 │              │
   │           │              │                 │                 │              │
   │           │              │ InternalLoad(assetName, callback) │              │
   │           │◀─────────────│                 │                 │              │
   │           │              │                 │                 │              │
   │           │  (异步加载 Prefab ...)          │                 │              │
   │           │              │                 │                 │              │
   │           │ OnWindowPrepare(window)        │                 │              │
   │           │◀───────────────────────────────│                 │              │
   │           │              │                 │                 │              │
   │           │ ┌──────────────────────────────────────────────────────────────┐
   │           │ │ 1. 检查 [DataContext] 特性                                    │
   │           │ │ 2. 解析 BagDataContext 构造函数参数                             │
   │           │ │ 3. 从 Singleton<T>.Instance 获取依赖                           │
   │           │ │ 4. new BagDataContext(player, bag, vip)                       │
   │           │ │ 5. DataContext 开始订阅 Model 变更                             │
   │           │ └──────────────────────────────────────────────────────────────┘
   │           │              │                 │                 │              │
   │           │ window.SetDataContext(ctx)      │                 │              │
   │           │──────────────▶│                 │                 │              │
   │           │              │                 │                 │              │
   │           │              │ InternalCreate() │                 │              │
   │           │──────────────▶│                 │                 │              │
   │           │              │                 │                 │              │
   │           │              │ ┌─────────────────────────────────────────────┐  │
   │           │              │ │ Inject()           ← 现有 DI 钩子           │  │
   │           │              │ │ ScriptGenerator()  ← 现有代码生成绑定       │  │
   │           │              │ │ BindMemberProperty()← 现有成员绑定          │  │
   │           │              │ │ SetupBindings()    ← 新增: Bind(ctx.Gold..)│  │
   │           │              │ │ RegisterEvent()    ← 现有事件注册           │  │
   │           │              │ │ OnCreate()         ← 现有用户初始化         │  │
   │           │              │ └─────────────────────────────────────────────┘  │
   │           │              │                 │                 │              │
   │           │              │ InternalRefresh()│                 │              │
   │           │──────────────▶│                 │                 │              │
   │           │              │                 │                 │              │
   │           │              │ OnRefresh()     │                 │              │
   │           │              │─────────────────│                 │              │
   │           │              │  (命令式业务逻辑: │                 │              │
   │           │              │   发网络请求等)   │                 │              │
   │           │              │                 │                 │              │
```

### 7.2 数据变更自动刷新流程

```
┌──────┐  ┌──────────┐  ┌────────────┐  ┌────────────────┐  ┌──────────┐
│Event │  │ Model     │  │ Batch      │  │ DataContext    │  │ View     │
│Handler│ │          │  │ Scheduler  │  │                │  │          │
└──┬───┘  └────┬─────┘  └─────┬──────┘  └───────┬────────┘  └────┬─────┘
   │           │              │                 │                │
   │ (服务器返回购买结果)      │                 │                │
   │           │              │                 │                │
   │ gold.Value -= 100        │                 │                │
   │──────────▶│              │                 │                │
   │           │ 值立即变化    │                 │                │
   │           │ MarkDirty()  │                 │                │
   │           │──────────────▶│                 │                │
   │           │              │ _dirtyProps     │                │
   │           │              │ .Add(gold)      │                │
   │           │              │                 │                │
   │ items.Replace(3, newItem)│                 │                │
   │──────────▶│              │                 │                │
   │           │ MarkDirty()  │                 │                │
   │           │──────────────▶│                 │                │
   │           │              │ _dirtyLists     │                │
   │           │              │ .Add(items)     │                │
   │           │              │                 │                │
   │ (业务代码继续执行...)     │                 │                │
   │           │              │                 │                │
   │           │              │ LateUpdate:     │                │
   │           │              │ Flush()         │                │
   │           │              │─────────────────│                │
   │           │              │                 │                │
   │           │              │ gold.FireCallback()              │                │
   │           │              │─────────────────────────────────▶│                │
   │           │              │                 │ OnValueChanged │                │
   │           │              │                 │ (oldGold,50)   │                │
   │           │              │                 │ → converter    │                │
   │           │              │                 │ → target.Value │                │
   │           │              │                 │ = "50"         │                │
   │           │              │                 │                │
   │           │              │                 │ target 变化 →  │                │
   │           │              │                 │ MarkDirty()    │                │
   │           │              │                 │ (本轮 Flush    │                │
   │           │              │                 │  中，下帧处理) │                │
   │           │              │                 │                │
   │           │              │                 │                │ txtGold.text
   │           │              │                 │                │ = "50"
   │           │              │                 │                │
   │           │              │ _dirty.Clear()  │                │
   │           │              │◀────────────────│                │
   │           │              │                 │                │
   │           │              │ 下一帧 Flush:   │                │
   │           │              │ target.FireCallback()             │                │
   │           │              │─────────────────────────────────▶│                │
   │           │              │                 │ Bind 回调触发  │
   │           │              │                 │                │
```

> **注意**: DataContext 的 converter 输出也是 BindableProperty，它的赋值也会经过 BatchScheduler。这意味着 DataContext 转换和 View 回调可能跨两帧。如果需要同帧完成，可以在 Flush 中做两轮处理（先处理 Model→DataContext，再处理 DataContext→View）。

### 7.3 窗口关闭流程

```
┌──────┐  ┌──────────┐  ┌────────────┐  ┌────────────────┐
│Caller│  │ UIModule │  │ UIWindow   │  │ DataContext    │
└──┬───┘  └────┬─────┘  └─────┬──────┘  └───────┬────────┘
   │           │              │                 │
   │ CloseUI<BagUI>()         │                 │
   │──────────▶│              │                 │
   │           │              │                 │
   │           │ InternalDestroy()              │
   │           │──────────────▶│                 │
   │           │              │                 │
   │           │              │ RemoveAllUIEvent()  ← 现有
   │           │              │─────────────────│
   │           │              │                 │
   │           │              │ RemoveAllBindings()  ← 新增
   │           │              │─────────────────│
   │           │              │ 取消所有 Bind    │
   │           │              │ 订阅             │
   │           │              │                 │
   │           │              │ DataContext.Dispose()  ← 新增
   │           │              │────────────────────────▶│
   │           │              │                 │ 取消所有 Model 订阅
   │           │              │                 │ 清理 MapProperty/MapList
   │           │              │                 │ 释放输出属性
   │           │              │                 │
   │           │              │ 销毁子 Widget   │
   │           │              │─────────────────│
   │           │              │ (每个 Widget 也 │
   │           │              │  RemoveAllBindings)
   │           │              │                 │
   │           │              │ _prepareCallback = null  ← 现有
   │           │              │                 │
   │           │              │ OnDestroy()     │  ← 现有
   │           │              │                 │
   │           │              │ Destroy(_panel) │  ← 现有
   │           │              │                 │
   │           │ Pop(window)  │                 │
   │           │◀─────────────│                 │
   │           │              │                 │
   │           │ OnSortWindowDepth()            │
   │           │ OnSetWindowVisible()           │
   │           │              │                 │
```

---

## 8. 示例代码

### 8.1 Model 定义

```csharp
// ===== Models/PlayerModel.cs =====
using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>
    /// 玩家数据模型。纯数据，不知道 UI 存在。
    /// </summary>
    public class PlayerModel : Singleton<PlayerModel>
    {
        public readonly BindableProperty<long> Gold = new(0);
        public readonly BindableProperty<int> Level = new(1);
        public readonly BindableProperty<string> Name = new("");
        public readonly BindableProperty<long> Exp = new(0);

        protected override void OnInit()
        {
            // 初始化时从存档加载
        }
    }
}
```

```csharp
// ===== Models/BagModel.cs =====
using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>
    /// 背包道具数据（不可变 struct）。
    /// </summary>
    public readonly record struct BagItemData
    {
        public int ItemId { get; init; }
        public int Count { get; init; }
        public bool IsNew { get; init; }
    }

    /// <summary>
    /// 背包数据模型。
    /// </summary>
    public class BagModel : Singleton<BagModel>
    {
        public readonly ObservableList<BagItemData> Items = new();

        public readonly BindableProperty<int> MaxCapacity = new(100);

        protected override void OnInit()
        {
        }
    }
}
```

```csharp
// ===== Models/VipModel.cs =====
using GameLogic.DataBinding;

namespace GameLogic
{
    public class VipModel : Singleton<VipModel>
    {
        public readonly BindableProperty<int> VipLevel = new(0);
        public readonly BindableProperty<int> BagCapacityBonus = new(0);
        public readonly BindableProperty<bool> HasDiscount = new(false);

        protected override void OnInit()
        {
        }
    }
}
```

### 8.2 DataContext 定义

```csharp
// ===== UI/Bag/BagDataContext.cs =====
using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>
    /// 背包面板的数据聚合层。
    /// 将 PlayerModel + BagModel + VipModel 组合为 View 友好的格式。
    /// </summary>
    public class BagDataContext : DataContext<BagUI>
    {
        // ──── 暴露给 View 的输出属性 ────

        /// <summary>格式化后的金币显示文本。</summary>
        public readonly BindableProperty<string> GoldText = new("");

        /// <summary>背包容量文本，如 "45/120"。</summary>
        public readonly BindableProperty<string> CapacityText = new("");

        /// <summary>VIP 等级显示，如 "VIP 3"。</summary>
        public readonly BindableProperty<string> VipText = new("");

        /// <summary>展示用道具列表（已转换格式）。</summary>
        public readonly ObservableList<BagItemDisplayData> DisplayItems = new();

        public BagDataContext()  // 无参构造函数，内部直接访问 Singleton（HybridCLR 安全）
        {
            // 直接获取 Singleton 依赖（零反射，与 UIModule.CreateInstance 模式一致）
            var player = PlayerModel.Instance;
            var bag = BagModel.Instance;
            var vip = VipModel.Instance;

            // ──── 标量映射 ────

            // 单源: 金币 → 格式化文本
            MapProperty(
                player.Gold,
                GoldText,
                gold => FormatGold(gold));

            // 三源: 金币+道具数量+容量 → 容量文本
            MapProperty(
                player.Gold,
                bag.MaxCapacity,
                vip.BagCapacityBonus,
                CapacityText,
                (gold, maxCap, bonus) =>
                {
                    int totalCap = maxCap + bonus;
                    int used = bag.Items.Count;
                    return $"{used}/{totalCap}";
                });

            // 单源: VIP 等级 → 显示文本
            MapProperty(
                vip.VipLevel,
                VipText,
                lv => lv > 0 ? $"VIP {lv}" : "");

            // ──── 列表映射 ────

            // Model 列表 → 展示列表（带转换）
            MapList(
                bag.Items,
                DisplayItems,
                item => new BagItemDisplayData
                {
                    ItemId = item.ItemId,
                    Count = item.Count,
                    IsNew = item.IsNew,
                    // 可在这里组合多源信息
                    // ShowDiscount = vip.HasDiscount.Value,
                });
        }

        private static string FormatGold(long gold)
        {
            if (gold >= 1_000_000_000)
                return $"{gold / 1_000_000_000.0:F1}B";
            if (gold >= 1_000_000)
                return $"{gold / 1_000_000.0:F1}M";
            if (gold >= 10_000)
                return $"{gold / 1_000.0:F1}K";
            return gold.ToString();
        }
    }

    /// <summary>
    /// 背包 Item 的展示数据（不可变）。
    /// </summary>
    public readonly record struct BagItemDisplayData
    {
        public int ItemId { get; init; }
        public int Count { get; init; }
        public bool IsNew { get; init; }
        public bool ShowDiscount { get; init; }
    }
}
```

### 8.3 View 层实现

```csharp
// ===== UI/Bag/BagUI.cs =====
using GameLogic.DataBinding;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    [Window(UILayer.UI)]
    [DataContext(typeof(BagDataContext))]
    class BagUI : UIWindow
    {
        #region 脚本工具生成的代码
        private Text txtGold;
        private Text txtCapacity;
        private Text txtVip;
        private RectTransform rectItemList;

        protected override void ScriptGenerator()
        {
            txtGold = FindChildComponent<Text>("m_txtGold");
            txtCapacity = FindChildComponent<Text>("m_txtCapacity");
            txtVip = FindChildComponent<Text>("m_txtVip");
            rectItemList = FindChildComponent<RectTransform>("m_rectItemList");
        }
        #endregion

        #region 数据绑定

        protected override void SetupBindings()
        {
            var ctx = GetDataContext<BagDataContext>();

            // Bind: 订阅 DataContext 输出属性
            // 首次绑定时自动用当前值回调一次（首次同步）
            Bind(ctx.GoldText, gold => txtGold.text = gold);
            Bind(ctx.CapacityText, cap => txtCapacity.text = cap);
            Bind(ctx.VipText, vip => txtVip.text = vip);

            // 列表数据绑定（Phase 2 接入 UIListWidget）
            // ctx.DisplayItems.OnChanged += OnItemListChanged;
        }

        #endregion

        #region 事件

        protected override void RegisterEvent()
        {
            AddUIEvent(EventId.OnCloseBagClick, OnCloseClick);
        }

        #endregion

        #region 业务逻辑（命令式，走 OnRefresh）

        protected override void OnRefresh()
        {
            // 命令式业务逻辑：
            // - 向服务器请求背包数据
            // - 记录打开日志
            // - Tab 切换逻辑
            // 这些和数据绑定无关，保留在 OnRefresh 中

            SendBagOpenRequest();
        }

        private void SendBagOpenRequest()
        {
            // 网络请求...
        }

        #endregion

        private void OnCloseClick()
        {
            Close();
        }
    }
}
```

### 8.4 Widget 层使用

```csharp
// ===== UI/Bag/BagHeaderWidget.cs =====
using GameLogic.DataBinding;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// 背包头部 Widget（显示金币和容量）。
    /// 通过 Parent 链获取 Window 的 DataContext。
    /// </summary>
    public class BagHeaderWidget : UIWidget
    {
        private Text txtGold;
        private Text txtCapacity;

        protected override void OnCreate()
        {
            txtGold = FindChildComponent<Text>("m_txtGold");
            txtCapacity = FindChildComponent<Text>("m_txtCapacity");

            // 通过 Parent 链获取 Window 的 DataContext
            var ctx = GetDataContext<BagDataContext>();

            Bind(ctx.GoldText, gold => txtGold.text = gold);
            Bind(ctx.CapacityText, cap => txtCapacity.text = cap);
        }
    }
}
```

### 8.5 业务逻辑触发数据变更

```csharp
// ===== EventHandlers/BagEventHandler.cs =====
namespace GameLogic
{
    /// <summary>
    /// 背包相关业务逻辑（MVE 的 E 层）。
    /// 修改 Model 数据，DataContext 自动同步到 View。
    /// </summary>
    public class BagEventHandler
    {
        public void OnBuyItemResponse(BuyItemResponse response)
        {
            // 修改 Model（直接赋值）
            var player = PlayerModel.Instance;
            var bag = BagModel.Instance;

            player.Gold.Value -= response.Cost;             // 金币变化 → View 自动刷新
            bag.Items.Replace(response.SlotIndex,
                bag.Items[response.SlotIndex] with
                {
                    Count = bag.Items[response.SlotIndex].Count + response.Count
                });                                           // 道具变化 → View 自动刷新

            // 不需要手动调 OnRefresh
            // 不需要手动通知哪个 UI
            // 所有绑定了 Gold 和 Items 的 UI 自动更新
        }

        public void OnUseItemResponse(UseItemResponse response)
        {
            var bag = BagModel.Instance;
            var player = PlayerModel.Instance;

            if (response.RemainingCount <= 0)
            {
                bag.Items.RemoveAt(response.SlotIndex);       // 删除道具
            }
            else
            {
                bag.Items.Replace(response.SlotIndex,
                    bag.Items[response.SlotIndex] with
                    {
                        Count = response.RemainingCount,
                        IsNew = false
                    });                                       // 更新道具
            }

            // 使用道具可能影响经验值
            player.Exp.Value += response.ExpGain;             // 经验变化
        }
    }
}
```

### 8.6 无 DataContext 的窗口（向后兼容）

```csharp
// ===== 现有窗口，不需要数据绑定 =====
namespace GameLogic
{
    // 没有 [DataContext] 特性 → 不创建 DataContext
    [Window(UILayer.UI, location: "BattleMainUI")]
    class BattleMainUI : UIWindow
    {
        #region 脚本工具生成的代码
        private RectTransform _rectContainer;
        protected override void ScriptGenerator()
        {
            _rectContainer = FindChildComponent<RectTransform>("m_rectContainer");
        }
        #endregion

        // 不需要 SetupBindings
        // 不需要 OnRefresh
        // 完全和以前一样，零改动
    }
}
```

---

## 9. 边界场景与约束

### 9.1 批次合并的两轮处理

```
问题: Model→DataContext 的转换输出也是 BindableProperty，
      它的赋值也经过 BatchScheduler。
      如果只做一轮 Flush，View 会在下一帧才看到数据。

方案: Flush 做两轮处理

void Flush()
{
    _isFlushing = true;

    // 第一轮: 处理 Model 层变更 → 触发 DataContext 转换
    foreach (var prop in _dirtyProps)
        prop.FireCallbacks();
    foreach (var list in _dirtyLists)
        list.FireCallbacks();

    _dirtyPropsFromFirstRound = new HashSet(_dirtyProps);
    _dirtyProps.Clear();
    _dirtyLists.Clear();

    // 第二轮: 处理 DataContext 输出属性的变更 → 触发 View 回调
    if (_dirtyProps.Count > 0 || _dirtyLists.Count > 0)
    {
        foreach (var prop in _dirtyProps)
            prop.FireCallbacks();
        foreach (var list in _dirtyLists)
            list.FireCallbacks();
    }

    _dirtyProps.Clear();
    _dirtyLists.Clear();
    _isFlushing = false;
}

效果: Model 变更 → 同帧 DataContext 转换 → 同帧 View 刷新
```

### 9.2 DataContext 转换函数无副作用

```
规则: DataContext 的 MapProperty/MapList converter 必须是纯函数
  ✗ 不能修改 Model
  ✗ 不能发起网络请求
  ✗ 不能发送 GameEvent
  ✓ 只做数据格式转换

违反示例（禁止）:
MapProperty(Gold, GoldText, gold =>
{
    if (gold > 10000)
        AchievementModel.Instance.Unlock("rich");  // 副作用！
    return FormatGold(gold);
});

正确做法: 在 EventHandler 中处理
void OnGoldChanged()
{
    if (PlayerModel.Instance.Gold.Value > 10000)
        AchievementModel.Instance.Unlock("rich");
}

执行机制: 通过文档和编码规范约束，不做运行时检测。
```

### 9.3 Widget 动态创建时的绑定

```
场景: Window.OnRefresh 中动态创建 Widget

protected override void OnRefresh()
{
    var header = CreateWidget<BagHeaderWidget>(goHeaderRoot);
    // Widget.OnCreate 中调用 GetDataContext<BagDataContext>()
    // 此时 Window 的 DataContext 已经注入 ✓
    // Parent 链: Widget → Window ✓
    // 绑定正常工作 ✓
}

时序保证:
  DataContext 创建 → InternalCreate → OnCreate/OnRefresh
  Widget 动态创建发生在 OnRefresh 中，此时 DataContext 已就绪
```

### 9.4 同一 Model 被多个 DataContext 订阅

```
场景: PlayerModel.Gold 被 MainUI、BagUI、ShopUI 三个 DataContext 订阅

Gold.Value -= 100;

BatchScheduler.Flush:
  Gold.FireCallbacks()
    → MainUIDataContext converter → MainUI Bind 回调
    → BagDataContext converter   → BagUI Bind 回调
    → ShopDataContext converter  → ShopUI Bind 回调

完全正确:
  - 每个 DataContext 有独立的订阅
  - 每个窗口有独立的 Bind 回调
  - 一处赋值，多处自动更新 ✓
```

### 9.5 DataContext 内部访问的 Singleton 未初始化

```
场景: DataContext 构造函数内访问的 Singleton 不存在

class BagDataContext : DataContext<BagUI>
{
    public BagDataContext()
    {
        var bad = NotFoundModel.Instance;  // ← 未注册的 Singleton
    }
}

行为: Singleton<T>.Instance 懒初始化时自动创建实例。
  如果 NotFoundModel 没有继承 Singleton<NotFoundModel>，编译期就会发现。
  这比反射方案更安全——编译期检查而非运行时异常。
```

### 9.6 Tab 切换场景

```
场景: 背包有"全部"和"装备"两个 Tab，切换时列表数据不同

推荐方案: 使用 OnRefresh 处理

private int _currentTab = 0;

protected override void OnRefresh()
{
    // Tab 切换是命令式逻辑，走 OnRefresh
    RefreshTabContent(_currentTab);
}

private void OnTabClick(int tabIndex)
{
    _currentTab = tabIndex;
    // 可以触发 OnRefresh
}

替代方案: 在 DataContext 中处理
// 用 BindableProperty<int> CurrentTab 驱动
// 根据不同 Tab 值映射不同的源列表
// 但这增加了 DataContext 的复杂度
// Phase 1 建议 Tab 逻辑放 OnRefresh，后续按需优化
```

### 9.7 Bind 首次同步

```
行为: Bind() 调用后立即用当前值触发一次回调

Bind(ctx.GoldText, gold => txtGold.text = gold);

// 如果 GoldText.Value 当前是 "1,500"
// 则回调立即被调用: gold = "1,500"
// txtGold.text = "1,500"  ← 立即显示

目的:
  - 首次打开窗口时数据立即显示
  - 不需要 OnRefresh 中手动赋值
  - 开发者只需写 Bind，首次和后续都自动处理

如果不需要首次同步（如等待网络数据）:
  - 可以先 SetValueSilently 初始化 DataContext 属性
  - 数据到来后赋值触发 Bind 回调
```

---

## 10. 文件结构规划

```
Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/
├── BindableProperty.cs            (~80 行)   响应式属性
├── ObservableList.cs              (~200 行)  响应式列表
├── ListChangedEventArgs.cs        (~30 行)   列表变更事件参数
├── ListChangeType.cs              (~15 行)   列表变更类型枚举
├── BatchScheduler.cs              (~80 行)   帧级批次合并
├── DataContext.cs                  (~150 行)  DataContext 基类
├── DataContextAttribute.cs        (~25 行)   DataContext 声明特性
├── UIExtension.cs                  (~60 行)   UIBase 绑定扩展
├── Binding.cs                     (~20 行)   绑定记录结构
└── DataBinding.meta                           Unity 文件夹元数据

预估代码量: ~660 行基础设施代码
测试代码: Assets/TEngine/Tests/EditMode/DataBinding/
├── BindablePropertyTests.cs
├── ObservableListTests.cs
├── BatchSchedulerTests.cs
├── DataContextTests.cs
└── UIExtensionTests.cs
```

---

## 11. 与现有系统的兼容性

### 11.1 零破坏性

```
┌─────────────────────────────────────────────────────────────┐
│ 现有系统                  │ 影响                             │
├───────────────────────────┼─────────────────────────────────┤
│ UIBase                    │ 新增方法，不修改现有方法签名      │
│ UIBase.Injector           │ 保留，不做修改                    │
│ UIWindow.InternalCreate() │ 末尾新增 SetupBindings() 调用     │
│ UIWindow.InternalDestroy()│ 新增 RemoveAllBindings() 调用     │
│ OnRefresh()               │ 保留，共存                        │
│ GameEvent / GameEventMgr  │ 保留，共存                        │
│ WindowAttribute           │ 保留，不做修改                    │
│ UIWidget                  │ 新增方法，不修改现有方法签名      │
│ CreateWidget 系列方法     │ 不修改                            │
│ ScriptGenerator()         │ 不修改                            │
│ BindMemberProperty()      │ 不修改                            │
│ RegisterEvent()           │ 不修改                            │
└───────────────────────────┴─────────────────────────────────┘

已有窗口（BattleMainUI, LoginUI）:
  - 没有 [DataContext] 特性 → 不创建 DataContext
  - 没有 SetupBindings override → 空实现，无影响
  - 完全向后兼容
```

### 11.2 渐进式采用

```
阶段 1: 新窗口使用 Bind
  - 新窗口加 [DataContext] 和 SetupBindings
  - 旧窗口不改

阶段 2: 按需迁移旧窗口
  - 旧窗口加 [DataContext]
  - OnRefresh 中的手动赋值迁移到 Bind
  - OnRefresh 只保留命令式逻辑

阶段 3: 全面使用
  - 所有窗口使用 Bind + OnRefresh 共存模式
```

### 11.3 BatchScheduler 的注册

```
已确认 SingletonSystem 支持 ILateUpdate 接口。
BatchScheduler 实现 ILateUpdate，由 SingletonSystem 在 LateUpdate 阶段自动调用 Flush。

public sealed class BatchScheduler : Singleton<BatchScheduler>, ILateUpdate
{
    public void OnLateUpdate() { Flush(); }
}

SingletonSystem.BuildLifeCycle 自动检测 ILateUpdate 并注册到 _lateUpdates 列表。
BatchScheduler 首次通过 Instance 访问时自动初始化（懒加载）。
```

---

## 12. Phase 2 展望

以下内容不在 Phase 1 范围内，作为后续规划参考。

### 12.1 UIListWidget（虚拟化列表）

```
UIListWidget<TData, TItem>
  - 绑定 ObservableList<TData>
  - 虚拟化: 只创建可视区域的 TItem Widget
  - 对象池: 滚出可视区的 Widget 回收到池
  - Diff: OnReplace → 刷新单个 Item Widget
  - 支持: 固定高度 / 动态高度 / 网格布局
  - 与 ScrollView 深度集成
```

### 12.2 编辑器辅助工具

```
- DataContext 骨架生成器: 从 Model 定义自动生成 DataContext 类
- 绑定预览: Inspector 中显示当前 View 绑定了哪些 DataContext 属性
- 编译期检查: 验证 DataContext 引用的 Model 字段是否存在
```

### 12.3 可视化绑定（远期）

```
- Inspector 中选择 Model + Property 绑定到 UI 控件
- 底层生成代码（不是序列化资产），避免 Model 改名后"断线"
- 编译期报错，而非运行时
```

---

> **文档结束**  
> 本文档定义了 TEngine UI 数据绑定系统 Phase 1 的完整设计。  
> 所有组件的接口、生命周期、边界行为均已明确，可直接作为实施蓝图。
