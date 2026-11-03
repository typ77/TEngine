# 红点系统 Demo（RedDotGraph 六态图版）

展示 RedDotGraph 新图核心能力的可交互 Demo：六态红点、聚合优先级、分组子图、动态节点、节点门控。

## 快速运行

1. 菜单 `TEngine → RedDot → Build Demo Scene` 生成/刷新 prefab 与场景
2. 打开场景 `Assets/Scenes/RedDotDemoScene.unity`
3. 点击 ▶ 进入 PlayMode
4. 操作右侧按钮观察左侧各行红点变化（Batched 模式下由模块帧驱动冲刷,下一帧生效）

## 红点图结构

源资产 `Assets/Editor/RedDotGraph/RedDotDemo.reddotgraph`（含分组 ActivityGroup）,导出 JSON 位于
`Assets/AssetRaw/Configs/Other/RedDotGraphConfig.json`（与编辑器 RedDotWindow 导出按钮的默认落点一致,
运行时由模块经 YooAsset 按文件名 `RedDotGraphConfig` 寻址加载）,为分组展平后的运行时结构:

```
Root (聚合, 掩码 63 全态)
├── Shop (聚合, 32 满)
│   └── Shop.LimitTime    (满 32, externalId=3)      ← 限时商店满溢
├── Activity (聚合, 8|16,分组 ActivityGroup 组根)     ← 主图经引用节点拼接
│   ├── Activity.SignIn   (新 16, externalId=4)      ← 每日签到"新"标签
│   └── Activity.Welfare  (蓝数字 8, externalId=5)    ← 福利领取数
└── Mail (聚合, 1|2|4)
    ├── Mail.Unread       (红数字 4, externalId=1)   ← 未读邮件数
    ├── Mail.Announce     (红点|蓝点 3, externalId=2) ← 公告开关双态
    └── dyn:act:{N}       (动态节点,运行时增删)
```

初始态设计:未读 2(红数字)、公告开(红点)、限时商店满(满)、签到未领(新)、福利 3(蓝数字)——
Root 依覆盖优先级(满>红点>蓝点>红数字>蓝数字>新)显示"满",Mail 显示"红点"(红点优先于红数字)。

## 交互按钮

| 按钮 | 行为 | 预期变化 |
|------|------|---------|
| +1 未读邮件 / 读取一封 | UnreadCount±1 + DataChange(Mail.Unread) | 三级行红数字 +1/-1,归零熄灭 |
| 公告 红→蓝→关 | AnnounceMode 循环 + DataChange(Mail.Announce) | Mail.Announce 红点/蓝点/熄灭 |
| 限时商店 满/空 | ShopFull toggle + DataChange(Shop.LimitTime) | 满态点亮/熄灭,Root 被满覆盖 |
| 签到 领取/重置 | SignInUnclaimed toggle + DataChange(Activity.SignIn) | "新"标签显隐 |
| +1/-1 福利 | WelfareCount±1 + DataChange(Activity.Welfare) | 蓝数字增减 |
| 新增动态节点 | RegisterDynamicNode→SetDynamicParent(挂 Mail)→BindDynamicChecker→DataChange→行 SetKey 换绑 | Mail 多一个红点子贡献 |
| 移除动态节点 | RemoveDynamicNode(摘边/摘 checker) | 父链补发重算,行回退绑定上一节点 |
| 门控 禁用/启用 Welfare | SetNodeEnabled(Activity.Welfare, …) | 禁用吞报脏+读回 Null,启用恢复聚合 |

## 文件结构

```
Assets/
├── Scenes/RedDotDemoScene.unity                   # 独立场景（Main Camera + UIRoot + GameEntry）
├── AssetRaw/UI/RedDotDemoUI.prefab                # 主界面 prefab（8 个 RedDotWidget 行,六态 8 槽位）
├── TEngine/Settings/ProcedureSetting_RedDot.asset # 流程配置（指向 ProcedureRedDotDemo）
├── Editor/RedDotGraph/
│   ├── RedDotDemo.reddotgraph                     # 红点图源资产（含分组 ActivityGroup）
│   └── DemoBuilder/RedDotDemoBuilder.cs           # 一键构建工具（菜单 TEngine/RedDot/Build Demo Scene）
└── GameScripts/
    ├── Procedure/ProcedureRedDotDemo.cs           # 流程入口（资源包+模块 YooAsset 建图+checker+UI）
    └── HotFix/GameLogic/UI/RedDotDemo/
        ├── RedDotDemoUI.cs                        # 主界面（RedDotWidget 六态渲染 + 动态行 SetKey）
        ├── MailDemoData.cs                        # 模拟数据（五叶数据源 + 动态期次）
        ├── RedDotDemoLogicIds.cs                  # externalId 注释对照常量
        └── README.md                              # 本文档
```

## 启动流程

```
场景加载 → GameEntry 启动 → ProcedureSystem 用 ProcedureSetting_RedDot
  → ProcedureRedDotDemo.OnEnter
    → 初始化 YooAsset 资源包（InitPackage + 版本 + 清单）
    → RegisterChecker ×5（经 RedDotGraphModule 收口,首次触达触发模块 OnInit:
      配置缓冲 + AddFrame(Global) + Batched + ILateUpdate 帧末驱动 + 异步加载 YooAsset 配置）
    → DataChange ×5                              ← 初始报脏（未就绪期缓冲,就绪后重放）
    → ShowUIAsync<RedDotDemoUI>()
      → RedDotWidget 逐行创建（binder key 固定行自动绑定,动态行 SetKey）
      → AddUI 注册即缓冲 → 配置加载完成 SetConfigReady 按 FIFO 重放
      → 回调内 GetRedDotStatus 读时刷新 → RenderState 六态渲染
```

## 关键技术点

### RedDotWidget 六态渲染
每行是独立 `RedDotWidget`（UIWidget）：`UIBindComponent` 八槽位（0=goFullDot 1=goDotTips 2=goReddot 3=goReddotNum 4=tmpReddot 5=goBluedot 6=goBluedotNum 7=tmpBluedot），`RenderState` 先全熄再按单态点亮；数字经 TMP 零分配 `SetText` 重载,99+ 封顶。

### 动态节点演示
活动期次场景：`RegisterDynamicNode(ownerKey)` 幂等分配 `dyn:` key → `SetDynamicParent` 挂到 Mail → `BindDynamicChecker` 绑该期次 checker → `RemoveDynamicNode` 摘边摘 checker 并对父链补发。演示行用 `SetKey` 换绑展示最新节点。

### TMP 字体兜底
工程未导入 TMP Essentials 时,数字文本无字体可渲染;`RedDotDemoUI.EnsureTmpFont` 在运行时用内置 LegacyRuntime.ttf 生成动态字体资产兜底（进程级缓存一份）。导入 TMP Essentials 后重建 prefab 即走默认字体。

### CanvasScaler scale=0 坑
prefab 根是 Canvas + CanvasScaler 时，`SaveAsPrefabAsset` 会因 prefab 无 Canvas 父级而把根 RectTransform 的 scale 置 0。`RedDotDemoBuilder.FixRootRectTransformAfterSave` 用 SerializedObject 在保存后强制修正。
