# RedDotGraph 红点模块

基于 **DAG（有向无环图）** 的红点提示系统，源自 fermsi/RedDotGraph，经 Ljdmw 项目生产化演进后同步至 TEngine。支持六态红点、多父聚合、覆盖优先级、帧绑定回调、运行时动态节点、配置未就绪缓冲与可视化图编辑器。

## 目录结构

```
Module/RedDotGraph/
├── RedDotGraphModule.cs        TEngine 集成层（Singleton）：配置加载/checker 收口/ILateUpdate 帧末驱动
├── Runtime/
│   ├── Dag/                    DAG.cs 环检测
│   ├── DigraphList/            邻接表图结构（顶点/边/算术 partial）
│   ├── RedDot/                 核心运行时
│   │   ├── RedDotManager.cs            门面（Ins() 手写单例）：AddUI/DataChange/动态节点/门控/缓冲
│   │   ├── RedDotManager_internal.cs   帧绑定存储/派发/巡检/私有状态
│   │   ├── RedDotManager_config.cs     JSON 建图/加载防御/常量表对账
│   │   ├── RedDotGraph.cs              状态聚合（掩码过滤+优先级归一）+ 脏传播（BFS）
│   │   ├── BaseRedDotGraph.cs          key→rank 字典/删点删边/父链收集
│   │   ├── RedDotConst.cs              六态枚举 + RedDotStatusRules 优先级权威
│   │   ├── RedDotExternalLogicVisitor.cs  checker 两张委托表
│   │   ├── RedDotKey.cs / RedDotKeys.cs   key 常量元数据 + 生成物
│   │   └── RedDotExportJson.cs         导出 JSON 结构
│   └── UI/                     帧节点（FrameNode/RedDotUIInfo）
└── LICENSE.md
```

业务组件（`System/Common/`）：`RedDotWidget`（UIWidget 六态渲染）+ `RedDotKeyBinder`（语义 key 序列化载体）。编辑器（`Assets/Editor/RedDotGraph/`）：GraphView 编辑器/分组子图/Keys 生成器/导出门禁。

## 六态与优先级

`RedDotStatus` 位值：红点 1、蓝点 2、红数字 4、蓝数字 8、新 16、满 32。

**覆盖优先级（高→低）：满 > 红点 > 蓝点 > 红数字 > 蓝数字 > 新**，唯一权威是 `RedDotStatusRules.PriorityOf`（位值与优先级解耦，禁止直接比较位值）。聚合规则：子贡献 = 子状态 & 父掩码，零不采纳；同数字态叠加 Value，跨态按优先级取高；归一后运行时结果恒为单态。

## 快速上手

### 1. 编辑器建图

1. Project 右键 `Create > 红点图`，双击进入 GraphView 编辑器
2. 节点配置：key（Key 字典）、逻辑 ID（externalId 字典，-1=聚合）、六态勾选（掩码）
3. 分组子图：框选归组，组根对外，主图放引用节点拼接；`dynamicMount` 组运行时 `SetDynamicParent` 挂载
4. 导出：窗口 Export 落盘运行时 JSON（标准地址 `Assets/AssetRaw/Configs/Other/RedDotGraphConfig.json`，文件名即 YooAsset 地址 `RedDotGraphConfig`），并同步再生成 key 常量表 `RedDotKeys.cs`

### 2. 业务接入（正式路径，全部走模块收口）

```csharp
// checker 注册（P0-5 收口:key 常量携带 externalId,业务零魔法数;聚合节点不可注册）
RedDotGraphModule.Instance.RegisterChecker(RedDotKeys.Mail_Unread, (out RedDotStatus s, out int n) =>
{
    int count = MailModule.UnreadCount;
    s = count > 0 ? RedDotStatus.RedDotStatusRedNumber : RedDotStatus.RedDotStatusNull;
    n = count;
});

// 报脏（配置未就绪期由 Manager 缓冲,加载完成后 FIFO 重放）
RedDotGraphModule.Instance.DataChange(RedDotKeys.Mail_Unread);

// UI 绑定:优先用 RedDotWidget（见下）;手工绑定时:
RedDotGraphModule.Instance.AddUI(RedDotKeys.Mail_Unread, OnRedDotChanged);
RedDotGraphModule.Instance.GetRedDotStatus(RedDotKeys.Mail_Unread, 0, out var status, out var num);
RedDotGraphModule.Instance.RemoveUI(RedDotKeys.Mail_Unread, OnRedDotChanged);
```

配置由 `RedDotGraphModule.OnInit` 自动从 YooAsset 加载（`AddJsonStr` + 一致性自检 + `SetConfigReady` 重放），业务不接触 JSON。

### 3. RedDotWidget 业务组件（推荐）

```csharp
// prefab 固定绑定:widget 节点挂 RedDotKeyBinder（Inspector 下拉选 RedDotKeys）,
// UIBindComponent 八槽位:0=goFullDot 1=goDotTips 2=goReddot 3=goReddotNum
//                      4=tmpReddot(TMP) 5=goBluedot 6=goBluedotNum 7=tmpBluedot(TMP)
// 动态绑定(列表复用/Tab):SetKey 运行时换绑,空串=解绑熄灭
m_widget.SetKey(RedDotKeys.Mail_Unread, cellId);
// 本地直控(不入树,纯表现):SetLocalState / SetLocalVisible
```

## 运行时行为

- **帧绑定回调**：`AddFrame/ChangeToFrame` 切换全屏面板帧，`AddUI` 只注册到当前帧；模块采用单帧全局模式（`AddFrame("Global")` 一次，被盖窗口照常收通知）
- **刷新模式**：`Immediate`（DataChange 同帧派发）与 `Batched`（入队，`FlushPendingChanges` 每帧至多 `MaxDispatchPerFrame` 项削峰）。项目侧默认 Batched，模块实现 ILateUpdate 帧末驱动冲刷（SingletonSystem 随 Retain/Release 自动注册/注销，帧号判重防御）
- **配置缓冲**：`BeginConfigBuffering/SetConfigReady`，未就绪期 DataChange/AddUI/RemoveUI/SetNodeEnabled 暂存，就绪后 FIFO 重放
- **节点门控**：`SetNodeEnabled(key, false)` 禁用吞 DataChange、不作 BFS 中继、聚合跳过、读回 Null
- **动态节点**：`RegisterDynamicNode(ownerKey, out dynKey)`（幂等，`dyn:` 前缀）→ `SetDynamicParent`（未就绪自动登记重放）→ `BindDynamicChecker` → `RemoveDynamicNode/RemoveDynamicParent`（父链逐 id 补发重算）
- **cell 汇总**：`SummarizeCells(key, idsProvider, mask)` 一行接入列表 per-id 聚合；列表刷新后 `ReleaseCellIds(key, keep)` 收敛槽位

## 集成约定

- 命名空间：运行时 `RedDot.Runtime.RedDot`，编辑器 `RedDot.Editor.*`，集成层 `GameLogic.RedDotGraphModule`
- 业务只认 `RedDotKeys.XXX` 常量与 `RedDotGraphModule` 收口，禁止直连 `RedDotManager`（绕过缓冲/自检）
- 启动链调用 `RedDotGraphModule.Instance.EnsureStarted()`：重登/切号走 `Reinitialize()` 全量重建
- JSON（热更）与 RedDotKeys（热更 dll）版本错配在加载期对账即报（`ValidateKeysConsistency`）

## Demo 与测试

- Demo：场景 `Assets/Scenes/RedDotDemoScene.unity`，菜单 `TEngine/RedDot/Build Demo Scene` 一键构建；说明见 `Assets/GameScripts/HotFix/GameLogic/UI/RedDotDemo/README.md`
- 测试：`Assets/TEngine/Tests/EditMode/RedDotModule/`（聚合/优先级/菱形、Visitor、帧绑定/刷新模式/缓冲/门控、动态节点）
