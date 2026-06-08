# TEngine 代码质量重构实施计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 修复 TEngine 框架中 21 个已确认缺陷（5 严重 + 8 中等 + 8 轻微），涵盖事件系统异常隔离、资源泄漏防护、UI 健壮性改进和代码规范统一。

**架构：** 4 个 Phase 分批修复，按 Phase 提交。Phase 1 修复致命 Bug 并补充单元测试；Phase 2 修复资源泄漏；Phase 3 做设计改进和代码清理。所有修改仅涉及内部实现，公共 API 签名零变更。

**技术栈：** Unity 2022+ / C# / NUnit（EditMode 测试）/ UniTask / YooAsset

**设计文档：** `docs/superpowers/specs/2026-06-08-tengine-quality-refactor-design.md`

---

## 文件结构

### 新建文件

| 文件 | 职责 |
|------|------|
| `Assets/TEngine/Tests/EditMode/Event/EventDelegateDataTests.cs` | 事件系统异常隔离测试 |
| `Assets/TEngine/Tests/EditMode/Event/TEngine.Tests.Event.asmdef` | 测试程序集定义 |
| `Assets/TEngine/Tests/EditMode/MemoryPool/MemoryPoolTests.cs` | 内存池计数器与异常类型测试 |
| `Assets/TEngine/Tests/EditMode/MemoryPool/TEngine.Tests.MemoryPool.asmdef` | 测试程序集定义 |

### 修改文件

| 文件 | 修改内容 | Phase |
|------|---------|-------|
| `Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs` | 6 个 Callback 方法增加异常隔离 | 1 |
| `Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs` | Unload 删除双重异步调用 | 1 |
| `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs` | 删除死代码 + async void 修复 | 1 |
| `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs` | UnloadAsset 空池告警 | 1 |
| `Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.cs` | 异常类型统一 | 1 |
| `Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.MemoryCollection.cs` | 计数器线程安全 | 1 |
| `Assets/TEngine/Runtime/AssemblyInfo.cs` | 增加 InternalsVisibleTo | 1 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs` | LoadFailed + 安全遍历 | 2+3 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs` | 僵尸防护 + 超时 + 反向遍历 + 实例字段 | 2+3 |
| `Assets/TEngine/Runtime/Module/AudioModule/AudioAgent.cs` | AssetHandle 追踪释放 | 2 |
| `Assets/GameScripts/HotFix/GameLogic/GameModule.cs` | fake null 修复 | 2 |
| `Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs` | InstanceID Key | 3 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/GameEventMgr.cs` | 删除死代码 | 3 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs` | UIModule.Resource → Instance | 3 |
| `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs` | UIModule.Resource → Instance | 3 |

---

## Phase 1: 安全加固

### 任务 1：搭建测试基础设施

**文件：**
- 修改：`Assets/TEngine/Runtime/AssemblyInfo.cs`
- 创建：`Assets/TEngine/Tests/EditMode/Event/TEngine.Tests.Event.asmdef`
- 创建：`Assets/TEngine/Tests/EditMode/MemoryPool/TEngine.Tests.MemoryPool.asmdef`

- [ ] **步骤 1：为测试程序集添加 InternalsVisibleTo**

在 `Assets/TEngine/Runtime/AssemblyInfo.cs` 中追加：

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TEngine.Tests.Timer")]
[assembly: InternalsVisibleTo("TEngine.Tests.Event")]
[assembly: InternalsVisibleTo("TEngine.Tests.MemoryPool")]
```

- [ ] **步骤 2：创建 Event 测试程序集定义**

创建 `Assets/TEngine/Tests/EditMode/Event/TEngine.Tests.Event.asmdef`：

```json
{
    "name": "TEngine.Tests.Event",
    "rootNamespace": "TEngine.Tests",
    "references": ["TEngine.Runtime"],
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

- [ ] **步骤 3：创建 MemoryPool 测试程序集定义**

创建 `Assets/TEngine/Tests/EditMode/MemoryPool/TEngine.Tests.MemoryPool.asmdef`：

```json
{
    "name": "TEngine.Tests.MemoryPool",
    "rootNamespace": "TEngine.Tests",
    "references": ["TEngine.Runtime"],
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

- [ ] **步骤 4：确认 Unity 编译通过**

打开 Unity Editor，等待脚本编译完成。确认 Console 无 error。

- [ ] **步骤 5：Commit**

```bash
git add Assets/TEngine/Runtime/AssemblyInfo.cs Assets/TEngine/Tests/EditMode/Event/TEngine.Tests.Event.asmdef Assets/TEngine/Tests/EditMode/MemoryPool/TEngine.Tests.MemoryPool.asmdef
git commit -m "test: 添加 Event 和 MemoryPool 测试程序集定义"
```

---

### 任务 2：EventDelegateData 异常隔离 — 编写失败测试

**文件：**
- 创建：`Assets/TEngine/Tests/EditMode/Event/EventDelegateDataTests.cs`

- [ ] **步骤 1：编写 EventDelegateDataTests**

创建 `Assets/TEngine/Tests/EditMode/Event/EventDelegateDataTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class EventDelegateDataTests
    {
        private const int TestEventId = 1;
        private EventDelegateData _data;

        [SetUp]
        public void SetUp()
        {
            _data = new EventDelegateData(TestEventId);
        }

        [Test]
        public void Callback_NormalHandlers_AllExecuted()
        {
            var callOrder = new List<int>();
            _data.AddHandler(new Action(() => callOrder.Add(1)));
            _data.AddHandler(new Action(() => callOrder.Add(2)));
            _data.AddHandler(new Action(() => callOrder.Add(3)));

            _data.Callback();

            Assert.AreEqual(3, callOrder.Count);
            Assert.AreEqual(1, callOrder[0]);
            Assert.AreEqual(2, callOrder[1]);
            Assert.AreEqual(3, callOrder[2]);
        }

        [Test]
        public void Callback_SecondHandlerThrows_ThirdStillExecutes()
        {
            var callOrder = new List<int>();
            _data.AddHandler(new Action(() => callOrder.Add(1)));
            _data.AddHandler(new Action(() => { callOrder.Add(2); throw new InvalidOperationException("test"); }));
            _data.AddHandler(new Action(() => callOrder.Add(3)));

            _data.Callback();

            // 关键断言：即使 handler2 抛异常，handler3 仍执行
            Assert.AreEqual(3, callOrder.Count);
            Assert.AreEqual(1, callOrder[0]);
            Assert.AreEqual(2, callOrder[1]);
            Assert.AreEqual(3, callOrder[2]);
        }

        [Test]
        public void Callback_AllHandlersThrow_CheckModifyStillRuns()
        {
            // 注册 handler
            _data.AddHandler(new Action(() => throw new Exception("err1")));
            _data.AddHandler(new Action(() => throw new Exception("err2")));

            // Callback 应该不抛异常（异常被内部捕获）
            Assert.DoesNotThrow(() => _data.Callback());

            // Callback 后应该能正常添加新 handler（证明 CheckModify 已执行，_isExecute 已重置）
            bool added = _data.AddHandler(new Action(() => { }));
            Assert.IsTrue(added, "AddHandler after exception-containing Callback should succeed");
        }

        [Test]
        public void Callback_AddHandlerDuringException_NewHandlerApplied()
        {
            var called = false;
            _data.AddHandler(new Action(() => throw new Exception("err")));

            // 在 Callback 执行过程中，通过另一个 handler 尝试添加新 handler
            // 这里用单 handler 场景，先 Callback 让异常发生
            _data.Callback();

            // 异常 Callback 后添加新 handler
            _data.AddHandler(new Action(() => called = true));

            // 再次 Callback，新 handler 应该被调用
            _data.Callback();
            Assert.IsTrue(called, "New handler added after exception-containing Callback should be invoked");
        }

        [Test]
        public void Callback_RmvHandlerDuringException_HandlerRemoved()
        {
            var handler1Called = false;
            Action handler1 = () => handler1Called = true;
            _data.AddHandler(handler1);

            // Callback 后删除 handler
            _data.Callback();
            Assert.IsTrue(handler1Called);

            // 删除后再次 Callback
            handler1Called = false;
            _data.RmvHandler(handler1);
            _data.Callback();
            Assert.IsFalse(handler1Called, "Removed handler should not be called");
        }

        [Test]
        public void Callback_EmptyList_NoException()
        {
            Assert.DoesNotThrow(() => _data.Callback());
        }

        [Test]
        public void Callback_WithArg_ExceptionIsolation()
        {
            var received = new List<string>();
            _data.AddHandler(new Action<string>(s => received.Add(s)));
            _data.AddHandler(new Action<string>(s => { received.Add(s); throw new Exception("err"); }));
            _data.AddHandler(new Action<string>(s => received.Add(s)));

            _data.Callback("test");

            Assert.AreEqual(3, received.Count);
            Assert.AreEqual("test", received[0]);
            Assert.AreEqual("test", received[1]);
            Assert.AreEqual("test", received[2]);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

在 Unity Test Runner 中运行 `EventDelegateDataTests`。
预期：`Callback_SecondHandlerThrows_ThirdStillExecutes` 和 `Callback_AllHandlersThrow_CheckModifyStillRuns` **FAIL**（当前代码中异常会中断循环，后续 handler 不执行）。

---

### 任务 3：EventDelegateData 异常隔离 — 实现

**文件：**
- 修改：`Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs`

- [ ] **步骤 1：修改 Callback() 无参方法**

将 `EventDelegateData.cs` 中的 `Callback()` 方法替换为：

```csharp
public void Callback()
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action action)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 2：修改 Callback<TArg1> 方法**

替换 `Callback<TArg1>` 为：

```csharp
public void Callback<TArg1>(TArg1 arg1)
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action<TArg1> action)
            {
                try
                {
                    action(arg1);
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 3：修改 Callback<TArg1, TArg2> 方法**

替换 `Callback<TArg1, TArg2>` 为：

```csharp
public void Callback<TArg1, TArg2>(TArg1 arg1, TArg2 arg2)
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action<TArg1, TArg2> action)
            {
                try
                {
                    action(arg1, arg2);
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 4：修改 Callback<TArg1, TArg2, TArg3> 方法**

替换 `Callback<TArg1, TArg2, TArg3>` 为：

```csharp
public void Callback<TArg1, TArg2, TArg3>(TArg1 arg1, TArg2 arg2, TArg3 arg3)
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action<TArg1, TArg2, TArg3> action)
            {
                try
                {
                    action(arg1, arg2, arg3);
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 5：修改 Callback<TArg1, TArg2, TArg3, TArg4> 方法**

替换 `Callback<TArg1, TArg2, TArg3, TArg4>` 为：

```csharp
public void Callback<TArg1, TArg2, TArg3, TArg4>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4)
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action<TArg1, TArg2, TArg3, TArg4> action)
            {
                try
                {
                    action(arg1, arg2, arg3, arg4);
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 6：修改 Callback<TArg1, TArg2, TArg3, TArg4, TArg5> 方法**

替换 `Callback<TArg1, TArg2, TArg3, TArg4, TArg5>` 为：

```csharp
public void Callback<TArg1, TArg2, TArg3, TArg4, TArg5>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5)
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action<TArg1, TArg2, TArg3, TArg4, TArg5> action)
            {
                try
                {
                    action(arg1, arg2, arg3, arg4, arg5);
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 7：修改 Callback<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> 方法**

替换 `Callback<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>` 为：

```csharp
public void Callback<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6>(TArg1 arg1, TArg2 arg2, TArg3 arg3, TArg4 arg4, TArg5 arg5, TArg6 arg6)
{
    _isExecute = true;
    try
    {
        for (var i = 0; i < _listExist.Count; i++)
        {
            var d = _listExist[i];
            if (d is Action<TArg1, TArg2, TArg3, TArg4, TArg5, TArg6> action)
            {
                try
                {
                    action(arg1, arg2, arg3, arg4, arg5, arg6);
                }
                catch (Exception e)
                {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.Message);
                }
            }
        }
    }
    finally
    {
        CheckModify();
    }
}
```

- [ ] **步骤 8：运行 EventDelegateDataTests 全部通过**

在 Unity Test Runner 中运行全部 8 个测试用例。
预期：全部 PASS。

- [ ] **步骤 9：Commit**

```bash
git add Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs Assets/TEngine/Tests/EditMode/Event/EventDelegateDataTests.cs
git commit -m "fix: EventDelegateData 异常隔离 — 内联 try-catch + try-finally 保证状态重置"
```

---

### 任务 4：SceneModule 双重 UnloadAsync 修复

**文件：**
- 修改：`Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs:378-384`

- [ ] **步骤 1：修复 Unload 方法**

找到 `SceneModule.cs` 中 `Unload` 方法内的以下代码（约 L378-384）：

```csharp
                subScene.UnloadAsync();
                subScene.UnloadAsync().Completed += @base =>
                {
                    _subScenes.Remove(location);
                    _handlingScene.Remove(location);
                    callBack?.Invoke();
                };
```

替换为：

```csharp
                var unloadHandle = subScene.UnloadAsync();
                unloadHandle.Completed += _ =>
                {
                    _subScenes.Remove(location);
                    _handlingScene.Remove(location);
                    callBack?.Invoke();
                };
```

- [ ] **步骤 2：确认编译通过**

Unity Editor 编译无 error。

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs
git commit -m "fix: SceneModule.Unload 修复双重 UnloadAsync 调用"
```

---

### 任务 5：ResourceModule 死代码删除 + async void 修复

**文件：**
- 修改：`Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`

- [ ] **步骤 1：删除 LoadAsset\<T\> 中重复的空检查**

找到 `LoadAsset<T>(string location, Action<T> callback, string packageName = "")` 方法（约 L769-780），将：

```csharp
            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Asset name is invalid.");
                return;
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new GameFrameworkException("Asset name is invalid.");
            }
```

替换为：

```csharp
            if (string.IsNullOrEmpty(location))
            {
                Log.Error("Asset name is invalid.");
                callback?.Invoke(default);
                return;
            }
```

- [ ] **步骤 2：修复 async void → UniTaskVoid**

找到 `LoadAssetAsync(string location, Type assetType, int priority, ...)` 方法（约 L935），将签名从：

```csharp
        public async void LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
```

改为：

```csharp
        public async UniTaskVoid LoadAssetAsync(string location, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData, string packageName = "")
```

确认文件顶部已有 `using Cysharp.Threading.Tasks;`（如没有则添加）。

- [ ] **步骤 3：确认编译通过**

- [ ] **步骤 4：Commit**

```bash
git add Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs
git commit -m "fix: ResourceModule 删除死代码 + async void → UniTaskVoid"
```

---

### 任务 6：ResourceModule.UnloadAsset 空池告警

**文件：**
- 修改：`Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs`

- [ ] **步骤 1：修改 UnloadAsset 方法**

将 `UnloadAsset` 方法（约 L47-53）替换为：

```csharp
        public void UnloadAsset(object asset)
        {
            if (_assetPool == null)
            {
                Log.Warning("ResourceModule: UnloadAsset called before object pool is initialized.");
                return;
            }

            if (asset == null)
            {
                Log.Warning("ResourceModule: UnloadAsset called with null asset.");
                return;
            }

            _assetPool.Unspawn(asset);
        }
```

- [ ] **步骤 2：确认编译通过**

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs
git commit -m "fix: ResourceModule.UnloadAsset 增加空池和空资源告警"
```

---

### 任务 7：异常类型统一 + MemoryPool 计数器安全 — 编写测试

**文件：**
- 创建：`Assets/TEngine/Tests/EditMode/MemoryPool/MemoryPoolTests.cs`

- [ ] **步骤 1：编写 MemoryPoolTests**

创建 `Assets/TEngine/Tests/EditMode/MemoryPool/MemoryPoolTests.cs`：

```csharp
using System;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class MemoryPoolTests
    {
        // 简单的 IMemory 实现用于测试
        private class TestMemory : IMemory
        {
            public bool Cleared { get; private set; }
            public void Clear() => Cleared = true;
        }

        [SetUp]
        public void SetUp()
        {
            MemoryPool.EnableStrictCheck = true;
        }

        [TearDown]
        public void TearDown()
        {
            MemoryPool.EnableStrictCheck = false;
            MemoryPool.ClearAll();
        }

        [Test]
        public void AcquireAndRelease_CountsMatch()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            Assert.IsNotNull(obj);
            // Acquire 后归还
            MemoryPool.Release(obj);
            // 再次获取应该复用
            var obj2 = MemoryPool.Acquire<TestMemory>();
            Assert.IsNotNull(obj2);
            Assert.IsTrue(obj2.Cleared, "Released object should have been cleared");
            MemoryPool.Release(obj2);
        }

        [Test]
        public void ReleaseNull_ThrowsGameFrameworkException()
        {
            var ex = Assert.Throws<GameFrameworkException>(() => MemoryPool.Release(null));
            Assert.That(ex.Message, Does.Contain("invalid"));
        }

        [Test]
        public void ReleaseTwice_StrictCheck_ThrowsException()
        {
            MemoryPool.EnableStrictCheck = true;
            var obj = MemoryPool.Acquire<TestMemory>();
            MemoryPool.Release(obj);
            // 严格模式下重复释放应抛异常
            Assert.Throws<Exception>(() => MemoryPool.Release(obj));
        }

        [Test]
        public void Acquire_NewObject_IsNotNull()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);
        }

        [Test]
        public void ClearAll_RemovesAllPools()
        {
            MemoryPool.Acquire<TestMemory>();
            MemoryPool.ClearAll();
            Assert.AreEqual(0, MemoryPool.Count);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证当前状态**

运行 `MemoryPoolTests`。预期大部分 PASS，但 `ReleaseNull_ThrowsGameFrameworkException` 可能 FAIL（当前抛 `Exception` 而非 `GameFrameworkException`）。

---

### 任务 8：异常类型统一 + MemoryPool 计数器安全 — 实现

**文件：**
- 修改：`Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.cs`
- 修改：`Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.MemoryCollection.cs`

- [ ] **步骤 1：统一 MemoryPool.cs 异常类型**

在 `MemoryPool.cs` 中，将所有 `throw new Exception(` 替换为 `throw new GameFrameworkException(`。

涉及位置（约 4 处）：
- L95: `throw new Exception("Memory is invalid.");`
- L173: `throw new Exception("Memory type is invalid.");`
- L176: `throw new Exception("Memory type is not a non-abstract class type.");`
- L183: `throw new Exception(string.Format("Memory type '{0}' is invalid.", ...));`

全部替换为：

```csharp
throw new GameFrameworkException("...");
```

- [ ] **步骤 2：统一 MemoryCollection.cs 异常类型**

在 `MemoryPool.MemoryCollection.cs` 中，将 `throw new Exception(` 替换为 `throw new GameFrameworkException(`。

涉及位置（约 4 处）：
- Acquire<T> 中的 `throw new Exception("Type is invalid.");`
- Acquire() 无参版本无 throw（使用 Activator）
- Release 中的 `throw new Exception("The memory has been released.");`

全部替换。

- [ ] **步骤 3：计数器移入 lock 块**

在 `MemoryPool.MemoryCollection.cs` 的 `Release` 方法中，将计数器操作移入 lock 块：

修改前：
```csharp
public void Release(IMemory memory)
{
    memory.Clear();
    lock (_memories)
    {
        if (_enableStrictCheck && _memories.Contains(memory))
        {
            throw new Exception("The memory has been released.");
        }
        _memories.Enqueue(memory);
    }
    _releaseMemoryCount++;
    _usingMemoryCount--;
}
```

修改后：
```csharp
public void Release(IMemory memory)
{
    memory.Clear();
    lock (_memories)
    {
        if (_enableStrictCheck && _memories.Contains(memory))
        {
            throw new GameFrameworkException("The memory has been released.");
        }
        _memories.Enqueue(memory);
        _releaseMemoryCount++;
        _usingMemoryCount--;
    }
}
```

- [ ] **步骤 4：运行 MemoryPoolTests 全部通过**

预期：全部 PASS。

- [ ] **步骤 5：确认全局无遗留 `throw new Exception(`**

搜索 `Assets/TEngine/Runtime/` 目录中 `throw new Exception(` 字符串。
预期：0 结果。

- [ ] **步骤 6：Commit**

```bash
git add Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.cs Assets/TEngine/Runtime/Core/MemoryPool/MemoryPool.MemoryCollection.cs Assets/TEngine/Tests/EditMode/MemoryPool/MemoryPoolTests.cs
git commit -m "fix: MemoryPool 统一 GameFrameworkException + 计数器线程安全 + 新增测试"
```

---

### 任务 9：Phase 1 验证提交

- [ ] **步骤 1：运行全部 EditMode 测试**

在 Unity Test Runner 中运行全部 EditMode 测试（包括已有的 Timer 测试和新增的 Event/MemoryPool 测试）。
预期：全部 PASS。

- [ ] **步骤 2：确认编译零 error、零新增 warning**

- [ ] **步骤 3：手动验证 Procedure 流程**

在 Unity Editor 中 Play，确认完整流程：启动 → 资源初始化 → 热更加载 → 进入游戏。

- [ ] **步骤 4：Phase 1 标签提交**

```bash
git tag phase1-safety-hardening
```

---

## Phase 2: 资源治理

### 任务 10：UIWindow 僵尸窗口防护

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`

- [ ] **步骤 1：添加 LoadFailed 属性**

在 `UIWindow.cs` 的 Properties region 中（约 L233 附近），在 `IsDestroyed` 字段后添加：

```csharp
        /// <summary>
        /// UI是否加载失败。
        /// </summary>
        internal bool LoadFailed { get; private set; } = false;
```

- [ ] **步骤 2：修改 Handle_Completed 方法**

找到 `Handle_Completed` 方法（约 L469-482），将 `panel == null` 的处理从：

```csharp
        private void Handle_Completed(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }
```

改为：

```csharp
        private void Handle_Completed(GameObject panel)
        {
            if (panel == null)
            {
                Log.Error("UIWindow: Failed to load panel resource for '{0}'.", WindowName);
                IsLoadDone = true;
                IsPrepare = false;
                LoadFailed = true;
                _prepareCallback?.Invoke(this);
                return;
            }
```

- [ ] **步骤 3：将 Handle_Completed 中的裸 Exception 改为 GameFrameworkException**

在同一方法中，将：

```csharp
                throw new Exception($"Not found {nameof(Canvas)} in panel {WindowName}");
```

改为：

```csharp
                throw new GameFrameworkException(Utility.Text.Format("Not found {0} in panel {1}", nameof(Canvas), WindowName));
```

确认文件顶部有 `using TEngine;`（`GameFrameworkException` 所在命名空间）。

- [ ] **步骤 4：确认编译通过**

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs
git commit -m "fix: UIWindow 加载失败标记 LoadFailed + 异常类型统一"
```

---

### 任务 11：UIModule 僵尸窗口清理 + 超时改进

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`

- [ ] **步骤 1：修改 OnWindowPrepare 方法**

找到 `OnWindowPrepare` 方法（约 L469-475），替换为：

```csharp
        private void OnWindowPrepare(UIWindow window)
        {
            if (window.LoadFailed)
            {
                Log.Warning("UIModule: Window '{0}' load failed, removing from stack.", window.WindowName);
                Pop(window);
                window.InternalDestroy(isShutDown: false);
                return;
            }

            window.InternalCreate();
            window.InternalRefresh();
            OnSortWindowDepth(window.WindowLayer);
            OnSetWindowVisible();
        }
```

- [ ] **步骤 2：添加超时常量并修改 ShowUIAwaitImp**

在 `UIModule.cs` 类体顶部（常量定义区域，约 L25 后）添加：

```csharp
        private const float UI_LOAD_TIMEOUT_SECONDS = 30f;
```

找到 `ShowUIAwaitImp<T>` 方法（约 L338-364），将整个方法替换为：

```csharp
        private async UniTask<T> ShowUIAwaitImp<T>(bool isAsync, params System.Object[] userDatas) where T : UIWindow, new()
        {
            Type type = typeof(T);
            string windowName = type.FullName;

            if (TryGetWindow(windowName, out UIWindow window, userDatas))
            {
                return window as T;
            }

            window = CreateInstance<T>();
            Push(window);
            window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(UI_LOAD_TIMEOUT_SECONDS));
            try
            {
                await UniTask.WaitUntil(
                    () => window.IsLoadDone || window.LoadFailed,
                    cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log.Error("UIModule: ShowUI await timeout for '{0}' ({1}s).", windowName, UI_LOAD_TIMEOUT_SECONDS);
                Pop(window);
                window.InternalDestroy();
                return null;
            }

            if (window.LoadFailed)
            {
                Log.Error("UIModule: ShowUI failed to load '{0}'.", windowName);
                return null;
            }

            return window as T;
        }
```

- [ ] **步骤 3：确认编译通过**

- [ ] **步骤 4：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs
git commit -m "fix: UIModule 僵尸窗口清理 + CancellationToken 超时替代 deltaTime"
```

---

### 任务 12：AudioAgent AssetHandle 追踪释放

**文件：**
- 修改：`Assets/TEngine/Runtime/Module/AudioModule/AudioAgent.cs`

- [ ] **步骤 1：添加 _currentHandle 字段**

在 `AudioAgent.cs` 的字段区域（类开头，约 L20 附近）添加：

```csharp
        private AssetHandle _currentHandle;
```

- [ ] **步骤 2：修改 OnAssetLoadComplete 释放旧句柄**

找到 `OnAssetLoadComplete` 方法（约 L313），在方法开头（`if (handle != null)` 之前）插入：

```csharp
            // 释放旧的非池化 handle
            if (_currentHandle != null && !_inPool)
            {
                _currentHandle.Dispose();
            }
            _currentHandle = handle;
```

注意：这段代码在 `if (_pendingLoad != null)` 分支中，`handle` 被 Dispose 后要跳过保存。调整后的 OnAssetLoadComplete 完整逻辑应该是：

```csharp
        void OnAssetLoadComplete(AssetHandle handle)
        {
            if (handle != null)
            {
                if (_inPool)
                {
                    _audioModule.AudioClipPool.TryAdd(handle.GetAssetInfo().Address, handle);
                }
            }

            if (_pendingLoad != null)
            {
                if (!_inPool && handle != null)
                {
                    handle.Dispose();
                }

                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                string path = _pendingLoad.Path;
                bool bAsync = _pendingLoad.BAsync;
                bool bInPool = _pendingLoad.BInPool;
                _pendingLoad = null;
                Load(path, bAsync, bInPool);
            }
            else if (handle != null)
            {
                if (_audioData != null)
                {
                    AudioData.DeAlloc(_audioData);
                    _audioData = null;
                }

                // 追踪当前 handle
                _currentHandle = handle;
                _audioData = AudioData.Alloc(handle, _inPool);

                _source.clip = handle.AssetObject as AudioClip;
                if (_source.clip != null)
                {
                    _source.Play();
                    _audioAgentRuntimeState = AudioAgentRuntimeState.Playing;
                }
                else
                {
                    _audioAgentRuntimeState = AudioAgentRuntimeState.End;
                }
            }
            else
            {
                _audioAgentRuntimeState = AudioAgentRuntimeState.End;
            }
        }
```

- [ ] **步骤 3：修改 Destroy 方法释放 handle**

找到 `Destroy` 方法（约 L406），替换为：

```csharp
        public void Destroy()
        {
            if (_transform != null)
            {
                Object.Destroy(_transform.gameObject);
            }

            if (_audioData != null)
            {
                AudioData.DeAlloc(_audioData);
                _audioData = null;
            }

            if (_currentHandle != null && !_inPool)
            {
                _currentHandle.Dispose();
                _currentHandle = null;
            }
        }
```

- [ ] **步骤 4：确认编译通过**

- [ ] **步骤 5：Commit**

```bash
git add Assets/TEngine/Runtime/Module/AudioModule/AudioAgent.cs
git commit -m "fix: AudioAgent 追踪 AssetHandle 生命周期并正确释放"
```

---

### 任务 13：GameModule fake null 修复

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/GameModule.cs`

- [ ] **步骤 1：修改 Base 属性**

找到 `Base` 属性（约 L20-23），将：

```csharp
        public static RootModule Base
        {
            get => _base ??= Object.FindObjectOfType<RootModule>();
            private set => _base = value;
        }

        private static RootModule _base;
```

替换为：

```csharp
        private static RootModule _base;

        public static RootModule Base
        {
            get
            {
                if (_base == null)
                {
                    _base = Object.FindObjectOfType<RootModule>();
                }
                return _base;
            }
            private set => _base = value;
        }
```

- [ ] **步骤 2：确认编译通过**

- [ ] **步骤 3：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/GameModule.cs
git commit -m "fix: GameModule.Base 去除 ??= 绕过 Unity fake null 检测"
```

---

### 任务 14：Phase 2 手动验证

- [ ] **步骤 1：编译零 error**

- [ ] **步骤 2：测试加载不存在的 UI Prefab**

在 GameApp.StartGameLogic 中临时改为加载一个不存在的窗口类型，Play 后确认：
- Console 无崩溃
- 无 NullReferenceException
- Log 输出 "load failed" 相关日志

测试完成后恢复原始代码。

- [ ] **步骤 3：测试 UI 快速开关**

快速连续调用 ShowUI/CloseUI 20 次，确认无异常。

- [ ] **步骤 4：测试音频切换**

连续播放不同音效，观察 Profiler 内存不持续增长。

- [ ] **步骤 5：Phase 2 标签**

```bash
git tag phase2-resource-governance
```

---

## Phase 3: 设计改进 + 代码规范

### 任务 15：UIWindow.InternalDestroy 安全遍历

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`

- [ ] **步骤 1：修改 InternalDestroy 方法**

找到 `InternalDestroy` 方法（约 L432-463），将遍历 ListChild 的部分：

```csharp
            for (int i = 0; i < ListChild.Count; i++)
            {
                var uiChild = ListChild[i];
                uiChild.CallDestroy();
                uiChild.OnDestroyWidget();
            }
```

替换为：

```csharp
            // 快照遍历：防止 OnDestroyWidget 修改正在遍历的集合
            var childrenSnapshot = ListChild.ToArray();
            ListChild.Clear();
            for (int i = 0; i < childrenSnapshot.Length; i++)
            {
                childrenSnapshot[i].CallDestroy();
                childrenSnapshot[i].OnDestroyWidget();
            }
```

- [ ] **步骤 2：确认编译通过**

- [ ] **步骤 3：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs
git commit -m "refactor: UIWindow.InternalDestroy ToArray 快照遍历防止集合修改异常"
```

---

### 任务 16：UIModule.CloseAll 反向遍历

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`

- [ ] **步骤 1：修改 CloseAll 方法**

找到 `CloseAll` 方法（约 L422-431），将：

```csharp
        public void CloseAll(bool isShutDown = false)
        {
            for (int i = 0; i < _uiStack.Count; i++)
            {
                UIWindow window = _uiStack[i];
                window.InternalDestroy(isShutDown);
            }

            _uiStack.Clear();
        }
```

替换为：

```csharp
        public void CloseAll(bool isShutDown = false)
        {
            for (int i = _uiStack.Count - 1; i >= 0; i--)
            {
                UIWindow window = _uiStack[i];
                window.InternalDestroy(isShutDown);
            }

            _uiStack.Clear();
        }
```

- [ ] **步骤 2：确认编译通过**

- [ ] **步骤 3：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs
git commit -m "refactor: UIModule.CloseAll 反向遍历防止嵌套销毁越界"
```

---

### 任务 17：SingletonSystem Key 改为 InstanceID

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs`

- [ ] **步骤 1：修改 _gameObjects 字典类型**

将（约 L72）：

```csharp
        private static readonly Dictionary<string, GameObject> _gameObjects = new Dictionary<string, GameObject>();
```

改为：

```csharp
        private static readonly Dictionary<int, GameObject> _gameObjects = new Dictionary<int, GameObject>();
```

- [ ] **步骤 2：修改 Retain(GameObject go, ...) 方法**

将（约 L84-96）：

```csharp
        public static void Retain(GameObject go, object singleton)
        {
            CheckInit();

            if (_gameObjects.TryAdd(go.name, go))
            {
```

改为：

```csharp
        public static void Retain(GameObject go, object singleton)
        {
            CheckInit();

            if (_gameObjects.TryAdd(go.GetInstanceID(), go))
            {
```

- [ ] **步骤 3：修改 Release(GameObject go, ...) 方法**

将（约 L138-146）：

```csharp
        public static void Release(GameObject go, object singleton)
        {
            if (_gameObjects != null && _gameObjects.ContainsKey(go.name))
            {
                _gameObjects.Remove(go.name);
```

改为：

```csharp
        public static void Release(GameObject go, object singleton)
        {
            if (_gameObjects != null && _gameObjects.ContainsKey(go.GetInstanceID()))
            {
                _gameObjects.Remove(go.GetInstanceID());
```

- [ ] **步骤 4：确认编译通过**

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/SingletonSystem/SingletonSystem.cs
git commit -m "refactor: SingletonSystem 使用 InstanceID 替代 name 做 Key"
```

---

### 任务 18：GameEventMgr 死代码清理

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/GameEventMgr.cs`

- [ ] **步骤 1：删除 _isInit 字段和无效检查**

将（约 L11-28）：

```csharp
        private readonly List<int> _listEventTypes;
        private readonly List<Delegate> _listHandles;
        private readonly bool _isInit = false;

        public GameEventMgr()
        {
            if (_isInit)
            {
                return;
            }

            _isInit = true;
            _listEventTypes = new List<int>();
            _listHandles = new List<Delegate>();
        }
```

替换为：

```csharp
        private readonly List<int> _listEventTypes = new List<int>();
        private readonly List<Delegate> _listHandles = new List<Delegate>();

        public GameEventMgr()
        {
        }
```

同时删除 `Clear()` 方法中的 `_isInit` 检查（约 L35-38）：

```csharp
        public void Clear()
        {
            if (!_isInit)
            {
                return;
            }
```

改为：

```csharp
        public void Clear()
        {
```

- [ ] **步骤 2：确认编译通过**

- [ ] **步骤 3：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/GameEventMgr.cs
git commit -m "refactor: GameEventMgr 删除无效 _isInit 死代码，列表初始化移至字段声明"
```

---

### 任务 19：UIModule._instanceRoot 和 Resource 改为实例字段

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs`
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs`
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs`
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs`

- [ ] **步骤 1：修改 UIModule.cs 字段和属性**

将（约 L19 和 L31-36）：

```csharp
        private static Transform _instanceRoot = null;
```

改为：

```csharp
        private Transform _instanceRoot = null;
```

将 `UIRoot` 属性：

```csharp
        public static Transform UIRoot => _instanceRoot;
```

改为：

```csharp
        public Transform UIRoot => _instanceRoot;
```

将（约 L31）：

```csharp
        public static IUIResourceLoader Resource;
```

改为：

```csharp
        public IUIResourceLoader Resource;
```

- [ ] **步骤 2：更新 UIWindow.cs 中的静态引用**

将 3 处 `UIModule.UIRoot` 替换为 `UIModule.Instance.UIRoot`（约 L327, L332, L338）。
将 3 处 `UIModule.Resource` 替换为 `UIModule.Instance.Resource`（同上位置）。

- [ ] **步骤 3：更新 UIWidget.cs 中的静态引用**

将 1 处 `UIModule.Resource` 替换为 `UIModule.Instance.Resource`（约 L166）。

- [ ] **步骤 4：更新 UIBase.cs 中的静态引用**

将 2 处 `UIModule.Resource` 替换为 `UIModule.Instance.Resource`（约 L406, L420）。

- [ ] **步骤 5：确认编译通过**

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIModule.cs Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWindow.cs Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIWidget.cs Assets/GameScripts/HotFix/GameLogic/Module/UIModule/UIBase.cs
git commit -m "refactor: UIModule._instanceRoot 和 Resource 从 static 改为实例字段"
```

---

### 任务 20：Phase 3 全量验证

- [ ] **步骤 1：运行全部 EditMode 测试**

预期：全部 PASS。

- [ ] **步骤 2：编译零 error、零新增 warning**

- [ ] **步骤 3：全局搜索验证**

```bash
grep -r "throw new Exception(" Assets/TEngine/Runtime/ Assets/GameScripts/
grep -r "async void" Assets/TEngine/Runtime/
grep -r "\.UnloadAsync()\.UnloadAsync()" Assets/TEngine/
```

预期：3 项搜索均返回 0 结果。

- [ ] **步骤 4：手动验证**

- 打开 5 个窗口 → CloseAll → 确认全部销毁
- 快速 ShowUI/CloseUI 循环测试
- Procedure 完整流程

- [ ] **步骤 5：Phase 3 标签**

```bash
git tag phase3-design-improvements
```

---

## 自检清单

### 规格覆盖度

| 规格 | 对应任务 |
|------|---------|
| event-safety: 异常隔离 | 任务 2-3 |
| event-safety: CheckModify 保证执行 | 任务 2-3 |
| event-safety: 异常日志含上下文 | 任务 3 |
| resource-lifecycle: SceneModule 单次卸载 | 任务 4 |
| resource-lifecycle: UnloadAsset 空池告警 | 任务 6 |
| resource-lifecycle: AudioAgent Handle 追踪 | 任务 12 |
| resource-lifecycle: 统一异常类型 | 任务 7-8 |
| resource-lifecycle: 无 async void | 任务 5 |
| resource-lifecycle: 计数器线程安全 | 任务 7-8 |
| ui-robustness: 僵尸窗口防护 | 任务 10-11 |
| ui-robustness: 安全集合遍历 | 任务 15 |
| ui-robustness: CloseAll 反向遍历 | 任务 16 |
| ui-robustness: 超时 CancellationToken | 任务 11 |
| ui-robustness: FindObjectOfType fake null | 任务 13 |
| ui-robustness: SingletonSystem InstanceID | 任务 17 |
| ui-robustness: GameEventMgr 死代码 | 任务 18 |

### 占位符扫描

无 TODO、TBD、"后续实现"、"类似任务 N"。每步都有具体代码和文件路径。

### 类型一致性

- `LoadFailed` 属性在任务 10 定义（`internal bool`），任务 11 使用 → 一致
- `_currentHandle` 字段在任务 12 定义（`AssetHandle`），任务 12 使用 → 一致
- `GameFrameworkException` 在任务 8 统一后，任务 10 使用 → 一致
- `UI_LOAD_TIMEOUT_SECONDS` 在任务 11 定义并使用 → 一致
