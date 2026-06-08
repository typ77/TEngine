# TEngine 代码质量重构设计文档

> 日期: 2026-06-08
> 范围: 全量修复 5 严重 + 8 中等 + 8 轻微缺陷
> 策略: 按 Phase 分批提交，补充单元测试，公共 API 零变更

---

## 1. 测试基础设施

### 1.1 测试文件结构

```
Assets/TEngine/Tests/
└── EditMode/
    ├── Timer/                           ← 已有
    │   ├── IndexedMinHeapTests.cs
    │   └── TimerModuleTests.cs
    ├── Event/
    │   └── EventDelegateDataTests.cs    ← 新增
    ├── MemoryPool/
    │   └── MemoryPoolTests.cs           ← 新增
    └── Resource/
        └── ResourceModulePoolTests.cs   ← 新增
```

### 1.2 可测试性分析

| 模块 | 依赖 Unity 运行时 | 测试策略 |
|------|-------------------|---------|
| `EventDelegateData` | ❌ 纯 C# | NUnit EditMode 直接测试 |
| `MemoryPool` + `MemoryCollection` | ❌ 纯 C# | NUnit EditMode 直接测试 |
| `ResourceModule.Pool` (UnloadAsset guard) | ⚠️ 引用接口 | 仅测试 null guard 逻辑 |
| `SceneModule.Unload` | ✅ YooAsset | 手动验证 |
| `UIWindow` / `UIModule` | ✅ Unity UI | 手动验证 |
| `AudioAgent` | ✅ AudioSource | 手动验证 |
| `GameModule` | ✅ FindObjectOfType | 手动验证 |

不引入额外 Mock 框架，使用 Unity 内置 NUnit。

---

## 2. Phase 1: 安全加固

### 2.1 EventDelegateData 异常隔离（C-02）

**文件**: `Assets/TEngine/Runtime/Core/GameEvent/EventDelegateData.cs`

**方案**: 内联 try-catch（放弃 SafeInvoke 辅助方法，避免闭包委托分配导致的 GC 压力）

**修改模式**（以无参 Callback 为例，6 个重载结构相同）：

```csharp
public void Callback() {
    _isExecute = true;
    try {
        for (var i = 0; i < _listExist.Count; i++) {
            var d = _listExist[i];
            if (d is Action action) {
                try {
                    action();
                } catch (Exception e) {
                    Log.Error("Event handler exception. EventId: {0}, Error: {1}",
                        RuntimeId.ToString(_eventType), e.ToString());
                }
            }
        }
    } finally {
        CheckModify();
    }
}
```

**设计决策**: 内联 try-catch vs SafeInvoke 辅助方法
- 选择内联：零 GC 分配（SafeInvoke 的 `() => action()` 闭包每次分配一个委托）
- 事件系统是热路径，正常路径不应有额外内存开销
- 代价是 6 个方法各自包含相同的 try-catch 结构

**测试用例** (`EventDelegateDataTests.cs`):

| 用例 | 验证点 |
|------|--------|
| `Callback_NormalHandlers_AllExecuted` | 3 个 handler 都被调用 |
| `Callback_SecondHandlerThrows_ThirdStillExecutes` | 异常不中断链 |
| `Callback_AllHandlersThrow_CheckModifyStillRuns` | `_isExecute` 重置为 false |
| `Callback_AddHandlerDuringException_NewHandlerApplied` | 挂起增删正确刷新 |
| `Callback_RmvHandlerDuringException_HandlerRemoved` | 同上验证删除 |
| `Callback_EmptyList_NoException` | 无 handler 时正常 |

### 2.2 SceneModule.Unload 双重卸载修复（C-01）

**文件**: `Assets/TEngine/Runtime/Module/SceneModule/SceneModule.cs:378-384`

**修改**: 删除第一次多余的 `UnloadAsync()` 调用，保存第二次返回的句柄：

```csharp
// 修改前
subScene.UnloadAsync();                           // ← 多余
subScene.UnloadAsync().Completed += @base => { ... };

// 修改后
var unloadHandle = subScene.UnloadAsync();        // ← 只调一次
unloadHandle.Completed += _ => {
    _subScenes.Remove(location);
    _handlingScene.Remove(location);
    callBack?.Invoke();
};
```

**验证**: 手动（需 YooAsset 运行时）

### 2.3 ResourceModule 死代码 + async void（C-04, C-05）

**文件**: `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.cs`

**C-04** L769-780: 删除重复的第二个 `string.IsNullOrEmpty` 检查，在第一个检查中增加 `callback?.Invoke(default)` 走失败路径。

**C-05** L935: `async void` → `async UniTaskVoid`；将 `throw GameFrameworkException` 改为走 `LoadAssetFailureCallback` 失败回调路径。

### 2.4 UnloadAsset 空池告警（M-01）

**文件**: `Assets/TEngine/Runtime/Module/ResourceModule/ResourceModule.Pool.cs`

```csharp
public void UnloadAsset(object asset) {
    if (_assetPool == null) {
        Log.Warning("ResourceModule: UnloadAsset called before object pool is initialized.");
        return;
    }
    if (asset == null) {
        Log.Warning("ResourceModule: UnloadAsset called with null asset.");
        return;
    }
    _assetPool.Unspawn(asset);
}
```

### 2.5 异常类型统一 + MemoryPool 计数器安全

**文件**: `MemoryPool.cs`, `MemoryPool.MemoryCollection.cs`

- 全局 `throw new Exception(` → `throw new GameFrameworkException(`
- `MemoryCollection.Release`: 将 `_releaseMemoryCount++` 和 `_usingMemoryCount--` 移入已有 lock 块

**测试用例** (`MemoryPoolTests.cs`):

| 用例 | 验证点 |
|------|--------|
| `AcquireAndRelease_CountsMatch` | 计数器准确 |
| `ReleaseNull_ThrowsGameFrameworkException` | 异常类型正确 |
| `ReleaseTwice_StrictCheck_ThrowsException` | 严格模式检测重复释放 |

### Phase 1 提交

```
fix: 事件系统异常隔离与场景/资源安全加固

- EventDelegateData: 内联 try-catch + try-finally 异常隔离
- SceneModule: 修复双重 UnloadAsync 调用
- ResourceModule: 删除死代码, async void → UniTaskVoid
- MemoryPool: 统一 GameFrameworkException, 计数器线程安全
- 新增测试: EventDelegateDataTests, MemoryPoolTests
```

---

## 3. Phase 2: 资源治理

### 3.1 僵尸窗口防护（M-03）

**文件**: `UIWindow.cs` + `UIModule.cs`

**数据流**:

```
Handle_Completed(panel)
       │
  panel == null?
   YES │     NO
       ▼      ▼
  SetFailed   正常初始化
       │
       ▼
  OnWindowPrepare(window)
       │
  LoadFailed?
  YES │    NO
      ▼    ▼
  Pop+Destroy  InternalCreate+Refresh
```

**UIWindow 新增字段**: `internal bool LoadFailed { get; private set; } = false;`

**UIWindow.Handle_Completed 修改** (panel == null 时):
```csharp
Log.Error("UIWindow: Failed to load panel for '{0}'.", WindowName);
IsLoadDone = true;
LoadFailed = true;
_prepareCallback?.Invoke(this);
return;
```

**UIModule.OnWindowPrepare 修改**:
```csharp
private void OnWindowPrepare(UIWindow window) {
    if (window.LoadFailed) {
        Log.Warning("UIModule: Window '{0}' load failed, removing.", window.WindowName);
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

### 3.2 ShowUIAwaitImp 超时改进（M-02）

**文件**: `UIModule.cs`

```csharp
private const float UI_LOAD_TIMEOUT_SECONDS = 30f;

private async UniTask<T> ShowUIAwaitImp<T>(bool isAsync, params System.Object[] userDatas)
    where T : UIWindow, new()
{
    Type type = typeof(T);
    string windowName = type.FullName;

    if (TryGetWindow(windowName, out UIWindow window, userDatas)) {
        return window as T;
    }

    window = CreateInstance<T>();
    Push(window);
    window.InternalLoad(window.AssetName, OnWindowPrepare, isAsync, userDatas).Forget();

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(UI_LOAD_TIMEOUT_SECONDS));
    try {
        await UniTask.WaitUntil(
            () => window.IsLoadDone || window.LoadFailed,
            cancellationToken: cts.Token);
    } catch (OperationCanceledException) {
        Log.Error("UIModule: ShowUI await timeout for '{0}' ({1}s).", windowName, UI_LOAD_TIMEOUT_SECONDS);
        Pop(window);
        window.InternalDestroy();
        return null;
    }

    if (window.LoadFailed) {
        Log.Error("UIModule: ShowUI failed to load '{0}'.", windowName);
        return null;
    }

    return window as T;
}
```

**行为变更**:

| 场景 | 修改前 | 修改后 |
|------|--------|--------|
| timeScale=0 时超时 | 永远不超时 | 30s 正确超时 |
| 超时后返回值 | 半初始化 window | null |
| 超时后窗口状态 | 僵尸在栈中 | 已 Pop + Destroy |

### 3.3 AudioAgent AssetHandle 泄漏修复（M-05）

**文件**: `AudioAgent.cs`

**新增字段**: `private AssetHandle _currentHandle;`

**OnAssetLoadComplete 修改**:
```csharp
// 在处理新 handle 前，释放旧的非池化 handle
if (_currentHandle != null && !_inPool) {
    _currentHandle.Dispose();
}
_currentHandle = handle;
```

**Destroy 修改**:
```csharp
public void Destroy() {
    if (_transform != null) {
        Object.Destroy(_transform.gameObject);
    }
    if (_audioData != null) {
        AudioData.DeAlloc(_audioData);
        _audioData = null;
    }
    if (_currentHandle != null && !_inPool) {
        _currentHandle.Dispose();
        _currentHandle = null;
    }
}
```

**注意**: `_inPool == true` 时不 Dispose（池化 handle 由 `AudioClipPool` 持有复用）。

### 3.4 GameModule fake null 修复（M-04）

**文件**: `GameModule.cs`

```csharp
// 修改前（??= 绕过 Unity == 重载）
get => _base ??= Object.FindObjectOfType<RootModule>();

// 修改后（显式 if 使用 Unity == 重载，覆盖 fake null）
private static RootModule _base;
public static RootModule Base {
    get {
        if (_base == null) {   // Unity 重载的 == 已处理 Destroy 后的对象
            _base = Object.FindObjectOfType<RootModule>();
        }
        return _base;
    }
    private set => _base = value;
}
```

### Phase 2 提交

```
fix: UI/音频资源泄漏防护与超时改进

- UIWindow: LoadFailed 标记 + UIModule 僵尸窗口自动清理
- UIModule: CancellationToken 替代 deltaTime 超时
- AudioAgent: AssetHandle 生命周期追踪与释放
- GameModule: FindObjectOfType Unity fake null 防护
```

---

## 4. Phase 3: 设计改进 + 代码规范

### 4.1 集合安全遍历（C-03）

**文件**: `UIWindow.cs` InternalDestroy

```csharp
// ToArray 快照 + 清空原始列表
var snapshot = ListChild.ToArray();
ListChild.Clear();
for (int i = 0; i < snapshot.Length; i++) {
    snapshot[i].CallDestroy();
    snapshot[i].OnDestroyWidget();
}
```

**开销**: 典型窗口 3~10 个 Widget，ToArray 分配极小数组。低频操作，GC 可忽略。

### 4.2 CloseAll 反向遍历（M-08）

**文件**: `UIModule.cs`

```csharp
for (int i = _uiStack.Count - 1; i >= 0; i--) {
    UIWindow window = _uiStack[i];
    window.InternalDestroy(isShutDown);
}
_uiStack.Clear();
```

### 4.3 SingletonSystem Key 改进（M-06）

**文件**: `SingletonSystem.cs`

```csharp
Dictionary<string, GameObject> _gameObjects → Dictionary<int, GameObject>
go.name → go.GetInstanceID()  (Retain + Release 两处)
```

### 4.4 GameEventMgr 死代码清理（m-05）

**文件**: `GameEventMgr.cs`

删除 `_isInit` 字段及构造函数中 3 行无效检查。

### 4.5 UIModule._instanceRoot static → 实例（m-06）

**文件**: `UIModule.cs` + `UIWindow.cs` + `UIWidget.cs` + `UIBase.cs`

```csharp
// UIModule.cs
- private static Transform _instanceRoot = null;
- public static Transform UIRoot => _instanceRoot;
+ private Transform _instanceRoot = null;
+ public Transform UIRoot => _instanceRoot;

// 同理 Resource 也改为实例属性
- public static IUIResourceLoader Resource;
+ public IUIResourceLoader Resource;
```

**静态引用替换点**（共 8 处）：

| 文件 | 旧引用 | 行号 |
|------|--------|------|
| `UIWindow.cs` | `UIModule.UIRoot` | L327, L332, L338 |
| `UIWindow.cs` | `UIModule.Resource` | L327, L332（同 UIRoot 引用） |
| `UIWidget.cs` | `UIModule.Resource` | L166 |
| `UIBase.cs` | `UIModule.Resource` | L406, L420 |

全部替换为 `UIModule.Instance.UIRoot` / `UIModule.Instance.Resource`。

### Phase 3 提交

```
refactor: 集合安全遍历、Singleton Key 改进与代码规范统一

- UIWindow.InternalDestroy: ToArray 快照遍历
- UIModule.CloseAll: 反向遍历
- SingletonSystem: InstanceID 替代 name 做 Key
- GameEventMgr: 删除 _isInit 死代码
- UIModule: _instanceRoot 改为实例字段
```

---

## 5. 提交策略与验证

### Git 提交图

```
main
  ├── fix: 事件系统异常隔离与场景/资源安全加固  (Phase 1)
  ├── fix: UI/音频资源泄漏防护与超时改进          (Phase 2)
  └── refactor: 集合安全遍历与代码规范统一        (Phase 3)
```

### Phase 1 验证

**自动化**:
- EventDelegateDataTests: 6 个用例全部 Pass
- MemoryPoolTests: 3 个用例全部 Pass
- 全局搜索 `throw new Exception(` → 0 结果
- 全局搜索 `async void` 在 TEngine 命名空间 → 0 结果

**手动**:
- Procedure 完整流程: 启动 → 资源初始化 → 热更加载 → 进入游戏
- 注册会抛异常的 handler → 验证后续 handler 仍执行

### Phase 2 验证（手动）

- 加载不存在的 UI Prefab → HasWindow 返回 false
- 快速 ShowUI/CloseUI 20 次 → 无异常
- 播放/切换音效 20 次 → Profiler 内存不持续增长
- Time.timeScale = 0 → 打开 UI → 30s 超时触发

### Phase 3 验证

**编译**: 零 error、零新增 warning
**手动**:
- 打开 5 个窗口 → CloseAll → 全部销毁
- 两个同名 "Manager" GameObject 都能注册 SingletonSystem

### 回滚策略

每个 Phase 独立提交，可单独 `git revert`，各 Phase 间无代码依赖。

---

## 6. 影响范围

共修改 **12 个源文件**，新增 **3 个测试文件**：

| 文件 | Phase | 修改类型 |
|------|-------|---------|
| `EventDelegateData.cs` | 1 | 异常隔离（核心） |
| `SceneModule.cs` | 1 | 双重卸载修复 |
| `ResourceModule.cs` | 1 | 死代码 + async 修复 |
| `ResourceModule.Pool.cs` | 1 | 空池告警 |
| `MemoryPool.cs` | 1 | 异常类型统一 |
| `MemoryPool.MemoryCollection.cs` | 1 | 计数器安全 |
| `AudioAgent.cs` | 2 | Handle 追踪 |
| `UIWindow.cs` | 2+3 | LoadFailed + 安全遍历 |
| `UIModule.cs` | 2+3 | 超时 + 反向遍历 + 实例字段 |
| `GameModule.cs` | 2 | fake null |
| `SingletonSystem.cs` | 3 | InstanceID Key |
| `GameEventMgr.cs` | 3 | 死代码清理 |
| `UIBase.cs` | 3 | UIModule.Resource → Instance.Resource |
| `UIWidget.cs` | 3 | UIModule.Resource → Instance.Resource |

**公共 API 变更**: 无。所有修改为内部实现增强。
