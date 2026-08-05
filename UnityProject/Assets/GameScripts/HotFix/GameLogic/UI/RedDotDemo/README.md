# 红点系统 Demo（邮件系统）

展示红点系统 4 大核心特性的可交互 Demo。

## 快速运行

1. 打开场景 `Assets/Scenes/RedDotDemoScene.unity`
2. 点击 ▶ 进入 PlayMode
3. 操作右侧 5 个按钮观察红点变化

## 展示的特性

| 特性 | 演示方式 |
|------|----------|
| **父子聚合 + 数量求和** | `Root` 聚合 `Mail`，`Mail` 聚合 `Mail.Unread` 的数值 |
| **多状态 + 优先级** | `Mail` 允许 4 种状态，有 New 邮件时 New(4) 覆盖 Number(2)/Normal(1) |
| **动态叶子增删** | `Mail.Item.{id}` 用 AddLeaf/RemoveLeaf 运行时增删 |
| **UI 按钮交互触发** | 5 个按钮调用 MarkDirty/AddLeaf/RemoveLeaf/ClearLeavesUnder |

## 红点树结构

```
Root (聚合, Number)                              ← 总未读数
└── Mail (聚合, Normal|Number|New|Full = 15)     ← 邮件入口，按最高优先级显示
    ├── Mail.Unread    (静态叶子, Number, id=1)  ← 未读普通邮件计数
    ├── Mail.Announce  (静态叶子, Normal, id=2)  ← 系统公告开关
    └── Mail.Item.{N}  (动态叶子, New, id=3)     ← 新功能邮件（运行时增删）
```

## 交互按钮

| 按钮 | 行为 | 预期红点变化 |
|------|------|-------------|
| **+1 未读邮件** | `UnreadCount++` + `MarkDirty("Mail.Unread")` | Root/Mail/Mail.Unread 数字 +1 |
| **读取一封** | `UnreadCount--` + `MarkDirty("Mail.Unread")` | 数字 -1（到 0 红点消失）|
| **新增 New 邮件** | `AddLeaf("Mail.Item.N","Mail",3,New)` | Mail 切换为 **New** 状态（红点无数字）|
| **清空 New 邮件** | `ClearLeavesUnder("Mail")` | Mail 回到 Number 状态 |
| **切换公告** | `HasAnnounce` toggle + `MarkDirty("Mail.Announce")` | Mail.Announce 红点显隐 |

## 文件结构

```
Assets/
├── Scenes/RedDotDemoScene.unity          # 独立场景（Main Camera + UIRoot + GameEntry）
├── AssetRaw/UI/RedDotDemoUI.prefab       # 主界面 prefab
├── TEngine/Settings/ProcedureSetting_RedDot.asset  # 流程配置（指向 ProcedureRedDotDemo）
└── GameScripts/
    ├── Procedure/ProcedureRedDotDemo.cs   # 流程入口（初始化资源+红点+显示UI）
    └── HotFix/GameLogic/UI/RedDotDemo/
        ├── MailDemoData.cs                # 模拟邮件数据（Singleton）
        ├── RedDotDemoUI.cs                # 主界面（[Window]，4 红点绑定 + 5 按钮）
        ├── RedDotDemoBoot.cs              # MonoBehaviour 入口（备选方案，未使用）
        └── README.md                      # 本文档

Assets/GameScripts/HotFix/GameLogic/Module/RedDotModule/Editor/DemoBuilder/
└── RedDotDemoBuilder.cs                   # 一键构建工具（菜单 TEngine/RedDot/Build Demo Scene）
```

## 重新构建 Demo

如果修改了 UI 结构，运行菜单 `TEngine → RedDot → Build Demo Scene` 重新生成 prefab 和场景。

## 启动流程

```
场景加载 → GameEntry 启动 → ProcedureSystem 用 ProcedureSetting_RedDot
  → ProcedureRedDotDemo.OnEnter
    → 初始化 YooAsset 资源包
    → RedDotSystem.Instance
      → LoadJsonText(内嵌JSON)
      → RegLogic(3个叶子逻辑)
      → RefreshAll()           ← 计算初始红点状态
    → ShowUIAsync<RedDotDemoUI>()
      → RedDotDemoUI.SetupBindings  ← Bind(node.State,...) 订阅 4 个红点节点
      → MVE 自动刷新 UI 显隐
```

## 关键技术点

### MVE 数据绑定（核心）
RedDotDemoUI 在 `SetupBindings` 里对每个红点节点调用 `Bind(node.State, callback)`：
- `node.State` 是 `BindableProperty<RedDotNodeState>`
- 状态变化时（`MarkDirty` → 图聚合 → `ApplyRefreshedState` → `State.Value = ...`）自动触发回调
- UIWindow 销毁时 `RemoveAllBindings` 自动解绑，无泄漏

### CanvasScaler scale=0 坑
prefab 根是 Canvas + CanvasScaler 时，`SaveAsPrefabAsset` 会因 prefab 无 Canvas 父级而把根 RectTransform 的 scale 置 0。`RedDotDemoBuilder.FixRootRectTransformAfterSave` 用 SerializedObject 在保存后强制修正。
