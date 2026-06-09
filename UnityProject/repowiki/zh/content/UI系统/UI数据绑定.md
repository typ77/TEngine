# UI 数据绑定系统

<cite>
**本文引用的文件**
- [BindableProperty.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BindableProperty.cs)
- [ObservableList.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ObservableList.cs)
- [BatchScheduler.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BatchScheduler.cs)
- [DataContext.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContext.cs)
- [DataContextAttribute.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextAttribute.cs)
- [DataContextFactory.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextFactory.cs)
- [IBatchDirtyTarget.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/IBatchDirtyTarget.cs)
- [UIBase.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs)
- [UIWindow.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs)
- [UIWidget.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs)
- [LoginModel.cs](file://Assets/GameScripts/HotFix/GameLogic/UI/LoginUI/LoginModel.cs)
- [LoginDataContext.cs](file://Assets/GameScripts/HotFix/GameLogic/UI/LoginUI/LoginDataContext.cs)
- [LoginUI.cs](file://Assets/GameScripts/HotFix/GameLogic/UI/LoginUI/LoginUI.cs)
</cite>

## 目录
1. [简介](#简介)
2. [架构总览](#架构总览)
3. [核心组件](#核心组件)
4. [MVE 四层架构](#mve-四层架构)
5. [API 参考](#api-参考)
6. [使用指南](#使用指南)
7. [生命周期管理](#生命周期管理)
8. [回调合并语义](#回调合并语义)
9. [与现有系统共存](#与现有系统共存)
10. [设计约束](#设计约束)

## 简介

UI 数据绑定系统为 TEngine 提供轻量级响应式数据绑定能力，解决传统 `OnRefresh()` 手动刷新模式的痛点：

| 痛点 | 数据绑定解决方案 |
|------|-----------------|
| 数据变了 UI 不知道 | Model 赋值自动传播到所有订阅的 View |
| 同一数据多处展示 | 一处赋值，多处自动更新 |
| OnRefresh 粒度太粗 | 属性级 Push，按需刷新 |
| 手动调 OnRefresh 容易遗漏 | Bind 订阅，数据驱动 |

**核心特性：**
- **帧级批次合并** — 同帧多次赋值合并为一次通知，对业务透明
- **多数据源透明聚合** — DataContext 自动组合多个 Model 数据
- **零外部依赖** — 纯 C# 实现，不引入第三方库
- **生命周期自动管理** — Bind/Unbind 自动配对，无泄漏风险
- **与现有系统共存** — 不替代 OnRefresh / GameEvent，渐进式采用

## 架构总览

```
┌─────────────────────────────────────────────────────────┐
│   Model 层        持有 BindableProperty / ObservableList │
│   (纯数据)        不知道 UI 存在                          │
│       │                                                 │
│       │  赋值触发脏标记                                   │
│       ▼                                                 │
│   BatchScheduler   帧级合并 + 两轮 Flush                 │
│       │                                                 │
│       │  Round 1: Model→DataContext                      │
│       │  Round 2: DataContext→View                       │
│       ▼                                                 │
│   DataContext 层   聚合多源 + 格式转换                    │
│   (MapProperty/MapList)                                  │
│       │                                                 │
│       │  Bind() 订阅                                     │
│       ▼                                                 │
│   View 层          UIBase.Bind / BindText 等便捷方法     │
│   (UIWindow / UIWidget)                                 │
└─────────────────────────────────────────────────────────┘
```

**数据流方向：**

```
Model(s) ──赋值──▶ BatchScheduler ──帧末──▶ DataContext ──转换──▶ View
```

## 核心组件

### BindableProperty\<T\>

响应式属性包装器。赋值时检测值是否变化，变化则标记为脏，由 BatchScheduler 在帧末统一触发回调。

```csharp
// 赋值语义：值立即变化 + 回调延迟到帧末
gold.Value = 100;       // 值立即变成 100
Debug.Log(gold.Value);  // 输出 100 ✓（值已变）
// 但 OnValueChanged 回调延迟到 LateUpdate
```

**来源**：[BindableProperty.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BindableProperty.cs)

**核心行为：**
- 值比较使用 `EqualityComparer<T>.Default`，支持自定义比较器
- 赋相同值不触发通知
- 一帧内多次赋值合并为一次回调：`(旧值, 最终新值)`
- Dispose 后赋值静默跳过

### ObservableList\<T\>

带变更通知的有序集合。增删改操作触发事件，同样通过 BatchScheduler 帧级合并。

```csharp
public class ObservableList<T> : IDisposable, IReadOnlyList<T>, IBatchDirtyTarget
    where T : struct, IEquatable<T>
```

**泛型约束：** `struct` + `IEquatable<T>` — 确保不可变语义和可靠的值比较。

**来源**：[ObservableList.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ObservableList.cs)

**支持的变更类型：**

| 类型 | 说明 |
|------|------|
| Add / Insert | 添加 |
| Remove / RemoveAt | 移除 |
| Replace | 替换指定位置 Item（修改的唯一方式） |
| Move | 移动位置 |
| Clear | 清空 |
| AddRange / ReplaceAll | 批量操作（一次通知） |

**边界检查：** Insert / RemoveAt / Move / Replace 均使用 `(uint)index >= (uint)Count` 风格的越界保护。

### BatchScheduler

帧级批次合并调度器。收集一帧内所有属性/列表变更，在 LateUpdate 时统一触发回调。

**来源**：[BatchScheduler.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BatchScheduler.cs)

**两轮 Flush 机制：**
- **Round 1**：处理 Model 层脏标记 → 触发 DataContext converter
- **Round 2**：处理 Round 1 中新产生的脏标记 → 触发 View 回调
- 效果：Model 变更 → 同帧 DataContext 转换 → 同帧 View 刷新

**EditMode 兼容：** `SafeMarkDirty` 在 BatchScheduler 单例不可用时（EditMode 测试），直接同步触发 FireCallback，不做帧级合并。

### DataContext

聚合多个 Model 的数据，转换为 View 友好的格式，自动管理订阅生命周期。

**来源**：[DataContext.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContext.cs)

**核心方法：**

```csharp
// 单源映射
MapProperty(source, target, converter);

// 多源映射（2~4 个源）
MapProperty(source1, source2, target, converter);

// 列表映射
MapList(sourceList, targetList, converter);
```

**converter 模式示例：**
```
Model: Gold = -500 (long)
  → MapProperty converter
DisplayGold = "破产" (string)
  → BindText
View: m_text.text = "破产"
```

### DataContextAttribute + DataContextFactory

```csharp
[Window(UILayer.UI)]
[DataContext(typeof(BagDataContext))]   // 声明 DataContext 类型
class BagUI : UIWindow { }
```

工厂通过 `Activator.CreateInstance` 创建实例，缓存工厂委托避免重复反射。支持 `ResetCache()` 方法用于 Enter Play Mode Options 场景。

**来源**：[DataContextAttribute.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextAttribute.cs)、[DataContextFactory.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextFactory.cs)

## MVE 四层架构

推荐按业务单位组织文件，形成 Model-View-EventHandler 四层架构：

```
UI/LoginUI/
├── LoginModel.cs          ← 纯数据层（BindableProperty）
├── LoginService.cs        ← 业务操作层（修改 Model）
├── LoginDataContext.cs    ← 数据映射层（MapProperty + converter）
└── LoginUI.cs             ← 视图层（BindText / BindInteractable）
```

| 层级 | 职责 | 示例 |
|------|------|------|
| **Model** | 纯数据，不知道 UI 存在 | `LoginModel` 持有 `Gold`、`Account` 等属性 |
| **Service** | 业务操作，修改 Model | `LoginService.RandomLogin()` 修改 Model 数据 |
| **DataContext** | 聚合+转换，暴露 View 友好属性 | `LoginDataContext` 的 converter 将 `Gold < 0` 转为 `"破产"` |
| **View** | 纯渲染，通过 Bind 订阅 | `LoginUI` 调用 `BindText(m_text, ctx.DisplayAccount)` |

## API 参考

### UIBase 绑定扩展

```csharp
// 通用绑定
Bind(property, onChanged);
Bind(property, (oldVal, newVal) => { ... });

// Phase 2 便捷方法
BindText(text, prop);                  // Text.text
BindText(tmp, prop);                   // TextMeshProUGUI.text
BindText(inputField, prop);            // InputField.text
BindInteractable(selectable, prop);    // Selectable.interactable
BindToggle(toggle, prop);              // Toggle.isOn
BindSlider(slider, prop);              // Slider.value (float/int)
BindSprite(image, prop);               // Image.sprite（异步加载）
```

**来源**：[UIBase.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs)

### Widget 自治模式

Widget 拥有相同的 Bind API，鼓励内部自己管理绑定：

```csharp
// 父级
var btn = CreateWidget<LoginButton>("m_btnLogin");
btn.BindLabel(dc.ButtonLabel);

// Widget 内部
public class LoginButton : UIWidget
{
    private Text _label;
    public void BindLabel(BindableProperty<string> prop) => BindText(_label, prop);
}
```

## 使用指南

### 新窗口接入数据绑定

```csharp
// 1. 定义 Model
public class PlayerModel : Singleton<PlayerModel>
{
    public readonly BindableProperty<long> Gold = new(0);
    public readonly BindableProperty<int> Level = new(1);
}

// 2. 定义 DataContext
public class BagDataContext : DataContext<BagUI>
{
    public readonly BindableProperty<string> GoldText = new("");

    public BagDataContext()
    {
        var player = PlayerModel.Instance;
        MapProperty(player.Gold, GoldText, gold => FormatGold(gold));
    }
}

// 3. 在 UIWindow 上声明
[Window(UILayer.UI)]
[DataContext(typeof(BagDataContext))]
class BagUI : UIWindow
{
    private Text txtGold;

    protected override void SetupBindings()
    {
        var ctx = GetDataContext<BagDataContext>();
        BindText(txtGold, ctx.GoldText);
    }
}

// 4. 业务代码修改 Model → UI 自动刷新
PlayerModel.Instance.Gold.Value += 100;  // 所有绑定了 Gold 的 UI 自动更新
```

### 旧窗口兼容

不添加 `[DataContext]` 特性的窗口完全不受影响，零改动。

## 生命周期管理

```
窗口创建流程:
  UIModule.OnWindowPrepare → DataContextFactory.CreateFor → 注入 DataContext
  → InternalCreate → SetupBindings() → Bind 订阅生效 → 首次同步

窗口销毁流程:
  InternalDestroy → RemoveAllBindings() → 取消所有 Bind 订阅
  → DataContext.Dispose() → 取消所有 Model 订阅 + 释放输出属性
  → Widget.OnDestroyWidget → _dataContext?.Dispose()
```

**关键保证：**
- DataContext 在 SetupBindings 之前创建，Bind 时数据已就绪
- Dispose 由框架自动调用，开发者无需手动管理
- Widget 通过 Parent 链获取 Window 的 DataContext

## 回调合并语义

### BindableProperty 合并

```
gold.Value = 100;   // 值立即变，脏标记：old=初始值
gold.Value = 200;   // 值立即变，已在脏集合
gold.Value = 50;    // 值立即变，已在脏集合
// → 帧末 Flush 回调：(初始值, 50)  — 只触发一次
```

### ObservableList 合并

```
list.Add(a);         // _operationCount=1, 保留原始事件
list.Replace(0, b);  // _operationCount=2
// → 帧末 Flush：_operationCount > 1 → 合并为 ReplaceAll（全量快照）
```

**设计意图：** 同帧多次操作合并为全量通知，避免中间状态导致 View 不一致。Phase 3 引入 UIListWidget 时可能提供更细粒度的 Diff 模式。

## 与现有系统共存

| 机制 | 适用场景 | 共存方式 |
|------|---------|---------|
| **Bind** | 数据驱动，值变化自动刷新 UI | 新功能，渐进式采用 |
| **OnRefresh** | 命令式逻辑（Tab 切换、网络请求、首次进入） | 保留，共存 |
| **GameEvent** | 跨模块事件通信 | 保留，共存 |
| **AddUIEvent** | UI 内部按钮点击等交互 | 保留，共存 |

```csharp
class BagUI : UIWindow
{
    protected override void SetupBindings()
    {
        // 数据绑定：自动刷新
        BindText(txtGold, ctx.GoldText);
    }

    protected override void RegisterEvent()
    {
        // UI 事件：按钮交互
        AddUIEvent(EventId.OnCloseBagClick, Close);
    }

    protected override void OnRefresh()
    {
        // 命令式逻辑：网络请求、Tab 切换
        SendBagOpenRequest();
    }
}
```

## 设计约束

1. **异步优先** — IO 操作用 UniTask，禁止同步加载
2. **DataContext 转换函数必须无副作用** — 纯函数，不改 Model，不发网络请求，不调 GameEvent
3. **ObservableList Item 必须不可变** — struct / record，修改必须 Replace
4. **赋值语义统一** — 所有类型：赋值 = 通知（引用类型内部修改不触发，必须替换对象）
5. **无订阅者优化** — 没有订阅者的 BindableProperty 赋值零开销
6. **HybridCLR 安全** — DataContext 使用无参构造函数 + 直接 `Singleton<T>.Instance`，避免 `MakeGenericType`

## 测试覆盖

| 测试文件 | 用例数 | 覆盖范围 |
|----------|--------|---------|
| `BindablePropertyTests.cs` | 14 | 赋值/比较/合并/Dispose/ForceNotify |
| `ObservableListTests.cs` | 29 | CRUD/事件/边界检查/合并/Dispose |
| `BatchSchedulerTests.cs` | 4 | 帧级合并/两轮 Flush |
| `DataContextTests.cs` | 6 | MapProperty 单/多源/列表/Dispose |
| `DataContextFactoryTests.cs` | 5 | 创建/缓存/ResetCache |
| `UIBaseBindingTests.cs` | 6 | Bind/首次同步/RemoveAll |
| `EndToEndTests.cs` | 3 | 完整数据流 |
