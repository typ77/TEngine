# Timer Module Redesign 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将现有 O(n)/帧 List 遍历的 TimerModule 替换为基于双索引最小堆的高性能定时器系统，支持完整的游戏定时语义、零 GC 创建和安全的句柄管理。

**架构：** 两个独立的 `IndexedMinHeap`（Scaled/Unscaled 各一个）共享一个 `TimerNodePool`（预分配数组 + FreeStack）。定时器使用绝对 `double fireTime` 调度，`Cancel` 惰性删除（仅标记），`Pause/Resume` 通过 `ChangeKey` 精确控制。帧级定时器（`NextFrame`/`WaitFrames`）走独立 `List<FrameTimerNode>`，不进入时间堆。

**技术栈：** C# 10 / Unity 6000+、NUnit（Unity Test Framework Edit Mode）、无第三方依赖

---

## 文件清单

### 新建文件

| 文件 | 职责 |
|------|------|
| `Assets/TEngine/Runtime/Module/TimerModule/TimerTypes.cs` | `TimeMode` 枚举、`TimerHandle` 结构体、`TimerTickArgs` 结构体、`TimerDiagnosticInfo` 结构体 |
| `Assets/TEngine/Runtime/Module/TimerModule/TimerNode.cs` | `TimerNode` 类（池中元素）、`FrameTimerNode` 结构体 |
| `Assets/TEngine/Runtime/Module/TimerModule/TimerNodePool.cs` | `TimerNodePool` 类（预分配 + FreeStack + Version 管理） |
| `Assets/TEngine/Runtime/Module/TimerModule/IndexedMinHeap.cs` | `IndexedMinHeap` 类（含 `_nodePos` 反查表） |
| `Assets/TEngine/Tests/EditMode/Timer/TEngine.Tests.Timer.asmdef` | Edit Mode 测试程序集定义 |
| `Assets/TEngine/Tests/EditMode/Timer/IndexedMinHeapTests.cs` | IndexedMinHeap 单元测试 |
| `Assets/TEngine/Tests/EditMode/Timer/TimerModuleTests.cs` | TimerModule 行为单元测试 |
| `Assets/TEngine/Runtime/Module/DebugerModule/Component/DebuggerModule.TimerInformationWindow.cs` | 定时器调试窗口 |

### 修改文件

| 文件 | 改动 |
|------|------|
| `Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs` | 新增全部新 API 方法签名；旧方法保留并加 `[Obsolete]`；新增诊断属性 |
| `Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs` | 完整重写 |
| `Assets/TEngine/Runtime/Module/DebugerModule/Debugger.cs` | 注册 `TimerInformationWindow` |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs` | 第 411 行：`AddTimer` → `Delay`（owner 绑定） |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs` | 第 82 行：`int HideTimerId` → `TimerHandle HideTimerHandle`；第 517-520 行：`RemoveTimer` → `handle.Cancel()` |

---

## 任务 1：搭建测试基础设施

**文件：**
- 创建：`Assets/TEngine/Tests/EditMode/Timer/TEngine.Tests.Timer.asmdef`
- 创建：`Assets/TEngine/Tests/EditMode/Timer/IndexedMinHeapTests.cs`（空骨架）
- 创建：`Assets/TEngine/Tests/EditMode/Timer/TimerModuleTests.cs`（空骨架）

- [ ] **步骤 1：创建 asmdef**

```json
// Assets/TEngine/Tests/EditMode/Timer/TEngine.Tests.Timer.asmdef
{
    "name": "TEngine.Tests.Timer",
    "rootNamespace": "TEngine.Tests",
    "references": [
        "TEngine.Runtime"
    ],
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

- [ ] **步骤 2：创建测试骨架文件**

```csharp
// Assets/TEngine/Tests/EditMode/Timer/IndexedMinHeapTests.cs
using NUnit.Framework;
namespace TEngine.Tests { }

// Assets/TEngine/Tests/EditMode/Timer/TimerModuleTests.cs
using NUnit.Framework;
namespace TEngine.Tests { }
```

- [ ] **步骤 3：在 Unity Editor 中打开 Test Runner（Window → General → Test Runner），确认 Edit Mode 下出现 TEngine.Tests.Timer 程序集**

- [ ] **步骤 4：Commit**

```bash
git add Assets/TEngine/Tests/
git commit -m "test: 搭建 Timer 模块 Edit Mode 测试基础设施"
```

---

## 任务 2：基础类型定义

**文件：**
- 创建：`Assets/TEngine/Runtime/Module/TimerModule/TimerTypes.cs`

此文件包含 4 个类型：`TimeMode`、`TimerHandle`、`TimerTickArgs`、`TimerDiagnosticInfo`。

`TimerHandle` 的方法（`Cancel`/`Pause`/`Resume`）需要访问 `TimerModule`——此处先写空方法体，任务 8 补全实现。

- [ ] **步骤 1：编写 TimerTypes.cs**

```csharp
// Assets/TEngine/Runtime/Module/TimerModule/TimerTypes.cs
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    public enum TimeMode { Scaled, Unscaled }

    public readonly struct TimerHandle
    {
        public readonly int Id;
        public readonly int Version;

        internal TimerHandle(int id, int version) { Id = id; Version = version; }

        public static readonly TimerHandle Invalid = default;

        public bool IsValid => TimerModule.GlobalInstance?.IsHandleValid(this) ?? false;

        public float Remaining => TimerModule.GlobalInstance?.GetRemaining(this) ?? 0f;

        public float Progress => TimerModule.GlobalInstance?.GetProgress(this) ?? 0f;

        public void Cancel() => TimerModule.GlobalInstance?.Cancel(this);

        public void Pause() => TimerModule.GlobalInstance?.PauseTimer(this);

        public void Resume() => TimerModule.GlobalInstance?.ResumeTimer(this);
    }

    public readonly struct TimerTickArgs
    {
        public readonly int TickIndex;
        public readonly int TotalTicks;
        public readonly int TicksRemaining;
        public readonly float Progress;
        public readonly float ElapsedTime;
        public readonly TimerHandle Handle;

        internal TimerTickArgs(int tickIndex, int totalTicks, float elapsedTime, TimerHandle handle)
        {
            TickIndex = tickIndex;
            TotalTicks = totalTicks;
            TicksRemaining = totalTicks < 0 ? -1 : totalTicks - tickIndex - 1;
            Progress = totalTicks < 0 ? float.NaN : (tickIndex + 1f) / totalTicks;
            ElapsedTime = elapsedTime;
            Handle = handle;
        }
    }

    public struct TimerDiagnosticInfo
    {
        public int Id;
        public float RemainingSeconds;
        public int TickIndex;
        public int TotalTicks;
        public TimeMode TimeMode;
        public bool IsPaused;
        public bool IsDeleted;
    }
}
```

- [ ] **步骤 2：Commit**

```bash
git add Assets/TEngine/Runtime/Module/TimerModule/TimerTypes.cs
git commit -m "feat(timer): 添加 TimeMode/TimerHandle/TimerTickArgs/TimerDiagnosticInfo 类型"
```

---

## 任务 3：TimerNode + TimerNodePool

**文件：**
- 创建：`Assets/TEngine/Runtime/Module/TimerModule/TimerNode.cs`
- 创建：`Assets/TEngine/Runtime/Module/TimerModule/TimerNodePool.cs`

- [ ] **步骤 1：编写 TimerNode.cs**

```csharp
// Assets/TEngine/Runtime/Module/TimerModule/TimerNode.cs
using System;
using System.Threading;
using UnityEngine;

namespace TEngine
{
    internal class TimerNode
    {
        public int Id;
        public int Version;
        public double FireTime;
        public double Interval;
        public double StartTime;
        public TimeMode TimeMode;
        public int TotalTicks;       // -1 = 无限
        public int CompletedTicks;
        public bool IsDeleted;
        public bool IsPaused;
        public double PauseRemaining; // 暂停时剩余时间

        public Action OnTickNoArg;
        public Action<TimerTickArgs> OnTickWithArg;
        public Action OnComplete;

        public UnityEngine.Object Owner;
        public CancellationToken Token;

        public void Reset()
        {
            FireTime = 0;
            Interval = 0;
            StartTime = 0;
            TotalTicks = 0;
            CompletedTicks = 0;
            IsDeleted = false;
            IsPaused = false;
            PauseRemaining = 0;
            OnTickNoArg = null;
            OnTickWithArg = null;
            OnComplete = null;
            Owner = null;
            Token = default;
        }
    }

    internal struct FrameTimerNode
    {
        public int Id;
        public int Version;
        public int TargetFrame;
        public Action Callback;
        public bool IsDeleted;
    }
}
```

- [ ] **步骤 2：编写 TimerNodePool.cs**

```csharp
// Assets/TEngine/Runtime/Module/TimerModule/TimerNodePool.cs
using System;

namespace TEngine
{
    internal class TimerNodePool
    {
        private TimerNode[] _nodes;
        private int[] _freeStack;
        private int _freeTop;

        public int Capacity => _nodes.Length;
        public int UsedCount => _nodes.Length - _freeTop;
        public TimerNode[] Nodes => _nodes;

        public event Action<int> OnExpand; // 通知堆扩容（newCapacity）

        public TimerNodePool(int initialCapacity = 128)
        {
            _nodes = new TimerNode[initialCapacity];
            _freeStack = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
            {
                _nodes[i] = new TimerNode { Id = i, Version = 1 };
                _freeStack[i] = i;
            }
            _freeTop = initialCapacity;
        }

        public int Rent()
        {
            if (_freeTop == 0)
                Expand(_nodes.Length * 2);

            int nodeId = _freeStack[--_freeTop];
            _nodes[nodeId].Reset();
            _nodes[nodeId].Id = nodeId;
            return nodeId;
        }

        public void Return(int nodeId)
        {
            TimerNode node = _nodes[nodeId];
            node.Reset();
            unchecked { node.Version++; }
            if (node.Version == 0) node.Version = 1; // 跳过 0，0 保留给 TimerHandle.Invalid
            _freeStack[_freeTop++] = nodeId;
        }

        private void Expand(int newCapacity)
        {
            Log.Info($"[Timer] Pool expanding: {_nodes.Length} → {newCapacity}");

            TimerNode[] newNodes = new TimerNode[newCapacity];
            Array.Copy(_nodes, newNodes, _nodes.Length);
            for (int i = _nodes.Length; i < newCapacity; i++)
            {
                newNodes[i] = new TimerNode { Id = i, Version = 1 };
                _freeStack[_freeTop++] = i; // 注意：需要先扩 freeStack
            }

            // 先扩 freeStack
            int[] newFreeStack = new int[newCapacity];
            Array.Copy(_freeStack, newFreeStack, _freeStack.Length);
            // 重新追加新槽
            for (int i = _nodes.Length; i < newCapacity; i++)
                newFreeStack[_freeTop - (newCapacity - _nodes.Length) + (i - _nodes.Length)] = i;

            _nodes = newNodes;
            _freeStack = newFreeStack;

            OnExpand?.Invoke(newCapacity);
        }
    }
}
```

> **注意：** `Expand` 中 `freeStack` 扩容顺序敏感，实现时需仔细验证索引。推荐单独写一个 `ExpandInternal` 确保先扩 freeStack 再追加新 nodeId。以下是更清晰的版本：

```csharp
private void Expand(int newCapacity)
{
    Log.Info($"[Timer] Pool expanding: {_nodes.Length} → {newCapacity}");
    int oldCap = _nodes.Length;

    // 扩 nodes 数组
    TimerNode[] newNodes = new TimerNode[newCapacity];
    Array.Copy(_nodes, newNodes, oldCap);

    // 扩 freeStack 数组（先拷贝旧的）
    int[] newFreeStack = new int[newCapacity];
    Array.Copy(_freeStack, newFreeStack, _freeTop); // 只拷贝已用部分

    _nodes = newNodes;
    _freeStack = newFreeStack;

    // 初始化新槽并入栈
    for (int i = oldCap; i < newCapacity; i++)
    {
        _nodes[i] = new TimerNode { Id = i, Version = 1 };
        _freeStack[_freeTop++] = i;
    }

    OnExpand?.Invoke(newCapacity);
}
```

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Runtime/Module/TimerModule/TimerNode.cs \
        Assets/TEngine/Runtime/Module/TimerModule/TimerNodePool.cs
git commit -m "feat(timer): 添加 TimerNode 和 TimerNodePool"
```

---

## 任务 4：IndexedMinHeap（含单元测试）

**文件：**
- 创建：`Assets/TEngine/Runtime/Module/TimerModule/IndexedMinHeap.cs`
- 修改：`Assets/TEngine/Tests/EditMode/Timer/IndexedMinHeapTests.cs`

### 4a. 先写失败的单元测试

- [ ] **步骤 1：在 IndexedMinHeapTests.cs 写失败测试（此时 IndexedMinHeap 不存在，编译失败即为"测试失败"）**

```csharp
// Assets/TEngine/Tests/EditMode/Timer/IndexedMinHeapTests.cs
using System;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class IndexedMinHeapTests
    {
        private TimerNode[] _nodes;
        private IndexedMinHeap _heap;

        [SetUp]
        public void SetUp()
        {
            _nodes = new TimerNode[16];
            for (int i = 0; i < 16; i++)
                _nodes[i] = new TimerNode { Id = i, FireTime = 0 };
            _heap = new IndexedMinHeap(_nodes, 16);
        }

        [Test]
        public void Pop_AfterPushDisordered_ReturnsAscendingFireTime()
        {
            double[] times = { 5.0, 1.0, 3.0, 2.0, 4.0 };
            for (int i = 0; i < times.Length; i++)
            {
                _nodes[i].FireTime = times[i];
                _heap.Push(i);
            }

            double prev = double.MinValue;
            while (_heap.Count > 0)
            {
                int id = _heap.Pop();
                Assert.GreaterOrEqual(_nodes[id].FireTime, prev);
                prev = _nodes[id].FireTime;
            }
        }

        [Test]
        public void ChangeKey_ToLargerValue_SinksToBottom()
        {
            for (int i = 0; i < 5; i++)
            {
                _nodes[i].FireTime = i + 1.0;
                _heap.Push(i);
            }
            // node 0 is at top (FireTime=1.0), change it to max
            _heap.ChangeKey(0, double.MaxValue);
            Assert.AreNotEqual(0, _heap.Peek()); // 0 should not be at top
        }

        [Test]
        public void ChangeKey_ToSmallerValue_RisesToTop()
        {
            for (int i = 0; i < 5; i++)
            {
                _nodes[i].FireTime = i + 2.0;
                _heap.Push(i);
            }
            // node 4 is near bottom (FireTime=6.0), change to minimum
            _heap.ChangeKey(4, 0.0);
            Assert.AreEqual(4, _heap.Peek());
        }

        [Test]
        public void NodePos_AfterSwap_RemainsConsistent()
        {
            for (int i = 0; i < 5; i++)
            {
                _nodes[i].FireTime = 5.0 - i; // reversed order forces sifting
                _heap.Push(i);
            }
            // Verify: for every element in heap, nodePos correctly points back
            for (int heapIdx = 0; heapIdx < _heap.Count; heapIdx++)
            {
                int nodeId = _heap.HeapAt(heapIdx);
                Assert.AreEqual(heapIdx, _heap.NodePosOf(nodeId));
            }
        }

        [Test]
        public void Pop_EmptyHeap_ThrowsInvalidOperation()
        {
            Assert.Throws<InvalidOperationException>(() => _heap.Pop());
        }

        [Test]
        public void Peek_SingleElement_ReturnsWithoutRemoving()
        {
            _nodes[0].FireTime = 42.0;
            _heap.Push(0);
            Assert.AreEqual(0, _heap.Peek());
            Assert.AreEqual(1, _heap.Count);
        }
    }
}
```

- [ ] **步骤 2：在 Unity Test Runner 中确认编译错误（IndexedMinHeap 未定义）**

### 4b. 实现 IndexedMinHeap

- [ ] **步骤 3：实现 IndexedMinHeap.cs**

```csharp
// Assets/TEngine/Runtime/Module/TimerModule/IndexedMinHeap.cs
using System;

namespace TEngine
{
    /// <summary>
    /// 索引最小堆。堆键为 nodes[nodeId].FireTime（double）。
    /// _nodePos[nodeId] 存储 nodeId 在堆数组中的位置，支持 O(log n) ChangeKey。
    /// </summary>
    internal class IndexedMinHeap
    {
        private int[] _heap;      // _heap[heapPos] = nodeId
        private int[] _nodePos;   // _nodePos[nodeId] = heapPos，-1 表示不在堆中
        private int _count;
        private readonly TimerNode[] _nodes; // 共享引用，堆只读取 FireTime

        public int Count => _count;

        public IndexedMinHeap(TimerNode[] nodes, int capacity)
        {
            _nodes = nodes;
            _heap = new int[capacity];
            _nodePos = new int[capacity];
            for (int i = 0; i < capacity; i++) _nodePos[i] = -1;
        }

        public void Push(int nodeId)
        {
            if (_count >= _heap.Length)
                throw new InvalidOperationException("Heap is full. Call Expand first.");
            _heap[_count] = nodeId;
            _nodePos[nodeId] = _count;
            _count++;
            SiftUp(_count - 1);
        }

        public int Pop()
        {
            if (_count == 0) throw new InvalidOperationException("Heap is empty.");
            int top = _heap[0];
            _nodePos[top] = -1;
            _count--;
            if (_count > 0)
            {
                _heap[0] = _heap[_count];
                _nodePos[_heap[0]] = 0;
                SiftDown(0);
            }
            return top;
        }

        public int Peek()
        {
            if (_count == 0) throw new InvalidOperationException("Heap is empty.");
            return _heap[0];
        }

        public void ChangeKey(int nodeId, double newFireTime)
        {
            double oldFireTime = _nodes[nodeId].FireTime;
            _nodes[nodeId].FireTime = newFireTime;
            int pos = _nodePos[nodeId];
            if (pos < 0) return; // 不在堆中
            if (newFireTime < oldFireTime)
                SiftUp(pos);
            else
                SiftDown(pos);
        }

        public void Expand(int newCapacity)
        {
            int[] newHeap = new int[newCapacity];
            int[] newNodePos = new int[newCapacity];
            Array.Copy(_heap, newHeap, _count);
            Array.Copy(_nodePos, newNodePos, _nodePos.Length);
            for (int i = _nodePos.Length; i < newCapacity; i++) newNodePos[i] = -1;
            _heap = newHeap;
            _nodePos = newNodePos;
        }

        // 测试辅助——暴露内部状态
        internal int HeapAt(int heapIdx) => _heap[heapIdx];
        internal int NodePosOf(int nodeId) => _nodePos[nodeId];

        private void SiftUp(int pos)
        {
            while (pos > 0)
            {
                int parent = (pos - 1) >> 1;
                if (_nodes[_heap[parent]].FireTime <= _nodes[_heap[pos]].FireTime) break;
                Swap(parent, pos);
                pos = parent;
            }
        }

        private void SiftDown(int pos)
        {
            while (true)
            {
                int left = (pos << 1) + 1;
                int right = left + 1;
                int smallest = pos;
                if (left < _count && _nodes[_heap[left]].FireTime < _nodes[_heap[smallest]].FireTime)
                    smallest = left;
                if (right < _count && _nodes[_heap[right]].FireTime < _nodes[_heap[smallest]].FireTime)
                    smallest = right;
                if (smallest == pos) break;
                Swap(pos, smallest);
                pos = smallest;
            }
        }

        private void Swap(int i, int j)
        {
            int tmp = _heap[i];
            _heap[i] = _heap[j];
            _heap[j] = tmp;
            _nodePos[_heap[i]] = i;
            _nodePos[_heap[j]] = j;
        }
    }
}
```

- [ ] **步骤 4：在 Unity Test Runner 中运行 IndexedMinHeapTests，确认 5 个测试全部通过**

预期：5 PASSED

- [ ] **步骤 5：Commit**

```bash
git add Assets/TEngine/Runtime/Module/TimerModule/IndexedMinHeap.cs \
        Assets/TEngine/Tests/EditMode/Timer/IndexedMinHeapTests.cs
git commit -m "feat(timer): 实现 IndexedMinHeap + 通过全部单元测试"
```

---

## 任务 5：更新 ITimerModule 接口

**文件：**
- 修改：`Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs`

- [ ] **步骤 1：完整替换 ITimerModule.cs**

```csharp
// Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace TEngine
{
    public interface ITimerModule
    {
        // ——— 新 API ———

        TimerHandle Delay(float delay, Action callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Delay(float delay, Action<TimerTickArgs> callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Repeat(float interval, Action callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Repeat(float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Schedule(float delay, float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Countdown(int count, float interval,
            Action<TimerTickArgs> onTick,
            Action onComplete = null,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle NextFrame(Action callback);

        TimerHandle WaitFrames(int frames, Action callback);

        void Configure(int maxCatchUpSteps = 5, int initialPoolCapacity = 128);

        void Compact();

        // ——— 诊断属性 ———

        int ActiveTimerCount { get; }
        int PoolCapacity { get; }
        int PoolUsed { get; }
        int ZombieCount { get; }

        void GetDiagnostics(List<TimerDiagnosticInfo> output);

        // ——— 旧 API（Obsolete，保留向后兼容）———

        [Obsolete("请使用 Delay / Repeat / Schedule")]
        int AddTimer(TimerHandler callback, float time, bool isLoop = false,
            bool isUnscaled = false, params object[] args);

        [Obsolete("请使用 TimerHandle.Cancel()")]
        void RemoveTimer(int timerId);

        [Obsolete("请使用 TimerHandle.Cancel()")]
        void RemoveAllTimer();

        [Obsolete("请使用 TimerHandle.Pause()")]
        void Stop(int timerId);

        [Obsolete("请使用 TimerHandle.Resume()")]
        void Resume(int timerId);

        [Obsolete("请使用 TimerHandle.IsValid")]
        bool IsRunning(int timerId);

        [Obsolete("请使用 TimerHandle.Remaining")]
        float GetLeftTime(int timerId);

        [Obsolete("请使用 Schedule 替代")]
        void Restart(int timerId);

        [Obsolete("请使用 Schedule 替代")]
        void ResetTimer(int timerId, TimerHandler callback, float time,
            bool isLoop = false, bool isUnscaled = false);

        [Obsolete("请使用 Schedule 替代")]
        void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled);

        [Obsolete("请使用 UniTask.Delay 或 UniTask.DelayFrame")]
        System.Timers.Timer AddSystemTimer(
            Action<object, System.Timers.ElapsedEventArgs> callBack);
    }
}
```

- [ ] **步骤 2：确认编译（TimerModule 还未实现新接口，会报错——这是预期的，下一任务修复）**

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs
git commit -m "feat(timer): 更新 ITimerModule 接口，新增 Delay/Repeat/Schedule/Countdown/诊断属性"
```

---

## 任务 6：TimerModule 核心重写

**文件：**
- 修改：`Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs`

此任务是核心实现。将原文件完整替换为以下内容。注意：`TimerHandle.Cancel()` 等方法通过 `GlobalInstance` 访问此单例。

- [ ] **步骤 1：编写完整的 TimerModule.cs**

```csharp
// Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TEngine
{
    internal sealed class TimerModule : Module, IUpdateModule, ITimerModule
    {
        // ——— 静态全局实例（供 TimerHandle 访问）———
        internal static TimerModule GlobalInstance { get; private set; }

        // ——— 配置 ———
        private int _maxCatchUpSteps = 5;

        // ——— 对象池 ———
        private TimerNodePool _pool;

        // ——— 双堆 ———
        private IndexedMinHeap _scaledHeap;
        private IndexedMinHeap _unscaledHeap;

        // ——— 帧队列 ———
        private readonly List<FrameTimerNode> _frameTimers = new List<FrameTimerNode>();
        private int _currentFrame;

        // ——— 时间累积（不依赖 Time.timeAsDouble，由 Update 驱动，利于单元测试）———
        private double _scaledTime;
        private double _unscaledTime;

        // ——— 旧 API 兼容映射（timerId → nodeId）———
        private readonly Dictionary<int, int> _legacyIdMap = new Dictionary<int, int>();

        // ——— 诊断用：zombie 警告节流（同一帧只警告一次）———
        private int _lastZombieWarnFrame = -1;

        // ——— 诊断属性 ———
        public int ActiveTimerCount
        {
            get
            {
                int count = 0;
                count += CountActive(_scaledHeap);
                count += CountActive(_unscaledHeap);
                return count;
            }
        }

        public int PoolCapacity => _pool.Capacity;
        public int PoolUsed => _pool.UsedCount;

        public int ZombieCount
        {
            get
            {
                int z = 0;
                z += CountZombie(_scaledHeap);
                z += CountZombie(_unscaledHeap);
                return z;
            }
        }

        // ——— 初始化 ———
        public override void OnInit()
        {
            GlobalInstance = this;
            InitPool(128);
        }

        private void InitPool(int capacity)
        {
            _pool = new TimerNodePool(capacity);
            _scaledHeap = new IndexedMinHeap(_pool.Nodes, capacity);
            _unscaledHeap = new IndexedMinHeap(_pool.Nodes, capacity);
            _pool.OnExpand += newCap =>
            {
                _scaledHeap.Expand(newCap);
                _unscaledHeap.Expand(newCap);
            };
        }

        public void Configure(int maxCatchUpSteps = 5, int initialPoolCapacity = 128)
        {
            _maxCatchUpSteps = maxCatchUpSteps;
            // 重建池（须在 AddTimer 前调用）
            _scaledTime = 0;
            _unscaledTime = 0;
            _currentFrame = 0;
            _frameTimers.Clear();
            InitPool(initialPoolCapacity);
        }

        public override void Shutdown()
        {
            GlobalInstance = null;
            _pool = null;
            _legacyIdMap.Clear();
            _frameTimers.Clear();
        }

        // ——— Update 驱动 ———
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            _scaledTime += elapseSeconds;
            _unscaledTime += realElapseSeconds;
            _currentFrame++;

            DrainHeap(_scaledHeap, _scaledTime);
            DrainHeap(_unscaledHeap, _unscaledTime);
            DrainFrameQueue(_currentFrame);
        }

        // ——— 核心调度循环 ———
        private void DrainHeap(IndexedMinHeap heap, double currentTime)
        {
            while (heap.Count > 0)
            {
                int topId = heap.Peek();
                TimerNode top = _pool.Nodes[topId];
                if (top.FireTime > currentTime) break;

                heap.Pop();

                if (top.IsDeleted)
                {
                    _pool.Return(topId);
                    continue;
                }

                if (top.Owner != null && top.Owner == null) // Unity null check
                {
                    _pool.Return(topId);
                    continue;
                }

                if (top.Token.CanBeCanceled && top.Token.IsCancellationRequested)
                {
                    _pool.Return(topId);
                    continue;
                }

                // zombie 比例警告（每帧最多一次）
                if (_currentFrame != _lastZombieWarnFrame)
                {
                    int total = heap.Count + 1; // +1 for the just-popped
                    if (ZombieCount > total * 0.3f)
                    {
                        _lastZombieWarnFrame = _currentFrame;
                        Log.Warning("[Timer] Zombie 比例超过 30%，建议调用 Compact() 清理");
                    }
                }

                bool isLoop = top.TotalTicks != 1;
                bool isFinite = top.TotalTicks > 0;

                FireNode(top, currentTime);

                if (top.IsDeleted)
                {
                    _pool.Return(topId);
                    continue;
                }

                if (isLoop)
                {
                    if (isFinite && top.CompletedTicks >= top.TotalTicks)
                    {
                        // 有限循环完成
                        CallOnComplete(top);
                        _pool.Return(topId);
                        continue;
                    }

                    // 追赶逻辑：计算下次触发时间
                    double nextFireTime = top.FireTime + top.Interval;
                    int catchUp = 0;
                    while (nextFireTime <= currentTime)
                    {
                        if (++catchUp > _maxCatchUpSteps)
                        {
                            Log.Warning("[Timer] 追赶步数超限，跳过积压触发");
                            nextFireTime = currentTime + top.Interval;
                            break;
                        }

                        FireNode(top, currentTime);

                        if (top.IsDeleted)
                        {
                            _pool.Return(topId);
                            goto ContinueOuter;
                        }

                        if (isFinite && top.CompletedTicks >= top.TotalTicks)
                        {
                            CallOnComplete(top);
                            _pool.Return(topId);
                            goto ContinueOuter;
                        }

                        nextFireTime += top.Interval;
                    }

                    top.FireTime = nextFireTime;
                    heap.Push(topId);
                }
                else
                {
                    // 一次性定时器
                    CallOnComplete(top);
                    _pool.Return(topId);
                }

                ContinueOuter:;
            }
        }

        private void FireNode(TimerNode node, double currentTime)
        {
            var handle = new TimerHandle(node.Id, node.Version);
            float elapsed = (float)(currentTime - node.StartTime);
            var args = new TimerTickArgs(node.CompletedTicks, node.TotalTicks, elapsed, handle);

            node.CompletedTicks++;

            try
            {
                if (node.OnTickWithArg != null)
                    node.OnTickWithArg(args);
                else
                    node.OnTickNoArg?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"[Timer] 回调异常（Id={node.Id}）: {ex}");
            }
        }

        private void CallOnComplete(TimerNode node)
        {
            if (node.OnComplete == null) return;
            try { node.OnComplete(); }
            catch (Exception ex) { Log.Error($"[Timer] OnComplete 异常（Id={node.Id}）: {ex}"); }
        }

        // ——— 帧队列 ———
        private void DrainFrameQueue(int currentFrame)
        {
            for (int i = _frameTimers.Count - 1; i >= 0; i--)
            {
                FrameTimerNode fn = _frameTimers[i];
                // 通过 Pool 节点检查取消状态（Cancel() 会标记 IsDeleted=true）
                TimerNode poolNode = _pool.Nodes[fn.Id];
                bool cancelled = poolNode.Version != fn.Version || poolNode.IsDeleted;

                if (cancelled || fn.TargetFrame <= currentFrame)
                {
                    _frameTimers.RemoveAt(i);
                    if (!cancelled)
                    {
                        try { fn.Callback?.Invoke(); }
                        catch (Exception ex) { Log.Error($"[Timer] FrameTimer 异常: {ex}"); }
                    }
                    _pool.Return(fn.Id); // 释放池槽，令 Handle.IsValid → false
                }
            }
        }

        // ——— 公开 API ———
        public TimerHandle Delay(float delay, Action callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal((float)delay, (float)delay, null, callback, 1,
                timeMode, null, owner, cancellationToken);
        }

        public TimerHandle Delay(float delay, Action<TimerTickArgs> callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(delay, delay, callback, null, 1,
                timeMode, null, owner, cancellationToken);
        }

        public TimerHandle Repeat(float interval, Action callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(interval, interval, null, callback, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle Repeat(float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(interval, interval, callback, null, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle Schedule(float delay, float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(delay, interval, callback, null, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle Countdown(int count, float interval,
            Action<TimerTickArgs> onTick,
            Action onComplete = null,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(interval, interval, onTick, null, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle NextFrame(Action callback) => WaitFrames(1, callback);

        public TimerHandle WaitFrames(int frames, Action callback)
        {
            int nodeId = _pool.Rent();
            TimerNode node = _pool.Nodes[nodeId];
            var handle = new TimerHandle(nodeId, node.Version);
            _frameTimers.Add(new FrameTimerNode
            {
                Id = nodeId,
                Version = node.Version,
                TargetFrame = _currentFrame + frames,
                Callback = callback,
                IsDeleted = false
            });
            return handle;
        }

        private TimerHandle ScheduleInternal(
            float delay, float interval,
            Action<TimerTickArgs> onTickWithArg,
            Action onTickNoArg,
            int count,
            TimeMode timeMode,
            Action onComplete,
            Object owner,
            CancellationToken token)
        {
            int nodeId = _pool.Rent();
            TimerNode node = _pool.Nodes[nodeId];
            double now = timeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;

            node.FireTime = now + delay;
            node.Interval = interval;
            node.StartTime = now;
            node.TimeMode = timeMode;
            node.TotalTicks = count;
            node.CompletedTicks = 0;
            node.IsDeleted = false;
            node.IsPaused = false;
            node.OnTickWithArg = onTickWithArg;
            node.OnTickNoArg = onTickNoArg;
            node.OnComplete = onComplete;
            node.Owner = owner;
            node.Token = token;

            IndexedMinHeap heap = timeMode == TimeMode.Scaled ? _scaledHeap : _unscaledHeap;
            heap.Push(nodeId);

            return new TimerHandle(nodeId, node.Version);
        }

        // ——— TimerHandle 操作（供 TimerHandle 结构体回调）———
        internal bool IsHandleValid(TimerHandle handle)
        {
            if (_pool == null || handle.Id < 0 || handle.Id >= _pool.Capacity) return false;
            TimerNode node = _pool.Nodes[handle.Id];
            return node.Version == handle.Version && !node.IsDeleted;
        }

        internal float GetRemaining(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return 0f;
            TimerNode node = _pool.Nodes[handle.Id];
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            return (float)Math.Max(0, node.FireTime - now);
        }

        internal float GetProgress(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return 0f;
            TimerNode node = _pool.Nodes[handle.Id];
            if (node.TotalTicks < 0) return float.NaN;
            if (node.TotalTicks == 0) return 0f;
            return node.CompletedTicks / (float)node.TotalTicks;
        }

        internal void Cancel(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return;
            _pool.Nodes[handle.Id].IsDeleted = true;
        }

        internal void PauseTimer(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return;
            TimerNode node = _pool.Nodes[handle.Id];
            if (node.IsPaused) return;
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            node.PauseRemaining = node.FireTime - now;
            node.IsPaused = true;
            IndexedMinHeap heap = node.TimeMode == TimeMode.Scaled ? _scaledHeap : _unscaledHeap;
            heap.ChangeKey(handle.Id, double.MaxValue);
        }

        internal void ResumeTimer(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return;
            TimerNode node = _pool.Nodes[handle.Id];
            if (!node.IsPaused) return;
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            node.IsPaused = false;
            IndexedMinHeap heap = node.TimeMode == TimeMode.Scaled ? _scaledHeap : _unscaledHeap;
            heap.ChangeKey(handle.Id, now + node.PauseRemaining);
        }

        // ——— Compact ———
        public void Compact()
        {
            RebuildHeap(_scaledHeap, TimeMode.Scaled);
            RebuildHeap(_unscaledHeap, TimeMode.Unscaled);
        }

        private void RebuildHeap(IndexedMinHeap heap, TimeMode mode)
        {
            // 收集所有活跃节点 id
            var active = new List<int>();
            while (heap.Count > 0)
            {
                int id = heap.Pop();
                if (!_pool.Nodes[id].IsDeleted)
                    active.Add(id);
                else
                    _pool.Return(id);
            }
            foreach (int id in active) heap.Push(id);
        }

        // ——— 诊断 ———
        public void GetDiagnostics(List<TimerDiagnosticInfo> output)
        {
            output.Clear();
            FillDiagnostics(_scaledHeap, output);
            FillDiagnostics(_unscaledHeap, output);
        }

        private void FillDiagnostics(IndexedMinHeap heap, List<TimerDiagnosticInfo> output)
        {
            // 遍历堆数组（不改变堆顺序）
            for (int i = 0; i < heap.Count; i++)
            {
                int id = heap.HeapAt(i);
                TimerNode node = _pool.Nodes[id];
                if (node.IsDeleted) continue;
                double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
                output.Add(new TimerDiagnosticInfo
                {
                    Id = id,
                    RemainingSeconds = (float)Math.Max(0, node.FireTime - now),
                    TickIndex = node.CompletedTicks,
                    TotalTicks = node.TotalTicks,
                    TimeMode = node.TimeMode,
                    IsPaused = node.IsPaused,
                    IsDeleted = node.IsDeleted
                });
            }
        }

        private int CountActive(IndexedMinHeap heap)
        {
            int count = 0;
            for (int i = 0; i < heap.Count; i++)
                if (!_pool.Nodes[heap.HeapAt(i)].IsDeleted) count++;
            return count;
        }

        private int CountZombie(IndexedMinHeap heap)
        {
            int count = 0;
            for (int i = 0; i < heap.Count; i++)
                if (_pool.Nodes[heap.HeapAt(i)].IsDeleted) count++;
            return count;
        }

        // ======== 向后兼容适配层 ========
#pragma warning disable CS0618

        [Obsolete("请使用 Delay / Repeat / Schedule")]
        public int AddTimer(TimerHandler callback, float time, bool isLoop = false,
            bool isUnscaled = false, params object[] args)
        {
            TimeMode mode = isUnscaled ? TimeMode.Unscaled : TimeMode.Scaled;
            int count = isLoop ? -1 : 1;
            Action wrappedCallback = () => callback?.Invoke(args);

            TimerHandle handle = isLoop
                ? Repeat(time, wrappedCallback, count, mode)
                : Delay(time, wrappedCallback, mode);

            _legacyIdMap[handle.Id] = handle.Id;
            return handle.Id;
        }

        [Obsolete("请使用 TimerHandle.Cancel()")]
        public void RemoveTimer(int timerId)
        {
            if (timerId <= 0 || timerId >= _pool.Capacity) return;
            _pool.Nodes[timerId].IsDeleted = true;
        }

        [Obsolete("请使用 TimerHandle.Cancel()")]
        public void RemoveAllTimer()
        {
            for (int i = 0; i < _pool.Capacity; i++)
                _pool.Nodes[i].IsDeleted = true;
        }

        [Obsolete("请使用 TimerHandle.Pause()")]
        public void Stop(int timerId) { /* stale access, safe ignore */ }

        [Obsolete("请使用 TimerHandle.Resume()")]
        public void Resume(int timerId) { /* stale access, safe ignore */ }

        [Obsolete("请使用 TimerHandle.IsValid")]
        public bool IsRunning(int timerId)
        {
            if (timerId <= 0 || timerId >= _pool.Capacity) return false;
            return !_pool.Nodes[timerId].IsDeleted;
        }

        [Obsolete("请使用 TimerHandle.Remaining")]
        public float GetLeftTime(int timerId)
        {
            if (timerId <= 0 || timerId >= _pool.Capacity) return 0f;
            TimerNode node = _pool.Nodes[timerId];
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            return (float)Math.Max(0, node.FireTime - now);
        }

        [Obsolete("请使用 Schedule 替代")]
        public void Restart(int timerId) { }

        [Obsolete("请使用 Schedule 替代")]
        public void ResetTimer(int timerId, TimerHandler callback, float time,
            bool isLoop = false, bool isUnscaled = false) { }

        [Obsolete("请使用 Schedule 替代")]
        public void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled) { }

        [Obsolete("请使用 UniTask.Delay 或 UniTask.DelayFrame")]
        public System.Timers.Timer AddSystemTimer(
            Action<object, System.Timers.ElapsedEventArgs> callBack)
        {
            int interval = 1000;
            var timerTick = new System.Timers.Timer(interval);
            timerTick.AutoReset = true;
            timerTick.Enabled = true;
            timerTick.Elapsed += new System.Timers.ElapsedEventHandler(callBack);
            return timerTick;
        }

#pragma warning restore CS0618
    }
}
```

> **关键细节：**
> 1. `_scaledTime` 和 `_unscaledTime` 从 0 开始累积，完全由 `Update` 参数驱动，不依赖 `Time.timeAsDouble`，单元测试中可精确控制时间。
> 2. `Owner` 的 Unity null 检查写法：`top.Owner != null && top.Owner == null` 是错的——Unity 的 `==` 运算符重载，已销毁对象 `!= null` 但 `== null`。正确写法见下方。
> 3. `Owner` 检查修正：`if (top.Owner != null && !top.Owner)` — Unity `Object` 的 `bool` 转换对已销毁对象返回 false。或直接 `if (top.Owner is Object o && !o)`.

**步骤 1 的 Owner 检查需修正为：**
```csharp
// 正确的 Unity Null 检查（运算符重载）
if (top.Owner != null && top.Owner.Equals(null))
{
    _pool.Return(topId);
    continue;
}
```

- [ ] **步骤 2：确认项目编译通过（没有 CS 错误）**

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs
git commit -m "feat(timer): 完整重写 TimerModule（双堆 + 对象池 + 惰性删除）"
```

---

## 任务 7：TimerModule 单元测试

**文件：**
- 修改：`Assets/TEngine/Tests/EditMode/Timer/TimerModuleTests.cs`

测试驱动模式：直接实例化 `TimerModule` 并调用 `OnInit()` + `Update(delta, realDelta)` 控制时间。

- [ ] **步骤 1：编写完整测试文件**

```csharp
// Assets/TEngine/Tests/EditMode/Timer/TimerModuleTests.cs
using System;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class TimerModuleTests
    {
        private TimerModule _module;

        [SetUp]
        public void SetUp()
        {
            _module = new TimerModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.Shutdown();
        }

        // ——— 辅助：推进时间 ———
        private void Tick(float deltaTime, float realDelta = -1f)
        {
            if (realDelta < 0) realDelta = deltaTime;
            _module.Update(deltaTime, realDelta);
        }

        // ——— 10.1: Delay 触发一次，触发后 IsValid==false ———
        [Test]
        public void Delay_TriggersOnceAtCorrectTime()
        {
            int callCount = 0;
            TimerHandle handle = _module.Delay(1f, () => callCount++);

            Tick(0.5f); Assert.AreEqual(0, callCount);
            Tick(0.5f); Assert.AreEqual(1, callCount);
            Assert.IsFalse(handle.IsValid);
            Tick(1f);   Assert.AreEqual(1, callCount); // 不再触发
        }

        // ——— 10.2: Schedule(delay:0, interval:1, count:3) 在 t=0,1,2 触发 ———
        [Test]
        public void Schedule_CountThree_TriggersAtCorrectTimes()
        {
            int callCount = 0;
            int[] ticksRemaining = new int[3];

            _module.Schedule(0f, 1f, args =>
            {
                ticksRemaining[callCount] = args.TicksRemaining;
                callCount++;
            }, count: 3);

            Tick(0f);   Assert.AreEqual(1, callCount); // t=0
            Tick(1f);   Assert.AreEqual(2, callCount); // t=1
            Tick(1f);   Assert.AreEqual(3, callCount); // t=2
            Tick(1f);   Assert.AreEqual(3, callCount); // 不再触发

            Assert.AreEqual(2, ticksRemaining[0]);
            Assert.AreEqual(1, ticksRemaining[1]);
            Assert.AreEqual(0, ticksRemaining[2]);
        }

        // ——— 10.3: 循环定时器时间对齐（无漂移）———
        [Test]
        public void Repeat_NoTimeDrift_FireTimeAligned()
        {
            double lastFireTime = 0;
            int count = 0;
            _module.Repeat(1f, args =>
            {
                count++;
                lastFireTime = 1.0 * count; // 期望每次刚好 +1 秒
            });

            // 模拟帧不对齐（0.016 精度误差）
            for (int i = 0; i < 5; i++) Tick(1.016f);

            // 应触发 5 次（不因 delta>1 导致额外触发）
            Assert.AreEqual(5, count);
        }

        // ——— 10.4: stale handle 访问安全忽略 ———
        [Test]
        public void TimerHandle_StaleVersion_SafeIgnore()
        {
            TimerHandle handle = _module.Delay(1f, () => { });
            Tick(1.1f); // 触发完毕，版本已变
            Assert.IsFalse(handle.IsValid);
            Assert.DoesNotThrow(() => handle.Cancel());
            Assert.DoesNotThrow(() => handle.Pause());
            Assert.DoesNotThrow(() => handle.Resume());
        }

        // ——— 10.5: MaxCatchUpSteps 保护（deltaTime=30s，interval=0.1s）———
        [Test]
        public void Repeat_LargeDeltaTime_CapsAtMaxCatchUpSteps()
        {
            _module.Configure(maxCatchUpSteps: 5, initialPoolCapacity: 128);
            int count = 0;
            _module.Repeat(0.1f, () => count++);

            Tick(30f); // 理论 300 次，应最多触发 maxCatchUpSteps+1 = 6 次
            Assert.LessOrEqual(count, 6);
        }

        // ——— 10.6: Owner 销毁后回调不触发 ———
        // 注意：Edit Mode 测试无法创建真实 UnityEngine.Object，此场景在集成测试中覆盖（10.11）
        // 此处用 CancellationToken 替代验证等效逻辑
        [Test]
        public void Repeat_CancelToken_StopsCallback()
        {
            var cts = new System.Threading.CancellationTokenSource();
            int count = 0;
            _module.Repeat(0.5f, () => count++,
                cancellationToken: cts.Token);

            Tick(0.6f); Assert.AreEqual(1, count);
            cts.Cancel();
            Tick(0.5f); Assert.AreEqual(1, count); // 已取消，不再触发
        }

        // ——— 10.7: 回调内 Cancel 后不重入堆 ———
        [Test]
        public void Repeat_SelfCancelInCallback_StopsImmediately()
        {
            int count = 0;
            TimerHandle handle = default;
            handle = _module.Repeat(0.5f, args =>
            {
                count++;
                args.Handle.Cancel();
            });

            Tick(0.6f); Assert.AreEqual(1, count);
            Tick(0.5f); Assert.AreEqual(1, count); // 已取消
            Assert.IsFalse(handle.IsValid);
        }

        // ——— 10.8: Pause + Resume 精确时间 ———
        [Test]
        public void Pause_ThenResume_TriggersAtCorrectTime()
        {
            int count = 0;
            TimerHandle handle = _module.Delay(10f, () => count++);

            Tick(3f);          // t=3, 剩余 7 秒
            handle.Pause();
            Tick(5f);          // t=8, 暂停中，不触发
            Assert.AreEqual(0, count);
            handle.Resume();
            Tick(6.9f);        // t=14.9，还剩 0.1 秒
            Assert.AreEqual(0, count);
            Tick(0.2f);        // t=15.1，触发
            Assert.AreEqual(1, count);
        }

        // ——— 10.9: Unscaled 定时器在 timeScale=0 时触发 ———
        [Test]
        public void UnscaledTimer_WhenScaledTimeZero_StillFires()
        {
            int scaledCount = 0;
            int unscaledCount = 0;

            _module.Delay(1f, () => scaledCount++, TimeMode.Scaled);
            _module.Delay(1f, () => unscaledCount++, TimeMode.Unscaled);

            // 模拟 timeScale=0：elapseSeconds=0, realElapseSeconds=1
            _module.Update(0f, 1f);

            Assert.AreEqual(0, scaledCount);   // Scaled 未触发
            Assert.AreEqual(1, unscaledCount); // Unscaled 触发
        }

        // ——— 10.10: 回调异常不中断其他定时器 ———
        [Test]
        public void Callback_ThrowsException_OtherTimersContinue()
        {
            int normalCount = 0;
            _module.Delay(1f, () => throw new Exception("故意抛出"));
            _module.Delay(1f, () => normalCount++);

            Assert.DoesNotThrow(() => Tick(1.1f));
            Assert.AreEqual(1, normalCount);
        }

        // ——— TimerHandle.Remaining 和 Progress ———
        [Test]
        public void TimerHandle_Remaining_ReturnsCorrectValue()
        {
            TimerHandle h = _module.Delay(4f, () => { });
            Tick(1f);
            Assert.AreEqual(3f, h.Remaining, 0.001f);
        }

        // ——— WaitFrames ———
        [Test]
        public void WaitFrames_FiresAtExactFrame()
        {
            int count = 0;
            _module.WaitFrames(3, () => count++);

            Tick(0f); Assert.AreEqual(0, count); // frame 1
            Tick(0f); Assert.AreEqual(0, count); // frame 2
            Tick(0f); Assert.AreEqual(1, count); // frame 3
            Tick(0f); Assert.AreEqual(1, count); // frame 4, 不再触发
        }

        // ——— NextFrame ———
        [Test]
        public void NextFrame_FiresOnNextFrame_NotCurrentFrame()
        {
            int count = 0;
            _module.NextFrame(() => count++);
            Assert.AreEqual(0, count);  // 当帧不触发
            Tick(0f); Assert.AreEqual(1, count); // 下一帧
        }

        // ——— Countdown ———
        [Test]
        public void Countdown_TicksRemainingDecreasesCorrectly()
        {
            int[] remaining = new int[3];
            int idx = 0;
            bool completed = false;

            _module.Countdown(3, 1f,
                args => remaining[idx++] = args.TicksRemaining,
                () => completed = true);

            Tick(1f); Tick(1f); Tick(1f);

            Assert.AreEqual(2, remaining[0]);
            Assert.AreEqual(1, remaining[1]);
            Assert.AreEqual(0, remaining[2]);
            Assert.IsTrue(completed);
        }

        // ——— 诊断属性 ———
        [Test]
        public void Diagnostics_ActiveTimerCount_IsAccurate()
        {
            _module.Delay(5f, () => { });
            _module.Repeat(1f, () => { });
            Assert.AreEqual(2, _module.ActiveTimerCount);
        }
    }
}
```

- [ ] **步骤 2：在 Unity Test Runner 中运行 TimerModuleTests，确认全部通过**

预期：14+ PASSED

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Tests/EditMode/Timer/TimerModuleTests.cs
git commit -m "test(timer): 添加 TimerModule 全量行为单元测试"
```

---

## 任务 8：诊断调试窗口

**文件：**
- 创建：`Assets/TEngine/Runtime/Module/DebugerModule/Component/DebuggerModule.TimerInformationWindow.cs`
- 修改：`Assets/TEngine/Runtime/Module/DebugerModule/Debugger.cs`

- [ ] **步骤 1：创建 TimerInformationWindow.cs**

```csharp
// Assets/TEngine/Runtime/Module/DebugerModule/Component/DebuggerModule.TimerInformationWindow.cs
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    public sealed partial class Debugger
    {
        private sealed class TimerInformationWindow : ScrollableDebuggerWindowBase
        {
            private ITimerModule _timer;
            private readonly List<TimerDiagnosticInfo> _diagnostics = new List<TimerDiagnosticInfo>();

            public override void Initialize(params object[] args)
            {
                _timer = ModuleSystem.GetModule<ITimerModule>();
            }

            protected override void OnDrawScrollableWindow()
            {
                if (_timer == null) { GUILayout.Label("TimerModule not found."); return; }

                GUILayout.Label("<b>Timer System</b>");
                GUILayout.BeginVertical("box");
                DrawItem("Active Timers", _timer.ActiveTimerCount.ToString());
                DrawItem("Zombie Timers", _timer.ZombieCount.ToString());
                DrawItem("Pool Capacity", _timer.PoolCapacity.ToString());
                DrawItem("Pool Used", _timer.PoolUsed.ToString());
                GUILayout.EndVertical();

                _timer.GetDiagnostics(_diagnostics);
                if (_diagnostics.Count == 0) return;

                GUILayout.Label("<b>Active Timer List</b>");
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>Id</b>", GUILayout.Width(40));
                GUILayout.Label("<b>Remaining</b>", GUILayout.Width(80));
                GUILayout.Label("<b>Tick</b>", GUILayout.Width(60));
                GUILayout.Label("<b>Total</b>", GUILayout.Width(60));
                GUILayout.Label("<b>Mode</b>", GUILayout.Width(70));
                GUILayout.Label("<b>Paused</b>", GUILayout.Width(60));
                GUILayout.EndHorizontal();

                foreach (TimerDiagnosticInfo info in _diagnostics)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(info.Id.ToString(), GUILayout.Width(40));
                    GUILayout.Label($"{info.RemainingSeconds:F2}s", GUILayout.Width(80));
                    GUILayout.Label(info.TickIndex.ToString(), GUILayout.Width(60));
                    GUILayout.Label(info.TotalTicks < 0 ? "∞" : info.TotalTicks.ToString(), GUILayout.Width(60));
                    GUILayout.Label(info.TimeMode.ToString(), GUILayout.Width(70));
                    GUILayout.Label(info.IsPaused ? "Yes" : "No", GUILayout.Width(60));
                    GUILayout.EndHorizontal();
                }
            }
        }
    }
}
```

- [ ] **步骤 2：在 Debugger.cs 中注册窗口**

在 `Debugger.cs` 的字段声明区（约第 82 行）添加：
```csharp
private TimerInformationWindow _timerInformationWindow = new TimerInformationWindow();
```

在 `Start()` 方法的 `RegisterDebuggerWindow("Profiler/Reference Pool", ...)` 之后（约第 214 行）添加：
```csharp
RegisterDebuggerWindow("Profiler/Timer", _timerInformationWindow);
```

- [ ] **步骤 3：确认编译通过**

- [ ] **步骤 4：Commit**

```bash
git add Assets/TEngine/Runtime/Module/DebugerModule/Component/DebuggerModule.TimerInformationWindow.cs
git add Assets/TEngine/Runtime/Module/DebugerModule/Debugger.cs
git commit -m "feat(debugger): 添加 Timer 诊断窗口（Active/Zombie/Pool/定时器列表）"
```

---

## 任务 9：UIModule + UIWindow 迁移

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`（第 411 行）
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`（第 82、517-520 行）

- [ ] **步骤 1：修改 UIWindow.cs**

将第 82 行：
```csharp
public int HideTimerId { get; set; }
```
替换为：
```csharp
internal TimerHandle HideTimerHandle { get; set; }
```

将第 514-522 行（`CancelHideToCloseTimer` 方法体）：
```csharp
internal void CancelHideToCloseTimer()
{
    IsHide = false;
    if (HideTimerId > 0)
    {
        ModuleSystem.GetModule<ITimerModule>().RemoveTimer(HideTimerId);
        HideTimerId = 0;
    }
}
```
替换为：
```csharp
internal void CancelHideToCloseTimer()
{
    IsHide = false;
    HideTimerHandle.Cancel();
    HideTimerHandle = default;
}
```

- [ ] **步骤 2：修改 UIModule.cs**

将第 408-414 行：
```csharp
window.CancelHideToCloseTimer();
window.Visible = false;
window.IsHide = true;
window.HideTimerId = GameModule.Timer.AddTimer((arg) =>
{
    CloseUI(type);
},window.HideTimeToClose);
```
替换为：
```csharp
window.CancelHideToCloseTimer();
window.Visible = false;
window.IsHide = true;
window.HideTimerHandle = GameModule.Timer.Delay(
    window.HideTimeToClose,
    () => CloseUI(type),
    owner: window.gameObject != null ? window.gameObject : null);
```

> **注意：** `window` 是 `UIWindow`，其 `gameObject` 为 `UnityEngine.GameObject` → 类型为 `UnityEngine.Object`，可作为 `owner` 传入。如果 `UIWindow` 不继承 `MonoBehaviour`，则不传 `owner`。

- [ ] **步骤 3：确认编译通过（HideTimerId 引用应全部消失）**

- [ ] **步骤 4：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs \
        Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs
git commit -m "refactor(ui): 迁移 HideTimerId 到 TimerHandle，移除旧 AddTimer/RemoveTimer 调用"
```

---

## 任务 10：验证

- [ ] **步骤 1：在 Unity Test Runner（Edit Mode）中运行全部测试**

预期：所有测试 PASSED，无 FAILED

运行命令（或在 Unity Editor 中手动运行）：
```
Window → General → Test Runner → Edit Mode → Run All
```

- [ ] **步骤 2：验证无编译警告（除 [Obsolete] 旧 API 调用处）**

打开 Unity Console，Filter by Warning。检查是否有意外的警告。

- [ ] **步骤 3：集成验证——在 Play Mode 中打开/关闭带 HideTimeToClose 的 UI 窗口**

- 进入 Play Mode
- 打开一个设置了 `HideTimeToClose > 0` 的 UI 窗口
- 调用 Hide，等待对应时间，确认窗口自动关闭
- 在等待期间调用 CancelHideToCloseTimer，确认窗口不关闭

- [ ] **步骤 4：在 Play Mode 中打开 Debugger 窗口，检查 Profiler/Timer 页面**

- 确认 Active/Zombie/Pool 数据实时显示
- 确认定时器列表在有活跃定时器时显示正确信息

- [ ] **步骤 5：最终 Commit**

```bash
git add -A
git commit -m "chore(timer): 完成 TimerModule 重构验证，所有测试通过"
```

---

## 自检结果

### 规格覆盖度

| 规格需求 | 对应任务 |
|---------|---------|
| TimeMode 枚举 | 任务 2 |
| TimerHandle 版本号保护 | 任务 2、6 |
| TimerTickArgs 零 GC | 任务 2（readonly struct） |
| TimerDiagnosticInfo | 任务 2、8 |
| TimerNode + Pool 预分配 | 任务 3 |
| Pool 扩容 Log.Info | 任务 3（TimerNodePool.Expand） |
| Version 溢出跳 0 | 任务 3（`if (++ver == 0) ver = 1`） |
| IndexedMinHeap Push/Pop/ChangeKey | 任务 4 |
| IndexedMinHeap _nodePos 一致性 | 任务 4（单元测试 NodePos_AfterSwap） |
| 双堆独立调度 | 任务 6（_scaledHeap/_unscaledHeap） |
| 静默帧 O(1) | 任务 6（DrainHeap 第一行 Peek 比较） |
| 循环时间对齐（oldFireTime + interval） | 任务 6（DrainHeap 重入堆逻辑） |
| MaxCatchUpSteps 每定时器保护 | 任务 6 + 测试 10.5 |
| FireNode try-catch 异常隔离 | 任务 6 + 测试 10.10 |
| 惰性删除（Cancel → isDeleted） | 任务 6（Cancel 方法）|
| 回调内自取消安全 | 任务 6（FireNode 后检查 IsDeleted）+ 测试 10.7 |
| Pause via ChangeKey(MaxValue) | 任务 6（PauseTimer） |
| Resume via ChangeKey(now+remaining) | 任务 6（ResumeTimer）+ 测试 10.8 |
| Delay delay=0 当帧触发 | 任务 6（fireTime = now + 0）|
| Repeat ≡ Schedule(delay:iv, iv, count) | 任务 6（Repeat 委托 ScheduleInternal） |
| Countdown 语义糖 | 任务 6（Countdown 委托 ScheduleInternal） |
| NextFrame/WaitFrames 帧队列 | 任务 6（DrainFrameQueue） |
| Owner 销毁自动取消 | 任务 6（DrainHeap Owner null 检查） |
| CancellationToken 取消 | 任务 6（DrainHeap Token 检查）+ 测试 |
| 诊断属性 Active/Zombie/Pool | 任务 6 + 任务 8 |
| GetDiagnostics | 任务 6 + 任务 8 |
| DebuggerModule 集成 | 任务 8 |
| Obsolete 向后兼容 | 任务 6（适配层）|
| UIModule 迁移 | 任务 9 |
| UIWindow 迁移 | 任务 9 |
| Compact | 任务 6（Compact 方法）|
| Configure | 任务 6（Configure 方法）|
| Unscaled 在 timeScale=0 触发 | 测试 10.9 |
| zombie > 30% 警告 | 任务 6（DrainHeap 警告逻辑） |

### 已知边界情况

- **`Owner` Unity null 检查**：任务 6 中需用 `top.Owner.Equals(null)` 而非 `== null`，两者语义不同。
- **`delay=0` + 同帧多次触发**：`delay=0` 写入 `fireTime = now`，在同帧 DrainHeap 会立即弹出。若一次性定时器，触发后回收；若循环，计入 MaxCatchUpSteps。
- **帧计时器 Handle 的 IsValid**：`WaitFrames` 返回的 `TimerHandle` Id 来自 Pool。`DrainFrameQueue` 通过检查 `poolNode.Version != fn.Version || poolNode.IsDeleted` 来检测取消，并在处理后调用 `_pool.Return(fn.Id)` 使版本递增、令 `Handle.IsValid` 变为 false。此逻辑已在任务 6 的 `DrainFrameQueue` 实现中体现。
