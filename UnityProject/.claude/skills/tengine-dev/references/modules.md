# TEngine 模块 API 速查

> **适用场景**：使用 GameModule.Timer/Scene/Audio/Fsm/MemoryPool/Log 等模块 API | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（UI 模块）、[resource-api.md](resource-api.md)（Resource 模块）、[event-system.md](event-system.md)（事件模块）

## 核心 API：GameModule 统一访问入口

所有模块通过 `GameModule` 静态类访问（已缓存），禁止重复 `ModuleSystem.GetModule<T>()`：

```csharp
GameModule.Base          // RootModule          — 根模块（框架初始化入口）
GameModule.Debugger      // IDebuggerModule     — 调试器（`~` 键呼出）
GameModule.Fsm           // IFsmModule          — 有限状态机
GameModule.Procedure     // IProcedureModule    — 流程
GameModule.Resource      // IResourceModule     — 资源加载
GameModule.Audio         // IAudioModule        — 音频
GameModule.UI            // UIModule            — UI 管理
GameModule.Scene         // ISceneModule        — 场景
GameModule.Timer         // ITimerModule        — 计时器
GameModule.Localization  // ILocalizationModule — 本地化

GameModule.Shutdown()   // 清空所有模块缓存引用，仅在游戏退出时调用
```

> **注意**：`UI` 属性类型是 `UIModule`（单例），不是 `IUIModule`；`Base` 属性通过 `FindObjectOfType<RootModule>()` 获取，其余通过 `ModuleSystem.GetModule<T>()` 获取。

---

## 使用模式

### TimerModule 计时器（重构版 v2.0）

```csharp
// 基础延迟定时器
TimerHandle handle = GameModule.Timer.Delay(3f, () => Debug.Log("延迟3秒触发"));
TimerHandle handle = GameModule.Timer.Delay(3f, args => Debug.Log($"进度: {args.Progress}"));

// 循环定时器
TimerHandle handle = GameModule.Timer.Repeat(1f, () => Debug.Log("每秒触发一次"));           // 无限循环
TimerHandle handle = GameModule.Timer.Repeat(0.5f, args => Debug.Log($"剩余次数: {args.TicksRemaining}"), count: 5);  // 重复5次

// 调度定时器（延迟+循环）
TimerHandle handle = GameModule.Timer.Schedule(2f, 1f, args => 
{
    Debug.Log($"延迟2秒后，每1秒触发一次，当前第{args.TickIndex + 1}次");
});

// 倒计时
TimerHandle handle = GameModule.Timer.Countdown(10, 1f, 
    args => Debug.Log($"倒计时: {args.TicksRemaining}秒"),
    onComplete: () => Debug.Log("倒计时结束"));

// 帧级定时器
TimerHandle handle = GameModule.Timer.NextFrame(() => Debug.Log("下一帧触发"));
TimerHandle handle = GameModule.Timer.WaitFrames(60, () => Debug.Log("60帧后触发"));

// 时间模式（受/不受 Time.timeScale 影响）
TimerHandle handle = GameModule.Timer.Delay(5f, callback, TimeMode.Scaled);    // 受时间缩放影响
TimerHandle handle = GameModule.Timer.Delay(5f, callback, TimeMode.Unscaled); // 不受时间缩放影响

// TimerHandle 控制
handle.Cancel();    // 取消定时器
handle.Pause();     // 暂停定时器
handle.Resume();    // 恢复定时器

// TimerHandle 查询
bool isValid = handle.IsValid;           // 定时器是否有效
float remaining = handle.Remaining;      // 剩余时间（秒）
float progress = handle.Progress;        // 进度（0~1，循环定时器为 NaN）

// 诊断与配置
int activeCount = GameModule.Timer.ActiveTimerCount;    // 活跃定时器数量
int zombieCount = GameModule.Timer.ZombieCount;          // 已取消但未清理的定时器
int poolCapacity = GameModule.Timer.PoolCapacity;        // 对象池容量
int poolUsed = GameModule.Timer.PoolUsed;                // 对象池已使用数

GameModule.Timer.Configure(maxCatchUpSteps: 10, initialPoolCapacity: 256);  // 配置定时器系统
GameModule.Timer.Compact();  // 清理已取消的定时器（减少内存占用）

// Owner 绑定（自动取消）
GameObject owner = new GameObject("TimerOwner");
TimerHandle handle = GameModule.Timer.Delay(5f, callback, owner: owner);
// owner 被销毁时，定时器自动取消

// CancellationToken 支持
var cts = new CancellationTokenSource();
TimerHandle handle = GameModule.Timer.Repeat(1f, callback, cancellationToken: cts.Token);
cts.Cancel();  // 取消令牌，定时器停止
```

**TimerTickArgs 回调参数：**
```csharp
args.TickIndex        // 当前触发索引（从0开始）
args.TotalTicks       // 总触发次数（-1表示无限循环）
args.TicksRemaining   // 剩余触发次数
args.Progress         // 进度（0~1，无限循环为 NaN）
args.ElapsedTime      // 已消耗时间（秒）
args.Handle           // TimerHandle 引用，可在回调中取消
```

### SceneModule 场景管理

```csharp
// 加载
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName");
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName", LoadSceneMode.Additive);
Scene scene = await GameModule.Scene.LoadSceneAsync("SceneName", LoadSceneMode.Single,
    progressCallBack: p => { /* 0~1 */ });

// 卸载/激活
bool ok = await GameModule.Scene.UnloadAsync("SceneName");
GameModule.Scene.ActivateScene("SceneName");
bool has = GameModule.Scene.IsContainScene("SceneName");
```

### AudioModule 音频

```csharp
// 播放
AudioAgent agent = GameModule.Audio.Play(AudioType.Music, "bgm_path", bLoop: true);   // BGM
AudioAgent agent = GameModule.Audio.Play(AudioType.Sound, "sfx_path");                  // 音效
AudioAgent agent = GameModule.Audio.Play(AudioType.UISound, "ui_click", bAsync: true);  // UI

// 停止
GameModule.Audio.Stop(AudioType.Music, fadeout: true);
GameModule.Audio.StopAll(fadeout: false);

// 音量
GameModule.Audio.Volume      = 1.0f;  // 全局 (0~1)
GameModule.Audio.MusicVolume = 0.8f;
GameModule.Audio.SoundVolume = 1.0f;
GameModule.Audio.MusicEnable = true;
GameModule.Audio.SoundEnable = true;
```

### FsmModule 有限状态机

```csharp
// 定义状态
public class IdleState : FsmState<MyOwner>
{
    protected override void OnEnter(IFsm<MyOwner> fsm)  { }
    protected override void OnUpdate(IFsm<MyOwner> fsm, float elapse, float real) { }
    protected override void OnLeave(IFsm<MyOwner> fsm, bool isShutdown) { }
}

// 创建并启动
IFsm<MyOwner> fsm = GameModule.Fsm.CreateFsm<MyOwner>("FsmName", owner,
    new IdleState(), new RunState(), new AttackState());
fsm.Start<IdleState>();

// 切换与传数据
fsm.ChangeState<RunState>();
fsm.SetData<int>("Key", value);
int val = fsm.GetData<int>("Key");

// 销毁
GameModule.Fsm.DestroyFsm<MyOwner>("FsmName");
```

### MemoryPool 内存池

频繁创建/销毁的纯 C# 对象，避免 GC：

```csharp
public class DamageInfo : IMemory
{
    public int Damage;
    public void Clear() { Damage = 0; }  // 归还时重置
}

var info = MemoryPool.Acquire<DamageInfo>();
info.Damage = 100;
MemoryPool.Release(info);  // Release 后禁止再访问，禁止 Release 两次
```

### Log 日志系统

```csharp
Log.Debug("仅 Development Build 输出");  // 发布包自动剥离
Log.Info("普通信息");
Log.Warning("警告");
Log.Error("错误，始终保留");
Log.Fatal("严重错误");
Log.Assert(condition, "断言失败提示");
```

---

## 常见错误

| 错误写法 | 正确写法 | 原因 |
|---------|---------|------|
| `ModuleSystem.GetModule<ITimerModule>()` | `GameModule.Timer` | 重复查找，未利用缓存 |
| `OnDestroy` 忘记 `RemoveTimer(tid)` | 必须调用 `RemoveTimer` | 计时器回调引用已销毁对象，导致空引用 |
| `SceneManager.LoadScene()` | `GameModule.Scene.LoadSceneAsync()` | 绕过框架资源管理，热更包无法加载 |
| `GameModule.UI` 误用接口 `IUIModule` | 类型是 `UIModule`（单例实现） | 源码中 UI 属性返回 `UIModule.Instance`，非 `GetModule<T>()` |
| `Shutdown()` 后继续访问模块属性 | `Shutdown()` 仅游戏退出时调用 | 所有缓存引用置 null，后续访问触发重新查找或空引用 |
| `MemoryPool.Release()` 后访问对象 | Release 后禁止再访问 | 对象已归还池中，状态不确定 |
| `MemoryPool.Release()` 同一对象两次 | 确保只 Release 一次 | 重复归还导致池状态异常 |
| `GameModule.LoadScene` | `GameModule.Scene.LoadSceneAsync` | 不存在 `GameModule.LoadScene`，场景加载通过 `GameModule.Scene` |
| `new FsmState<>()` | 继承 `FsmState<TOwner>` | 状态必须继承基类，不能直接 new |
| `GameModule.Timer.AddTimer(time, callback)` | `GameModule.Timer.Delay(callback, time)` | 新版 API 使用 TimerHandle，参数顺序：回调在前，时间在后 |
| `int timerId` 管理 | 使用 `TimerHandle` 结构体 | 新版使用句柄而非整数ID，避免版本冲突和内存泄漏 |
| `Stop/Resume/RemoveTimer` | `TimerHandle.Pause/Resume/Cancel()` | 方法移至 TimerHandle，更符合面向对象设计 |
| `IsRunning/GetLeftTime(timerId)` | `handle.IsValid/handle.Remaining` | 查询属性移至 TimerHandle，语义更清晰 |
| `handle.Cancel()` 后继续访问 | 检查 `handle.IsValid` | 句柄被取消后变为无效，访问会返回安全默认值 |
| 大量短周期循环定时器 | 考虑合并或降频 | 双堆结构虽高效，但大量定时器仍会影响性能 |
| 忘记调用 `Compact()` | 定期清理 zombie 定时器 | 已取消但未清理的定时器会占用内存，建议在合适时机调用 |
| 时间模式选择错误 | 根据需求选择 Scaled/Unscaled | 受时间缩放影响用 Scaled（如动画），不受影响用 Unscaled（如加载进度） |

---

## 交叉引用

| 关联主题 | 文档 | 说明 |
|---------|------|------|
| 资源加载/卸载 | resource-api.md | `GameModule.Resource` 的完整 API 与生命周期 |
| UI 管理 | ui-lifecycle.md | `GameModule.UI` 的窗口生命周期与层级 |
| UI 进阶 | ui-patterns.md | Widget 模板与节点绑定 |
| 事件系统 | event-system.md | `GameEvent` 模块间解耦，`AddUIEvent` UI 内部事件 |
| 热更边界 | hotfix-workflow.md | `GameModule` 所在程序集与热更边界 |
| 资源管理模式 | resource-patterns.md | 资源生命周期与模块协作 |
