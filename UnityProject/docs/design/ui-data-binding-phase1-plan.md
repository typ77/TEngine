# UI 数据绑定系统 Phase 1 — 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为 TEngine UI 系统添加轻量级响应式数据绑定（BindableProperty + ObservableList + DataContext），实现 Model 变更自动传播到 View。

**架构：** MVE（Model-View-EventHandler）架构。Model 持有 BindableProperty/ObservableList，DataContext 聚合多源数据并转换，View 通过 Bind() 订阅。BatchScheduler 在 LateUpdate 帧级合并变更通知。

**技术栈：** Unity + HybridCLR + UniTask + NUnit EditMode Tests

**规格文档：**
- [架构设计](./ui-data-binding-phase1.md) — 组件职责、API、时序图
- [实现规格](./ui-data-binding-phase1-impl.md) — 内部算法、集成修改点、测试矩阵

---

## 文件结构

### 新增文件

| 文件 | 职责 | 依赖 |
|------|------|------|
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/IBatchDirtyTarget.cs` | BatchScheduler 脏标记非泛型接口 | 无 |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BindableProperty.cs` | 响应式属性包装器 | IBatchDirtyTarget, BatchScheduler |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangeType.cs` | 列表变更类型枚举 | 无 |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangedEventArgs.cs` | 列表变更事件参数 | ListChangeType |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ObservableList.cs` | 响应式有序集合 | IBatchDirtyTarget, BatchScheduler, ListChangeType, ListChangedEventArgs |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BatchScheduler.cs` | 帧级批次合并调度器 | IBatchDirtyTarget, Singleton, ILateUpdate |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContext.cs` | DataContext 抽象基类 | BindableProperty, ObservableList |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextAttribute.cs` | DataContext 声明特性 | DataContext |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextFactory.cs` | DataContext 工厂（无参构造+缓存） | DataContext, DataContextAttribute |
| `Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/Binding.cs` | 绑定记录结构 | 无 |

### 新增测试文件

| 文件 | 职责 |
|------|------|
| `Assets/TEngine/Tests/EditMode/DataBinding/TEngine.Tests.DataBinding.asmdef` | 测试程序集定义 |
| `Assets/TEngine/Tests/EditMode/DataBinding/DataBindingTestBase.cs` | 测试基类（Singleton 重置、Flush 辅助） |
| `Assets/TEngine/Tests/EditMode/DataBinding/BindablePropertyTests.cs` | BindableProperty 测试 |
| `Assets/TEngine/Tests/EditMode/DataBinding/ObservableListTests.cs` | ObservableList 测试 |
| `Assets/TEngine/Tests/EditMode/DataBinding/BatchSchedulerTests.cs` | BatchScheduler 测试 |
| `Assets/TEngine/Tests/EditMode/DataBinding/DataContextTests.cs` | DataContext 测试 |
| `Assets/TEngine/Tests/EditMode/DataBinding/DataContextFactoryTests.cs` | DataContextFactory 测试 |
| `Assets/TEngine/Tests/EditMode/DataBinding/UIBaseBindingTests.cs` | UIBase 绑定扩展测试 |

### 修改文件

| 文件 | 修改行 | 描述 |
|------|--------|------|
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs` | 末尾追加 | 新增 DataContext、Bind、SetupBindings、RemoveAllBindings |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs` | 348, 432 | InternalCreate 插入 SetupBindings(); InternalDestroy 插入 RemoveAllBindings + DataContext.Dispose |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs` | 483 | OnWindowPrepare 插入 DataContext 创建 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs` | 282 | OnDestroyWidget 开头加 RemoveAllBindings() |

---

## 任务 1：项目基础设施

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/` 目录
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/` 目录
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/TEngine.Tests.DataBinding.asmdef`
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/DataBindingTestBase.cs`

- [ ] **步骤 1：创建目录结构和程序集定义**

DataBinding 源码在 `GameLogic` 热更程序集中，不需要独立的 asmdef。

测试程序集：

```json
// Assets/TEngine/Tests/EditMode/DataBinding/TEngine.Tests.DataBinding.asmdef
{
    "name": "TEngine.Tests.DataBinding",
    "rootNamespace": "TEngine.Tests",
    "references": ["GameLogic"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false,
    "optionalUnityReferences": ["TestAssemblies"]
}
```

- [ ] **步骤 2：创建测试基类**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/DataBindingTestBase.cs
using System.Reflection;
using GameLogic;
using NUnit.Framework;

namespace TEngine.Tests
{
    /// <summary>
    /// 数据绑定测试基类。
    /// 提供 Singleton 重置和 BatchScheduler.Flush 辅助方法。
    /// </summary>
    public abstract class DataBindingTestBase
    {
        [SetUp]
        public virtual void SetUp()
        {
            ResetSingleton<BatchScheduler>();
        }

        [TearDown]
        public virtual void TearDown()
        {
            ResetSingleton<BatchScheduler>();
        }

        /// <summary>
        /// 通过反射重置 Singleton 静态实例，确保测试间隔离。
        /// </summary>
        protected static void ResetSingleton<T>() where T : Singleton<T>, new()
        {
            var field = typeof(T).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, null);
        }

        /// <summary>
        /// 手动触发 BatchScheduler.Flush（不依赖 Unity LateUpdate）。
        /// EditMode 测试中 PlayerLoop 不运行，需手动调用。
        /// </summary>
        protected static void FlushScheduler()
        {
            BatchScheduler.Instance.OnLateUpdate();
        }
    }
}
```

- [ ] **步骤 3：创建空目录占位**

```bash
# Unity 需要有 .meta 文件，创建目录后 Unity Editor 会自动生成
# 确保以下目录存在：
mkdir -p Assets/GameScripts/HotFix/GameLogic/Module/DataBinding
mkdir -p Assets/TEngine/Tests/EditMode/DataBinding
```

- [ ] **步骤 4：Commit**

```bash
git add Assets/TEngine/Tests/EditMode/DataBinding/
git commit -m "chore: 创建 DataBinding 测试基础设施（程序集定义 + 测试基类）

- TEngine.Tests.DataBinding.asmdef 引用 GameLogic
- DataBindingTestBase 提供 Singleton 重置和手动 Flush 辅助
- DataBinding 源码目录占位"
```

---

## 任务 2：IBatchDirtyTarget + BindableProperty（TDD）

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/IBatchDirtyTarget.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BindableProperty.cs`
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/BindablePropertyTests.cs`

**依赖关系：** BindableProperty 需要 BatchScheduler.MarkDirty，但 BatchScheduler 还没创建。采用**前向声明策略**：BindableProperty 先调用 `BatchScheduler.Instance.MarkDirty(this)`，BatchScheduler 在任务 5 实现。编译通过但运行时 MarkDirty 暂时无效，等 BatchScheduler 就位后自动生效。

- [ ] **步骤 1：编写 IBatchDirtyTarget 接口**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/IBatchDirtyTarget.cs
namespace GameLogic.DataBinding
{
    /// <summary>
    /// BatchScheduler 脏标记目标的非泛型接口。
    /// BindableProperty{T} 和 ObservableList{T} 都实现此接口，
    /// 以便 BatchScheduler 用统一集合管理。
    /// </summary>
    internal interface IBatchDirtyTarget
    {
        void FireCallback();
    }
}
```

- [ ] **步骤 2：编写 BindableProperty 失败测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/BindablePropertyTests.cs
using System;
using System.Collections.Generic;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class BindablePropertyTests : DataBindingTestBase
    {
        // ──── 赋值基础 ────

        [Test]
        public void Default_Value_IsDefault()
        {
            var prop = new BindableProperty<int>();
            Assert.AreEqual(0, prop.Value);
        }

        [Test]
        public void SetValue_UpdatesImmediately()
        {
            var prop = new BindableProperty<int>(42);
            prop.Value = 100;
            Assert.AreEqual(100, prop.Value);
        }

        [Test]
        public void SetSameValue_DoesNotMarkDirty()
        {
            var prop = new BindableProperty<int>(42);
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;

            prop.Value = 42; // 相同值

            FlushScheduler();
            Assert.IsFalse(fired);
        }

        // ──── 回调触发 ────

        [Test]
        public void Flush_TriggersCallback()
        {
            var prop = new BindableProperty<int>(10);
            int receivedNew = 0;
            prop.OnValueChanged += (_, newVal) => receivedNew = newVal;

            prop.Value = 20;
            FlushScheduler();

            Assert.AreEqual(20, receivedNew);
        }

        [Test]
        public void Callback_HasOldAndNewValue()
        {
            var prop = new BindableProperty<int>(10);
            int receivedOld = 0;
            int receivedNew = 0;
            prop.OnValueChanged += (oldVal, newVal) =>
            {
                receivedOld = oldVal;
                receivedNew = newVal;
            };

            prop.Value = 20;
            FlushScheduler();

            Assert.AreEqual(10, receivedOld);
            Assert.AreEqual(20, receivedNew);
        }

        [Test]
        public void MultipleSetSameFrame_MergesCallback()
        {
            var prop = new BindableProperty<int>(0);
            int callCount = 0;
            prop.OnValueChanged += (_, _) => callCount++;

            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            FlushScheduler();

            Assert.AreEqual(1, callCount, "同帧多次赋值应只触发一次回调");
        }

        [Test]
        public void MultipleSetSameFrame_OldestOldValue()
        {
            var prop = new BindableProperty<int>(0);
            int receivedOld = -1;
            int receivedNew = -1;
            prop.OnValueChanged += (oldVal, newVal) =>
            {
                receivedOld = oldVal;
                receivedNew = newVal;
            };

            prop.Value = 10;
            prop.Value = 20;
            prop.Value = 30;
            FlushScheduler();

            Assert.AreEqual(0, receivedOld, "旧值应为最早的值");
            Assert.AreEqual(30, receivedNew, "新值应为最终的值");
        }

        [Test]
        public void NoSubscriber_NoDirtyMark()
        {
            var prop = new BindableProperty<int>(0);
            // 无订阅者，赋值不应抛异常
            Assert.DoesNotThrow(() => prop.Value = 10);
            Assert.AreEqual(10, prop.Value);
        }

        // ──── 比较器 ────

        [Test]
        public void CustomComparer_Works()
        {
            // 忽略大小写的字符串比较器
            var prop = new BindableProperty<string>("hello",
                StringComparer.OrdinalIgnoreCase);
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;

            prop.Value = "HELLO"; // 比较器认为相等

            FlushScheduler();
            Assert.IsFalse(fired);
        }

        // ──── 生命周期 ────

        [Test]
        public void Dispose_PreventsCallback()
        {
            var prop = new BindableProperty<int>(0);
            prop.OnValueChanged += (_, _) => { };
            prop.Dispose();

            Assert.IsTrue(prop.IsDisposed);
            Assert.DoesNotThrow(() => prop.Value = 10);
            // 赋值不触发异常，也不标记脏
        }

        [Test]
        public void ForceNotify_TriggersCallback()
        {
            var prop = new BindableProperty<int>(42);
            int receivedNew = 0;
            prop.OnValueChanged += (_, newVal) => receivedNew = newVal;

            prop.ForceNotify(); // 即使值未变也触发
            FlushScheduler();

            Assert.AreEqual(42, receivedNew);
        }

        [Test]
        public void SetValueSilently_NoNotification()
        {
            var prop = new BindableProperty<int>(0);
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;

            prop.SetValueSilently(100);

            Assert.AreEqual(100, prop.Value);
            Assert.IsFalse(fired);
        }

        [Test]
        public void RecordStruct_ValueComparison()
        {
            var prop = new BindableProperty<TestData>(new TestData(1, "a"));
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;

            prop.Value = new TestData(1, "a"); // 相同值

            FlushScheduler();
            Assert.IsFalse(fired, "record struct 值比较应认为相等");
        }

        [Test]
        public void RecordStruct_DifferentValue_Fires()
        {
            var prop = new BindableProperty<TestData>(new TestData(1, "a"));
            bool fired = false;
            prop.OnValueChanged += (_, _) => fired = true;

            prop.Value = new TestData(2, "b"); // 不同值

            FlushScheduler();
            Assert.IsTrue(fired);
        }

        // 测试用 record struct
        private readonly record struct TestData(int Id, string Name);
    }
}
```

- [ ] **步骤 3：运行测试验证失败**

在 Unity Editor 中打开 Test Runner → EditMode → 运行 `BindablePropertyTests`。
预期：编译失败（BindableProperty 类不存在）。

- [ ] **步骤 4：编写 BindableProperty 实现**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BindableProperty.cs
using System;
using System.Collections.Generic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 响应式属性包装器。
    /// 赋值时检测值变化，变化则通过 BatchScheduler 在帧末触发回调。
    /// 语义：赋值 = 值立即变化 + 回调延迟到帧末。
    /// </summary>
    public sealed class BindableProperty<T> : IDisposable, IBatchDirtyTarget
    {
        private T _value;
        private readonly IEqualityComparer<T> _comparer;
        private bool _isDirty;
        private T _oldValue;
        private bool _isDisposed;

        /// <summary>
        /// 值变化回调。(oldValue, newValue)
        /// 在 BatchScheduler.Flush 中触发。
        /// </summary>
        public event Action<T, T> OnValueChanged;

        public BindableProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            _value = initialValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        /// <summary>
        /// 当前值。赋值时自动检测变化并标记脏。
        /// 值立即变化，回调延迟到帧末 BatchScheduler.Flush。
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                if (_isDisposed) return;
                if (_comparer.Equals(_value, value)) return;

                if (!_isDirty)
                {
                    _oldValue = _value;
                    _isDirty = true;
                }
                _value = value;

                if (OnValueChanged != null)
                    BatchScheduler.Instance.MarkDirty(this);
            }
        }

        public bool IsDisposed => _isDisposed;

        void IBatchDirtyTarget.FireCallback()
        {
            if (!_isDirty || _isDisposed) return;
            _isDirty = false;
            var old = _oldValue;
            var current = _value;
            OnValueChanged?.Invoke(old, current);
        }

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
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            OnValueChanged = null;
        }
    }
}
```

- [ ] **步骤 5：运行测试验证通过**

在 Unity Editor 中打开 Test Runner → EditMode → 运行 `BindablePropertyTests`。
预期：全部通过（BatchScheduler.Instance 的 MarkDirty 不会报错，因为 Singleton 懒创建）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/IBatchDirtyTarget.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BindableProperty.cs
git add Assets/TEngine/Tests/EditMode/DataBinding/BindablePropertyTests.cs
git commit -m "feat: 实现 BindableProperty<T> 响应式属性包装器

- 赋值立即更新值，回调延迟到帧末
- 同帧多次赋值合并为一次回调
- 支持 EqualityComparer、ForceNotify、SetValueSilently
- 15 个 EditMode 测试全部通过"
```

---

## 任务 3：ListChangeType + ListChangedEventArgs

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangeType.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangedEventArgs.cs`

- [ ] **步骤 1：编写 ListChangeType 枚举**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangeType.cs
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

- [ ] **步骤 2：编写 ListChangedEventArgs**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangedEventArgs.cs
using System.Collections.Generic;

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
}
```

- [ ] **步骤 3：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangeType.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ListChangedEventArgs.cs
git commit -m "feat: 添加 ListChangeType 枚举和 ListChangedEventArgs 事件参数"
```

---

## 任务 4：ObservableList（TDD）

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ObservableList.cs`
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/ObservableListTests.cs`

- [ ] **步骤 1：编写 ObservableList 失败测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/ObservableListTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class ObservableListTests : DataBindingTestBase
    {
        private readonly record struct Item(int Id, string Name);

        // ──── 单次操作 ────

        [Test]
        public void Add_IncreasesCount()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(new Item(1, "A"), list[0]);
        }

        [Test]
        public void Add_FiresEvent_AfterFlush()
        {
            var list = new ObservableList<Item>();
            ListChangeType? receivedType = null;
            int receivedIndex = -1;
            list.OnChanged += args =>
            {
                receivedType = args.Type;
                receivedIndex = args.Index;
            };

            list.Add(new Item(1, "A"));
            FlushScheduler();

            Assert.AreEqual(ListChangeType.Add, receivedType);
            Assert.AreEqual(0, receivedIndex);
        }

        [Test]
        public void Insert_AtCorrectIndex()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));
            list.Insert(0, new Item(2, "B"));

            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(new Item(2, "B"), list[0]);
        }

        [Test]
        public void RemoveAt_DecreasesCount()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));
            list.Add(new Item(2, "B"));
            list.RemoveAt(0);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(new Item(2, "B"), list[0]);
        }

        [Test]
        public void Replace_UpdatesValue()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));
            list.Replace(0, new Item(1, "B"));

            Assert.AreEqual(new Item(1, "B"), list[0]);
        }

        [Test]
        public void Replace_FiresEvent_WithOldItem()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));

            Item receivedOld = default;
            Item receivedNew = default;
            list.OnChanged += args =>
            {
                receivedOld = args.OldItem;
                receivedNew = args.Item;
            };

            list.Replace(0, new Item(1, "B"));
            FlushScheduler();

            Assert.AreEqual(new Item(1, "A"), receivedOld);
            Assert.AreEqual(new Item(1, "B"), receivedNew);
        }

        [Test]
        public void Clear_RemovesAll()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));
            list.Add(new Item(2, "B"));
            list.Clear();

            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void Move_ChangesPositions()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));
            list.Add(new Item(2, "B"));
            list.Add(new Item(3, "C"));

            list.Move(0, 2);

            Assert.AreEqual(new Item(2, "B"), list[0]);
            Assert.AreEqual(new Item(3, "C"), list[1]);
            Assert.AreEqual(new Item(1, "A"), list[2]);
        }

        // ──── 批量操作 ────

        [Test]
        public void AddRange_FiresAddRangeEvent()
        {
            var list = new ObservableList<Item>();
            ListChangeType? receivedType = null;
            list.OnChanged += args => receivedType = args.Type;

            list.AddRange(new[] { new Item(1, "A"), new Item(2, "B") });
            FlushScheduler();

            Assert.AreEqual(ListChangeType.AddRange, receivedType);
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void ReplaceAll_FiresReplaceAllEvent()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "OLD"));

            ListChangeType? receivedType = null;
            int receivedCount = 0;
            list.OnChanged += args =>
            {
                receivedType = args.Type;
                receivedCount = args.NewItems.Count;
            };

            list.ReplaceAll(new[] { new Item(2, "A"), new Item(3, "B") });
            FlushScheduler();

            Assert.AreEqual(ListChangeType.ReplaceAll, receivedType);
            Assert.AreEqual(2, receivedCount);
        }

        // ──── 事件合并 ────

        [Test]
        public void SingleOp_PreservesEventType()
        {
            var list = new ObservableList<Item>();
            ListChangeType? receivedType = null;
            list.OnChanged += args => receivedType = args.Type;

            list.Add(new Item(1, "A")); // 只有一次操作
            FlushScheduler();

            Assert.AreEqual(ListChangeType.Add, receivedType, "单次操作应保留原事件类型");
        }

        [Test]
        public void MultipleOps_MergesToReplaceAll()
        {
            var list = new ObservableList<Item>();
            ListChangeType? receivedType = null;
            int receivedCount = 0;
            list.OnChanged += args =>
            {
                receivedType = args.Type;
                receivedCount = args.NewItems.Count;
            };

            list.Add(new Item(1, "A"));
            list.Add(new Item(2, "B"));
            list.Replace(0, new Item(1, "X"));
            FlushScheduler();

            Assert.AreEqual(ListChangeType.ReplaceAll, receivedType, "多次操作应合并为 ReplaceAll");
            Assert.AreEqual(2, receivedCount);
        }

        // ──── 只读语义 ────

        [Test]
        public void Indexer_IsReadOnly()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));

            // this[int] 只有 get，编译期保证
            var item = list[0];
            Assert.AreEqual(new Item(1, "A"), item);
        }

        [Test]
        public void AsReadOnly_ReturnsReadOnlyView()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));
            var readOnly = list.AsReadOnly();

            Assert.IsInstanceOf<IReadOnlyList<Item>>(readOnly);
            Assert.AreEqual(1, readOnly.Count);
        }

        [Test]
        public void Contains_And_IndexOf()
        {
            var list = new ObservableList<Item>();
            list.Add(new Item(1, "A"));

            Assert.IsTrue(list.Contains(new Item(1, "A")));
            Assert.AreEqual(0, list.IndexOf(new Item(1, "A")));
            Assert.IsFalse(list.Contains(new Item(2, "B")));
            Assert.AreEqual(-1, list.IndexOf(new Item(2, "B")));
        }

        // ──── 生命周期 ────

        [Test]
        public void Dispose_PreventsEvents()
        {
            var list = new ObservableList<Item>();
            list.Dispose();

            Assert.IsTrue(list.IsDisposed);
            // Dispose 后操作不触发异常
            Assert.DoesNotThrow(() => list.Add(new Item(1, "A")));
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

在 Unity Editor Test Runner 中运行 `ObservableListTests`。
预期：编译失败（ObservableList 类不存在）。

- [ ] **步骤 3：编写 ObservableList 实现**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ObservableList.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 带变更通知的有序集合。
    /// Item 类型约束为 struct（不可变），修改必须通过 Replace。
    /// 同帧多次操作合并为 ReplaceAll 事件。
    /// </summary>
    public sealed class ObservableList<T> : IDisposable, IReadOnlyList<T>, IBatchDirtyTarget
        where T : struct, IEquatable<T>
    {
        private readonly List<T> _items;
        private int _operationCount;
        private ListChangedEventArgs<T> _firstEventArgs;
        private bool _isDirty;
        private bool _isDisposed;

        public event Action<ListChangedEventArgs<T>> OnChanged;

        public ObservableList()
        {
            _items = new List<T>();
        }

        public ObservableList(int capacity)
        {
            _items = new List<T>(capacity);
        }

        public ObservableList(IEnumerable<T> collection)
        {
            _items = new List<T>(collection);
        }

        // ──── 读操作 ────

        public int Count => _items.Count;

        public T this[int index] => _items[index];

        public IReadOnlyList<T> AsReadOnly() => _items.AsReadOnly();

        public bool Contains(T item) => _items.Contains(item);

        public int IndexOf(T item) => _items.IndexOf(item);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        // ──── 写操作（触发变更通知）────

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

        public void Insert(int index, T item)
        {
            if (_isDisposed) return;
            _items.Insert(index, item);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Insert,
                Index = index,
                Item = item
            });
        }

        public bool Remove(T item)
        {
            if (_isDisposed) return false;
            int index = _items.IndexOf(item);
            if (index < 0) return false;
            _items.RemoveAt(index);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Remove,
                Index = index
            });
            return true;
        }

        public void RemoveAt(int index)
        {
            if (_isDisposed) return;
            _items.RemoveAt(index);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.RemoveAt,
                Index = index
            });
        }

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

        public void Move(int fromIndex, int toIndex)
        {
            if (_isDisposed) return;
            var item = _items[fromIndex];
            _items.RemoveAt(fromIndex);
            _items.Insert(toIndex, item);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Move,
                Index = toIndex,
                OldIndex = fromIndex
            });
        }

        public void Clear()
        {
            if (_isDisposed) return;
            _items.Clear();
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.Clear,
                Index = -1
            });
        }

        // ──── 批量操作 ────

        public void AddRange(IEnumerable<T> items)
        {
            if (_isDisposed) return;
            var list = items as IList<T> ?? items.ToList();
            _items.AddRange(list);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.AddRange,
                NewItems = list.ToList().AsReadOnly()
            });
        }

        public void ReplaceAll(IEnumerable<T> items)
        {
            if (_isDisposed) return;
            _items.Clear();
            _items.AddRange(items);
            NotifyChanged(new ListChangedEventArgs<T>
            {
                Type = ListChangeType.ReplaceAll,
                NewItems = _items.ToList().AsReadOnly()
            });
        }

        // ──── 内部：脏标记与事件合并 ────

        private void NotifyChanged(in ListChangedEventArgs<T> args)
        {
            if (OnChanged == null) return;

            _operationCount++;
            if (_operationCount == 1)
            {
                _firstEventArgs = args;
            }

            _isDirty = true;
            BatchScheduler.Instance.MarkDirty(this);
        }

        void IBatchDirtyTarget.FireCallback()
        {
            if (!_isDirty || _isDisposed) return;
            _isDirty = false;

            if (_operationCount > 1)
            {
                OnChanged?.Invoke(new ListChangedEventArgs<T>
                {
                    Type = ListChangeType.ReplaceAll,
                    NewItems = _items.ToList().AsReadOnly()
                });
            }
            else
            {
                OnChanged?.Invoke(_firstEventArgs);
            }

            _operationCount = 0;
            _firstEventArgs = default;
        }

        // ──── 生命周期 ────

        public bool IsDisposed => _isDisposed;

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            OnChanged = null;
            _items.Clear();
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

在 Unity Editor Test Runner 中运行 `ObservableListTests`。
预期：全部 16 个测试通过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/ObservableList.cs
git add Assets/TEngine/Tests/EditMode/DataBinding/ObservableListTests.cs
git commit -m "feat: 实现 ObservableList<T> 响应式集合

- struct/IEquatable 约束确保不可变语义
- 同帧多次操作合并为 ReplaceAll 事件
- 隐藏 indexer setter，强制使用 Replace
- 16 个 EditMode 测试覆盖增删改查和事件合并"
```

---

## 任务 5：BatchScheduler（TDD）

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BatchScheduler.cs`
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/BatchSchedulerTests.cs`

- [ ] **步骤 1：编写 BatchScheduler 失败测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/BatchSchedulerTests.cs
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class BatchSchedulerTests : DataBindingTestBase
    {
        [Test]
        public void Flush_TriggersAllDirty()
        {
            var prop1 = new BindableProperty<int>(0);
            var prop2 = new BindableProperty<string>("");

            int received1 = 0;
            string received2 = "";
            prop1.OnValueChanged += (_, v) => received1 = v;
            prop2.OnValueChanged += (_, v) => received2 = v;

            prop1.Value = 42;
            prop2.Value = "hello";
            FlushScheduler();

            Assert.AreEqual(42, received1);
            Assert.AreEqual("hello", received2);
        }

        [Test]
        public void SameProperty_Merges()
        {
            var prop = new BindableProperty<int>(0);
            int callCount = 0;
            prop.OnValueChanged += (_, _) => callCount++;

            prop.Value = 10;
            prop.Value = 20;
            FlushScheduler();

            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void TwoRoundFlush_DataContextToView()
        {
            // 模拟 Model → DataContext → View 两轮处理
            var modelProp = new BindableProperty<int>(0);       // Model 层
            var dcProp = new BindableProperty<string>("");       // DataContext 输出
            string viewText = "";

            // Model → DataContext 转换
            modelProp.OnValueChanged += (_, newVal) =>
            {
                dcProp.Value = newVal.ToString();  // 第一轮触发
            };

            // DataContext → View
            dcProp.OnValueChanged += (_, newVal) =>
            {
                viewText = newVal;  // 第二轮触发
            };

            modelProp.Value = 42;
            FlushScheduler();

            Assert.AreEqual("42", viewText, "两轮 Flush 应使 Model 变更同帧传播到 View");
        }

        [Test]
        public void FlushDuringFlush_QueuesForNextFrame()
        {
            var prop1 = new BindableProperty<int>(0);
            var prop2 = new BindableProperty<int>(0);

            // prop1 回调中修改 prop2（模拟重入）
            prop1.OnValueChanged += (_, _) =>
            {
                prop2.Value = 99;  // Flush 期间产生的脏标记
            };

            int prop2Value = 0;
            prop2.OnValueChanged += (_, v) => prop2Value = v;

            prop1.Value = 1;
            FlushScheduler();  // 第一帧：prop1 触发，prop2 变脏但本轮不处理

            // prop2 的新值应该在 _dirty 中等待下一帧
            // 但由于测试中 Flush 后 _isFlushing=false，
            // 第二轮处理中产生的脏标记已被收集到 _dirty 中
            // 需要再次 Flush
            FlushScheduler();  // 第二帧：处理 prop2

            Assert.AreEqual(99, prop2Value, "重入产生的脏标记应延迟到下次 Flush");
        }

        [Test]
        public void EmptyFlush_NoOp()
        {
            Assert.DoesNotThrow(() => FlushScheduler());
        }

        [Test]
        public void HasPendingChanges_Correct()
        {
            var scheduler = BatchScheduler.Instance;
            Assert.IsFalse(scheduler.HasPendingChanges);

            var prop = new BindableProperty<int>(0);
            prop.OnValueChanged += (_, _) => { };
            prop.Value = 1;

            Assert.IsTrue(scheduler.HasPendingChanges);

            FlushScheduler();
            Assert.IsFalse(scheduler.HasPendingChanges);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

预期：编译失败（BatchScheduler 缺少 MarkDirty 方法或接口不匹配）。

- [ ] **步骤 3：编写 BatchScheduler 实现**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BatchScheduler.cs
using System.Collections.Generic;
using GameLogic;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 帧级批次合并调度器。
    /// 实现 ILateUpdate，由 SingletonSystem 在 LateUpdate 阶段自动调用 Flush。
    /// 两轮 Flush: 第一轮处理 Model→DataContext，第二轮处理 DataContext→View。
    /// </summary>
    public sealed class BatchScheduler : Singleton<BatchScheduler>, ILateUpdate
    {
        private readonly HashSet<IBatchDirtyTarget> _dirty = new();
        private bool _isFlushing;

        /// <summary>
        /// 标记目标为脏。由 BindableProperty 赋值或 ObservableList 变更时内部调用。
        /// </summary>
        internal void MarkDirty(IBatchDirtyTarget target)
        {
            _dirty.Add(target);
        }

        /// <summary>
        /// SingletonSystem 在 LateUpdate 阶段自动调用。
        /// </summary>
        public void OnLateUpdate()
        {
            Flush();
        }

        /// <summary>
        /// 执行两轮 Flush。
        /// </summary>
        internal void Flush()
        {
            if (_isFlushing || _dirty.Count == 0) return;
            _isFlushing = true;

            try
            {
                // 第一轮：处理 Model 层变更 → 触发 DataContext converter
                var round1 = new List<IBatchDirtyTarget>(_dirty);
                _dirty.Clear();

                foreach (var target in round1)
                    target.FireCallback();

                // 第二轮：处理 DataContext 输出属性变更 → 触发 View 回调
                if (_dirty.Count > 0)
                {
                    var round2 = new List<IBatchDirtyTarget>(_dirty);
                    _dirty.Clear();

                    foreach (var target in round2)
                        target.FireCallback();
                }

                // 第二轮之后产生的脏标记延迟到下一帧（不做第三轮）
            }
            finally
            {
                _isFlushing = false;
            }
        }

        /// <summary>
        /// 当前帧是否有待处理的变更。调试用。
        /// </summary>
        public bool HasPendingChanges => _dirty.Count > 0;
    }
}
```

- [ ] **步骤 4：运行全部 DataBinding 测试**

在 Unity Editor Test Runner 中运行所有 DataBinding 测试：
- `BindablePropertyTests`
- `ObservableListTests`
- `BatchSchedulerTests`

预期：全部通过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/BatchScheduler.cs
git add Assets/TEngine/Tests/EditMode/DataBinding/BatchSchedulerTests.cs
git commit -m "feat: 实现 BatchScheduler 帧级批次合并调度器

- 两轮 Flush: Model→DataContext→View 同帧完成
- 实现 ILateUpdate 自动注册到 SingletonSystem
- 防重入：Flush 期间新脏标记延迟到下次
- 6 个 EditMode 测试覆盖核心场景"
```

---

## 任务 6：DataContext + DataContextAttribute + DataContextFactory（TDD）

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContext.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextAttribute.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextFactory.cs`
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/DataContextTests.cs`
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/DataContextFactoryTests.cs`

- [ ] **步骤 1：编写 DataContextAttribute**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextAttribute.cs
using System;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 标记 UIWindow 使用的 DataContext 类型。
    /// 框架在窗口创建时自动实例化 DataContext（无参构造函数）。
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

- [ ] **步骤 2：编写 DataContext 基类**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContext.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// DataContext 抽象基类。
    /// 聚合多个 Model 数据，转换为 View 友好格式。
    /// 使用无参构造函数 + 直接 Singleton 访问（HybridCLR 安全）。
    /// </summary>
    public abstract class DataContext : IDisposable
    {
        private readonly List<Action> _unsubscribers = new();
        private readonly List<IDisposable> _ownedProperties = new();
        private bool _isDisposed;

        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// 单源标量映射。
        /// </summary>
        protected void MapProperty<TSource, TTarget>(
            BindableProperty<TSource> source,
            BindableProperty<TTarget> target,
            Func<TSource, TTarget> converter)
        {
            target.SetValueSilently(converter(source.Value));

            Action<TSource, TSource> handler = (_, _) =>
            {
                target.Value = converter(source.Value);
            };
            source.OnValueChanged += handler;
            _unsubscribers.Add(() => source.OnValueChanged -= handler);
        }

        /// <summary>
        /// 双源标量映射。任一 source 变化时自动重算。
        /// </summary>
        protected void MapProperty<T1, T2, TTarget>(
            BindableProperty<T1> source1,
            BindableProperty<T2> source2,
            BindableProperty<TTarget> target,
            Func<T1, T2, TTarget> converter)
        {
            target.SetValueSilently(converter(source1.Value, source2.Value));

            Action<T1, T1> h1 = (_, _) => target.Value = converter(source1.Value, source2.Value);
            Action<T2, T2> h2 = (_, _) => target.Value = converter(source1.Value, source2.Value);

            source1.OnValueChanged += h1;
            source2.OnValueChanged += h2;
            _unsubscribers.Add(() => source1.OnValueChanged -= h1);
            _unsubscribers.Add(() => source2.OnValueChanged -= h2);
        }

        /// <summary>
        /// 三源标量映射。
        /// </summary>
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

        /// <summary>
        /// 列表映射。
        /// </summary>
        protected void MapList<TSource, TTarget>(
            ObservableList<TSource> source,
            ObservableList<TTarget> target,
            Func<TSource, TTarget> converter)
            where TSource : struct, IEquatable<TSource>
            where TTarget : struct, IEquatable<TTarget>
        {
            target.ReplaceAll(source.Select(converter));

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
                    default:
                        target.ReplaceAll(source.Select(converter));
                        break;
                }
            };

            source.OnChanged += handler;
            _unsubscribers.Add(() => source.OnChanged -= handler);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var unsub in _unsubscribers)
                unsub();
            _unsubscribers.Clear();

            foreach (var prop in _ownedProperties)
                prop.Dispose();
            _ownedProperties.Clear();
        }
    }

    /// <summary>
    /// 泛型 DataContext 基类，关联到具体 View 类型。
    /// </summary>
    public abstract class DataContext<TView> : DataContext where TView : UIBase
    {
    }
}
```

- [ ] **步骤 3：编写 DataContextFactory**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextFactory.cs
using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// DataContext 工厂。根据 [DataContext] 特性创建 DataContext 实例。
    /// 无参构造函数 + 缓存策略，HybridCLR 完全兼容。
    /// </summary>
    internal static class DataContextFactory
    {
        private static readonly Dictionary<Type, Func<DataContext>> _factories = new();

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
}
```

- [ ] **步骤 4：编写 DataContext 测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/DataContextTests.cs
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class DataContextTests : DataBindingTestBase
    {
        // ──── 标量映射 ────

        [Test]
        public void MapProperty_SingleSource()
        {
            var source = new BindableProperty<int>(10);
            var target = new BindableProperty<string>("");

            var ctx = new TestDataContext();
            ctx.TestMapProperty(source, target, v => v.ToString());

            Assert.AreEqual("10", target.Value, "MapProperty 后 target 应有初始值");

            source.Value = 20;
            FlushScheduler();

            Assert.AreEqual("20", target.Value);
        }

        [Test]
        public void MapProperty_DualSource()
        {
            var s1 = new BindableProperty<int>(10);
            var s2 = new BindableProperty<int>(20);
            var target = new BindableProperty<int>(0);

            var ctx = new TestDataContext();
            ctx.TestMapProperty2(s1, s2, target, (a, b) => a + b);

            Assert.AreEqual(30, target.Value);

            s1.Value = 5;
            FlushScheduler();
            Assert.AreEqual(25, target.Value);

            s2.Value = 10;
            FlushScheduler();
            Assert.AreEqual(15, target.Value);
        }

        [Test]
        public void MapProperty_TripleSource()
        {
            var s1 = new BindableProperty<int>(1);
            var s2 = new BindableProperty<int>(2);
            var s3 = new BindableProperty<int>(3);
            var target = new BindableProperty<int>(0);

            var ctx = new TestDataContext();
            ctx.TestMapProperty3(s1, s2, s3, target, (a, b, c) => a + b + c);

            Assert.AreEqual(6, target.Value);

            s3.Value = 10;
            FlushScheduler();
            Assert.AreEqual(13, target.Value);
        }

        [Test]
        public void MapProperty_InitializesTarget()
        {
            var source = new BindableProperty<string>("hello");
            var target = new BindableProperty<string>("");

            var ctx = new TestDataContext();
            ctx.TestMapProperty(source, target, s => s.ToUpper());

            Assert.AreEqual("HELLO", target.Value);
        }

        // ──── 列表映射 ────

        [Test]
        public void MapList_InitialConversion()
        {
            var source = new ObservableList<TestItem>();
            source.Add(new TestItem(1, "A"));
            source.Add(new TestItem(2, "B"));

            var target = new ObservableList<string>();

            var ctx = new TestDataContext();
            FlushScheduler(); // 清空 source.Add 的事件
            ctx.TestMapList(source, target, item => item.Name);

            Assert.AreEqual(2, target.Count);
            Assert.AreEqual("A", target[0]);
            Assert.AreEqual("B", target[1]);
        }

        // ──── 生命周期 ────

        [Test]
        public void Dispose_RemovesSubscriptions()
        {
            var source = new BindableProperty<int>(10);
            var target = new BindableProperty<string>("");

            var ctx = new TestDataContext();
            ctx.TestMapProperty(source, target, v => v.ToString());
            ctx.Dispose();

            source.Value = 20;
            FlushScheduler();

            Assert.AreEqual("10", target.Value, "Dispose 后源变化不应传播到 target");
        }

        // 测试辅助类型
        private readonly record struct TestItem(int Id, string Name);

        private class TestDataContext : DataContext
        {
            public void TestMapProperty<TS, TT>(
                BindableProperty<TS> source, BindableProperty<TT> target,
                Func<TS, TT> converter)
                => MapProperty(source, target, converter);

            public void TestMapProperty2<T1, T2, TT>(
                BindableProperty<T1> s1, BindableProperty<T2> s2,
                BindableProperty<TT> target, Func<T1, T2, TT> converter)
                => MapProperty(s1, s2, target, converter);

            public void TestMapProperty3<T1, T2, T3, TT>(
                BindableProperty<T1> s1, BindableProperty<T2> s2, BindableProperty<T3> s3,
                BindableProperty<TT> target, Func<T1, T2, T3, TT> converter)
                => MapProperty(s1, s2, s3, target, converter);

            public void TestMapList<TS, TT>(
                ObservableList<TS> source, ObservableList<TT> target,
                Func<TS, TT> converter)
                where TS : struct, IEquatable<TS>
                where TT : struct, IEquatable<TT>
                => MapList(source, target, converter);
        }
    }
}
```

- [ ] **步骤 5：编写 DataContextFactory 测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/DataContextFactoryTests.cs
using System;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class DataContextFactoryTests
    {
        [Test]
        public void CreateFor_WithAttribute_Succeeds()
        {
            var ctx = DataContextFactory.CreateFor(typeof(TestViewWithContext));
            Assert.IsNotNull(ctx);
            Assert.IsInstanceOf<TestContext>(ctx);
        }

        [Test]
        public void CreateFor_NoAttribute_ReturnsNull()
        {
            var ctx = DataContextFactory.CreateFor(typeof(TestViewWithoutContext));
            Assert.IsNull(ctx);
        }

        [Test]
        public void CreateFor_InvalidType_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var attr = new DataContextAttribute(typeof(string)); // string 不是 DataContext 子类
            });
        }

        // 测试辅助类型
        [DataContext(typeof(TestContext))]
        private class TestViewWithContext { }

        private class TestViewWithoutContext { }

        private class TestContext : DataContext { }
    }
}
```

注意：`DataContextFactory` 是 `internal` 的，但测试在同一程序集内可以访问。如果 `DataContextFactory.CreateFor` 无法访问，需要添加 `[assembly: InternalsVisibleTo("TEngine.Tests.DataBinding")]` 到 GameLogic 程序集，或将测试文件移入 GameLogic 程序集。替代方案：将 `CreateFor` 改为 `public`。

- [ ] **步骤 6：运行测试验证通过**

在 Unity Editor Test Runner 中运行 `DataContextTests` 和 `DataContextFactoryTests`。
预期：全部通过。

- [ ] **步骤 7：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContext.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextAttribute.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/DataContextFactory.cs
git add Assets/TEngine/Tests/EditMode/DataBinding/DataContextTests.cs
git add Assets/TEngine/Tests/EditMode/DataBinding/DataContextFactoryTests.cs
git commit -m "feat: 实现 DataContext 聚合层 + Attribute + Factory

- MapProperty 支持 1-3 源标量映射，订阅后立即初始化 target
- MapList 支持列表同步（Add/Replace/Remove/Clear/批量操作）
- 无参构造函数，直接 Singleton 访问，HybridCLR 安全
- DataContextFactory 使用 Activator.CreateInstance + 缓存
- 10 个 EditMode 测试覆盖映射、生命周期和工厂"
```

---

## 任务 7：Binding + UIBase 绑定扩展（TDD）

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/Binding.cs`
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs`（末尾追加）
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/UIBaseBindingTests.cs`

- [ ] **步骤 1：编写 Binding 记录结构**

```csharp
// Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/Binding.cs
using System;

namespace GameLogic.DataBinding
{
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
}
```

- [ ] **步骤 2：编写 UIBase 扩展失败测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/UIBaseBindingTests.cs
using System;
using GameLogic.DataBinding;
using GameLogic;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class UIBaseBindingTests : DataBindingTestBase
    {
        private UIBase _uiBase;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _uiBase = new TestUIBase();
        }

        [Test]
        public void Bind_SingleProperty()
        {
            var prop = new BindableProperty<string>("hello");
            string received = null;

            _uiBase.Bind(prop, v => received = v);

            Assert.AreEqual("hello", received, "首次同步应立即回调");
        }

        [Test]
        public void Bind_WithOldValue()
        {
            var prop = new BindableProperty<int>(42);
            int receivedOld = 0;
            int receivedNew = 0;

            _uiBase.Bind(prop, (oldVal, newVal) =>
            {
                receivedOld = oldVal;
                receivedNew = newVal;
            });

            Assert.AreEqual(42, receivedOld, "首次同步 old == current");
            Assert.AreEqual(42, receivedNew);
        }

        [Test]
        public void Bind_PropertyChange_TriggersCallback()
        {
            var prop = new BindableProperty<int>(0);
            int received = 0;

            _uiBase.Bind(prop, v => received = v);

            prop.Value = 99;
            FlushScheduler();

            Assert.AreEqual(99, received);
        }

        [Test]
        public void RemoveAllBindings_NoFurtherCallback()
        {
            var prop = new BindableProperty<int>(0);
            int callCount = 0;

            _uiBase.Bind(prop, v => callCount++);
            Assert.AreEqual(1, callCount, "首次同步");

            _uiBase.RemoveAllBindings();

            prop.Value = 99;
            FlushScheduler();

            Assert.AreEqual(1, callCount, "RemoveAllBindings 后不应再触发");
        }

        [Test]
        public void MultipleBinds_AllTrigger()
        {
            var prop = new BindableProperty<int>(0);
            int count1 = 0;
            int count2 = 0;

            _uiBase.Bind(prop, v => count1++);
            _uiBase.Bind(prop, v => count2++);

            Assert.AreEqual(1, count1, "首次同步 1");
            Assert.AreEqual(1, count2, "首次同步 2");

            prop.Value = 1;
            FlushScheduler();

            Assert.AreEqual(2, count1);
            Assert.AreEqual(2, count2);
        }

        [Test]
        public void Bind_NullProperty_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _uiBase.Bind<int>(null, v => { }));
        }

        // 测试辅助：可实例化的 UIBase 子类
        private class TestUIBase : UIBase
        {
            // UIBase 不需要 GameObject 即可测试 Bind/RemoveAllBindings
        }
    }
}
```

- [ ] **步骤 3：修改 UIBase.cs — 末尾追加绑定扩展成员**

在 `UIBase.cs` 的 `OnSetVisible` 方法之后，class 结束大括号之前，追加以下代码：

```csharp
        // ──── 数据绑定扩展 ────

        private List<DataBinding.Binding> _bindings;
        internal DataBinding.DataContext _dataContext;

        /// <summary>
        /// 数据上下文。UIWindow 直接持有，UIWidget 向上查找。
        /// 无 DataContext 时返回 null。
        /// </summary>
        public virtual DataBinding.DataContext DataContext => _dataContext;

        /// <summary>
        /// 获取强类型的 DataContext。
        /// </summary>
        public T GetDataContext<T>() where T : DataBinding.DataContext
        {
            return _dataContext as T;
        }

        /// <summary>
        /// 绑定到 BindableProperty，值变化时自动回调。
        /// 首次绑定时用当前值立即回调一次（首次同步）。
        /// OnDestroy 时自动解绑。
        /// </summary>
        public void Bind<T>(DataBinding.BindableProperty<T> property, Action<T> onChanged)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (onChanged == null) throw new ArgumentNullException(nameof(onChanged));

            if (_bindings == null) _bindings = new List<DataBinding.Binding>();

            Action<T, T> wrapper = (oldVal, newVal) => onChanged(newVal);
            property.OnValueChanged += wrapper;
            _bindings.Add(new DataBinding.Binding(() => property.OnValueChanged -= wrapper));

            onChanged(property.Value);  // 首次同步
        }

        /// <summary>
        /// 绑定到 BindableProperty（带旧值）。
        /// </summary>
        public void Bind<T>(DataBinding.BindableProperty<T> property, Action<T, T> onChanged)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (onChanged == null) throw new ArgumentNullException(nameof(onChanged));

            if (_bindings == null) _bindings = new List<DataBinding.Binding>();

            property.OnValueChanged += onChanged;
            _bindings.Add(new DataBinding.Binding(() => property.OnValueChanged -= onChanged));

            onChanged(property.Value, property.Value);  // 首次同步
        }

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

需要在 UIBase.cs 文件顶部添加 using：

```csharp
using System;
using System.Collections.Generic;
```

> **注意**：UIBase.cs 已有 `using System;`。只需确认 `using System.Collections.Generic;` 存在，若不存在则添加。

- [ ] **步骤 4：运行测试验证通过**

在 Unity Editor Test Runner 中运行 `UIBaseBindingTests`。
预期：全部 6 个测试通过。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/DataBinding/Binding.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs
git add Assets/TEngine/Tests/EditMode/DataBinding/UIBaseBindingTests.cs
git commit -m "feat: 实现 UIBase 绑定扩展（DataContext + Bind + SetupBindings）

- Binding 记录结构管理订阅清理
- UIBase 新增 Bind/RemoveAllBindings/SetupBindings/GetDataContext
- 首次绑定时用当前值立即回调（首次同步）
- 不修改现有方法签名，完全向后兼容
- 6 个 EditMode 测试覆盖绑定、清理、多绑定"
```

---

## 任务 8：UIWindow + UIModule + UIWidget 集成

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`（InternalCreate + InternalDestroy）
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`（OnWindowPrepare）
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs`（OnDestroyWidget）

- [ ] **步骤 1：修改 UIWindow.InternalCreate — 插入 SetupBindings()**

文件：`UIWindow.cs` 行 348-359

在 `BindMemberProperty();` 和 `RegisterEvent();` 之间插入 `SetupBindings();`：

```csharp
// 修改后的 InternalCreate:
internal void InternalCreate()
{
    if (_isCreate == false)
    {
        _isCreate = true;
        Inject();
        ScriptGenerator();
        BindMemberProperty();
        SetupBindings();      // ← 新增：数据绑定
        RegisterEvent();
        OnCreate();
    }
}
```

- [ ] **步骤 2：修改 UIWindow.InternalDestroy — 插入绑定清理和 DataContext 释放**

文件：`UIWindow.cs` 行 432

在 `RemoveAllUIEvent();` 之后、销毁子 Widget 之前，插入清理逻辑：

```csharp
// 修改后的 InternalDestroy:
internal void InternalDestroy(bool isShutDown = false)
{
    _isCreate = false;

    RemoveAllUIEvent();

    RemoveAllBindings();  // ← 新增：清理数据绑定

    var snapshot = ListChild.ToArray();
    ListChild.Clear();
    for (int i = 0; i < snapshot.Length; i++)
    {
        snapshot[i].CallDestroy();
        snapshot[i].OnDestroyWidget();
    }

    // 释放 DataContext
    if (_dataContext != null)
    {
        _dataContext.Dispose();
        _dataContext = null;
    }

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

- [ ] **步骤 3：修改 UIModule.OnWindowPrepare — 创建 DataContext**

文件：`UIModule.cs` 行 483-496

在 `window.InternalCreate();` 之前插入 DataContext 创建：

在文件顶部添加 using：

```csharp
using GameLogic.DataBinding;
```

修改 `OnWindowPrepare`：

```csharp
private void OnWindowPrepare(UIWindow window)
{
    if (window.LoadFailed)
    {
        Log.Warning("UIModule: Window '{0}' load failed, removing.", window.WindowName);
        Pop(window);
        window.InternalDestroy(isShutDown: false);
        return;
    }

    // 创建并注入 DataContext（在 InternalCreate 之前）
    var ctx = DataContextFactory.CreateFor(window.GetType());
    if (ctx != null)
    {
        window._dataContext = ctx;
    }

    window.InternalCreate();
    window.InternalRefresh();
    OnSortWindowDepth(window.WindowLayer);
    OnSetWindowVisible();
}
```

- [ ] **步骤 4：修改 UIWidget.OnDestroyWidget — 插入 RemoveAllBindings**

文件：`UIWidget.cs` 行 282

在方法开头插入 `RemoveAllBindings()`：

```csharp
protected internal void OnDestroyWidget()
{
    RemoveAllBindings();    // ← 新增

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

- [ ] **步骤 5：编译验证**

在 Unity Editor 中确认无编译错误。

- [ ] **步骤 6：运行全部 DataBinding 测试回归**

在 Unity Editor Test Runner 中运行所有 DataBinding 测试。
预期：全部通过。

- [ ] **步骤 7：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs
git commit -m "feat: 集成 DataBinding 到 UI 生命周期

- UIWindow.InternalCreate: SetupBindings() 插入在 BindMemberProperty 和 RegisterEvent 之间
- UIWindow.InternalDestroy: RemoveAllBindings + DataContext.Dispose
- UIModule.OnWindowPrepare: DataContext 创建和注入
- UIWidget.OnDestroyWidget: RemoveAllBindings 清理
- 全部 DataBinding 测试回归通过"
```

---

## 任务 9：端到端集成验证

**目标：** 用一个完整示例验证 Model → DataContext → View 数据流。

**文件：**
- 创建：`Assets/TEngine/Tests/EditMode/DataBinding/EndToEndTests.cs`

- [ ] **步骤 1：编写端到端测试**

```csharp
// Assets/TEngine/Tests/EditMode/DataBinding/EndToEndTests.cs
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class EndToEndTests : DataBindingTestBase
    {
        // 模拟 Model
        private BindableProperty<long> _gold;
        private BindableProperty<int> _level;
        private ObservableList<ItemData> _items;

        // 模拟 DataContext 输出
        private BindableProperty<string> _goldText;
        private BindableProperty<string> _levelText;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();

            // Model 层
            _gold = new BindableProperty<long>(0);
            _level = new BindableProperty<int>(1);
            _items = new ObservableList<ItemData>();

            // DataContext 输出
            _goldText = new BindableProperty<string>("");
            _levelText = new BindableProperty<string>("");

            // 模拟 DataContext 映射
            MapGold();
            MapLevel();
        }

        private void MapGold()
        {
            _goldText.SetValueSilently(FormatGold(_gold.Value));
            _gold.OnValueChanged += (_, newVal) => _goldText.Value = FormatGold(newVal);
        }

        private void MapLevel()
        {
            _levelText.SetValueSilently($"Lv.{_level.Value}");
            _level.OnValueChanged += (_, newVal) => _levelText.Value = $"Lv.{newVal}";
        }

        [Test]
        public void FullPipeline_ModelToView()
        {
            // View 层绑定
            string viewGold = null;
            string viewLevel = null;
            _goldText.OnValueChanged += (_, v) => viewGold = v;
            _levelText.OnValueChanged += (_, v) => viewLevel = v;

            // 首次同步（模拟 Bind 的立即回调）
            viewGold = _goldText.Value;
            viewLevel = _levelText.Value;
            Assert.AreEqual("0", viewGold);
            Assert.AreEqual("Lv.1", viewLevel);

            // Model 变更
            _gold.Value = 1_500_000;
            _level.Value = 10;
            FlushScheduler();

            Assert.AreEqual("1.5M", viewGold);
            Assert.AreEqual("Lv.10", viewLevel);
        }

        [Test]
        public void MultipleModelChanges_MergeToSingleViewUpdate()
        {
            int goldUpdateCount = 0;
            _goldText.OnValueChanged += (_, _) => goldUpdateCount++;

            _gold.Value = 100;
            _gold.Value = 200;
            _gold.Value = 500;
            FlushScheduler();

            Assert.AreEqual(1, goldUpdateCount, "同帧多次 Model 变更应只触发一次 View 更新");
        }

        [Test]
        public void Dispose_BreaksPipeline()
        {
            string viewGold = null;
            _goldText.OnValueChanged += (_, v) => viewGold = v;

            _gold.Value = 100;
            FlushScheduler();
            Assert.AreEqual("100", viewGold);

            // 模拟 DataContext Dispose
            _gold.OnValueChanged = null;
            _goldText.OnValueChanged = null;

            _gold.Value = 999;
            FlushScheduler();

            Assert.AreEqual("100", viewGold, "Dispose 后数据流应断开");
        }

        private static string FormatGold(long gold)
        {
            if (gold >= 1_000_000_000) return $"{gold / 1_000_000_000.0:F1}B";
            if (gold >= 1_000_000) return $"{gold / 1_000_000.0:F1}M";
            if (gold >= 10_000) return $"{gold / 1_000.0:F1}K";
            return gold.ToString();
        }

        private readonly record struct ItemData(int Id, int Count);
    }
}
```

- [ ] **步骤 2：运行端到端测试**

在 Unity Editor Test Runner 中运行 `EndToEndTests`。
预期：全部 3 个测试通过。

- [ ] **步骤 3：运行全量 DataBinding 测试回归**

运行所有 DataBinding 目录下的测试：
- BindablePropertyTests (15)
- ObservableListTests (16)
- BatchSchedulerTests (6)
- DataContextTests (7)
- DataContextFactoryTests (3)
- UIBaseBindingTests (6)
- EndToEndTests (3)

预期：全部 56 个测试通过。

- [ ] **步骤 4：Commit**

```bash
git add Assets/TEngine/Tests/EditMode/DataBinding/EndToEndTests.cs
git commit -m "test: 添加端到端集成测试验证完整数据流

- Model→DataContext→View 全链路验证
- 同帧多次 Model 变更合并测试
- Dispose 断开数据流测试
- 全量 56 个 DataBinding 测试通过"
```

---

## 自检清单

### 1. 规格覆盖度

| 规格章节 | 对应任务 | 状态 |
|----------|---------|------|
| IBatchDirtyTarget | 任务 2 | ✅ |
| BindableProperty 实现 | 任务 2 | ✅ |
| ListChangeType + ListChangedEventArgs | 任务 3 | ✅ |
| ObservableList 实现 | 任务 4 | ✅ |
| BatchScheduler 实现 | 任务 5 | ✅ |
| DataContext 实现 | 任务 6 | ✅ |
| DataContextAttribute | 任务 6 | ✅ |
| DataContextFactory | 任务 6 | ✅ |
| Binding + UIBase 扩展 | 任务 7 | ✅ |
| UIWindow.InternalCreate 修改 | 任务 8 | ✅ |
| UIWindow.InternalDestroy 修改 | 任务 8 | ✅ |
| UIModule.OnWindowPrepare 修改 | 任务 8 | ✅ |
| UIWidget.OnDestroyWidget 修改 | 任务 8 | ✅ |
| 端到端验证 | 任务 9 | ✅ |

### 2. 占位符扫描

无 TODO/TBD/待定。所有代码步骤包含完整实现。

### 3. 类型一致性

- `IBatchDirtyTarget.FireCallback()` — BindableProperty 和 ObservableList 都用显式实现 `void IBatchDirtyTarget.FireCallback()` ✅
- `BatchScheduler.Instance.MarkDirty(this)` — 接受 `IBatchDirtyTarget` 参数，BindableProperty 和 ObservableList 都实现了此接口 ✅
- `UIBase.Bind<T>()` — 接受 `BindableProperty<T>` 和 `Action` 回调 ✅
- `DataContextFactory.CreateFor(Type)` — 返回 `DataContext`（可为 null） ✅
- `_dataContext` 字段类型 `GameLogic.DataBinding.DataContext` — 与 UIBase 新增的 `DataContext` 属性一致 ✅

### 4. 发现的问题（已内联修复）

- `DataContextFactory` 是 `internal` 的，测试可能无法访问 → 需要添加 `InternalsVisibleTo` 或改为 `public`，在任务 6 步骤 5 中已标注。
- `UIBase.cs` 可能缺少 `using System.Collections.Generic;` → 在任务 7 步骤 3 中已标注需确认。
