# 命名规范与禁止模式

> **适用场景**：C# 类型/成员命名约定、UI 节点前缀规范、禁止的代码模式（Resources.Load/Coroutine/SetSpriteAsync 等）| **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（节点前缀）、[resource-api.md](resource-api.md)（禁止模式）

## 核心 API

### C# 类型命名

| 类型 | 规范 | 示例 |
|------|------|------|
| 模块接口 | `IXxxModule` | `IResourceModule` |
| 模块实现 | `XxxModule` | `ResourceModule` |
| 事件接口 | `IXxxEvent` / `IXxxUI` + `[EventInterface]` | `ILoginUI` |
| UIWindow 子类 | `XxxUI` / `XxxWindow` | `BattleMainUI` |
| UIWidget 子类 | `XxxWidget` / `XxxItem` | `SkillSlotWidget` |
| 流程状态 | `ProcedureXxx` | `ProcedureLogin` |
| 状态机状态 | `XxxState` | `IdleState` |
| 系统类 | `XxxSystem` | `LoginSystem` |
| 配置类（Luban） | `TbXxx` / `Xxx`（行数据） | `TbItem` / `Item` |
| 内存池对象 | 实现 `IMemory` | `DamageInfo : IMemory` |

#### 字段与方法

```csharp
// 私有字段：_小驼峰（组件引用见下方 UI 前缀）
private int _currentHp;
private const int MAX_LEVEL = 100;      // 常量全大写下划线
public int CurrentHp => _currentHp;     // 公开属性大驼峰
public event Action OnDead;             // 事件大驼峰

// 异步方法：Async 后缀
public async UniTask LoadDataAsync() { }
// 事件回调：On 前缀
private void OnHpChanged(int hp) { }
```

---

### UI 节点命名规范

Prefab 节点前缀决定 `UIScriptGenerator` 自动生成的绑定类型（基于 `ScriptGeneratorSetting.asset`）：

| 前缀 | 生成类型 | 示例节点名 |
|------|---------|----------|
| `m_go_` | `GameObject` | `m_go_Effect` |
| `m_item_` | `UIWidget`（子类）| `m_item_Slot` |
| `m_tf_` | `Transform` | `m_tf_Container` |
| `m_rect_` | `RectTransform` | `m_rect_Panel` |
| `m_text_` | `Text` | `m_text_Title` |
| `m_richText_` | `RichTextItem` | `m_richText_Desc` |
| `m_btn_` | `Button` | `m_btn_Start` |
| `m_img_` | `Image` | `m_img_Icon` |
| `m_rimg_` | `RawImage` | `m_rimg_Avatar` |
| `m_scroller_` | `Scroller` + 配对生成 `FancyScrollerController`（new+Initialize+OnDestroy Dispose） | `m_scroller_List` |
| `m_scroll_` | `ScrollRect` | `m_scroll_List` |
| `m_scrollBar_` | `Scrollbar` | `m_scrollBar_Vert` |
| `m_input_` | `InputField` | `m_input_Name` |
| `m_grid_` | `GridLayoutGroup` | `m_grid_Items` |
| `m_hlay_` | `HorizontalLayoutGroup` | `m_hlay_Tabs` |
| `m_vlay_` | `VerticalLayoutGroup` | `m_vlay_List` |
| `m_slider_` | `Slider` | `m_slider_Volume` |
| `m_toggle_` | `Toggle` | `m_toggle_Sound` |
| `m_group_` | `ToggleGroup` | `m_group_Tab` |
| `m_curve_` | `AnimationCurve` | `m_curve_Anim` |
| `m_canvasGroup_` | `CanvasGroup` | `m_canvasGroup_Fade` |
| `m_tmp_` | `TextMeshProUGUI` | `m_tmp_Name` |
| `m_tmpInput_` | `TMP_InputField` | `m_tmpInput_Search` |
| `m_tmpDropdown_` | `TMP_Dropdown` | `m_tmpDropdown_Lang` |
| `m_canvas_` | `Canvas` | `m_canvas_Overlay` |
| `m_dropdown_` | `Dropdown` | `m_dropdown_Select` |

**注意**：
- 前缀匹配规则来自 `ScriptGeneratorSetting.asset` 中的 `uiElementRegex` 字段，匹配时不带尾部下划线（如 regex 是 `m_tmpInput`，节点名 `m_tmpInput_XXX` 可匹配）
- `m_scrollBar_` 必须写在 `m_scroll_` 之前，否则 `m_scrollBar_X` 会被 `m_scroll` 先匹配（setting 中确实 scrollBar 在 scroll 之前）
- `m_tmpInput_` 和 `m_tmpDropdown_` 必须写在 `m_tmp_` 之前，同理（setting 中 tmpInput/tmpDropdown 在 tmp 之前）
- `m_fscroll_` / `m_scroller_` 必须写在 `m_scroll_` 之前，否则 FancyScrollView 节点可能被普通 ScrollRect 规则先匹配。
- `m_richText_` 必须写在 `m_text_` 之前（setting 中 richText 在 text 之前）
- 不需要绑定的节点无需加前缀，生成器会忽略

### 节点命名 → 生成标识符（重要）

ScriptGenerator 把节点名转为字段名/方法名。它**只去首个 `m_` 前缀**，不识别 `m_btn_`/`m_text_` 等复合前缀。因此描述部分必须用 PascalCase、**不加下划线**：

| 节点名 | 生成字段名 | 按钮方法名 | 评价 |
|--------|-----------|-----------|------|
| `m_btnClose` | `m_btnClose` | `OnClickCloseBtn` | ✅ 规范 |
| `m_btn_Close` | `m_btn_Close` | `OnClick_CloseBtn` | ❌ 下划线残留 |
| `m_textMessage` | `m_textMessage` | — | ✅ |
| `m_btnRandom` | `m_btnRandom` | `OnClickRandomBtn` | ✅ |

**规则**：节点名 = `m_` + 前缀类型(btn/text/img/tf/scroller...) + PascalCase 描述，**描述段无下划线**。

各类节点生成的方法名：Button→`OnClickXxxBtn`、Toggle→`OnXxxToggle`、Slider→`OnXxxSlider`、TMP_Dropdown→`OnXxxDropdown`（均在 `_Gen.g.cs` 声明 `partial void`，业务文件实现）。

---

## 使用模式

### 异步编程规范

```csharp
// ✅ UniTask 替代 Task，UniTaskVoid 替代 void async
public async UniTask<int> GetDataAsync() { }
public async UniTaskVoid StartBattleAsync() { }  // 调用方加 .Forget()

// ✅ CancellationToken 防止销毁后回调
private CancellationTokenSource _cts = new();
protected override void OnDestroy() { _cts.Cancel(); _cts.Dispose(); }

// ✅ 并发加载
var (a, b, c) = await UniTask.WhenAll(LoadA(), LoadB(), LoadC());
```

### 字符串拼接规范

业务/高频拼接用 `Utility.Text.Format`（`TEngine.Utility.Text`，复用缓存 StringBuilder，减少 GC）；`$""` 插值与 `string.Format` 每次分配新字符串 + 装箱，热路径会产 GC。

```csharp
// ✅ 高频/业务拼接：Utility.Text.Format（using TEngine; 后可用）
Items[idx].DisplayText = Utility.Text.Format("Cell {0} id:{1}", idx, id);

// ✅ Log.Info/Debug/Warning 可用 $"..."：发布版本由宏屏蔽，无运行时分配
Log.Info($"[Example01] 选中切换: #{old} → #{index}");

// ❌ 高频业务路径用 $"..."：每次分配字符串
Items[idx].DisplayText = $"Cell {idx} id:{id}";
```

- `Utility.Text.Format` 命名空间为 `TEngine`（`using TEngine;` 或 `using Utility = TEngine.Utility;`）。
- **Log 例外**：`Log.Info/Debug/Warning` 可用 `$""`（发布版本宏屏蔽，无运行时分配）；但 `Log.Error/Assert/Fatal` 不屏蔽，高频或复杂拼接也应改 `Utility.Text.Format`。
- 一次性/低频拼接（启动初始化、配置字符串）可用 `$""`，无需过度优化。

### 集合与向量运算性能规范

#### Dictionary 访问：避免 `ContainsKey` + 索引的双重 hash

`ContainsKey(key)` 与 `this[key]` 各做一次 hash 查询。需要"判断存在 + 取值"时用 `TryGetValue`（一次查询带取值）；需要"判断存在 + 写入"且新增是多数路径时用 `TryAdd`（新增一次查询）。

```csharp
// ❌ ContainsKey + 索引取值：两次 hash
if (_pending.ContainsKey(url))
{
    _pending[url].Add(cb);        // 第二次 hash
}

// ✅ TryGetValue：一次 hash 带取值
if (_pending.TryGetValue(url, out var list))
{
    list.Add(cb);
}

// ❌ ContainsKey + 索引赋值：两次 hash（新增是多数路径时尤甚）
if (_cache.ContainsKey(url))
{
    _cache[url] = tex;            // 第二次 hash
    TouchLru(url);
    return;
}
_cache[url] = tex;                // 又一次 hash

// ✅ TryAdd：不存在则添加（一次 hash）返回 true；存在返回 false
if (_cache.TryAdd(url, tex))
{
    RegisterLru(url);             // 新增路径：只付一次 hash
}
else
{
    _cache[url] = tex;            // 已存在：更新（少数路径才付第二次 hash）
    TouchLru(url);
}
```

> 注意：改用 `TryAdd` 后 `_cache.Count` 在调用返回时已 +1，后续容量判断条件要从 `>= Capacity` 改为 `> Capacity`，否则会多淘汰一项。

#### Vector 运算：标量先相乘，最后乘向量

Unity 的 `Vector3` 运算符是显式重载，编译器**不会**把 `v * a * b` 重排为 `(a * b) * v`。`向量 × 标量 × 标量` 产生 6 次乘法；`标量 × 标量 × 向量` 只需 4 次。热路径（`Update` / `UpdatePosition` 每帧每 Cell 调用）务必用后者。

```csharp
// ❌ 向量 × 标量 × 标量：6 次乘法（向量三分量各乘两次）
transform.localPosition = Vector3.left * 500f * slide;

// ✅ 标量 × 标量 × 向量：4 次乘法（标量先合并）
transform.localPosition = 500f * slide * Vector3.left;
```

推广：任何 `VectorN` 连乘表达式中，先把所有标量因子合并为单个标量，最后再乘向量；多向量混合运算同理优先合并同类因子。

---

## 常见错误

| 错误 | 原因 | 修复 |
|------|------|------|
| 节点 `m_tInput_Search` 不生成 TMP_InputField | 旧文档前缀 `m_tInput_` 已过时 | 正确前缀为 `m_tmpInput_` |
| `m_tmp_Search` 被识别为 TMP_InputField | `m_tmpInput` regex 先于 `m_tmp` 匹配 | 节点名不含 `Input` 时不会被误匹配 |
| 前缀匹配顺序错误 | regex 按序匹配，长前缀需先声明 | scrollBar 在 scroll 前、tmpInput/tmpDropdown 在 tmp 前 |
| 缺少 TMP_Dropdown 绑定 | 旧文档未记录此前缀 | 使用 `m_tmpDropdown_` 前缀 |
| 缺少 Canvas/Dropdown 绑定 | 旧文档未记录此前缀 | 使用 `m_canvas_` / `m_dropdown_` 前缀 |

### 禁止的异步模式

```csharp
// ❌ Task → 用 UniTask
// ❌ Coroutine → 用 async/await
// ❌ Update 中 await → 用 Timer
// ❌ 忽略 UniTask 返回值 → 加 .Forget() 或 await
```

### 禁止的代码模式

```csharp
// ❌ Resources.Load → GameModule.Resource
// ❌ Instantiate(prefab) → LoadGameObjectAsync
// ❌ FindObjectOfType → GameModule 或事件
// ❌ Update 中 new 对象 → MemoryPool
// ❌ 跨模块强引用 → GameEvent
// ❌ 外部访问 UI 私有组件 → 事件或公共方法
// ❌ 静态持有 Asset 引用 → 内存泄漏
// ❌ 忽略 async 返回值 → await 或 .Forget()
// ❌ 高频业务拼接用 $"..." / string.Format → Utility.Text.Format（log.Info/Debug/Warning 例外）
// ❌ GetComponentInChildren/GetComponentsInChildren/FindChildComponent 取子节点组件 → UIBindComponent 生成 m_xxx（仅根节点可 GetComponent）
// ❌ 手动 override ScriptGenerator 取子节点 → _Gen.g.cs 自动绑定（UIWindow/UIWidget/Cell 通用；根节点组件放 BindMemberProperty）
// ❌ ContainsKey + 索引双重 hash → 取值用 TryGetValue、写入用 TryAdd（见"集合与向量运算性能规范"）
// ❌ 向量 × 标量 × 标量 运算顺序 → 标量先合并再乘向量（热路径必备）
```

---

## 交叉引用

- 架构总览见 [architecture.md](architecture.md)
- UI 生命周期见 [ui-lifecycle.md](ui-lifecycle.md)
- UI 进阶模式见 [ui-patterns.md](ui-patterns.md)
