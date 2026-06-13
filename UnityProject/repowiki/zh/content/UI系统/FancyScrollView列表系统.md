# FancyScrollView 列表系统

<cite>
**本文档引用的文件**
- [FancyScrollerView.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Scroller/FancyScrollerView.cs)
- [FancyScrollerController.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollerController.cs)
- [FancyScrollerWidget.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollerWidget.cs)
- [FancyCellWidget.cs](file://Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyCellWidget.cs)
- [FancyScrollerMenu.cs](file://Assets/Editor/FancyScrollView/FancyScrollerMenu.cs)
- [Example01_BasicUI.cs](file://Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example01_Basic/View/Example01_BasicUI.cs)
- [Example01_BasicCell.cs](file://Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example01_Basic/View/Example01_BasicCell.cs)
- [Example01_BasicCell_Gen.g.cs](file://Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example01_Basic/View/Example01_BasicCell_Gen.g.cs)
</cite>

## 简介

FancyScrollView 是 TEngine 的高性能虚拟化列表方案，适用于**大量数据、Cell 复用、滚动动画、Grid 虚拟化**等场景。普通少量列表继续使用 `AdjustIconNum<T>()` / `AsyncAdjustIconNum<T>()`（见 [UI数据绑定](UI数据绑定.md) / UI 开发模式）；数据量大或需要复用 Cell 时使用本系统。

核心代码位于 `Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/`，示例位于 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/`。

## 核心组件

| 组件 | 类型 | 职责 |
|------|------|------|
| `FancyScrollerView` | MonoBehaviour（继承 `Scroller`）| 额外持有 `CellContainer` 字段；挂在 `m_scroller` 节点 |
| `FancyScrollerController` | 纯 C# 控制器 | 管理 Cell 池、滚动位置、数据更新；由 ScriptGenerator 自动 `new` + `Initialize` + `OnDestroy Dispose` |
| `FancyCellWidget<TItemData,TContext>` | Cell 基类（继承 `UIWidget`）| 复用 UIBindComponent 绑定机制 + MVE 数据绑定 |
| `FancyScrollerMenu` | 编辑器菜单 | `GameObject/UI/Scroller` 创建标准 Scroller 结构 |

**关键关系**：`FancyCellWidget` 继承 `UIWidget`，因此 Cell 可像 UIWindow/UIWidget 一样用 UIBindComponent 自动生成 `_Gen.g.cs`，复用同一套节点绑定与事件机制。

## 创建 Scroller（标准结构）

右键菜单 `GameObject/UI/Scroller`（`FancyScrollerMenu`）自动生成：

```
m_scroller  [Image, Mask, FancyScrollerView]   ← viewport 指向自身
  └─ CellContainer  [RectTransform]             ← cellContainer 字段自动赋值
```

- `m_scroller` 命名遵循 `ScriptGenerateRule`，生成器会**配对生成** `m_scroller`（Scroller 组件）+ `m_scrollerController`（`new FancyScrollerController()` + `Initialize` + `OnDestroy` 中 `Dispose`）。
- `CellContainer` **无需 m_ 前缀**——它不作为独立绑定字段，而是由 `FancyScrollerView` 持有，运行时由 Controller 读取。

> ⚠️ **必须挂 `FancyScrollerView`**：`FancyScrollerController.Initialize` 通过 `(scroller as FancyScrollerView)?.CellContainer` 取容器。若 `m_scroller` 只挂基类 `Scroller`（旧 prefab 常见），转型得 null，抛 `InvalidOperationException: Scroller 'xxx' is not a FancyScrollerView...`。修复：改挂 `FancyScrollerView` 并赋值 `CellContainer`。

## Cell 走 UIBindComponent 自动生成（无基类 partial 模式）

`FancyCellWidget<TItemData,TContext>` 的基类是**双泛型**，ScriptGenerator 的泛型模式（`T<类名>`）表达不了，故采用「无基类 partial」：基类声明放业务文件，`_Gen.g.cs` 不带基类，C# partial 合并后基类生效。

| 文件 | 基类声明 | 内容 |
|------|---------|------|
| `XxxCell_Gen.g.cs`（生成）| 无（`public partial class XxxCell`）| 节点绑定字段 + `ScriptGenerator()` + 事件 partial 声明 |
| `XxxCell.cs`（业务）| `: FancyCellWidget<TItemData,TContext>` | 泛型基类 + `BindMemberProperty`（根节点/MVE）+ `BindData`/`UpdatePosition` |

partial 合并后基类为 `FancyCellWidget<TItemData,TContext>`，`override ScriptGenerator` 合法。

**启用方式**：
1. `ScriptGeneratorSetting.asset` 的 `UIGenTypes` 配置 `FancyCellWidget`(`noBaseClass=true`)
2. Cell prefab 的 UIBindComponent 选 UI 类型为 `FancyCellWidget`
3. Inspector「生成脚本」生成 `_Gen.g.cs`

```csharp
// Example01_BasicCell_Gen.g.cs（自动生成，无基类）
public partial class Example01_BasicCell
{
    private UIBindComponent m_bindComponent;
    private Image m_imgBackground = null!;
    private Text m_textMessage = null!;

    protected override void ScriptGenerator()
    {
        m_bindComponent = gameObject.GetComponent<UIBindComponent>();
        if (m_bindComponent == null) { Log.Error(...); return; }
        m_imgBackground = m_bindComponent.GetComponent<Image>(0);
        m_textMessage = m_bindComponent.GetComponent<Text>(1);
    }
}

// Example01_BasicCell.cs（业务，声明泛型基类）
public partial class Example01_BasicCell : FancyCellWidget<Example01_BasicItemData, NullContext>
{
    protected override void BindMemberProperty() { /* 根节点 Animator + MVE */ }
    protected override void BindData(Example01_BasicItemData data) { /* 写响应式属性 */ }
    public override void UpdatePosition(float normalizedPosition) { /* 滚动动画 */ }
}
```

## 根节点组件：生成器不遍历根节点

`AutoErgodic` 只遍历子节点，不处理根节点本身。Cell 根节点的 `Animator`/`CanvasGroup` 等组件无法自动绑定，业务在 **`BindMemberProperty()`** 手写（钩子顺序在 `ScriptGenerator` 之后，子节点字段已就绪）。

UIWindow/UIWidget 生命周期钩子顺序（`UIWidget.CreateImp`）：
```
Inject → ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate → OnRefresh
```

## Cell 的 MVE 数据绑定

Cell 继承 `UIWidget`，MVE 用法与 UIWindow 一致，但绑定位置不同：

| 项 | UIWindow | Cell |
|----|---------|------|
| 响应式属性创建 | `SetupBindings()` 中 `Bind()` | `BindMemberProperty()` 中 `CreateProperty<T>()` |
| 数据写入触发刷新 | Service 改 Model → Bind 回调 | `BindData(data)` 写 `property.Value` |
| 列表数据来源 | DataContext MapProperty | UIWindow 调 `m_scrollerController.UpdateData(items)` |

```csharp
private BindableProperty<string> _displayText;

protected override void BindMemberProperty()
{
    m_animator = gameObject.GetComponent<Animator>();      // 根节点手动绑定
    _displayText = CreateProperty<string>("");              // 随 Cell 生命周期自动 Dispose
    BindText(m_textMessage, _displayText);                  // m_textMessage 来自 _Gen.g.cs
}

protected override void BindData(Example01_BasicItemData data)
{
    _displayText.Value = data.DisplayText;                  // 写入触发 UI 刷新
}
```

- `CreateProperty<T>()` 返回的 `BindableProperty` 随 Cell 生命周期自动 Dispose。
- Cell 复用时 `BindData` 必须重置瞬态状态（选中态、加载状态等）。
- 列表数据更新：UIWindow 调 `m_scrollerController.UpdateData(itemList)`（注意是 `UpdateData`，非旧版 `UpdateContents`）。

## 完整工作流（改 prefab → 重新生成 _Gen）

1. 改 prefab 节点（增删/改名/改组件）
2. UIBindComponent Inspector「重新绑定组件」（`GenerateUIComponentScript`，重遍历子节点填充 `m_components`）
3. 同 Inspector「生成脚本」（`GenerateCSharpScript`，`isAutoGenerate=true` 写 `_Gen.g.cs`）
4. 业务文件实现新增的 `partial void OnClickXxxBtn()` 等

**UIWindow 侧使用**（Example01_BasicUI）：
```csharp
protected override async void OnCreate()
{
    m_scrollerController.SetCellFactory(() => new Example01_BasicCell());   // _Gen 字段
    await m_scrollerController.LoadCellPrefabAsync("Example01_BasicCell");
    m_scrollerController.UpdateData(Singleton<Example01_BasicService>.Instance.BuildItems());
}
```

> **MCP 自动化**：`GenerateCSharpScript`/`GenerateUIComponentScript` 依赖 `Selection.activeTransform`。`execute_code` 中 `PrefabUtility.LoadPrefabContents` → `Selection.activeTransform = contents.transform` → 调用 → `SaveAsPrefabAsset`（持久化 `GenerateUIComponentScript` 对实例的修改）→ `UnloadPrefabContents`。`GenerateCSharpScript` 末尾 `AssetDatabase.Refresh` 会触发 Domain reload，可能短暂断开 MCP。

## 节点命名 → 生成标识符

ScriptGenerator 把节点名转为字段名/方法名，**只去首个 `m_` 前缀**，不识别 `m_btn_` 等复合前缀。描述段必须 PascalCase、不加下划线：

| 节点名 | 字段名 | 按钮方法名 | 评价 |
|--------|--------|-----------|------|
| `m_btnClose` | `m_btnClose` | `OnClickCloseBtn` | ✅ |
| `m_btn_Close` | `m_btn_Close` | `OnClick_CloseBtn` | ❌ 下划线残留 |

各类节点生成方法名：Button→`OnClickXxxBtn`、Toggle→`OnXxxToggle`、Slider→`OnXxxSlider`、TMP_Dropdown→`OnXxxDropdown`。

## Cell 点击冒泡

Cell 内 `NotifyItemClicked()` / `NotifyButtonClicked()` 冒泡到 `FancyScrollerController.OnCellItemClicked` / `OnCellButtonClicked`，UIWindow 订阅后调用 Service 或发送 GameEvent，实现解耦。

## 常见错误

| 错误现象 | 根因 | 修复 |
|---------|------|------|
| `InvalidOperationException: Scroller is not a FancyScrollerView...` | `m_scroller` 只挂基类 `Scroller`，缺 `FancyScrollerView` | 改挂 `FancyScrollerView` 并赋 `CellContainer`（或用 `GameObject/UI/Scroller` 重建） |
| 生成方法名带多余下划线（如 `OnClick_CloseBtn`） | 节点名 `m_btn_Close`（复合前缀下划线） | 改为 `m_btnClose`（PascalCase 无下划线） |
| Cell 的 `m_textMessage` 等「未定义」 | `_Gen.g.cs` 未生成或未选对 UI 类型 | Cell prefab 的 UIBindComponent 选 `FancyCellWidget` 类型并生成 `_Gen.g.cs` |
| Animator 等根节点组件绑定丢失 | 生成器不遍历根节点 | 在 `BindMemberProperty()` 手写 `GetComponent` |
| Cell 的 MVE 绑定写在 `ScriptGenerator` | `ScriptGenerator` 已被 `_Gen.g.cs` 占用 | 移到 `BindMemberProperty()` |

## 关联文档

- [UI脚本生成器](UI脚本生成器.md)（节点绑定、命名→方法名、noBaseClass）
- [UI数据绑定](UI数据绑定.md)（MVE 四层、BindableProperty、BindText）
- [UI系统](UI系统.md)（UIWindow/UIWidget 生命周期）
