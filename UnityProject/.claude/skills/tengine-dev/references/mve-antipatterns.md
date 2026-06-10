# MVE 数据绑定反模式与避坑指南

> **适用场景**：BindableProperty 内存泄漏排查、DataContext 映射异常调试、BatchScheduler 时序问题定位、绑定无响应诊断 | **关联文档**：[mve-patterns.md](mve-patterns.md)（核心 API）、[ui-lifecycle.md](ui-lifecycle.md)（UIWindow/UIWidget 生命周期）、[event-antipatterns.md](event-antipatterns.md)（GameEvent 反模式）

> 本文档是 [mve-patterns.md](mve-patterns.md) 的进阶补充，聚焦于 MVE 开发中难以排查的陷阱和反模式。

---

## 一、BindableProperty 误用

### 反模式 1：在 Service 外直接写 Model 属性

```csharp
// ❌ 错误：View 层直接修改 Model 数据，绕过 Service 层
public class BagUI : UIWindow
{
    private void OnItemSold()
    {
        Singleton<BagModel>.Instance.Gold.Value += 100;  // View 直接写 Model
    }
}

// ✅ 正确：View 只调 Service，由 Service 操作 Model
public class BagUI : UIWindow
{
    private void OnItemSold()
    {
        Singleton<BagService>.Instance.SellItem(itemId);  // Service 内部修改 Model
    }
}
```

**为何危险**：MVE 的核心约束是 View → Service → Model 单向依赖。View 直接写 Model 会导致：业务逻辑散落在 View 中难以测试、数据流追踪断裂、响应式更新链可能被意外触发。

---

### 反模式 2：忘记 Dispose 导致泄漏

```csharp
// ❌ 错误：Model 的 BindableProperty 未在 OnRelease 中 Dispose
public class PlayerModel : Singleton<PlayerModel>
{
    public readonly BindableProperty<int> Level = new(1);

    // 缺少 OnRelease，Level 永不释放
}

// ✅ 正确：所有 BindableProperty 在 OnRelease 中 Dispose
public class PlayerModel : Singleton<PlayerModel>
{
    public readonly BindableProperty<int> Level = new(1);

    protected override void OnRelease()
    {
        Level.Dispose();
        base.OnRelease();
    }
}
```

**为何危险**：`BindableProperty` 持有 `OnValueChanged` 委托引用。如果外部订阅者未取消订阅且 Property 未 Dispose，会形成 GC 引用链，阻止订阅者被回收。Singleton 的 Model 在整个生命周期内存在，风险尤为突出。

---

### 反模式 3：在回调中修改自身属性造成无限循环

```csharp
// ❌ 错误：在 OnValueChanged 回调中修改触发源属性
public class ShopUI : UIWindow
{
    protected override void SetupBindings()
    {
        var model = Singleton<ShopModel>.Instance;
        Bind(model.Price, (oldPrice, newPrice) =>
        {
            if (newPrice < 0)
                model.Price.Value = 0;  // 回调中写源属性 → 再次触发回调 → 无限循环
        });
    }
}

// ✅ 正确：在 Service 层做数据约束，回调只做 UI 刷新
public class ShopService : Singleton<ShopService>
{
    public void SetPrice(int price)
    {
        Singleton<ShopModel>.Instance.Price.Value = Math.Max(0, price);
    }
}
```

**实际行为**：虽然 `BindableProperty` 有同值判断（`_comparer.Equals`），如果回调赋新值会继续触发。BatchScheduler 的两轮 Flush 不会无限循环（只做两轮），但可能导致状态不一致。

---

### 反模式 4：SetValueSilently 后期望回调触发

```csharp
// ❌ 错误：SetValueSilently 不会触发回调
model.Gold.SetValueSilently(500);
// 下面的 Bind 回调不会触发！

// ✅ 正确：使用 Value 赋值触发响应式更新
model.Gold.Value = 500;

// ✅ 如果确实需要静默赋值后通知，用 ForceNotify
model.Gold.SetValueSilently(500);
model.Gold.ForceNotify();  // 强制触发一次回调
```

---

## 二、DataContext 常见陷阱

### 反模式 5：在 DataContext 中执行副作用

```csharp
// ❌ 错误：DataContext 的 MapProperty 转换器中执行副作用
public class GameDataContext : DataContext<GameUI>
{
    public GameDataContext()
    {
        var model = Singleton<GameModel>.Instance;
        MapProperty(model.Level, DisplayLevel, level =>
        {
            AudioManager.PlaySound("levelup");  // ❌ 副作用！
            return $"Lv.{level}";
        });
    }
}

// ✅ 正确：DataContext 只做纯映射，副作用放在 View 的 Bind 回调中
public class GameDataContext : DataContext<GameUI>
{
    public GameDataContext()
    {
        MapProperty(model.Level, DisplayLevel, level => $"Lv.{level}");
    }
}

public class GameUI : UIWindow
{
    protected override void SetupBindings()
    {
        var dc = GetDataContext<GameDataContext>();
        Bind(model.Level, (oldLevel, newLevel) =>
        {
            if (newLevel > oldLevel) AudioManager.PlaySound("levelup");
        });
    }
}
```

**为何危险**：DataContext 的转换器可能在 BatchScheduler Flush 期间被调用，此时执行副作用（音效播放、网络请求、场景跳转）可能导致不可预期的时序问题。转换器应该是纯函数。

---

### 反模式 6：MapProperty 转换器抛异常

```csharp
// ❌ 错误：转换器可能抛 NullReferenceException
MapProperty(model.UserData, DisplayName, data => data.Name.ToUpper());
// 如果 UserData 为 null → 异常在 BatchScheduler.Flush 内抛出 → 影响其他绑定

// ✅ 正确：转换器中做空值保护
MapProperty(model.UserData, DisplayName, data => data?.Name.ToUpper() ?? "未知");
```

**为何危险**：`MapProperty` 的 handler 在 `BatchScheduler.Flush()` 内执行。如果转换器抛异常，会影响同批次所有后续回调的执行（try/finally 只保证 `_isFlushing` 标记恢复）。

---

### 反模式 7：忘记标记 DataContextAttribute

```csharp
// ❌ 错误：DataContext 类没有标记 [DataContext] 特性
public class BagDataContext : DataContext<BagUI>  // 缺少特性
{
    // ...
}

// UIModule.OnWindowPrepare() 中 DataContextFactory.CreateFor(typeof(BagUI)) 返回 null
// SetupBindings() 中 GetDataContext<BagDataContext>() 返回 null → NullReferenceException

// ✅ 正确：在 DataContext 或 View 类上标记特性
[DataContext(typeof(BagUI))]
public class BagDataContext : DataContext<BagUI> { ... }
```

---

### 反模式 8：DataContext 构造函数中访问 Model 的 Singleton 不可用

```csharp
// ❌ 错误：DataContext 构造时机早于 Model Singleton 初始化
[DataContext(typeof(EarlyUI))]
public class EarlyDataContext : DataContext<EarlyUI>
{
    public EarlyDataContext()
    {
        var model = Singleton<CriticalModel>.Instance;  // 可能为 null
        MapProperty(model.SomeProp, DisplayProp, x => x);
    }
}

// ✅ 正确：确保 Model 在 App 吸动流程中优先初始化
// GameApp.Initialize() 中按顺序创建 Model → Service → UI
```

**注意**：`DataContextFactory.CreateFor()` 在 `UIModule.OnWindowPrepare()` 中被调用，此时 Singleton 应该已经初始化。但如果是编辑器测试或特殊启动顺序，需要确认 Model 就绪。

---

## 三、BatchScheduler 时序问题

### 反模式 9：依赖同帧回调时序

```csharp
// ❌ 错误：假设属性赋值后立即能读到 View 状态
model.Gold.Value = 500;
Assert.AreEqual("500", textGold.text);  // ❌ 回调还未触发！text 仍是旧值

// ✅ 正确：如果需要同步验证，手动 Flush
model.Gold.Value = 500;
BatchScheduler.Instance.Flush();  // 强制触发帧回调
Assert.AreEqual("500", textGold.text);  // ✅ 现在是对的
```

**EditMode 测试**：EditMode 下 BatchScheduler 单例不存在，`SafeMarkDirty` 会直接同步触发。所以测试中的行为可能与运行时不同。测试需要显式调用 `Flush()` 或依赖即时模式。

---

### 反模式 10：在 EditMode 测试中未调用 Flush

```csharp
// ❌ 错误：EditMode 测试中期望回调自动触发
[Test]
public void TestBinding()
{
    var prop = new BindableProperty<int>(0);
    int received = 0;
    prop.OnValueChanged += (old, val) => received = val;
    prop.Value = 42;
    // EditMode 下 SafeMarkDirty 走即时模式，实际上会立即触发
    // 但如果通过 DataContext.MapProperty 绑定，链式更新不会自动发生
}

// ✅ 正确：EditMode 测试中通过 DataContext 映射的链式更新需要手动 Flush
// 参考 DataBindingTestBase 中的 FlushScheduler() 方法
```

---

## 四、绑定生命周期

### 反模式 11：UIWidget 手动创建 DataContext

```csharp
// ❌ 错误：UIWidget 中手动 new DataContext
public class MyWidget : UIWidget
{
    protected override void OnCreate()
    {
        _dataContext = new MyDataContext();  // ❌ 绕过工厂，生命周期管理混乱
    }
}

// ✅ 正确：UIWidget 通过父窗口获取 DataContext
public class MyWidget : UIWidget
{
    protected override void SetupBindings()
    {
        var dc = GetParentDataContext<MyDataContext>();  // 从父窗口获取
        BindText(m_text, dc.DisplayText);
    }
}
```

**原理**：`UIModule` 只为 UIWindow 自动创建 DataContext。UIWidget 的 `_dataContext` 默认为 null，需要通过 `GetParentDataContext` 或直接引用父窗口来获取 DataContext。

---

### 反模式 12：在 SetupBindings 外注册绑定

```csharp
// ❌ 错误：在 OnRefresh 或其他地方注册绑定
public class MyUI : UIWindow
{
    protected override void OnRefresh()
    {
        var dc = GetDataContext<MyDataContext>();
        BindText(m_text, dc.DisplayName);  // ❌ 每次 Refresh 都新增绑定，旧的不清理
    }
}

// ✅ 正确：所有绑定在 SetupBindings 中一次性声明
public class MyUI : UIWindow
{
    protected override void SetupBindings()
    {
        var dc = GetDataContext<MyDataContext>();
        BindText(m_text, dc.DisplayName);
    }
}
```

**为何危险**：`BindText` 内部调用 `Bind`，每次调用都会新增一个订阅。如果 `OnRefresh` 被多次调用，会产生重复绑定。`RemoveAllBindings()` 只在 `OnDestroy` 时调用，期间积累的重复绑定不会被清理。

---

### 反模式 13：绑定时组件引用为 null（静默失败）

```csharp
// ❌ 危险：BindText 对 null 组件静默跳过，不报错
protected override void SetupBindings()
{
    BindText(null, dc.DisplayName);  // 静默跳过，UI 不显示但不会报错
}

// ✅ 正确：绑定前检查组件是否正确获取
protected override void SetupBindings()
{
    if (m_textName == null)
    {
        Log.Error($"[{nameof(MyUI)}] m_textName 未在 ScriptGenerator 中正确绑定");
        return;
    }
    BindText(m_textName, dc.DisplayName);
}
```

**设计意图**：`BindText` 等便捷方法对 null 组件静默返回（`if (text == null || prop == null) return;`），这在某些场景下是便利的（如可选组件），但也可能隐藏节点绑定错误。建议在开发阶段增加 null 检查日志。

---

## 五、ObservableList 约束

### 反模式 14：使用 class 而非 struct 作为元素类型

```csharp
// ❌ 错误：ObservableList 要求 T 为 struct
public class ItemData  // class
{
    public int Id;
    public string Name;
}

var list = new ObservableList<ItemData>();  // 编译错误！where T : struct

// ✅ 正确：使用 struct + IEquatable<T>
public struct ItemData : IEquatable<ItemData>
{
    public int Id;
    public string Name;

    public bool Equals(ItemData other) => Id == other.Id;
}
```

---

### 反模式 15：在 OnChanged 回调中修改列表

```csharp
// ❌ 错误：在变更回调中修改同一列表 → 可能触发二次 Flush
list.OnChanged += args =>
{
    if (args.Type == ListChangeType.Add)
        list.Add(default);  // ❌ 回调中修改 → 可能产生死循环
};

// ✅ 正确：回调中只读取，修改操作放在 Service 层
list.OnChanged += args =>
{
    Log.Info($"列表变更: {args.Type}, 当前数量: {list.Count}");
};
```

---

## 六、诊断清单

遇到 MVE 绑定问题时，按以下顺序排查：

| # | 检查项 | 排查方法 |
|---|--------|---------|
| 1 | DataContext 是否被创建 | `GetDataContext<T>()` 返回值是否为 null |
| 2 | `[DataContext]` 特性是否标记 | 检查 DataContext 类或 View 类上的特性 |
| 3 | Model Singleton 是否就绪 | `Singleton<T>.Instance` 是否为 null |
| 4 | MapProperty 映射是否执行 | 在构造函数中打断点/加日志 |
| 5 | BindText 组件是否为 null | 检查 `ScriptGenerator()` 中节点绑定 |
| 6 | 回调是否被触发 | 在 `Bind` 回调中加日志 |
| 7 | BatchScheduler 是否 Flush | 运行时检查 `HasPendingChanges` |
| 8 | 属性是否被 Dispose | 检查 `IsDisposed` 状态 |
| 9 | 是否有重复绑定 | 在 `Bind` 回调中加计数日志 |
| 10 | 转换器是否抛异常 | 在 MapProperty handler 中加 try-catch |
