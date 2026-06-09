# UI 数据绑定系统 Phase 1 — 实现规格

> **版本**: v1.1 — HybridCLR 兼容修订（DataContext 无参构造函数 + 直接 Singleton 访问）
> **日期**: 2026-06-08
> **状态**: 实现规格
> **配套文档**: [ui-data-binding-phase1.md](./ui-data-binding-phase1.md)（架构设计）
> **范围**: 核心组件实现算法、集成修改点、测试策略
>
> **v1.1 变更**: 移除 DataContext 反射依赖解析（避免 `MakeGenericType` 的 HybridCLR 兼容风险），
> 改为无参构造函数 + 直接 `Singleton<T>.Instance` 访问，与 UIModule.CreateInstance 模式一致。

---

## 目录

1. [概述](#1-概述)
2. [BindableProperty\<T\> 实现](#2-bindablepropertyt-实现)
3. [ObservableList\<T\> 实现](#3-observablelistt-实现)
4. [BatchScheduler 实现](#4-batchscheduler-实现)
5. [DataContext 实现](#5-datacontext-实现)
6. [DataContextFactory 实现](#6-datacontextfactory-实现)
7. [UIBase 绑定扩展实现](#7-uibase-绑定扩展实现)
8. [集成修改点](#8-集成修改点)
9. [Binding 辅助结构](#9-binding-辅助结构)
10. [文件清单与代码量估算](#10-文件清单与代码量估算)
11. [测试策略](#11-测试策略)
12. [实现顺序建议](#12-实现顺序建议)

---

## 1. 概述

本文档是 [ui-data-binding-phase1.md](./ui-data-binding-phase1.md) 的配套实现规格，聚焦于：
- 每个组件的**内部算法和数据结构**
- 与现有代码的**精确集成修改点**（含行号）
- 完整的**测试用例矩阵**

阅读前提：已阅读架构设计文档并理解 MVE 架构、BindableProperty、DataContext 等核心概念。

---

## 2. BindableProperty\<T\> 实现

### 2.1 内部字段

```csharp
public sealed class BindableProperty<T> : IDisposable
{
    private T _value;                                // 当前值
    private readonly IEqualityComparer<T> _comparer; // 值比较器
    private bool _isDirty;                           // 脏标记
    private T _oldValue;                             // 变化前的快照（仅首次脏时记录）
    private bool _isDisposed;                        // 释放标记
    
    /// <summary>
    /// 值变化回调。(oldValue, newValue)
    /// 在 BatchScheduler.Flush 中触发。
    /// </summary>
    public event Action<T, T> OnValueChanged;
}
```

### 2.2 构造函数

```csharp
public BindableProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
{
    _value = initialValue;
    _comparer = comparer ?? EqualityComparer<T>.Default;
    _isDirty = false;
    _isDisposed = false;
}
```

### 2.3 Value 赋值算法

```csharp
public T Value
{
    get => _value;
    set
    {
        if (_isDisposed) return;
        if (_comparer.Equals(_value, value)) return;  // 值未变，跳过

        if (!_isDirty)           // 首次变脏
        {
            _oldValue = _value;  // 快照旧值（仅第一次）
            _isDirty = true;
        }
        _value = value;          // 值立即更新

        if (OnValueChanged != null)  // 有订阅者才入队
            BatchScheduler.Instance.MarkDirty(this);
    }
}
```

**设计要点**：
- `_oldValue` 只在 `_isDirty = false → true` 时快照，同帧 `gold=100; gold=200; gold=50;` → `_oldValue` = 原值（如 0），`_value` = 50
- 引用类型：`_oldValue` 存的是引用。D3 约束引用类型必须替换对象，所以旧引用不会被外部修改
- `EqualityComparer<T>.Default` 对 `record struct` 自动按值比较，对引用类型用 `ReferenceEquals`（除非重写了 Equals）

### 2.4 Flush 回调触发

```csharp
/// <summary>
/// 由 BatchScheduler.Flush 调用。触发 OnValueChanged 回调。
/// </summary>
internal void FireCallback()
{
    if (!_isDirty || _isDisposed) return;
    _isDirty = false;
    
    var old = _oldValue;
    var current = _value;
    OnValueChanged?.Invoke(old, current);
}
```

### 2.5 辅助方法

```csharp
public bool IsDisposed => _isDisposed;

/// <summary>
/// 手动标记脏，强制触发回调（即使值未变）。
/// </summary>
public void ForceNotify()
{
    if (_isDisposed) return;
    _isDirty = true;
    if (OnValueChanged != null)
        BatchScheduler.Instance.MarkDirty(this);
}

/// <summary>
/// 静默赋值，不触发通知。
/// 用于 DataContext MapProperty 初始化 target。
/// </summary>
public void SetValueSilently(T value)
{
    if (_isDisposed) return;
    _value = value;
    // 不标记脏，不通知
}

public void Dispose()
{
    if (_isDisposed) return;
    _isDisposed = true;
    OnValueChanged = null;  // 清除所有订阅者
}
```

### 2.6 边界行为速查

| 场景 | 行为 |
|------|------|
| 构造后无赋值 | `_isDirty = false`，Flush 不触发 |
| 赋相同值 | `_comparer` 判断相等，直接 return |
| 赋值后立刻读 | 读到新值（`_value` 已更新） |
| 同帧赋值多次 | `_oldValue` 只记录第一次的旧值，回调一次 `(旧值, 最终值)` |
| 赋值 null（引用类型） | 与旧 null 比较，`ReferenceEquals` → 不触发 |
| Dispose 后赋值 | `_isDisposed = true`，直接 return |
| 无订阅者赋值 | `OnValueChanged == null`，不入 BatchScheduler |
| ForceNotify | 标脏但 `_oldValue` 可能过时，回调参数为 `(_oldValue, _value)` |

---

## 3. ObservableList\<T\> 实现

### 3.1 内部字段

```csharp
public sealed class ObservableList<T> : IDisposable, IReadOnlyList<T>
    where T : struct, IEquatable<T>
{
    private readonly List<T> _items;           // 内部存储
    private int _operationCount;               // 本帧操作计数
    private ListChangeType _firstOpType;       // 第一次操作类型
    private ListChangedEventArgs<T> _firstEventArgs; // 第一次事件参数
    private bool _isDirty;                     // 是否有待处理事件
    private bool _isDisposed;

    public event Action<ListChangedEventArgs<T>> OnChanged;
}
```

### 3.2 操作与事件映射

每个写操作内部流程：

```csharp
// 以 Add 为例
public void Add(T item)
{
    if (_isDisposed) return;
    _items.Add(item);
    
    NotifyChanged(new ListChangedEventArgs<T>
    {
        Type = ListChangeType.Add,
        Index = _items.Count - 1,
        Item = item
    });
}
```

```csharp
private void NotifyChanged(in ListChangedEventArgs<T> args)
{
    if (_isDisposed || OnChanged == null) return;
    
    _operationCount++;
    
    if (_operationCount == 1)
    {
        // 第一次操作：保留原始事件
        _firstOpType = args.Type;
        _firstEventArgs = args;
    }
    // 第二次及以后：不再更新 _firstEventArgs
    // Flush 时根据 _operationCount 决定是否合并
    
    _isDirty = true;
    BatchScheduler.Instance.MarkDirty(this);
}
```

### 3.3 各操作的 NotifyChanged 参数

| 操作 | ListChangeType | Index | Item | OldItem | NewItems |
|------|---------------|-------|------|---------|----------|
| `Add(item)` | Add | `_items.Count-1` | item | - | - |
| `Insert(i, item)` | Insert | i | item | - | - |
| `Remove(item)` | Remove | `IndexOf(item)` | - | - | - |
| `RemoveAt(i)` | RemoveAt | i | - | - | - |
| `Replace(i, item)` | Replace | i | item | `_items[i]`(旧值) | - |
| `Move(from, to)` | Move | to | - | - | - (OldIndex=from) |
| `Clear()` | Clear | -1 | - | - | - |
| `AddRange(items)` | AddRange | -1 | - | - | items.ToList().AsReadOnly() |
| `ReplaceAll(items)` | ReplaceAll | -1 | - | - | items.ToList().AsReadOnly() |

### 3.4 Replace 具体实现

```csharp
/// <summary>
/// 替换指定位置的元素。修改 Item 的唯一方式。
/// </summary>
public void Replace(int index, T newItem)
{
    if (_isDisposed) return;
    if ((uint)index >= (uint)_items.Count)
        throw new ArgumentOutOfRangeException(nameof(index));
    
    var oldItem = _items[index];
    _items[index] = newItem;
    
    NotifyChanged(new ListChangedEventArgs<T>
    {
        Type = ListChangeType.Replace,
        Index = index,
        Item = newItem,
        OldItem = oldItem
    });
}
```

### 3.5 ReplaceAll 具体实现

```csharp
/// <summary>
/// 整体替换。清空后添加新元素，触发一次 ReplaceAll 通知。
/// </summary>
public void ReplaceAll(IEnumerable<T> items)
{
    if (_isDisposed) return;
    _items.Clear();
    _items.AddRange(items);
    
    NotifyChanged(new ListChangedEventArgs<T>
    {
        Type = ListChangeType.ReplaceAll,
        NewItems = _items.ToList().AsReadOnly()  // 快照
    });
}
```

### 3.6 只读访问

```csharp
public T this[int index] => _items[index];  // 只有 get，无 setter

public int Count => _items.Count;

public IReadOnlyList<T> AsReadOnly() => _items.AsReadOnly();

public bool Contains(T item) => _items.Contains(item);

public int IndexOf(T item) => _items.IndexOf(item);

public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
```

### 3.7 Flush 回调触发

```csharp
/// <summary>
/// 由 BatchScheduler.Flush 调用。
/// </summary>
internal void FireCallback()
{
    if (!_isDirty || _isDisposed) return;
    _isDirty = false;
    
    if (_operationCount > 1)
    {
        // 多次操作 → 合并为 ReplaceAll
        OnChanged?.Invoke(new ListChangedEventArgs<T>
        {
            Type = ListChangeType.ReplaceAll,
            NewItems = _items.ToList().AsReadOnly()
        });
    }
    else
    {
        // 单次操作 → 保留原事件语义
        OnChanged?.Invoke(_firstEventArgs);
    }
    
    _operationCount = 0;
    _firstEventArgs = default;
}
```

### 3.8 Dispose

```csharp
public bool IsDisposed => _isDisposed;

public void Dispose()
{
    if (_isDisposed) return;
    _isDisposed = true;
    OnChanged = null;
    _items.Clear();
}
```

---

## 4. BatchScheduler 实现

### 4.1 类定义

```csharp
/// <summary>
/// 帧级批次合并调度器。
/// 实现 ILateUpdate，由 SingletonSystem 在 LateUpdate 阶段自动调用 Flush。
/// </summary>
public sealed class BatchScheduler : Singleton<BatchScheduler>, ILateUpdate
{
    private readonly HashSet<BindablePropertyCore> _dirtyProps = new();
    private readonly HashSet<ObservableListCore> _dirtyLists = new();
    private bool _isFlushing;
}
```

> **BindablePropertyCore / ObservableListCore**：由于 BindableProperty\<T\> 和 ObservableList\<T\> 是泛型类，不能直接放入 `HashSet<T>`。使用非泛型基类或接口解决：

```csharp
/// <summary>
/// BindableProperty 的非泛型接口，供 BatchScheduler 统一管理。
/// </summary>
internal interface IBatchDirtyTarget
{
    void FireCallback();
}
```

BindableProperty\<T\> 和 ObservableList\<T\> 都实现此接口：

```csharp
public sealed class BindableProperty<T> : IDisposable, IBatchDirtyTarget
{
    // ... 现有代码 ...
    
    void IBatchDirtyTarget.FireCallback() => FireCallback();  // 显式实现
}
```

### 4.2 内部字段（修正版）

```csharp
public sealed class BatchScheduler : Singleton<BatchScheduler>, ILateUpdate
{
    private readonly HashSet<IBatchDirtyTarget> _dirty = new();
    private bool _isFlushing;
    
    /// <summary>
    /// 标记目标为脏。由 BindableProperty 赋值或 ObservableList 变更时内部调用。
    /// </summary>
    internal void MarkDirty(IBatchDirtyTarget target)
    {
        _dirty.Add(target);  // HashSet 自动去重
    }
    
    /// <summary>
    /// SingletonSystem 在 LateUpdate 阶段自动调用。
    /// </summary>
    public void OnLateUpdate()
    {
        Flush();
    }
}
```

### 4.3 两轮 Flush 算法

```csharp
internal void Flush()
{
    if (_isFlushing || _dirty.Count == 0) return;
    _isFlushing = true;
    
    try
    {
        // ── 第一轮：处理 Model 层变更 ──
        // 触发 DataContext 的 MapProperty/MapList converter
        var round1 = new List<IBatchDirtyTarget>(_dirty);
        _dirty.Clear();
        
        foreach (var target in round1)
            target.FireCallback();
        
        // ── 第二轮：处理 DataContext 输出属性变更 ──
        // converter 执行时给 target BindableProperty 赋值产生的新脏标记
        if (_dirty.Count > 0)
        {
            var round2 = new List<IBatchDirtyTarget>(_dirty);
            _dirty.Clear();
            
            foreach (var target in round2)
                target.FireCallback();
        }
        
        // 第二轮产生的脏标记延迟到下一帧处理（不做第三轮，防止无限循环）
    }
    finally
    {
        _isFlushing = false;
    }
}
```

### 4.4 时序保证

```
Frame N:
  Update:
    gold.Value = 100;    → _dirty.Add(gold)     // Model 赋值
  
  LateUpdate (BatchScheduler.Flush):
    第一轮:
      gold.FireCallback()
        → DataContext converter 执行
        → goldText.Value = "100"                  // DataContext 输出
        → _dirty.Add(goldText)                    // 新脏标记
    
    第二轮:
      goldText.FireCallback()
        → View Bind 回调
        → txtGold.text = "100"                    // UI 更新
    
    结果: Model → DataContext → View 同帧完成 ✓
```

### 4.5 公共调试属性

```csharp
/// <summary>
/// 当前帧是否有待处理的变更。调试用。
/// </summary>
public bool HasPendingChanges => _dirty.Count > 0;
```

---

## 5. DataContext 实现

### 5.1 基类定义

```csharp
/// <summary>
/// DataContext 抽象基类。
/// 聚合多个 Model 数据，转换为 View 友好格式。
/// 框架自动管理订阅生命周期。
/// </summary>
public abstract class DataContext : IDisposable
{
    // 订阅清理列表
    private readonly List<Action> _unsubscribers = new();
    // 持有的输出属性（Dispose 时一起释放）
    private readonly List<IDisposable> _ownedProperties = new();
    private bool _isDisposed;
    
    public bool IsDisposed => _isDisposed;
}
```

### 5.2 MapProperty 单源实现

```csharp
/// <summary>
/// 单源标量映射。
/// source 变化时自动用 converter 转换后赋给 target。
/// 订阅后立即初始化 target（SetValueSilently）。
/// </summary>
protected void MapProperty<TSource, TTarget>(
    BindableProperty<TSource> source,
    BindableProperty<TTarget> target,
    Func<TSource, TTarget> converter)
{
    // 立即初始化 target（同步，不走 BatchScheduler）
    target.SetValueSilently(converter(source.Value));
    
    // 注册变更订阅
    Action<TSource, TSource> handler = (_, _) =>
    {
        target.Value = converter(source.Value);  // 赋值走 BatchScheduler
    };
    
    source.OnValueChanged += handler;
    
    // 记录清理动作
    _unsubscribers.Add(() => source.OnValueChanged -= handler);
}
```

### 5.3 MapProperty 多源实现（双源示例）

```csharp
/// <summary>
/// 双源标量映射。任一 source 变化时自动重算。
/// </summary>
protected void MapProperty<T1, T2, TTarget>(
    BindableProperty<T1> source1,
    BindableProperty<T2> source2,
    BindableProperty<TTarget> target,
    Func<T1, T2, TTarget> converter)
{
    // 立即初始化
    target.SetValueSilently(converter(source1.Value, source2.Value));
    
    // 订阅 source1
    Action<T1, T1> handler1 = (_, _) =>
    {
        target.Value = converter(source1.Value, source2.Value);
    };
    source1.OnValueChanged += handler1;
    _unsubscribers.Add(() => source1.OnValueChanged -= handler1);
    
    // 订阅 source2
    Action<T2, T2> handler2 = (_, _) =>
    {
        target.Value = converter(source1.Value, source2.Value);
    };
    source2.OnValueChanged += handler2;
    _unsubscribers.Add(() => source2.OnValueChanged -= handler2);
}
```

> **BatchScheduler 合并保证**：如果 source1 和 source2 在同一帧都变了，各自 handler 都会给 target 赋值。但 target 是 BindableProperty，同帧多次赋值只会保留最终值，回调只触发一次。

### 5.4 MapProperty 三源实现

```csharp
protected void MapProperty<T1, T2, T3, TTarget>(
    BindableProperty<T1> source1,
    BindableProperty<T2> source2,
    BindableProperty<T3> source3,
    BindableProperty<TTarget> target,
    Func<T1, T2, T3, TTarget> converter)
{
    target.SetValueSilently(converter(source1.Value, source2.Value, source3.Value));
    
    Action<T1, T1> h1 = (_, _) => target.Value = converter(source1.Value, source2.Value, source3.Value);
    Action<T2, T2> h2 = (_, _) => target.Value = converter(source1.Value, source2.Value, source3.Value);
    Action<T3, T3> h3 = (_, _) => target.Value = converter(source1.Value, source2.Value, source3.Value);
    
    source1.OnValueChanged += h1;
    source2.OnValueChanged += h2;
    source3.OnValueChanged += h3;
    
    _unsubscribers.Add(() => source1.OnValueChanged -= h1);
    _unsubscribers.Add(() => source2.OnValueChanged -= h2);
    _unsubscribers.Add(() => source3.OnValueChanged -= h3);
}
```

### 5.5 MapList 实现

```csharp
/// <summary>
/// 列表映射。source 的变更同步到 target，每条元素通过 converter 转换。
/// </summary>
protected void MapList<TSource, TTarget>(
    ObservableList<TSource> source,
    ObservableList<TTarget> target,
    Func<TSource, TTarget> converter)
    where TSource : struct, IEquatable<TSource>
    where TTarget : struct, IEquatable<TTarget>
{
    // 立即初始化：转换 source 所有元素到 target
    var initial = source.Select(converter).ToList();
    target.ReplaceAll(initial);  // 注意：这会触发事件，但此时可能还没人订阅
    
    Action<ListChangedEventArgs<TSource>> handler = args =>
    {
        switch (args.Type)
        {
            case ListChangeType.Add:
                target.Add(converter(args.Item));
                break;
            case ListChangeType.Insert:
                target.Insert(args.Index, converter(args.Item));
                break;
            case ListChangeType.RemoveAt:
                target.RemoveAt(args.Index);
                break;
            case ListChangeType.Replace:
                target.Replace(args.Index, converter(args.Item));
                break;
            case ListChangeType.Move:
                target.Move(args.OldIndex, args.Index);
                break;
            case ListChangeType.Clear:
                target.Clear();
                break;
            case ListChangeType.ReplaceAll:
            case ListChangeType.AddRange:
            default:
                // 批量操作 → 全量重算
                target.ReplaceAll(source.Select(converter));
                break;
        }
    };
    
    source.OnChanged += handler;
    _unsubscribers.Add(() => source.OnChanged -= handler);
}
```

### 5.6 Dispose

```csharp
public void Dispose()
{
    if (_isDisposed) return;
    _isDisposed = true;
    
    // 取消所有源订阅
    foreach (var unsub in _unsubscribers)
        unsub();
    _unsubscribers.Clear();
    
    // 释放持有的输出属性
    foreach (var prop in _ownedProperties)
        prop.Dispose();
    _ownedProperties.Clear();
}
```

### 5.7 泛型 DataContext\<TView\>

```csharp
/// <summary>
/// 泛型 DataContext 基类，关联到具体 View 类型。
/// </summary>
/// <typeparam name="TView">关联的 UIWindow 或 UIWidget 类型。</typeparam>
public abstract class DataContext<TView> : DataContext where TView : UIBase
{
}
```

### 5.8 DataContext 无参构造函数的依赖获取模式

> **重要**：DataContext 必须使用**无参构造函数**。
> Singleton 依赖通过直接访问 `XxxModel.Instance` 获取。
> 这样做是为了避免 `MakeGenericType` 在 HybridCLR 中的兼容风险。
> 详见 [第 6 节](#6-datacontextfactory-实现)。

```csharp
// DataContext 构造函数标准写法:
public BagDataContext()  // 无参
{
    // 1. 直接获取 Singleton（零反射，HybridCLR 安全）
    var player = PlayerModel.Instance;
    var bag = BagModel.Instance;

    // 2. 声明映射
    MapProperty(player.Gold, GoldText, gold => FormatGold(gold));
}
```

---

## 6. DataContextFactory 实现

### 6.1 设计决策

> **为什么使用无参构造函数？**
>
> 原方案通过 `typeof(Singleton<>).MakeGenericType(hotfixType)` 反射解析构造函数参数。
> 这在 HybridCLR 环境下存在兼容风险：
> - `MakeGenericType` 混合 AOT 泛型定义与热更类型参数，解释器可能无法正确实例化
> - `PropertyInfo.GetValue` 在解释模式下性能不佳
> - 修改热更包后缓存失效需重新反射
>
> **无参方案优势**：
> - 完全消除 `MakeGenericType`，零 HybridCLR 风险
> - 与 `UIModule.CreateInstance` 完全一致的模式（`Activator.CreateInstance` + `Attribute.GetCustomAttribute`）
> - 工厂代码从 ~70 行降到 ~25 行
> - DataContext 依赖关系在构造函数体内显式声明，更易调试
> - 符合项目现有惯例（到处都是 `XxxModel.Instance` 直接访问）

### 6.2 完整实现

```csharp
/// <summary>
/// DataContext 工厂。根据 [DataContext] 特性创建 DataContext 实例。
/// 使用无参构造函数 + 缓存策略。
/// 与 UIModule.CreateInstance 模式一致，HybridCLR 完全兼容。
/// </summary>
internal static class DataContextFactory
{
    /// <summary>
    /// 缓存: DataContext Type → 创建委托。
    /// 首次反射获取 DataContextAttribute，后续走缓存。
    /// </summary>
    private static readonly Dictionary<Type, Func<DataContext>> _factories = new();

    /// <summary>
    /// 为指定 View 类型创建 DataContext。
    /// 无 [DataContext] 特性时返回 null。
    /// </summary>
    public static DataContext CreateFor(Type viewType)
    {
        var attr = viewType.GetCustomAttribute<DataContextAttribute>();
        if (attr == null) return null;

        if (!_factories.TryGetValue(attr.DataContextType, out var factory))
        {
            var contextType = attr.DataContextType;
            factory = () => (DataContext)Activator.CreateInstance(contextType);
            _factories[attr.DataContextType] = factory;
        }

        return factory();
    }
}
```

### 6.3 DataContext 编写约定

```csharp
// ✅ 正确：无参构造函数，内部直接访问 Singleton
public class BagDataContext : DataContext<BagUI>
{
    public readonly BindableProperty<string> GoldText = new("");
    public readonly BindableProperty<string> CapacityText = new("");

    public BagDataContext()
    {
        // 直接访问 Singleton，零反射，HybridCLR 安全
        var player = PlayerModel.Instance;
        var bag = BagModel.Instance;
        var vip = VipModel.Instance;

        MapProperty(player.Gold, GoldText, gold => FormatGold(gold));
        MapProperty(
            player.Gold,
            bag.MaxCapacity,
            vip.BagCapacityBonus,
            CapacityText,
            (gold, maxCap, bonus) => $"{bag.Items.Count}/{maxCap + bonus}");
    }
}

// ❌ 错误：不要使用有参构造函数
public class BagDataContext : DataContext<BagUI>
{
    public BagDataContext(PlayerModel player, BagModel bag) // ← 会触发 MakeGenericType
    { ... }
}
```

### 6.4 异常场景

| 场景 | 行为 |
|------|------|
| 无 [DataContext] 特性 | 返回 null，不创建 DataContext |
| DataContext 类型不是 DataContext 子类 | `DataContextAttribute` 构造函数校验并抛出 |
| DataContext 没有无参构造函数 | `Activator.CreateInstance` 抛出 `MissingMethodException` |
| DataContext 内部 Singleton 未初始化 | `Singleton<T>.Instance` 懒初始化时自动创建 |

---

## 7. UIBase 绑定扩展实现

### 7.1 Binding 记录结构

```csharp
/// <summary>
/// 绑定记录。存储 Bind 操作的清理动作。
/// </summary>
internal sealed class Binding
{
    private readonly Action _unsubscribe;
    
    public Binding(Action unsubscribe)
    {
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
    }
    
    public void Unsubscribe() => _unsubscribe();
}
```

### 7.2 UIBase 新增成员

以下成员添加到 `UIBase` 类中（不修改现有方法签名）：

```csharp
// ──── 新增字段 ────
private List<Binding> _bindings;
internal DataContext _dataContext;

// ──── DataContext 访问 ────

/// <summary>
/// 数据上下文。UIWindow 直接持有，UIWidget 向上查找。
/// 无 DataContext 时返回 null。
/// </summary>
public virtual DataContext DataContext => _dataContext;

/// <summary>
/// 获取强类型的 DataContext。
/// UIWindow 直接返回，UIWidget 向上遍历 Parent 链。
/// </summary>
public T GetDataContext<T>() where T : DataContext
{
    return _dataContext as T;
}

// ──── 标量绑定 ────

/// <summary>
/// 绑定到 BindableProperty，值变化时自动回调。
/// 首次绑定时用当前值立即回调一次（首次同步）。
/// OnDestroy 时自动解绑。
/// </summary>
public void Bind<T>(BindableProperty<T> property, Action<T> onChanged)
{
    if (property == null) throw new ArgumentNullException(nameof(property));
    if (onChanged == null) throw new ArgumentNullException(nameof(onChanged));
    
    if (_bindings == null) _bindings = new List<Binding>();
    
    Action<T, T> wrapper = (oldVal, newVal) => onChanged(newVal);
    property.OnValueChanged += wrapper;
    _bindings.Add(new Binding(() => property.OnValueChanged -= wrapper));
    
    // 首次同步：用当前值立即回调
    onChanged(property.Value);
}

/// <summary>
/// 绑定到 BindableProperty（带旧值）。
/// </summary>
public void Bind<T>(BindableProperty<T> property, Action<T, T> onChanged)
{
    if (property == null) throw new ArgumentNullException(nameof(property));
    if (onChanged == null) throw new ArgumentNullException(nameof(onChanged));
    
    if (_bindings == null) _bindings = new List<Binding>();
    
    property.OnValueChanged += onChanged;
    _bindings.Add(new Binding(() => property.OnValueChanged -= onChanged));
    
    // 首次同步
    onChanged(property.Value, property.Value);
}

// ──── 生命周期 ────

/// <summary>
/// 声明数据绑定。子类 override 在这里调用 Bind()。
/// 在 InternalCreate 流程中自动调用。
/// </summary>
protected virtual void SetupBindings() { }

/// <summary>
/// 移除所有绑定。在销毁时自动调用。
/// </summary>
internal void RemoveAllBindings()
{
    if (_bindings == null) return;
    foreach (var binding in _bindings)
        binding.Unsubscribe();
    _bindings.Clear();
}
```

---

## 8. 集成修改点

### 8.1 修改文件总览

| 文件 | 行号 | 修改类型 | 描述 |
|------|------|---------|------|
| UIBase.cs | 末尾 | 新增成员 | DataContext、Bind、SetupBindings、RemoveAllBindings |
| UIWindow.cs:348 | InternalCreate | 插入调用 | 在 BindMemberProperty 后调用 SetupBindings() |
| UIWindow.cs:432 | InternalDestroy | 插入调用 | RemoveAllBindings + DataContext.Dispose |
| UIModule.cs:483 | OnWindowPrepare | 插入逻辑 | DataContext 创建和注入 |
| UIWidget.cs:282 | OnDestroyWidget | 插入调用 | 开头加 RemoveAllBindings() |

### 8.2 UIWindow.InternalCreate 修改

**文件**: `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`  
**行号**: 348-359

```csharp
// ── 修改前 ──
internal void InternalCreate()
{
    if (_isCreate == false)
    {
        _isCreate = true;
        Inject();
        ScriptGenerator();
        BindMemberProperty();
        RegisterEvent();
        OnCreate();
    }
}

// ── 修改后 ──
internal void InternalCreate()
{
    if (_isCreate == false)
    {
        _isCreate = true;
        Inject();
        ScriptGenerator();
        BindMemberProperty();
        SetupBindings();      // ← 新增：数据绑定（在 RegisterEvent 之前）
        RegisterEvent();
        OnCreate();
    }
}
```

**调用顺序说明**：
```
Inject()           ← 依赖注入（现有）
ScriptGenerator()  ← 脚本工具生成的控件查找（现有）
BindMemberProperty() ← 成员属性绑定（现有）
SetupBindings()    ← 数据绑定声明（新增）
RegisterEvent()    ← UI 事件注册（现有，可能引用 Bind 赋值后的控件状态）
OnCreate()         ← 用户初始化（现有）
```

### 8.3 UIWindow.InternalDestroy 修改

**文件**: `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`  
**行号**: 432

```csharp
// ── 修改后 ──
internal void InternalDestroy(bool isShutDown = false)
{
    _isCreate = false;

    RemoveAllUIEvent();       // 现有：清理 UI 事件
    
    // ── 新增开始 ──
    RemoveAllBindings();      // 新增：清理数据绑定
    
    var snapshot = ListChild.ToArray();
    ListChild.Clear();
    for (int i = 0; i < snapshot.Length; i++)
    {
        snapshot[i].CallDestroy();
        snapshot[i].OnDestroyWidget();  // Widget.OnDestroyWidget 内部也会 RemoveAllBindings
    }
    
    // 释放 DataContext（取消 Model 层订阅）
    if (_dataContext != null)
    {
        _dataContext.Dispose();
        _dataContext = null;
    }
    // ── 新增结束 ──

    _prepareCallback = null;
    OnDestroy();

    if (_panel != null)
    {
        Object.Destroy(_panel);
        _panel = null;
    }
    
    IsDestroyed = true;
}
```

**销毁顺序说明**：
```
RemoveAllUIEvent()    → 先清理 UI 事件（避免事件回调中引用已销毁的绑定）
RemoveAllBindings()   → 清理数据绑定（取消 View 层订阅）
销毁子 Widget         → 每个 Widget 也 RemoveAllBindings
DataContext.Dispose() → 最后清理 DataContext（取消 Model 层订阅）
OnDestroy()           → 用户销毁回调
Destroy(_panel)       → 销毁 GameObject
```

### 8.4 UIModule.OnWindowPrepare 修改

**文件**: `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`  
**行号**: 483-496

```csharp
// ── 修改后 ──
private void OnWindowPrepare(UIWindow window)
{
    if (window.LoadFailed)
    {
        Log.Warning("UIModule: Window '{0}' load failed, removing.", window.WindowName);
        Pop(window);
        window.InternalDestroy(isShutDown: false);
        return;
    }

    // ── 新增：创建并注入 DataContext ──
    var ctx = DataContextFactory.CreateFor(window.GetType());
    if (ctx != null)
    {
        window._dataContext = ctx;  // internal 字段，同程序集可访问
    }
    // ── 新增结束 ──

    window.InternalCreate();    // SetupBindings() 在这里执行
    window.InternalRefresh();
    OnSortWindowDepth(window.WindowLayer);
    OnSetWindowVisible();
}
```

**时序保证**：
```
OnWindowPrepare:
  1. DataContext 创建 + 注入  ← DataContext 订阅 Model
  2. InternalCreate
     └─ SetupBindings()       ← View 订阅 DataContext 输出属性（Bind 首次同步生效）
  3. InternalRefresh          ← OnRefresh() 命令式逻辑
```

### 8.5 UIWidget.OnDestroyWidget 修改

**文件**: `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs`  
**行号**: 282

```csharp
// ── 修改后 ──
protected internal void OnDestroyWidget()
{
    RemoveAllBindings();    // ← 新增：在销毁前清理数据绑定
    
    Parent?.SetUpdateDirty();
    RemoveAllUIEvent();

    foreach (var uiChild in ListChild)
    {
        uiChild.CallDestroy();
        uiChild.OnDestroyWidget();
    }

    if (gameObject != null)
    {
        Object.Destroy(gameObject);
    }
}
```

### 8.6 UIWidget 的 DataContext 获取

UIWidget 不需要 override `DataContext` 属性。因为 UIBase 中的 `GetDataContext<T>()` 返回 `_dataContext as T`，Widget 的 `_dataContext` 始终为 null（只有 UIWindow 被注入）。

Widget 需要通过 OwnerWindow 获取：

```csharp
// UIWidget 使用示例（无需框架修改）:
protected override void OnCreate()
{
    var ctx = OwnerWindow?.GetDataContext<BagDataContext>();
    Bind(ctx.GoldText, gold => txtGold.text = gold);
}
```

> **注意**：如果希望 Widget 也支持 `DataContext` 属性自动向上查找，可以在 UIBase 中 override：

```csharp
// 可选：UIWidget 中添加
public override DataContext DataContext => OwnerWindow?.DataContext;
```

但这不是必须的，Phase 1 通过 `OwnerWindow.GetDataContext<T>()` 已足够。

---

## 9. Binding 辅助结构

### 9.1 IBatchDirtyTarget

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// BatchScheduler 脏标记目标的非泛型接口。
    /// BindableProperty{T} 和 ObservableList{T} 都实现此接口。
    /// </summary>
    internal interface IBatchDirtyTarget
    {
        void FireCallback();
    }
}
```

### 9.2 ListChangeType 枚举

```csharp
namespace GameLogic.DataBinding
{
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

### 9.3 ListChangedEventArgs\<T\>

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 集合变更事件参数。
    /// </summary>
    public readonly struct ListChangedEventArgs<T>
    {
        public ListChangeType Type { get; init; }
        public int Index { get; init; }
        public int OldIndex { get; init; }
        public T Item { get; init; }
        public T OldItem { get; init; }
        public IReadOnlyList<T> NewItems { get; init; }
    }
}
```

### 9.4 DataContextAttribute

```csharp
namespace GameLogic.DataBinding
{
    /// <summary>
    /// 标记 UIWindow 使用的 DataContext 类型。
    /// </summary>
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
}
```

---

## 10. 文件清单与代码量估算

### 10.1 新增文件

```
Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/
├── IBatchDirtyTarget.cs           (~10 行)   脏标记接口
├── BindableProperty.cs            (~80 行)   响应式属性
├── ObservableList.cs              (~220 行)  响应式列表
├── ListChangeType.cs              (~15 行)   变更类型枚举
├── ListChangedEventArgs.cs        (~25 行)   事件参数
├── BatchScheduler.cs              (~70 行)   帧级批次合并
├── DataContext.cs                  (~180 行)  DataContext 基类
├── DataContextAttribute.cs        (~25 行)   特性声明
├── DataContextFactory.cs          (~25 行)   工厂（无参构造+缓存）
├── Binding.cs                     (~20 行)   绑定记录
└── DataBinding.meta                           Unity 文件夹元数据

新增代码量: ~670 行
```

### 10.2 修改文件

```
Assets/GameScripts/HotFix/GameLogic/Module/UIModule/
├── UIBase.cs       +~60 行  (DataContext + Bind + SetupBindings)
├── UIWindow.cs     +~15 行  (InternalCreate/Destroy 修改)
├── UIModule.cs     +~6 行   (OnWindowPrepare 创建 DataContext)
└── UIWidget.cs     +~2 行   (OnDestroyWidget 加 RemoveAllBindings)

修改代码量: +~83 行
```

### 10.3 测试文件

```
Assets/TEngine/Tests/EditMode/DataBinding/
├── BindablePropertyTests.cs       (~150 行)
├── ObservableListTests.cs         (~200 行)
├── BatchSchedulerTests.cs         (~120 行)
├── DataContextTests.cs            (~180 行)
├── DataContextFactoryTests.cs     (~60 行)
├── UIBaseBindingTests.cs          (~100 行)
└── TEngine.Tests.DataBinding.asmdef

测试代码量: ~810 行
```

**总计**: ~1563 行（基础设施 670 + 集成修改 83 + 测试 810）

---

## 11. 测试策略

### 11.1 测试基础设施

```csharp
// 测试基类：重置 BatchScheduler 单例
public abstract class DataBindingTestBase
{
    [SetUp]
    public void SetUp()
    {
        // 重置 BatchScheduler 单例（通过反射）
        ResetSingleton<BatchScheduler>();
    }
    
    [TearDown]
    public void TearDown()
    {
        ResetSingleton<BatchScheduler>();
    }
    
    protected static void ResetSingleton<T>() where T : Singleton<T>, new()
    {
        typeof(T).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
    }
    
    /// <summary>
    /// 手动触发 BatchScheduler.Flush（不依赖 Unity LateUpdate）。
    /// </summary>
    protected static void FlushScheduler()
    {
        BatchScheduler.Instance.OnLateUpdate();
    }
}
```

### 11.2 BindablePropertyTests

| # | 测试用例 | 验证点 |
|---|---------|--------|
| 1 | `Default_Value_IsDefault` | 构造后 Value == default(T) |
| 2 | `SetValue_UpdatesImmediately` | 赋值后 Value 立即更新 |
| 3 | `SetSameValue_DoesNotMarkDirty` | 赋相同值不标记脏 |
| 4 | `SetSameValue_CustomComparer` | 自定义比较器生效 |
| 5 | `NoCallback_WithoutFlush` | 赋值后 Flush 前无回调 |
| 6 | `Flush_TriggersCallback` | Flush 后触发 OnValueChanged |
| 7 | `Callback_HasOldAndNewValue` | 回调参数正确 |
| 8 | `MultipleSetSameFrame_MergesCallback` | 同帧多次赋值只触发一次 |
| 9 | `MultipleSetSameFrame_OldestOldValue` | 同帧合并保留最早旧值 |
| 10 | `NoSubscriber_NoDirtyMark` | 无订阅者赋值不标记脏 |
| 11 | `RecordStruct_ValueComparison` | record struct 按值比较 |
| 12 | `Dispose_PreventsCallback` | Dispose 后赋值不触发 |
| 13 | `ForceNotify_TriggersCallback` | ForceNotify 强制触发 |
| 14 | `SetValueSilently_NoNotification` | SetValueSilently 不通知 |
| 15 | `CustomComparer_Works` | 自定义比较器控制触发 |

### 11.3 ObservableListTests

| # | 测试用例 | 验证点 |
|---|---------|--------|
| 1 | `Add_IncreasesCount` | Add 后 Count 增加 |
| 2 | `Add_FiresEvent_AfterFlush` | Add 触发正确事件 |
| 3 | `Insert_AtCorrectIndex` | Insert 在正确位置 |
| 4 | `RemoveAt_DecreasesCount` | RemoveAt 后 Count 减少 |
| 5 | `Replace_UpdatesValue` | Replace 替换指定位置 |
| 6 | `Replace_OldItem_Captured` | Replace 事件包含 OldItem |
| 7 | `Move_ChangesPositions` | Move 正确移动元素 |
| 8 | `Clear_RemovesAll` | Clear 清空列表 |
| 9 | `AddRange_FiresAddRange` | AddRange 触发批量事件 |
| 10 | `ReplaceAll_FiresReplaceAll` | ReplaceAll 触发整体替换 |
| 11 | `Indexer_IsReadOnly` | `this[int]` 只有 get |
| 12 | `AsReadOnly_ReturnsReadOnly` | AsReadOnly 返回只读视图 |
| 13 | `Contains_And_IndexOf` | 查找方法正确 |
| 14 | `SingleOp_PreservesEventType` | 单次操作保留原始事件类型 |
| 15 | `MultipleOps_MergesToReplaceAll` | 多次操作合并为 ReplaceAll |
| 16 | `Dispose_PreventsEvents` | Dispose 后操作不触发 |

### 11.4 BatchSchedulerTests

| # | 测试用例 | 验证点 |
|---|---------|--------|
| 1 | `Flush_TriggersAllDirty` | Flush 触发所有脏属性回调 |
| 2 | `SameProperty_Merges` | 同属性同帧合并 |
| 3 | `TwoRoundFlush_DataContext` | 两轮 Flush: Model→DC→View 同帧 |
| 4 | `FlushDuringFlush_QueuesForNextFrame` | Flush 期间新脏标记延迟 |
| 5 | `EmptyFlush_NoOp` | 无脏标记时 Flush 空操作 |
| 6 | `HasPendingChanges_Correct` | 调试属性正确 |

### 11.5 DataContextTests

| # | 测试用例 | 验证点 |
|---|---------|--------|
| 1 | `MapProperty_SingleSource` | 单源转换正确 |
| 2 | `MapProperty_DualSource` | 双源任一变化都重算 |
| 3 | `MapProperty_TripleSource` | 三源组合正确 |
| 4 | `MapProperty_InitializesTarget` | 订阅后 target 有初始值 |
| 5 | `MapList_SyncsAdd` | 列表 Add 同步 |
| 6 | `MapList_SyncsReplace` | 列表 Replace 同步 |
| 7 | `MapList_SyncsRemove` | 列表 Remove 同步 |
| 8 | `MapList_InitialConversion` | MapList 后 target 有初始数据 |
| 9 | `Dispose_RemovesSubscriptions` | Dispose 后源变化不触发 |
| 10 | `Dispose_ReleasesProperties` | Dispose 释放输出属性 |

### 11.6 DataContextFactoryTests

| # | 测试用例 | 验证点 |
|---|---------|--------|
| 1 | `CreateFor_WithAttribute_Succeeds` | 有特性窗口创建成功（无参构造） |
| 2 | `CreateFor_NoAttribute_ReturnsNull` | 无特性返回 null |
| 3 | `CreateFor_CachesFactory` | 第二次不重新反射 |
| 4 | `CreateFor_InvalidType_Throws` | DataContextType 不是 DataContext 子类时抛异常 |
| 5 | `CreateFor_NoParameterlessCtor_Throws` | 无无参构造函数时 Activator 抛 MissingMethodException |

### 11.7 UIBaseBindingTests

| # | 测试用例 | 验证点 |
|---|---------|--------|
| 1 | `Bind_SingleProperty` | 订阅属性变化 |
| 2 | `Bind_ImmediateCallback` | 首次同步立即回调 |
| 3 | `Bind_WithOldValue` | 带旧值回调正确 |
| 4 | `RemoveAllBindings_NoFurtherCallback` | 清理后不再回调 |
| 5 | `MultipleBinds_AllTrigger` | 多个绑定都触发 |
| 6 | `Bind_NullProperty_Throws` | 空属性抛 ArgumentNullException |

---

## 12. 实现顺序建议

按依赖关系从底向上实现：

```
Phase 1-A: 基础组件（无外部依赖，可独立测试）
  ① IBatchDirtyTarget.cs
  ② BindableProperty.cs + BindablePropertyTests.cs
  ③ ListChangeType.cs + ListChangedEventArgs.cs
  ④ ObservableList.cs + ObservableListTests.cs
  ⑤ BatchScheduler.cs + BatchSchedulerTests.cs

Phase 1-B: 数据层（依赖 Phase 1-A）
  ⑥ DataContext.cs + DataContextTests.cs
  ⑦ DataContextAttribute.cs
  ⑧ DataContextFactory.cs + DataContextFactoryTests.cs

Phase 1-C: UI 集成（依赖 Phase 1-B）
  ⑨ Binding.cs
  ⑩ UIBase.cs 扩展 + UIBaseBindingTests.cs
  ⑪ UIWindow.cs 修改
  ⑫ UIWidget.cs 修改
  ⑬ UIModule.cs 修改
  ⑭ 集成测试

Phase 1-D: 收尾
  ⑮ 全量回归测试
  ⑯ 示例代码（BagUI/BagDataContext/BagModel）
```

---

> **文档结束**  
> 本文档定义了 UI 数据绑定系统 Phase 1 的完整实现规格。  
> 所有组件的内部算法、集成修改点、测试用例均已明确，可直接编码实施。
