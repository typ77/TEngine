# RedDotModule — 红点系统

基于 DAG（有向无环图）的红点系统，移植自 [fermsi/RedDotGraph](https://github.com/fermsi/RedDotGraph)，适配 TEngine 的 MVE 数据绑定体系。

## 特性

- **DAG 架构**：支持多父节点（菱形依赖 A→B, A→C, B→D, C→D），内置环检测
- **多状态红点**：普通 / 数量 / 新 / 满（位掩码 + 优先级，自动取最高）
- **MVE 接入**：每个节点一个 `BindableProperty<RedDotNodeState>`，UIWidget `Bind()` 自动刷新
- **脏传播 + 帧末批处理**：标脏 BFS 向上传播，`ILateUpdate` 帧末统一刷新，避免中间态抖动
- **动态叶子增删**：列表项红点（邮件/任务/商品）运行时增删，对象池复用，软删除不触发全图重映射
- **配置驱动**：JSON 配置红点树结构（编辑器导出，见阶段 5）

## 架构

```
Core/                    纯逻辑层（移植自 fermsi，几乎零 Unity 依赖）
├── Graph/               底层图结构（DiGraphList + DAG 环检测）
├── RedDotConst.cs       状态枚举（位掩码）
├── RedDotVertex.cs      顶点 + RuntimeInfo（按业务 id 分桶）
├── RedDotGraph.cs       核心：状态聚合（DFS）+ 脏传播（BFS）
├── BaseRedDotGraph.cs   key→rank 字典 + 软删除支持
├── RedDotExternalLogicVisitor.cs  叶子逻辑注册器
└── RedDotExportJson.cs  JSON 配置结构

RedDotNodeState.cs       readonly struct（Status + Count，接入 MVE）
RedDotNode.cs            节点门面（持 BindableProperty）
RedDotSystem.cs          Singleton<RedDotSystem> + ILateUpdate（对门外壳）
RedDotWidget.cs          UIWidget 组件（挂在 prefab 自动刷新）
```

## 快速上手

### 1. 配置红点树（JSON）

```json
{
  "vertexs": [
    { "key": "Root",       "redDotType": 2, "externalId": -1 },
    { "key": "Root.Mail",  "redDotType": 2, "externalId": 1 },
    { "key": "Root.Shop",  "redDotType": 1, "externalId": -1 }
  ],
  "edges": [
    { "key": "Root", "outKeys": ["Root.Mail", "Root.Shop"] }
  ]
}
```

字段说明：
- `key`：红点全局唯一标识
- `redDotType`：允许的状态位掩码（Normal=1 | Number=2 | New=4 | Full=8，可组合）
- `externalId`：叶子逻辑 ID（-1 表示聚合节点，由子节点聚合）
- `edges`：父→子关系（`outKeys` 是子节点列表）

### 2. 初始化

```csharp
// 在游戏启动时（如 GameApp.StartGameLogic）
var sys = RedDotSystem.Instance;

// 加载配置
sys.LoadJsonAsset("Assets/AssetRaw/Config/reddot");  // 走资源系统
// 或 sys.LoadJsonText(jsonString);

// 注册叶子逻辑（externalId 对应配置里的值）
sys.RegLogic(1, (out RedDotStatus s, out int n) =>
{
    int unread = MailModule.UnreadCount;
    s = unread > 0 ? RedDotStatus.Number : RedDotStatus.Null;
    n = unread;
});

// 配置加载后全量刷新一次（让所有节点状态正确计算）
sys.RefreshAll();
```

### 3. UI 绑定（RedDotWidget）

在 prefab 上挂 `RedDotWidget` 组件：
- `RedDot Key`：填配置里的 key（如 `Root.Mail`）
- `Dot Obj`：拖入红点 GameObject（显隐控制）
- `Num Text`：拖入 TMP 数字文本（可选，仅 Number 状态显示）

刷新全自动：状态变化时 MVE 的 `BindableProperty` 触发回调，UIWidget 销毁时绑定自动解除。

> 手动绑定（不用 RedDotWidget）：
> ```csharp
> protected override void SetupBindings() {
>     var node = RedDotSystem.Instance.GetNode("Root.Mail");
>     Bind(node.State, state => {
>         m_dotObj.SetActive(state.IsActive);
>     });
> }
> ```

### 4. 触发刷新

```csharp
// 业务数据变化时（如收到新邮件）
RedDotSystem.Instance.MarkDirty("Root.Mail");

// 帧末自动 Flush（ILateUpdate 驱动），无需手动调用
```

### 5. 动态叶子增删（列表项红点）

```csharp
// 打开邮件面板：为每封邮件添加叶子红点
foreach (var mail in mails)
{
    RedDotSystem.Instance.AddLeaf(
        key: $"Root.Mail.Item.{mail.Id}",
        parentKey: "Root.Mail",
        logicId: MAIL_ITEM_LOGIC_ID,
        allowed: RedDotStatus.Number);
}

// 关闭面板：批量清理
RedDotSystem.Instance.ClearLeavesUnder("Root.Mail");
```

### 6. 列表项红点（带 id，不用动态建节点）

固定一个叶子节点 + 业务 id：
```csharp
// 订阅某 id 的红点
RedDotSystem.Instance.AddIdListener("Root.Mail.Item", mailId, (status, value) => {
    // 刷新该项 UI
});

// 数据变化时
RedDotSystem.Instance.MarkDirty("Root.Mail.Item", mailId);
```

## 核心 API

| 方法 | 说明 |
|------|------|
| `LoadJsonText/LoadJsonAsset/LoadJsonAssetAsync` | 加载红点树配置 |
| `RegLogic(id, delegate)` | 注册叶子判断逻辑 |
| `GetNode(key)` | 获取节点门面（含 `BindableProperty<RedDotNodeState> State`） |
| `MarkDirty(key)` / `MarkDirty(key, id)` | 标记数据变更（触发脏传播 + 帧末刷新） |
| `RefreshAll()` | 全量刷新（配置加载后调用） |
| `AddLeaf/RemoveLeaf/ClearLeavesUnder` | 动态叶子增删 |
| `AddIdListener/RemoveIdListener` | 带 id 的列表项红点订阅 |

## 关键设计点

### DataChange 的正确语义（易错点）

`MarkDirty(key)` 标记的是"**数据源变更**的节点"。对于叶子，触发其 visitor 重算；BFS 沿入边向上传播到所有祖先，让它们重新聚合。

**陷阱**：从父节点调 `MarkDirty` **不会**让子节点重算。要让父重算，应标脏**叶子**（真正的数据源）。

### 软删除（动态叶子）

不调用 `DiGraphList.Remove`（会触发 O(V+E) 全图 rank 重映射），而是：
1. 断开父→子边（O(度数)）
2. 从 key 映射移除
3. 顶点归还对象池（List 里的槽位保留为孤儿，避免 rank 漂移）

聚合节点（系统入口、页签）静态配置，运行时不可删。

### EditMode 测试

`RedDotSystem` 是 `ILateUpdate`，首次 `Instance` 初始化会触发 `SingletonSystem.CheckInit` → `UpdateDriver.DontDestroyOnLoad`（仅 PlayMode 可用）。测试用 `RedDotSystem.CreateForTest()` 绕过。

## 测试

EditMode 测试 43 例（全通过），覆盖：
- 图结构与环检测、状态聚合（数量求和/状态优先级/三层级联）、脏传播
- MVE 端到端链路、带 id 列表项
- 动态叶子增删、池化复用、静态节点保护

## GraphView 可视化编辑器

红点树结构可通过可视化图编辑器编辑，导出 JSON 供运行时加载。

### 使用流程

1. **创建图资产**：Project 窗口右键 `Create > Red Dot Graph`，生成 `.reddotgraph` 文件
2. **打开编辑器**：双击 `.reddotgraph` 文件，或选中后在 Inspector 点 `Open Red Dot Editor`
3. **编辑节点**：
   - 右键空白处 → `Create RedDot Node` 创建节点
   - 节点上配置：红点 key（下拉）、逻辑ID（下拉）、可存在状态（Normal/New/Number/Full Toggle）
   - 从父节点 OutPort("子节点") 拖线到子节点 InPort 建立父子关系（自动环检测）
4. **管理 Key/逻辑ID**：工具栏 `Key设置` / `逻辑ID设置` 打开字典管理窗口（增删条目、生成枚举代码）
5. **保存**：`保存` 按钮写回 `.reddotgraph`
6. **导出运行时配置**：`导出红点配置` 按钮生成 `.json`，运行时用 `LoadJsonAsset` 加载

### 编辑器架构（Editor/ 目录，独立 asmdef `GameLogic.RedDot.Editor`）

```
Editor/
├── RedDotWindow.cs              主编辑器窗口（加载/保存/导出）
├── RedDotEnumDictWindow.cs      Key/逻辑ID 字典管理窗口
├── RedDotSearchWindowProvider.cs 右键创建节点搜索
├── Config/RedDotEditorConfig.cs  扩展名/状态图标配置
├── Data/
│   ├── RedDotGraphData.cs       图数据模型（序列化+建图+导出JSON）
│   ├── RedDotNodeContext.cs     节点上下文
│   ├── RedDotEnumDict.cs        Key/逻辑ID 字典（+代码生成）
│   └── EditorRedDotGraph.cs     编辑器图（继承运行时 BaseRedDotGraph，含顶点删除）
├── Views/
│   ├── RedDotGraphView.cs       GraphView 主视图
│   ├── RedDotNodeView.cs        节点视图
│   ├── RedDotNodeStatusItem.cs  状态切换项
│   └── RedDotEnumSettingItem.cs 字典条目项
├── Importers/
│   ├── RedDotGraphImporter.cs   .reddotgraph ScriptedImporter
│   ├── RedDotGraphImporterEditor.cs 双击打开 + Inspector 按钮
│   └── RedDotGraphAssetPostProcessor.cs 资产变更监听
├── AssetsCallback/              Create 菜单
└── Util/                        FileUtilities / PathUtil
```

> 移植说明：原 fermsi 编辑器依赖 6 个 USS/UXML 资源文件。移植版本改为**纯代码构建 UI**（VisualElement 直接构造），去掉了 USS/UXML 依赖。可选的 USS 样式（`Node.uss`/`Label.uss`）若存在会加载，缺失则用默认样式。

### 图标资源（可选）

编辑器引用以下图标（位于任一 `Resources/Icons/` 目录下，缺失时图标位置为空不影响功能）：
- `icon_normal.png` / `icon_new.png` / `icon_number.png` / `icon_full.png`（节点状态图标）
- `icon_reddot_title.png`（窗口标题图标）

可从 fermsi/RedDotGraph 仓库的 `Assets/RedDot/Editor/Resources/Icons/` 复制。
