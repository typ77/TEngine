# TEngine 红点系统（RedDotGraph）速查

> **适用场景**：接入红点、注册 checker、UI 绑定红点、动态节点（列表项/期次）、编辑器维护红点图 | **关联文档**：[ui-patterns.md](ui-patterns.md)（RedDotWidget 属 UIWidget 体系）、[event-system.md](event-system.md)（数据变更点驱动报脏） | **详细文档**：`Books/3-9-红点模块.md`

## 架构一图流

```
业务数据变更 ──DataChange(RedDotKeys.XXX)──► RedDotGraphModule（TEngine 集成层,Singleton）
                                              │ YooAsset 加载 RedDotGraphConfig → AddJsonStr 建图
                                              ▼
                                    RedDotManager（运行时库 RedDot.Runtime.RedDot）
                                              │ DFS 向上聚合 + 六态优先级归一 + Batched 帧末冲刷(ILateUpdate)
                                              ▼
UI 绑定 ◄──AddUI(key, cb, id) 注册即回放──┘   RedDotWidget（UIWidget）渲染六态
叶子逻辑 ◄──RegisterChecker(RedDotKeys.XXX, (out status, out num) => ...)
```

## 核心红线

1. **业务只认 `RedDotKeys.XXX` 与 `RedDotGraphModule` 收口**，禁止直连 `RedDotManager`（绕过 P0-3 缓冲）
2. **`RedDotKeys.cs` 是生成物**（编辑器导出图时由 `RedDotKeysGenerator` 产出），禁止手改
3. **报脏禁挂 UI 回调里**——在数据变更点的业务逻辑里调 `DataChange`
4. **聚合节点（externalId=-1）不可注册 checker**（`RegisterChecker` 会硬报错）
5. **checker 注册须与图配置加载对称**：配置未就绪期操作进缓冲，`SetConfigReady` 后 FIFO 重放（模块自动处理，业务无感）

## 六态位掩码（`RedDotStatus`，`RedDotConst.cs`）

| 状态 | 位值 | 优先级秩（大者胜） |
|---|---|---|
| `RedDotStatusNull` | 0（熄灭） | — |
| `RedDotStatusFull` | 1<<5 满 | **6（最高）** |
| `RedDotStatusRedDot` | 1<<0 红点 | 5 |
| `RedDotStatusBlueDot` | 1<<1 蓝点 | 4 |
| `RedDotStatusRedNumber` | 1<<2 红数字 | 3 |
| `RedDotStatusBlueNumber` | 1<<3 蓝数字 | 2 |
| `RedDotStatusTips` | 1<<4 "新"标签 | 1（最低） |

优先级唯一权威 `RedDotStatusRules.PriorityOf`（`PriorityByBit={5,4,3,2,1,6}`，下标=枚举位序）。**秩 ≠ 位值**（满位值 32 < 新位值 16 但满优先级更高），禁止直接比较位值；聚合结果恒为单态（归一取秩最高位）；节点掩码（编辑器勾选）可为组合。

## 业务接入三步

```csharp
// 1. 注册 checker（数据源计算,业务收口;数据未变不报脏,读时才刷新）
RedDotGraphModule.Instance.RegisterChecker(RedDotKeys.Mail_Unread, (out RedDotStatus s, out int n) =>
{
    int count = MailModule.UnreadCount;
    s = count > 0 ? RedDotStatus.RedDotStatusRedNumber : RedDotStatus.RedDotStatusNull;
    n = count;
});

// 2. 数据变更点报脏（Batched 入队,ILateUpdate 帧末统一冲刷;未就绪期自动缓冲）
RedDotGraphModule.Instance.DataChange(RedDotKeys.Mail_Unread);
RedDotGraphModule.Instance.DataChange(RedDotKeys.Mail_Unread, mailId);   // per-id（cell 粒度）

// 3. UI 侧二选一
//  a) prefab 固定绑定:widget 节点挂 RedDotKeyBinder（Inspector 下拉选 key）+ UIBindComponent 八槽位
//  b) 动态绑定(列表复用/Tab):SetKey 换绑,空串=解绑熄灭
m_widget.SetKey(RedDotKeys.Mail_Unread.Key, cellId);
m_widget.ClearKey();
m_widget.SetLocalState(RedDotStatus.RedDotStatusRedDot);   // 本地直控(不入树,零订阅,纯表现)
```

**UIBindComponent 八槽位顺序**（与 `RedDotWidget_Gen.g.cs` 严格一致，Builder/prefab 制作必须遵守）：
`0=goFullDot 1=goDotTips 2=goReddot 3=goReddotNum 4=tmpReddot(TMP) 5=goBluedot 6=goBluedotNum 7=tmpBluedot(TMP)`
（注意 3/6 是 Image 底色节点 + 子 TMP 文本节点两层，UGUI 单节点不能同时挂 Image 与 TextMeshProUGUI）

## 动态节点（列表项/期次红点）

```csharp
RedDotGraphModule.Instance.RegisterDynamicNode(RedDotKeys.Mail.Key, out string dynKey);  // 幂等,生成 dyn:ownerKey
RedDotGraphModule.Instance.SetDynamicParent(dynKey, RedDotKeys.Mail.Key);                // 挂父(未就绪自动重放)
RedDotGraphModule.Instance.BindDynamicChecker(dynKey, (out RedDotStatus s, out int n) => { ... });
RedDotGraphModule.Instance.RemoveDynamicNode(RedDotKeys.Mail.Key);                       // 摘边/摘 checker/父链补发
```

## UI 绑定底层 API（RedDotWidget 内部用的帧模型）

```csharp
RedDotGraphModule.Instance.AddUI(key, OnRedDotChanged);        // 注册即回放一次回调
RedDotGraphModule.Instance.GetRedDotStatus(key, 0, out var status, out var num);  // 读时刷新
RedDotGraphModule.Instance.RemoveUI(key, OnRedDotChanged);     // 与 AddUI 配对
RedDotGraphModule.Instance.SetNodeEnabled(key, false);         // 门控:吞报脏/不中继/读回 Null
RedDotGraphModule.Instance.FlushNow();                         // 显式即时冲刷(开窗/切帧等零延迟节点)
RedDotGraphModule.Instance.SummarizeCells(key, idsProvider, mask);   // cell 汇总(列表 per-id 聚合一行接入)
RedDotGraphModule.Instance.ReleaseCellIds(key, keepIds);       // 列表刷新后收敛 per-id 槽位(id=0 强制保留)
```

## 编辑器（Assets/Editor/RedDotGraph）

- 双击 `*.reddotgraph` 资产（或 Inspector "Open Red Dot Editor"）打开图编辑器
- **分组子图**：组内独立树，组根（橙框）+ 引用节点（占位连接面）；左栏组树面板导航；导出自动展平
- **导出**：窗口导出按钮 → 写 `Assets/AssetRaw/Configs/Other/RedDotGraphConfig.json`（运行时 YooAsset 按文件名寻址）+ 同步再生成 `RedDotKeys.cs`
- **双轴校验**：K1-K6（key/逻辑ID）+ V1-V9（组结构）在导出门禁强制
- 菜单：`Tools/红点系统/生成Key常量表(选导出JSON)`（CI 复跑）；"使用说明"帮助窗由编辑器工具栏按钮打开（无菜单项）。实际菜单仅三个：`Assets/Create/红点图`、生成Key常量表、`TEngine/RedDot/Build Demo Scene`
- Demo 图：`Assets/Editor/RedDotGraph/RedDotDemo.reddotgraph`；Demo 构建：菜单 `TEngine/RedDot/Build Demo Scene`

## 生命周期与陷阱

- **重登/切号**：`GameApp.StartGameLogic` 已接线 `EnsureStarted()`——首启激活，再次调用即 `Reinitialize()` 全量重建（清图/订阅/队列）。重登若不重走 StartGameLogic，业务在 UI 重建后自行再调，否则存量 widget 订阅丢失红点失联
- **帧驱动**：模块实现 `ILateUpdate`（SingletonSystem 自动注册/注销），Batched 模式每帧冲刷一次；不要给它再挂定时器
- **TMP**：数字态需 TMP Essentials（`Assets/TextMesh Pro/` 已导入）；缺失时 `TMP_Settings` getter 会 NRE
- **配置缺失**：`RedDotGraphConfig` 加载失败时图保持为空 + Warning，业务操作照常重放（未知 key 运行期告警大声暴露）

## 测试

`Assets/TEngine/Tests/EditMode/RedDotModule/` 四组（聚合/优先级、Visitor、Manager 帧绑定/缓冲/门控、动态节点），asmdef `TEngine.Tests.RedDotModule`，每用例 `RedDotManager.Ins().Reset()` 复位。
