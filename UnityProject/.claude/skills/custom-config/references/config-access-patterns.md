# 配置表访问治理规范

> 本文档规范配置表的**运行时访问性能**。配置表生成与基础访问、懒加载机制见 [custom-config.md](custom-config.md)。
>
> 本文方法论自 LjdmwMiniClient 项目实证沉淀（多次全表遍历治理 change 后总结），移植到本仓库后按同样流程执行。

---

## 1. 背景：懒加载与「全量遍历击穿」

配置表（`TableBaseTemp`）采用懒加载（详见 custom-config.md「懒加载实现」）：

```
加载期: 只解析 header(key→offset)，字节存 m_byteBuf，m_cache = {}
运行期: GetItem(id) → 命中 m_cache 直接返回(零GC)；未命中才按 offset 反序列化 + 入 cache
        当 cache 全部命中后，主动释放 TextAsset 句柄
```

**关键性质**：单点 `GetItem(id)` 命中缓存后是 **O(1) 零 GC**，廉价。但 `foreach (key in GetAllKeys()) { GetItem(key); }` 会**遍历全表、逐个反序列化**，把懒加载彻底击穿——首次执行产生 GC spike + 卡顿，并丧失懒加载的内存收益。

```
GetAllKeys() 本身: 首次 new List(offsetMap.Keys) 分配 1 次，之后返回缓存 m_allKeys（零GC）
全量遍历的真正代价: 不在 GetAllKeys，而在「逐个 GetItem 触发全表反序列化」
```

---

## 2. 三类访问模式与成本

| 模式 | 写法 | 成本 | 适用 |
|------|------|------|------|
| **单点查询** | `GetItem(id)` | 命中 cache 零 GC | ✅ 绝大多数场景 |
| **全量遍历** | `foreach (GetAllKeys) { GetItem }` | 首次全表反序列化 + GC spike | ⚠️ 仅冷启动索引构建 |
| **聚合查询** | `GetAllKeys().Max() / .OrderBy().ToList()` | 每次遍历或分配 | ⚠️ 应预计算并缓存 |

> ⚠️ **GC 成本澄清**：全表遍历的 **GC spike 主要是首次**（cache 冷时逐个反序列化）；cache 热后 `GetItem` 命中缓存、`foreach` 用 struct enumerator，**热路径零 GC，仅 O(N) CPU**。因此治理价值需**双维度判断**：① **性能**——仅当该遍历在高频热点（每帧/每秒多次）时，O(N)→O(1)/O(M) 才有实感；② **可维护性**——收敛重复逻辑、统一访问入口，**与频率无关，恒有价值**。低频 UI 级遍历的治理主要兑现 ②（DemonRole 即此例：GC≈0、CPU 收益有限，主价值是消除 VO/Widget 重复）。

> ⚠️ custom-config.md 的「遍历所有行」「GetChapterLevels」示例仅作 API 演示，**不应在运行时热点照抄**——运行时全量遍历必须先收敛到 CfgLoader（见第 4 节）。

---

## 3. 治理第 0 条：先验证，后优化 ⭐

**统计层面发现的「高危模式」必须先 grep 验证调用链，再决定如何处理。不验证不优化。**

```
统计发现: foreach GetAllKeys { GetItem }（或 LINQ 全表属性）
        │
        │  grep 验证: 有几个调用者？调用频率？调用时机（Init/运行时）？
        ▼
   ┌──────────────────┬────────────────────┐
   │ 0 调用者 = 死代码  │ 真实热点 = 需优化    │
   │ → 清理删除        │ → 收敛到 CfgLoader   │
   └──────────────────┴────────────────────┘
```

> **实证**：Synthesis 审计中，统计评级 P0 的 `SynthesisModel.IdToCfgIndex`（LINQ 全表属性陷阱）经 grep 验证是**死代码**（全项目 0 调用者）。直接给它加缓存纯属浪费——删除才是正解。

验证要点：
- grep 标识符全项目引用（**含反射字符串**）
- 区分调用时机：一次性 Init vs 运行时反复
- 死代码（0 引用）→「清理」；真实热点 →「收敛」
- ⚠️ codegraph callers 对**实例方法调用**（`obj.Method()`）可能漏报；判定死代码/删除前 MUST grep 复核（DemonRole 治理中 `GetDemonRoleEvolutionConfigItem` 被 codegraph 报 0 调用者，grep 实际有 3 个调用者）

---

## 4. CfgLoader 强 Init 模式（核心规范）

业务模块的配置访问**必须收敛到 `XxxCfgLoader`**（与 custom-config.md 的 ConfigMgr 概念一致），并采用**强 Init 模式**。

### 模式选择：强 Init（A）✅ vs 懒兜底（B）❌

| | 强 Init（A）✅ 规范 | 懒兜底（B）❌ |
|---|---|---|
| 初始化 | `Init()` 统一初始化所有索引 | getter 内 `Count == 0` 自初始化 |
| 首次访问 | 无卡顿（Init 期已完成） | 首次触发全表反序列化卡顿 |
| 重置 | 可清空重建 | map 永不清空 |
| 风险 | 依赖 Init 调用顺序（见第 5 节） | 掩盖 Init 遗漏（bug 根源） |

**统一采用强 Init（A）**。混用 A/B 是 bug 根源——Synthesis 的 `ItemSynthesisItemMap` 漏初始化正是来自混用（`Init()` 初始化了 3 个 map，唯独它靠 getter 懒兜底）。

### 强 Init 模板（Before / After）

```csharp
// ❌ Before: getter 内懒兜底（模式 B）—— 首次卡顿、混用易漏初始化
public static SynthesisItem get_synthesis_by_material(int cfgId)
{
    if (ItemSynthesisItemMap.Count == 0)        // 懒兜底
    {
        var cfg = ConfigSystem.Instance.GetConfig<SynthesisConfig>();
        foreach (var key in cfg.GetAllKeys())    // 首次全表遍历建索引
        { /* ... */ }
    }
    return ItemSynthesisItemMap.ContainsKey(cfgId) ? ItemSynthesisItemMap[cfgId][0] : null;
}

// ✅ After: 强 Init（模式 A）—— Init 期建索引，getter 直接查
public static void Init()
{
    InitItemSynthesisItemMap();                 // 统一初始化全部索引
    InitTargetItemSynthesisItemMap();
    InitTargetItemId2MatItemIds();
    InitChipMatItem2ChipSynthesisItemMap();
}

public static SynthesisItem get_synthesis_by_material(int cfgId)
{
    if (ItemSynthesisItemMap.TryGetValue(cfgId, out var list))
        return list[0];                         // 直接查，无兜底、无分支
    return null;
}
```

### 进阶：区间表的双视图索引

当配置行带「区间」语义（如 `level_range = [startFloor, endFloor]`），且运行时**同时**需要「floor 落在哪个区间」与「按起点排序的极值」两种查询时，单一排序视图无法兼顾——**排序会破坏原顺序，导致「首个命中区间」的结果变化**，行为不再等价。

**双视图解法**：Init 期构建两个互补视图，各司其职：

```csharp
// 视图 A：原顺序（区间查找用，与原全表遍历严格等价，不依赖区间是否重叠）
_groupsInKeyOrder = ... foreach GetAllKeys { if IsValid(item) Add(item) }   // 保留配置原顺序

// 视图 B：按区间起点排序（极值查询用，极值与顺序无关）
_groupsSortedByStart = new List(_groupsInKeyOrder);
_groupsSortedByStart.Sort((a, b) => a.range[0].CompareTo(b.range[0]));

// 区间查找：原顺序线性，首个命中即返回（严格等价原全表遍历）
GroupItem FindGroupByFloor(int floor) {
    foreach (var g in _groupsInKeyOrder)
        if (floor >= g.range[0] && floor <= g.range[1]) return g;   // 必须用原顺序视图
    return null;
}

// 极值查询：排序视图取首/尾，O(1)
GroupItem GetEarliestGroup() => _groupsSortedByStart[0];
```

**关键约束**：
- 区间查找**必须用原顺序视图**——用排序视图会让「首个命中区间」因排序而错位，破坏等价性
- 极值查询用排序视图，O(1) 取首尾
- 若能**证明区间不重叠**，可退化为「排序后二分」O(log n)；但默认**不假设不重叠**，双视图是零行为风险的通用解（无需 fallback）

> **实证**：`optimize-config-access-mirrortower` 的 `MirrorTowerCfgLoader`——`FindGroupByFloor`（原顺序线性）/ `GetGroupsSortedByStart`（起点排序极值）双视图，消除 6 处运行时全表遍历且行为严格等价（见该 change `tasks.md` 1.1 区间策略）。

---

## 5. 初始化顺序约束

强 Init 模式的前提：`XxxCfgLoader.Init()` 必须早于任何运行时 getter 调用。

**机制保障**：`SystemMgr.OnInit()` 反射注册所有 `SystemBase` 子类并逐一调用各自 `Init()`。模块 Mgr 的 `Init()` 在此阶段调用对应 `CfgLoader.Init()`，早于一切运行时业务。

**硬约束**：`CfgLoader` 的 getter **不得在任何 `SystemBase.Init()` 内调用**——反射注册顺序不确定，Init 阶段调用 getter 可能读到尚未初始化的空索引（返回 null/默认值）。

> 实证：Synthesis 的 6 处 `get_synthesis_by_material` 调用经逐一核实全部在运行时方法（`IsEggChipCanUse` / `ReqChipProgress` / UI Widget 等），无一在 `System.Init()` 内。

---

## 6. 反模式清单（治理时重点排查）

| 反模式 | 示例 | 问题 | 正解 |
|--------|------|------|------|
| **表达式体属性陷阱** | `Dict Index => cfg.GetAllKeys()...ToDictionary()` | 每次读属性重算全表 | Init 构建、存字段 |
| **LINQ 链式分配** | `cfg.GetAllKeys().OrderBy(k=>k).ToList()` | 每次调用 GC | CfgLoader 预排序缓存 |
| **List 复制** | `new List<int>(cfg.GetAllKeys())` | 每次复制分配 | 直接复用缓存的 `GetAllKeys()` |
| **聚合重复计算** | `cfg.GetAllKeys().Max()` 反复调 | 每次全表遍历 | Init 期算一次存字段 |
| **重复哈希查找** | `ContainsKey`+`Add`/`[id]` 建分组索引 | 命中 2 次、未命中 3 次哈希 | `TryGetValue` 取引用（命中 1 次、未命中 2 次） |
| **同表重复遍历** | 同一 Config 的多个索引各自 `foreach GetAllKeys` | 遍历 N 次、`GetItem` N 次 | 合并为单次遍历 + 多个 `AddTo*` 私有方法（循环体不变、行为等价） |
| **string key 拼接** | getter 内 `"{a}_{b}"` 拼 string 作 Dictionary key | 每次查询分配 string（**运行时 GC**） | 改 `ValueTuple` 值类型 key，零堆分配 |
| **懒兜底混用** | 部分 map 走 Init、部分走 `Count==0` | 漏初始化 bug | 统一强 Init |
| **死代码伪热点** | 0 调用者的 LINQ 全表属性 | 优化无收益 | grep 验证后删除 |
| **缓存过度失效** | 配置缓存随无关运行时事件清空（如 `InitAllTasks` 清配置链） | 每次事件后首次访问全表重建 | 配置缓存仅配置热更失效，运行时数据刷新不清 |

---

## 7. 治理流程（逐 module 推进）

```
1. 统计  → grep 该 module 所有 GetAllKeys / GetItem 调用点
2. 验证  → 逐点判定：死代码？调用时机（Init/运行时）？频率？
3. 分类  → 死代码(清理) / 全量遍历(收敛) / 单点(保留)
4. 收敛  → 全量遍历收敛到 CfgLoader.Init，运行时只读缓存字段
5. 统一  → CfgLoader 采用强 Init，getter 去懒兜底改直查
6. 验证  → 静态检查 getter 不在 System.Init 内；运行时回归
```

每个 module 一个独立 change，单独验证、单独回滚，降低风险。

---

## 8. 参考样板（源项目实证）

以下为源项目（LjdmwMiniClient）治理实证，说明本规范均为实战验证过的方法论；本项目治理时参照执行并建立对应记录：

- **Synthesis**（首个验证样板）：删死代码 + 强 Init 统一 + 修 Init 完整性。
- **MirrorTower**：强 Init + 区间双视图索引（原顺序查找 / 起点排序极值），消除 11 处运行时全表遍历。
- **Task**：懒兜底→强 Init（按 type 分组链），并修正「缓存过度失效」（配置链不再随运行时数据刷新清空）。
- **DemonRole**：旧表建分组索引（与新表并存，不切表），收敛 VO + Widget 共 6 处遍历并消除跨子系统代码重复。
- **Devil**：按 MasterId 分组索引（原顺序视图 + 单独 Sort 严格等价），收敛 5 处遍历 + 删死代码。
- **Achivement**（首个大表 1.1MB 治理）：CfgLoader 懒加载→强 Init，消除每 group 大表全表遍历 + 修表达式体属性反模式，真实兑现大表 CPU 收益。

### 治理批次推进方式

按 module 分批推进（每批一个独立 change，单独验证、单独回滚）：先战斗/UI 等高频热点，再大表（>500KB）+ GetAllKeys 组合，最后冷启动路径评估全量预加载必要性。本项目业务模块落地后按此清单自行排期。
