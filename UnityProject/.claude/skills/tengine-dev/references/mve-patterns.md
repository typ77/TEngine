# MVE 数据绑定开发模式

> **适用场景**：BindableProperty 响应式属性、DataContext 数据映射、UIBase.Bind 绑定、ObservableList 响应式集合、MVE 四层架构 | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（UIWindow/UIWidget 生命周期）、[event-system.md](event-system.md)（GameEvent 全局事件）、[ui-patterns.md](ui-patterns.md)（Widget 创建/列表管理）

---

## 一、MVE 四层架构概览

TEngine 采用 **MVE (Model-View-Event)** 四层分离架构，也称为 **MVES** (Model-View-Event-Service)：

```
┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
│  Model   │    │ Service  │    │DataContext│   │   View   │
│ 纯数据层 │◄───│ 业务逻辑 │    │ 数据映射 │───►│ UI 渲染  │
│BindableP │    │ 读写Model│    │MapProperty│   │BindText  │
└──────────┘    └──────────┘    └──────────┘    └──────────┘
```

| 层 | 命名规范 | 职责 | 禁止 |
|---|---------|------|------|
| **Model** | `XxxModel : Singleton<T>` | 仅持有 `BindableProperty<T>` 响应式数据 | 禁止引用 UI、禁止含业务逻辑 |
| **Service** | `XxxService : Singleton<T>` | 封装业务操作，读写 Model 数据 | 禁止引用 UI、禁止引用 DataContext |
| **DataContext** | `XxxDataContext : DataContext<TView>` | Model→View 的数据转换/映射 | 禁止修改 Model、禁止引用 UI 组件 |
| **View** | `XxxUI : UIWindow` / `XxxWidget : UIWidget` | UI 渲染 + 用户交互 | 禁止直接读写 Model（通过 Service） |

**数据流**：

```
用户点击 → View 调用 Service → Service 修改 Model.BindableProperty.Value
  → BatchScheduler 帧末 Flush → DataContext MapProperty 转换
    → View BindText/Bind 回调 → UI 自动刷新
```

**核心模块位置**：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/DataBinding/`

---

## 二、核心组件 API

### 2.1 BindableProperty\<T\> — 响应式属性

**文件**：`Module/UIModule/DataBinding/BindableProperty.cs`
**命名空间**：`GameLogic.DataBinding`

```csharp
public sealed class BindableProperty<T> : IDisposable, IBatchDirtyTarget
```

**构造函数**：

```csharp
new BindableProperty<T>(T initialValue = default, IEqualityComparer<T> comparer = null)
```

**属性与方法**：

| 成员 | 类型 | 说明 |
|------|------|------|
| `Value` | `T` (get/set) | 赋值立即更新，回调延迟到帧末。同值赋值不触发。 |
| `OnValueChanged` | `event Action<T, T>` | 值变化事件，参数为 (旧值, 新值) |
| `IsDisposed` | `bool` | 是否已释放 |
| `ForceNotify()` | `void` | 强制触发回调，即使值未变化 |
| `SetValueSilently(T)` | `void` | 静默设值，不触发脏标记和回调 |
| `Dispose()` | `void` | 释放资源，清除所有订阅 |

**帧级合并语义**：
- 赋值 → 立即更新内部值 + 标脏
- 同帧多次赋值 → 合并为一次回调，`_oldValue` 保留第一次赋值前的值
- 回调在 `BatchScheduler.Flush()` 时统一触发（LateUpdate）

---

### 2.2 ObservableList\<T\> — 响应式集合

**文件**：`Module/UIModule/DataBinding/ObservableList.cs`
**约束**：`where T : struct, IEquatable<T>`

```csharp
public sealed class ObservableList<T> : IDisposable, IReadOnlyList<T>, IBatchDirtyTarget
    where T : struct, IEquatable<T>
```

**事件**：

```csharp
public event Action<ListChangedEventArgs<T>> OnChanged;
```

**操作方法**：

| 方法 | 说明 |
|------|------|
| `Add(T)` | 末尾添加 |
| `Insert(int, T)` | 指定位置插入 |
| `Remove(T)` | 移除元素（返回 bool） |
| `RemoveAt(int)` | 移除指定位置 |
| `Replace(int, T)` | 替换指定位置 |
| `Move(int, int)` | 移动元素 |
| `Clear()` | 清空 |
| `AddRange(IEnumerable<T>)` | 批量添加 |
| `ReplaceAll(IEnumerable<T>)` | 替换所有元素 |

**查询方法**：`Count`、`this[int]`、`Contains(T)`、`IndexOf(T)`、`AsReadOnly()`、`GetEnumerator()`

**合并语义**：同帧多次操作合并为一次 `ReplaceAll` 回调（携带全量快照），单次操作保留原始事件类型。

---

### 2.3 ListChangeType / ListChangedEventArgs\<T\>

**文件**：`Module/UIModule/DataBinding/ListChangeType.cs`、`Module/UIModule/DataBinding/ListChangedEventArgs.cs`

```csharp
public enum ListChangeType
{
    Add, Insert, Remove, RemoveAt, Replace, Clear, Move, ReplaceAll, AddRange
}

public struct ListChangedEventArgs<T>
{
    public ListChangeType Type { get; set; }
    public int Index { get; set; }
    public int OldIndex { get; set; }
    public T Item { get; set; }
    public T OldItem { get; set; }
    public IReadOnlyList<T> NewItems { get; set; }
}
```

---

### 2.4 DataContext — 数据上下文基类

**文件**：`Module/UIModule/DataBinding/DataContext.cs`

```csharp
public abstract class DataContext : IDisposable
public abstract class DataContext<TView> : DataContext where TView : UIBase
```

**映射方法**（protected，子类在构造函数中调用）：

| 方法 | 签名 | 说明 |
|------|------|------|
| 单源映射 | `MapProperty<TSource, TTarget>(BindableProperty<TSource> source, BindableProperty<TTarget> target, Func<TSource, TTarget> converter)` | source 变化时自动转换并更新 target |
| 双源映射 | `MapProperty<T1, T2, TTarget>(BindableProperty<T1> s1, BindableProperty<T2> s2, BindableProperty<TTarget> target, Func<T1, T2, TTarget> converter)` | 任一 source 变化时重新计算 |
| 三源映射 | `MapProperty<T1, T2, T3, TTarget>(...)` | 三个源联合映射 |
| 列表映射 | `MapList<TSource, TTarget>(ObservableList<TSource> source, ObservableList<TTarget> target, Func<TSource, TTarget> converter)` | 增量同步列表变更 |

**所有 MapProperty 行为**：
1. 用 source 当前值立即初始化 target（`SetValueSilently`）
2. 订阅 source 的 `OnValueChanged` 事件
3. 注册取消订阅到 `_unsubscribers`
4. 将 target 注册到 `_ownedProperties`（Dispose 时释放）

**Dispose**：自动取消所有订阅并释放持有的属性资源。

> ⚠️ **订阅托管约束**：`Dispose()` 非 virtual，`_unsubscribers`/`_ownedProperties` 为 private。**子类无法手动 `source.OnValueChanged +=` 后自动取消订阅**——必须通过 `MapProperty`/`MapList` 建立映射，框架才会在 Window/Widget OnDestroy 时 `_dataContext.Dispose()` 自动取消订阅并释放 target。手动 `+=` 会内存泄漏（详见第八章 carrier 模式的变通方案）。
>
> **DataContext 不可空壳**：挂 `[DataContext]` 特性的类必须真正承担 Model→View 转换（含 `MapProperty`/`MapList` 或对外输出 `BindableProperty`）。空壳 DataContext（无映射、无输出）会被 `DataContextFactory` 反射创建却无人消费，纯属浪费——若 UIWindow 无需 Model→View 转换（直接绑 Model 或纯交互），**删掉 `[DataContext]` 特性与 DataContext 类**，`DataContextFactory.CreateFor` 返回 null 即「非 MVE 窗口」，安全。

---

### 2.5 BatchScheduler — 帧级批次合并调度器

**文件**：`Module/UIModule/DataBinding/BatchScheduler.cs`

```csharp
public sealed class BatchScheduler : Singleton<BatchScheduler>, ILateUpdate
```

**关键机制**：

| 特性 | 说明 |
|------|------|
| **驱动方式** | `ILateUpdate`，由 `SingletonSystem` 每帧 `LateUpdate` 自动调用 `Flush()` |
| **两轮 Flush** | 第一轮处理已有脏标记；第二轮处理回调中新产生的脏标记（处理 DataContext→View 链式更新） |
| **EditMode 兼容** | `SafeMarkDirty` 检测单例是否存在，不存在时直接同步触发（即时模式回退） |
| **合并** | `BindableProperty` 同帧多次赋值只触发一次回调；`ObservableList` 同帧多次操作合并为 `ReplaceAll` |

**内部 API**（测试可直接调用）：

| 成员 | 说明 |
|------|------|
| `Flush()` | 执行两轮 Flush（internal） |
| `HasPendingChanges` | 是否有待处理的脏标记 |

---

### 2.6 DataContextAttribute — View-DataContext 关联

**文件**：`Module/UIModule/DataBinding/DataContextAttribute.cs`

```csharp
[DataContext(typeof(LoginDataContext))]  // 标记在 DataContext 类上
[DataContext(typeof(LoginDataContext))]  // 也可标记在 View 类上
```

构造时验证类型必须继承 `DataContext`。由 `DataContextFactory` 读取此特性进行反射创建。

---

### 2.7 DataContextFactory — 反射工厂

**文件**：`Module/UIModule/DataBinding/DataContextFactory.cs`

```csharp
public static class DataContextFactory
{
    public static DataContext CreateFor(Type viewType);  // 反射创建 DataContext
    public static void ResetCache();                     // 清空缓存
}
```

内部使用 `Dictionary<Type, Func<DataContext>>` 缓存工厂委托，避免重复反射。

**自动注入**：`UIModule.OnWindowPrepare()` 中自动调用 `DataContextFactory.CreateFor(window.GetType())` 并注入到 `window._dataContext`。

---

### 2.8 辅助类型

| 类型 | 文件 | 说明 |
|------|------|------|
| `IBatchDirtyTarget` | `IBatchDirtyTarget.cs` | 接口：`void FireCallback()`。BindableProperty 和 ObservableList 均实现 |
| `Binding` (internal) | `Binding.cs` | 轻量取消订阅包装，UIBase 持有 `List<Binding>` |

---

## 三、UIBase 绑定 API

**文件**：`Module/UIModule/UIBase.cs`（第 612-741 行）
UIWindow 和 UIWidget 均可使用（都继承 UIBase）。

### 3.1 DataContext 访问

```csharp
public virtual DataContext DataContext { get; }       // 获取 DataContext 实例
public T GetDataContext<T>() where T : DataContext     // 获取强类型 DataContext
```

> **注意**：只有 UIWindow 的 DataContext 会被 UIModule 自动注入。UIWidget 如需使用 DataContext，需要自行获取父窗口的 DataContext 或手动创建。

> **UIWidget + MVE 的适用边界**：MVE 数据绑定（BindableProperty + Bind）的价值是「数据变化 → 自动刷新」。**只有数据会运行期变化的 UIWidget 才需要 MVE**——静态数据（创建时设置一次、不变）+ 动画/协程驱动的 Widget 不需要，强行套 BindableProperty 是过度设计。
> - **需要 MVE 的 UIWidget**：数据变化的子 Widget（背包格子数量、技能冷却进度、动态数值面板）；Cell（`FancyCellWidget`）是典型范例（`BindMemberProperty` 创建响应式属性 + `BindData` 写入触发刷新，见第七章）。
> - **不需要 MVE 的 UIWidget**：静态展示 + 动画/过渡的 Widget（如 `Example06_SlideScreenTransition`：标签创建时 `SetLabel` 一次、滑动由 UniTask 协程驱动，无数据变化，直接赋值即可）。
> - **判断口诀**：问自己「这块 UI 的数据会在运行期变化吗？」——**会 → MVE，不会 → 直接赋值**。

### 3.2 核心绑定方法

```csharp
// 订阅属性变化（仅新值），绑定后立即用当前值触发一次回调
public void Bind<T>(BindableProperty<T> property, Action<T> onChanged)

// 订阅属性变化（旧值+新值），绑定后立即用当前值触发一次回调
public void Bind<T>(BindableProperty<T> property, Action<T, T> onChanged)
```

### 3.3 便捷绑定方法

| 方法 | 目标组件 | 属性类型 | 行为 |
|------|---------|---------|------|
| `BindText(Text, BindableProperty<string>)` | `UnityEngine.UI.Text` | string | 自动刷新 `text.text` |
| `BindText(TextMeshProUGUI, BindableProperty<string>)` | `TMPro.TextMeshProUGUI` | string | 自动刷新 `tmp.text` |
| `BindText(InputField, BindableProperty<string>)` | `UnityEngine.UI.InputField` | string | 自动刷新 `input.text` |
| `BindInteractable(Selectable, BindableProperty<bool>)` | `UnityEngine.UI.Selectable` | bool | 自动切换 `interactable` |
| `BindToggle(Toggle, BindableProperty<bool>)` | `UnityEngine.UI.Toggle` | bool | 自动切换 `isOn` |
| `BindSlider(Slider, BindableProperty<float>)` | `UnityEngine.UI.Slider` | float | 自动刷新 `slider.value` |
| `BindSlider(Slider, BindableProperty<int>)` | `UnityEngine.UI.Slider` | int | 自动刷新 `slider.value`（int→float） |
| `BindSprite(Image, BindableProperty<string>)` | `UnityEngine.UI.Image` | string | 异步加载精灵，自动管理资源释放和 UI 销毁取消 |

### 3.4 生命周期管理

```csharp
protected virtual void SetupBindings() { }    // 子类重写，声明数据绑定
public void RemoveAllBindings()                // 取消所有绑定（UIWindow.OnDestroy 自动调用）
```

**调用时序**：`SetupBindings()` 在 `OnCreate()` 期间被调用，此时 DataContext 已由 UIModule 注入。

---

## 四、完整开发示例（LoginUI 四层）

### Model 层 — LoginModel.cs

```csharp
using GameLogic.DataBinding;

namespace GameLogic
{
    public class LoginModel : Singleton<LoginModel>
    {
        public readonly BindableProperty<string> AccountName = new("Player_001");
        public readonly BindableProperty<int> LoginCount = new(0);
        public readonly BindableProperty<bool> IsLoggedIn = new(false);
        public readonly BindableProperty<long> Gold = new(1000);

        protected override void OnRelease()
        {
            AccountName.Dispose();
            LoginCount.Dispose();
            IsLoggedIn.Dispose();
            Gold.Dispose();
            base.OnRelease();
        }
    }
}
```

### Service 层 — LoginService.cs

```csharp
namespace GameLogic
{
    public class LoginService : Singleton<LoginService>
    {
        public void RandomLogin()
        {
            var model = Singleton<LoginModel>.Instance;
            model.AccountName.Value = RandomNames[Random.Range(0, RandomNames.Length)];
            model.LoginCount.Value++;
            model.IsLoggedIn.Value = true;
            model.Gold.Value += Random.Range(-1500, 500);
        }

        public void Logout()
        {
            Singleton<LoginModel>.Instance.IsLoggedIn.Value = false;
        }
    }
}
```

### DataContext 层 — LoginDataContext.cs

```csharp
using GameLogic.DataBinding;

namespace GameLogic
{
    [DataContext(typeof(LoginUI))]
    public class LoginDataContext : DataContext<LoginUI>
    {
        // View 输出属性：格式化后的展示数据
        public readonly BindableProperty<string> DisplayAccount = new("");
        public readonly BindableProperty<string> DisplayStatus = new("");
        public readonly BindableProperty<string> DisplayGold = new("");

        public LoginDataContext()
        {
            var model = Singleton<LoginModel>.Instance;

            // Model → View 映射（含类型转换/格式化）
            MapProperty(model.AccountName, DisplayAccount, name => $"当前账号: {name}");
            MapProperty(model.LoginCount, DisplayStatus, c => c > 0 ? $"已登录 {c} 次" : "未登录");
            MapProperty(model.Gold, DisplayGold, FormatGold);
        }

        private static string FormatGold(long gold)
        {
            if (gold < 0) return "破产";
            if (gold >= 1_000_000_000) return $"{gold / 1_000_000_000.0:F1}B";
            if (gold >= 1_000_000) return $"{gold / 1_000_000.0:F1}M";
            if (gold >= 10_000) return $"{gold / 1_000.0:F1}K";
            return $"金币: {gold}";
        }
    }
}
```

### View 层 — LoginUI.cs

```csharp
using GameLogic.DataBinding;
using UnityEngine.UI;
using TEngine;

namespace GameLogic
{
    [Window(UILayer.UI)]
    [DataContext(typeof(LoginDataContext))]
    public class LoginUI : UIWindow
    {
        private InputField m_inputAccount;
        private InputField m_inputPassword;
        private Button m_btnLogin;
        private Text m_textAccount;

        protected override void ScriptGenerator()
        {
            m_inputAccount = FindChildComponent<InputField>("m_inputAccount");
            m_inputPassword = FindChildComponent<InputField>("m_inputPassword");
            m_btnLogin = FindChildComponent<Button>("m_btnLogin");
            m_textAccount = FindChildComponent<Text>("m_text_Account");

            m_btnLogin.onClick.AddListener(OnLoginClicked);
        }

        protected override void SetupBindings()
        {
            var dc = GetDataContext<LoginDataContext>();
            var model = Singleton<LoginModel>.Instance;

            BindText(m_textAccount, dc.DisplayAccount);      // DataContext 转换后输出
            BindText(m_inputAccount, model.AccountName);     // 也可直接绑定 Model 属性
            BindText(m_inputPassword, dc.DisplayGold);
            BindInteractable(m_btnLogin, model.IsLoggedIn);
            Bind(dc.DisplayStatus, status => Log.Info($"状态变更: {status}"));
        }

        private void OnLoginClicked()
        {
            Singleton<LoginService>.Instance.RandomLogin();  // 只调 Service
        }
    }
}
```

---

## 五、开发工作流

### 新建 MVE UI 的步骤

```
1. 创建 Model     → XxxModel : Singleton<T>，声明 BindableProperty 字段
2. 创建 Service   → XxxService : Singleton<T>，封装业务方法
3. 创建 DataContext→ [DataContext(typeof(XxxUI))] + DataContext<XxxUI>
                     构造函数中 MapProperty 建立映射
4. 创建 View      → [Window(UILayer.UI)] + [DataContext(typeof(XxxDataContext))]
                     ScriptGenerator() 绑定节点
                     SetupBindings() 声明数据绑定
                     用户交互调用 Service
5. 创建 Prefab    → 节点命名遵循 naming-rules.md 前缀规范
```

### DataContext 自动注入流程

```
UIModule.ShowUI(typeof(LoginUI))
  → UIModule.OnWindowPrepare(window)
    → DataContextFactory.CreateFor(window.GetType())
      → 读取 [DataContext] 特性 → 反射创建 LoginDataContext
        → LoginDataContext 构造函数执行 MapProperty
    → window._dataContext = ctx
  → window.OnCreate()
    → window.SetupBindings()  // 此时 DataContext 已就绪
```

### DataContextFactory 匹配规则

1. 扫描 View 类上的 `[DataContext(typeof(XxxDataContext))]` 特性
2. 扫描 DataContext 类上的 `[DataContext(typeof(XxxUI))]` 特性
3. 如果 View 类没有标记特性，返回 `null`（非 MVE 窗口）
4. 工厂委托缓存后不再反射，Domain Reload 时静态字段自动清空

---

## 六、GameEvent 与 MVE 的协作

两套系统独立运行，适用场景不同：

| 系统 | 适用场景 | 作用域 | 清理方式 |
|------|---------|--------|---------|
| **DataContext/Bind** | 同一模块内的数据驱动 UI 刷新 | 窗口局部 | `RemoveAllBindings()`（UIWindow.OnDestroy 自动） |
| **GameEvent** | 跨模块通信、全局事件广播 | 全局 | `RemoveAllUIEvent()`（UIWindow.OnDestroy 自动） |

**选择原则**：
- **同一 UI 内的数据刷新** → 用 BindableProperty + Bind
- **模块间解耦通信**（如背包变化→主界面金币更新）→ 用 GameEvent
- **混合使用**：UIWindow.OnDestroy 同时调用 `RemoveAllUIEvent()` 和 `RemoveAllBindings()`

---

## 七、FancyScrollView Cell 的 MVE 模式

Cell（`FancyCellWidget<TItemData,TContext>`）继承 `UIWidget`，MVE 用法与 UIWindow 一致，但绑定位置不同：

| 项 | UIWindow | Cell |
|----|---------|------|
| 响应式属性创建 | `SetupBindings()` 中 `Bind()` | `BindMemberProperty()` 中 `CreateProperty<T>()` |
| 数据写入触发刷新 | Service 改 Model → Bind 回调 | `BindData(data)` 写 `property.Value` |
| 列表数据来源 | DataContext MapProperty | UIWindow 调 `m_scrollerController.UpdateData(items)` |

**关键**：Cell 的 MVE 绑定放在 `BindMemberProperty()`（钩子顺序在 `ScriptGenerator` 之后，节点字段已就绪），**不放 `ScriptGenerator()`**（已由 `_Gen.g.cs` 占用）：
```csharp
private BindableProperty<string> _displayText;

protected override void BindMemberProperty()
{
    _displayText = CreateProperty<string>("");     // 生命周期随 Cell，OnDestroy 自动 Dispose
    BindText(m_textMessage, _displayText);         // m_textMessage 来自 _Gen.g.cs
}

protected override void BindData(Example01_BasicItemData data)
{
    _displayText.Value = data.DisplayText;         // 写入触发 UI 刷新
}
```

要点：
- `CreateProperty<T>()` 返回的 `BindableProperty` 随 Cell 生命周期自动 Dispose（无需手动）。
- Cell 复用时 `BindData` 必须重置瞬态状态（选中态、按钮可用态、加载状态等）。
- 列表数据更新：UIWindow 调 `m_scrollerController.UpdateData(itemList)`（是 `UpdateData`，非旧 `UpdateContents`）。
- Cell 自动生成与根节点组件处理见 [ui-patterns.md](ui-patterns.md) FancyScrollView 章节。

---

## 八、FancyScrollView 列表的 UIWindow 侧 MVE（carrier-as-lifecycle）

FancyScrollView 列表场景，UIWindow 需把 `Model.BindableProperty<T>[]`（数组）转成 `List<ItemData>` 喂给 `m_scrollerController.UpdateData()`。这是典型的 Model→View 集合转换，归 DataContext。但 DataContext 基类不支持「N 源→1 目标」映射（`MapProperty` 最多 3 源，`MapList` 要求 `ObservableList<struct>`），故用 **carrier-as-lifecycle** 模式托管订阅。

**模式**：DataContext 遍历 Model 数组，每项借 `MapProperty(source, carrier, converter)` 把订阅生命周期托管给基类；`carrier` 是无业务意义的私有 `BindableProperty`（仅作生命周期载体，值无意义）；converter 内维护输出 `Items[idx]` 并推进单一 `ItemsRevision`（public `BindableProperty<int>`，版本号信号）。View 绑 `ItemsRevision` 刷新 scroller。

```csharp
[DataContext(typeof(XxxUI))]
public class XxxDataContext : DataContext<XxxUI>
{
    public readonly List<XxxItemData> Items = new(XxxModel.Count);
    public readonly BindableProperty<int> ItemsRevision = new(0);   // View 绑它刷新

    public XxxDataContext()
    {
        var model = Singleton<XxxModel>.Instance;
        for (int i = 0; i < XxxModel.Count; i++)
        {
            int idx = i;
            Items.Add(BuildItem(idx, model.Ids[idx].Value));
            var carrier = new BindableProperty<int>(model.Ids[idx].Value);  // 生命周期载体，值无意义
            MapProperty(model.Ids[idx], carrier, newVal =>
            {
                Items[idx].Id = newVal;
                Items[idx].DisplayText = Utility.Text.Format("Cell {0} id:{1}", idx, newVal);  // 高频拼接用 Utility.Text.Format 减少 GC（见 naming-rules.md）
                ItemsRevision.Value++;            // 同帧多次 ++ 由 BatchScheduler 合并为一次刷新
                return newVal;
            });
        }
    }
}
```

```csharp
// View 侧
protected override void SetupBindings()
{
    var dc = GetDataContext<XxxDataContext>();
    Bind(dc.ItemsRevision, _ => RefreshList());   // 首次触发时 prefab 未就绪，UpdateData 内部早退
}

private void RefreshList()
{
    m_scrollerController.UpdateData(GetDataContext<XxxDataContext>().Items);
}
```

**要点**：
- `MapProperty` 订阅的是 source（Model.Ids），**不订阅 carrier**；carrier.Value 标脏无人监听，是空操作。真正驱动 View 的是 `ItemsRevision.Value++`。
- 同帧多次改 ID → BatchScheduler 两轮 Flush 合并 → View 仅刷新一次（契合框架「两轮 Flush 处理 DataContext→View 链式更新」设计）。
- 生命周期闭环：carrier 进 `_ownedProperties` + 订阅进 `_unsubscribers`（DataContext.Dispose 清理）；`ItemsRevision` 进 `UIBase._bindings`（RemoveAllBindings 清理）。零泄漏。
- **时序**：`SetupBindings()` 在 `OnCreate()`（含 `await LoadCellPrefabAsync`）之前执行（`UIWindow.cs:354-358`）。`Bind` 首次触发的 RefreshList 会调 `UpdateData`，但 `FancyScrollerController.UpdatePosition` 有 `if (_cellPrefab == null) return;` 框架级早退 guard，安全。**首帧渲染由 `OnCreate` 在 `await` 后显式 `UpdateData` 触发**。
- **`async void OnCreate` 必须 try-catch**：`await` 点（`LoadCellPrefabAsync` 等异步加载）的异常会冒泡到同步上下文难以追踪。聚焦包住 `await` 及其后续渲染逻辑（不包同步初始化——编程错误应暴露而非吞掉）：

```csharp
protected override async void OnCreate()
{
    /* 同步初始化：SetCellFactory / SetContext / += 事件（抛异常属编程错误，应暴露） */
    try
    {
        await m_scrollerController.LoadCellPrefabAsync("XxxCell");
        m_scrollerController.UpdateData(dc.Items);   // 依赖 prefab 成功
        ScrollToIndex(...);
    }
    catch (Exception e)
    {
        Log.Error($"[Xxx] OnCreate 初始化失败: {e}");   // prefab 失败时优雅降级，窗口仍可关闭
    }
}
```

> **不要把 Service 当转换层**：`BuildItems()` 这类「Model→List<ItemData> + 格式化 DisplayText」是 Model→View 转换，归 DataContext，不归 Service。Service 只做纯业务（改 Model.Value）。
>
> 参考实现：`Example01_Basic/`（MVE 四层 + carrier 模式 + async try-catch）。选中态广播用 `FancySelectionContext` 基类，见 [ui-patterns.md](ui-patterns.md) FancyScrollView 章节。
