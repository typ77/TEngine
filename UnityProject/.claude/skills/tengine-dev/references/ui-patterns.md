# UI 开发模式与模板

> **适用场景**：UIWidget 8 种创建方式（CreateWidget/CreateWidgetByPath/CreateWidgetByPrefab 等）、AdjustIconNum 列表数量管理、动态列表复用模式 | **关联文档**：[ui-lifecycle.md](ui-lifecycle.md)（生命周期）、[naming-rules.md](naming-rules.md)（节点前缀）

## 一、核心 API

### UIWidget 创建方式（8 个方法）

| # | 方法签名 | 说明 | 同步/异步 |
|---|---------|------|----------|
| 1 | `CreateWidget<T>(string goPath, bool visible = true)` | 从子节点路径创建（Prefab 中已有节点） | 同步 |
| 2 | `CreateWidget<T>(Transform parentTrans, string goPath, bool visible = true)` | 从父节点 + 子路径创建 | 同步 |
| 3 | `CreateWidget<T>(GameObject goRoot, bool visible = true)` | 从 GameObject 创建 | 同步 |
| 4 | `CreateWidgetByPath<T>(Transform parentTrans, string assetLocation, bool visible = true)` | 从资源路径创建，内部调用 `LoadGameObject` | 同步 |
| 5 | `CreateWidgetByPathAsync<T>(Transform parentTrans, string assetLocation, bool visible = true)` | 从资源路径创建，内部调用 `LoadGameObjectAsync` | 异步 |
| 6 | `CreateWidgetByPrefab<T>(GameObject goPrefab, Transform parentTrans = null, bool visible = true)` | 从预制体克隆创建 | 同步 |
| 7 | `CreateWidgetByType<T>(Transform parentTrans, bool visible = true)` | 用类型名作资源路径，内部调用 `CreateWidgetByPath` | 同步 |
| 8 | `CreateWidgetByTypeAsync<T>(Transform parentTrans, bool visible = true)` | 用类型名作资源路径，内部调用 `CreateWidgetByPathAsync` | 异步 |

> **约束**：所有泛型 `T` 必须满足 `where T : UIWidget, new()`。

### 列表管理方法（2 个方法）

```csharp
// 同步调整列表数量
void AdjustIconNum<T>(List<T> listIcon, int number, Transform parentTrans,
    GameObject prefab = null, string assetPath = "")
    where T : UIWidget, new()

// 异步调整列表数量（支持分帧创建 + 逐个回调）
void AsyncAdjustIconNum<T>(List<T> listIcon, int tarNum, Transform parentTrans,
    GameObject prefab = null, string assetPath = "",
    int maxNumPerFrame = 5, Action<T, int> updateAction = null)
    where T : UIWidget, new()
```

**AdjustIconNum 逻辑**：若 `prefab != null` 用 `CreateWidgetByPrefab`，否则用 `CreateWidgetByType`。

**AsyncAdjustIconNum 逻辑**：若 `prefab != null` 用 `CreateWidgetByPrefab`，否则用 `CreateWidgetByPathAsync`（注意：此处即使无 prefab 也走异步路径，与同步版不同）。每帧最多创建 `maxNumPerFrame` 个，每创建一个调用 `updateAction(widget, index)`。

---

## 二、使用模式

### 完整 UIWindow 示例

```csharp
[Window(UILayer.UI, "BattleMainUI", fullScreen: true)]
public class BattleMainUI : UIWindow
{
    private Button    _btnBack;
    private Text      _textHp;
    private Text      _textGold;
    private Transform _tfSkillPanel;
    private readonly List<SkillSlotWidget> _skillSlots = new();

    protected override void ScriptGenerator()
    {
        _btnBack      = FindChildComponent<Button>("m_btn_Back");
        _textHp       = FindChildComponent<Text>("m_text_Hp");
        _textGold     = FindChildComponent<Text>("m_text_Gold");
        _tfSkillPanel = FindChild("m_tf_SkillPanel");
        RegisterButtonClick(_btnBack, OnBackClicked);
    }

    protected override void RegisterEvent()
    {
        AddUIEvent<int>(IBattleEvent_Event.OnHpChanged, RefreshHp);
        AddUIEvent<int>(IBattleEvent_Event.OnGoldChanged, RefreshGold);
    }

    protected override void OnCreate()
    {
        for (int i = 0; i < 4; i++)
        {
            var slot = CreateWidget<SkillSlotWidget>($"m_tf_SkillPanel/Slot_{i}");
            _skillSlots.Add(slot);
        }
    }

    protected override void OnRefresh()
    {
        RefreshHp(PlayerData.Hp);
        RefreshGold(PlayerData.Gold);
        for (int i = 0; i < _skillSlots.Count; i++)
            _skillSlots[i].SetData(PlayerData.Skills[i]);
    }

    private void RefreshHp(int hp)    => _textHp.text   = $"HP: {hp}";
    private void RefreshGold(int gold) => _textGold.text = $"Gold: {gold}";
    private void OnBackClicked() => GameModule.UI.CloseUI<BattleMainUI>();
}
```

### UIWidget 模板

```csharp
public class ItemWidget : UIWidget
{
    private Text  _textName;
    private Image _imgIcon;

    protected override void ScriptGenerator()
    {
        _textName = FindChildComponent<Text>("m_text_Name");
        _imgIcon  = FindChildComponent<Image>("m_img_Icon");
    }

    public void SetData(ItemConfig cfg)
    {
        _textName.text = cfg.Name;
        _imgIcon.SetSprite(cfg.IconPath);  // 内置缓存池，无需手动释放
    }
}
```

### Widget 创建方法速查

```csharp
// 1. Prefab 中已有节点 — 最常用
var w1 = CreateWidget<ItemWidget>("path/to/node");

// 2. 指定父级 + 子路径
var w2 = CreateWidget<ItemWidget>(parentTrans, "goPath");

// 3. 直接用 GameObject
var w3 = CreateWidget<ItemWidget>(goRoot);

// 4. 从资源路径创建（同步，会阻塞）
var w4 = CreateWidgetByPath<ItemWidget>(parent, "Assets/ItemWidget.prefab");

// 5. 从资源路径创建（异步，推荐）
var w5 = await CreateWidgetByPathAsync<ItemWidget>(parent, "Assets/ItemWidget.prefab");

// 6. 从预制体克隆 — 列表项常用
var w6 = CreateWidgetByPrefab<ItemWidget>(prefab, parent);

// 7. 用类型名作资源路径（同步）— 资源名须与类型名一致
var w7 = CreateWidgetByType<ItemWidget>(parentTrans);

// 8. 用类型名作资源路径（异步）
var w8 = await CreateWidgetByTypeAsync<ItemWidget>(parentTrans);
```

### 列表 Widget 复用

```csharp
// 同步调整数量（prefab 优先，无 prefab 时用 CreateWidgetByType）
AdjustIconNum<ItemWidget>(listIcon, number: items.Count, parent, prefab: prefab);

// 同步调整数量（指定 assetPath 时用 CreateWidgetByPath）
AdjustIconNum<ItemWidget>(listIcon, number: items.Count, parent, assetPath: "Assets/ItemWidget.prefab");

// 异步调整数量（支持分帧创建，每帧最多 5 个）
AsyncAdjustIconNum<ItemWidget>(listIcon, tarNum: items.Count, parent,
    prefab: prefab, maxNumPerFrame: 3,
    updateAction: (widget, idx) => widget.SetData(items[idx]));

// 刷新每个 Widget（同步版用后手动刷新）
for (int i = 0; i < items.Count; i++)
    listIcon[i].SetData(items[i]);
```

### 手动绑定 API

```csharp
protected override void ScriptGenerator()
{
    var trans = FindChild("m_tf_Container");
    var btn   = FindChildComponent<Button>("m_btn_Start");
    var rect  = FindChildComponent<RectTransform>("m_rect_Panel");
    var tmp   = FindChildComponent<TextMeshProUGUI>("m_tmp_Name");
    RegisterButtonClick(btn, OnStartClicked);
}
```

---

## 三、常见错误

1. **混淆同步/异步创建**：`CreateWidgetByPath` 是同步阻塞，`CreateWidgetByPathAsync` 是异步非阻塞。在 UIWindow 的 `OnCreate` 中优先使用异步版本，避免卡顿。
2. **CreateWidgetByType 的资源命名**：该方法用 `typeof(T).Name` 作为 assetLocation，所以资源名必须与 Widget 类名完全一致，否则加载失败。
3. **AsyncAdjustIconNum 无 prefab 时走异步路径**：无 prefab 时内部调用 `CreateWidgetByPathAsync`（异步），而同步版 `AdjustIconNum` 无 prefab 时调用 `CreateWidgetByType`（同步）。两者行为不一致，混用可能导致部分 Widget 异步创建未完成就被访问。
4. **AsyncAdjustIconNum 返回 void**：该方法内部用 `.Forget()` 启动异步任务，调用方无法 await 等待完成。如需等待创建完成再操作，应手动循环使用 `CreateWidgetByPathAsync`。
5. **AdjustIconNum 的 assetPath 参数**：同步版 `AdjustIconNum` 声明了 `assetPath` 参数，但内部实现并未使用该参数（只看 `prefab` 是否为 null 来决定用 `CreateWidgetByType` 还是 `CreateWidgetByPrefab`），传 `assetPath` 无实际效果。

---

## 四、交叉引用

| 主题 | 文档 |
|------|------|
| UIWindow 生命周期、层级、属性 | [ui-lifecycle.md](ui-lifecycle.md) |
| 事件系统（AddUIEvent / GameEvent） | [event-system.md](event-system.md) |
| 节点前缀命名规范 | [naming-rules.md](naming-rules.md#ui-节点命名规范) |
| 资源加载/卸载 API | [resource-api.md](resource-api.md) |

## FancyScrollView 虚拟化列表

普通少量列表用 `AdjustIconNum<T>()` / `AsyncAdjustIconNum<T>()`；**大量数据、Cell 复用、滚动动画或虚拟化**用 FancyScrollView。核心组件在 `Module/UIModule/FancyScrollView/`。

### 组件职责

| 组件 | 职责 |
|------|------|
| `FancyScrollerView` | 继承 `Scroller`，额外持有 `CellContainer` 字段；挂 `m_scroller` 节点 |
| `FancyScrollerController` | 纯 C# 控制器，管 Cell 池/位置/数据；ScriptGenerator 自动 `new` + `Initialize` + `OnDestroy Dispose` |
| `FancyCellWidget<TItemData,TContext>` | Cell 基类，继承 `UIWidget`，复用 UIBindComponent 绑定 |

### 创建 Scroller

右键 `GameObject/UI/Scroller`（`FancyScrollerMenu`）自动生成标准结构：
```
m_scroller  [Image, Mask, FancyScrollerView]   ← viewport 指向自身
  └─ CellContainer  [RectTransform]             ← cellContainer 字段自动赋值
```
- `CellContainer` **无需 m_ 前缀**（不作独立绑定字段，由 `FancyScrollerView` 持有）。
- ⚠️ `m_scroller` 必须挂 `FancyScrollerView`（不是基类 `Scroller`）。否则 `FancyScrollerController.Initialize` 抛 `InvalidOperationException`。旧 prefab 需补挂并赋 `CellContainer`。

### Cell 走 UIBindComponent 自动生成（无基类 partial 模式）

`FancyCellWidget` 继承 `UIWidget`，Cell 可像 UI 一样用 UIBindComponent 生成 `_Gen.g.cs`。但 Cell 基类是双泛型 `FancyCellWidget<TData,TContext>`，生成器表达不了，故用「无基类 partial」：

| 文件 | 基类 | 内容 |
|------|------|------|
| `XxxCell_Gen.g.cs`（生成）| 无（`public partial class XxxCell`）| 节点绑定字段 + `ScriptGenerator()` + 事件 partial |
| `XxxCell.cs`（业务）| `: FancyCellWidget<TData,TContext>` | 泛型基类 + `BindMemberProperty`（根节点/MVE）+ `BindData`/`UpdatePosition` |

partial 合并后基类为 `FancyCellWidget<TData,TContext>`，`override ScriptGenerator` 合法。

**启用**：`ScriptGeneratorSetting.asset` 的 `UIGenTypes` 配 `FancyCellWidget`(noBaseClass=true) → Cell prefab 的 UIBindComponent 选 UI 类型 `FancyCellWidget` → 生成脚本。

### 根节点组件：生成器不遍历根节点

`AutoErgodic` 只遍历子节点。Cell 根节点的 `Animator`/`CanvasGroup` 等无法自动绑定，业务在 **`BindMemberProperty()`** 手写（它在 `ScriptGenerator` 之后，子节点字段已就绪）：
```csharp
protected override void BindMemberProperty()
{
    m_animator = gameObject.GetComponent<Animator>();     // 根节点手动绑定
    _displayText = CreateProperty<string>("");             // MVE 响应式属性
    BindText(m_textMessage, _displayText);                 // m_textMessage 来自 _Gen
}
```

> ⚠️ **禁止用 `GetComponentInChildren` / `GetComponentsInChildren` 获取子节点组件**：子节点（Image/Text/Button 等）必须通过 UIBindComponent 生成 `m_xxx`（节点遵循命名前缀）。**只有根节点组件**（生成器不遍历根）才在 `BindMemberProperty` 用 `GetComponent`。手写 `GetComponentInChildren` 绕过绑定系统属于违规——移植原版时易遗留（如 `Example03_InfiniteScrollCell` 曾用 `GetComponentsInChildren<Text>[0]` 取子节点，且 prefab 缺 UIBindComponent、节点无 `m_` 前缀，需补齐后再重新生成 `_Gen`）。

### 改 prefab → 重新生成 _Gen 工作流

1. 改 prefab 节点（增删/改名/改组件）
2. UIBindComponent Inspector「重新绑定组件」(`GenerateUIComponentScript`，重遍历子节点填充 `m_components`)
3. 同 Inspector「生成脚本」(`GenerateCSharpScript`，isAutoGenerate=true 写 `_Gen.g.cs`)
4. 业务文件实现新增的 `partial void OnClickXxxBtn()` 等

> **MCP**：上述两函数依赖 `Selection.activeTransform`。`execute_code` 中 `PrefabUtility.LoadPrefabContents` → `Selection.activeTransform = contents.transform` → 调用 → `SaveAsPrefabAsset`（持久化 `GenerateUIComponentScript` 对实例的修改）→ `UnloadPrefabContents`。注意 `GenerateCSharpScript` 末尾 `AssetDatabase.Refresh` 会触发 Domain reload，可能短暂断开 MCP。

### Cell 点击冒泡

Cell 内 `NotifyItemClicked()` / `NotifyButtonClicked()` 冒泡到 `FancyScrollerController.OnCellItemClicked`，UIWindow 订阅后调 Service 或发 GameEvent。Cell 的 MVE 绑定细节见 [mve-patterns.md](mve-patterns.md) 第七章。

### 选中态上下文：FancySelectionContext（不要自定义）

FancyScrollView 的选中态（Cell 高亮广播）统一用框架基类 `FancySelectionContext`（`Core/FancySelectionContext.cs`，含 `public int SelectedIndex = -1`）。**不要每个列表自定义只含 `SelectedIndex` 的 Context 类**——这是常见冗余（曾出现在 Example01/03/06，三个一模一样的类）。

| 场景 | 用法 |
|------|------|
| 纯选中态列表（推荐默认） | Cell 泛型直接用 `FancyCellWidget<TData, FancySelectionContext>`，UI `_context` 类型为 `FancySelectionContext` |
| 需额外字段（布局接口/回调） | 继承扩展：`class XxxContext : FancySelectionContext, IFancyScrollRectContext`（复用 `SelectedIndex` + 实现接口） |

```csharp
// 纯选中态（Example01/03/06）
private FancySelectionContext _context;
_context = new FancySelectionContext { SelectedIndex = 0 };
m_scrollerController.SetContext(_context);
// Cell: FancyCellWidget<XxxItemData, FancySelectionContext>

// 需接口 + 回调（Example07/08，用 ScrollRect/GridView Widget）
public class XxxContext : FancySelectionContext, IFancyScrollRectContext
{
    public ScrollDirection ScrollDirection { get; set; }       // 接口成员
    public Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }
    public Action<int> OnCellClicked;                          // 业务字段
    // SelectedIndex 继承自 FancySelectionContext，无需重复声明
}
```

### 三种 Context 概念区分（勿混淆）

FancyScrollView 列表里会出现多个「Context」，语义完全不同，命名要消歧：

| 概念 | 是什么 | 归属 | 命名 |
|------|--------|------|------|
| **MVE DataContext** | Model→View 数据映射层（含 `MapProperty`/`BindableProperty` 输出） | MVE 第三层 | `XxxDataContext : DataContext<XxxUI>` |
| **Cell Context** | FancyScrollView 的 UI↔Cell 共享广播态（选中索引等） | View 层（UI 创建，广播给 Cell） | `FancySelectionContext`（基类）或其子类 |
| **业务传递对象** | POCO（如 ItemData），由 DataContext/Service 构建后传给 Cell | 数据层 | `XxxItemData` |

- Cell Context 实例必须由 UIWindow 创建并 `m_scrollerController.SetContext(ctx)` 注入（FancyScrollView 契约）。
- 选中态是 **UI 交互态**，不归 DataContext/Model（DataContext 只映射 Model 数据）；放 `FancySelectionContext` 是语义最正确的位置。
- 无选中态的列表（如纯展示）Cell 泛型用 `NullContext`（见 Example09）。

> UIWindow 侧的列表 MVE（DataContext 如何订阅 Model 数组、用 `ItemsRevision` 信号驱动 scroller 刷新、`OnCreate` 时序与 `async` try-catch）见 [mve-patterns.md](mve-patterns.md) 第八章 carrier-as-lifecycle 模式。
