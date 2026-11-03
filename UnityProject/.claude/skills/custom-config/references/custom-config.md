# TEngine Custom 配置表指南

## 目录

- [技术栈说明](#技术栈说明)
- [配置工程目录结构](#配置工程目录结构)
- [数据表定义规范](#数据表定义规范)
- [代码生成及原理](#代码生成及原理)
- [ConfigSystem 加载器](#configsystem-加载器)
- [配置数据访问](#配置数据访问)
- [配置管理器封装（推荐）](#配置管理器封装推荐)
- [添加新配置表流程](#添加新配置表流程)
- [配套工具](#配套工具)

---

## 技术栈说明

- **Custom**：自定义高性能游戏配置表工具。原生实现，不依赖第三方工具。支持 Excel → C# 代码 + 二进制数据
- **生成格式**：`cs`（C# 数据类代码）+ `.bytes`（二进制数据文件）
- **数据位置**：`Assets/AssetRaw/Configs/Bytes/`（由 YooAsset 管理加载，tag `Configs`）
- **代码位置**：`GameScripts/HotFix/GameProto/GameConfig/`（热更程序集）
- **特性**：支持懒加载；支持单表内数据共享缓存（去重优化内存占用）；支持组合索引；免反射建表注册

---

## 配置工程目录结构

通过 ProjectSetting → TEngine → UpdateSettings 或菜单 TEngine/Settings/TEngine UpdateSettings 设置（`Assets/TEngine/Settings/UpdateSetting.asset`）：

- **Config Table Path**：配置表源路径（相对 Assets）。本项目为 `../../Configs/Tables`（仓库内 `Configs/Tables/`）
- **Config Out Put Path**：生成二进制文件路径。本项目为 `AssetRaw/Configs/Bytes`
- **Config Language Path**：多语言统计与翻译工作簿路径（相对 Assets，项目外可写）。本项目为 `../../Configs/Language`

目录总览：

```
F:/code/TEngine/
├── Configs/
│   ├── Tables/            [源 Excel，入库] Goods.xlsx 等
│   └── Language/          [多语言工作簿，工具自动创建 Localization.xlsx]
└── UnityProject/Assets/
    ├── Editor/ExcelTool/                          [导出工具，Editor-only]
    ├── GameScripts/HotFix/GameProto/
    │   ├── ConfigSystem.cs / TableBase.cs / LanguageConfigItem.cs
    │   ├── CustomConfig/                          [RewardObject 等复合对象]
    │   ├── LubanLib/                              [ByteBuf 反序列化运行时]
    │   └── GameConfig/                            [全自动生成 XxxItem.cs + ConfigTableRegistry.cs]
    └── AssetRaw/Configs/
        ├── Bytes/*.bytes                          [表数据，YooAsset tag "Configs"]
        └── LanguageBytes/Language_*.bytes         [多语言产物]
```

---

## 数据表定义规范

### 表格命名
文件命名支持中英文，可按业务命名。

### Sheets 命名规范
- sheet 首字母为 `#` 代表不导出
- 需要导出的 sheet 名请用英文，不支持特殊字符
- sheet 名与最终导出数据名一致。一个 Excel 可含多个 sheet，每个 sheet 导出为独立配置表

### Excel 数据表结构

#### 标签说明（A 列标签行驱动）
- **##lb-row##**：此行数据忽略处理，可在任意行填写
- **##lb-labels##**：描述行。中文描述此列作用；首字母 `#` 表示此列为索引列（类型校验允许 int/uint/long/ulong/short/ushort/str/string，惯例用 int/string，单主键，建议放第一个数据列）
- **##lb-target##**：输出平台。`c` 仅客户端 / `s` 仅服务端（客户端忽略）/ `cs` 双端 / `l` 多语言 / 不填默认双端
- **##lb-keys##**：数据类型（见下文类型系统）
- **##lb-names##**：字段名（代码访问用）

> **自动清理规则**：工具自动移除字段名中的所有空格/回车/换行，并输出 Error 日志提醒修正 Excel。

#### 结构说明
- A1 首格以 `##` 开头的 sheet 才导出
- 工具不依赖固定行号，按 A 列标签匹配；推荐排版：

| 行号 | 内容 | 说明 |
|------|------|------|
| 1-4 | `##lb-row##` | 保留空行 |
| 5 | `##lb-labels##` | 描述信息行 |
| 6 | `##lb-target##` | 输出目标行 |
| 7 | `##lb-keys##` | 数据类型行 |
| 8 | `##lb-names##` | 字段名行 |
| 9+ | 数据行 | 实际配置数据 |

**本项目示例表（Configs/Tables/Goods.xlsx）**：

|##lb-row##|       |      |     |     |     |          |      |
|----------|-------|------|-----|-----|-----|----------|------|
|##lb-labels##| #索引id | 名称  | 类型 | 血量加成 | 攻击加成 | 奖励 | 描述 |
|##lb-target##|       | l    |     |     |     |          | l    |
|##lb-keys##| int   | string | int | int | int | RewardObject | string |
|##lb-names##| id   | name  | type | hp  | atk | rewards | desc |
| 1001  | 铁剑    | 1   | 10  | 5   | [[1,1001,10]] | 新手铁剑 |

### 支持的数据类型【##lb-keys##】

**通用类型**

- int：默认值 0
- long：超长数值用 long。同时支持 byte/short/uint/ulong（完整链路可用：解析+写入+代码生成）
- string/str：默认 ""；作为多语言文案时在 ##lb-target## 填 `l`
- bool：1,0,TRUE,FALSE。默认 false
- double/float：支持但**不建议**（精度问题，建议整型万进制：1 = 10000）

**特殊类型（完整链路可用）**

- vec2/vector2、vec3/vector3、vec4/vector4：`x,y`（,z,w）格式
- quat/quaternion：x,y,z,w 浮点

**解析可用但暂不可导出**（二进制写入端/生成端未实现，导表报 `TypeNotSupportedException`）

- vec2int、vec3int：`x,y`（,z）整型格式
- color：r,g,b,a 浮点（0~1）
- datetime：时间戳或可被 DateTime.TryParse 解析的格式
- decimal、sbyte：数值类型
- obj/object：可自动解析类型填啥解析啥
- Enum：解析端类型名残留源项目命名空间（`TypeRegistry.CodeNamespace`），暂不可用

### 自定义对象格式

以下四种自定义复合对象有固定格式要求，导表工具在**共享缓存分析阶段校验**，格式错误输出详细日志但不停表（仅跳过该数据）。空数据 `[]`/`[[]]` 不算错误：

| 对象类型 | 格式 | 元素要求 | 示例 |
|---------|---------|-----------|------|
| **RewardObject** | `[奖励类型, 道具id, 数量]` | 每条目正好 3 个元素 | `[[1, 1001, 10],[2, 1002, 5]]` |
| **ConditionObject** | `[条件类型, 参数...]` | 每条目至少 2 个元素，不定长 | `[[hp_less, 1000],[level_greater, 10]]` |
| **EffectCfgObject** | `[效果名称, 参数列表]` | 正好 2 个元素 | `["add_hp", [[1, 1000]]]` |
| **GradedPriceObject** | `[奖励类型, 道具id, 数量, 档位]` | 每条目正好 4 个元素 | `[[1, 10002, 1, 2]]` |

**自定义复合类型**

- 数组：`类型[]`，如 `int[]`
- 列表：`list<类型>`，支持嵌套，如 `list<list<int>>`
- 字典：`dict<k, v>` / `dictionary<k,v>`，如 `dict<int, list<string>>`
- 元组：`valuetuple<...>`，最大 7 元素，任意嵌套但不建议滥用

### 数据填写规则

- **bool**：1,0,TRUE,FALSE
- **str/string**：填入 `Assets/` 开头资源路径会自动取文件名（png/jpg 后缀忽略）
- **列表**：`[1,2,3,4]`；嵌套 `[[1,2,3],[4,5,6]]`；dict `["key",1]` / `["key",[1,2,3],"key2",[2,3]]`；元组 `[1,"test",true]`
- 尽量不用小数（万进制）；集合可嵌套但不建议过深
- 集合用 `[]`/`{}` 定位，逗号分隔；dict 的 key-value 均以逗号分隔

---

## 代码生成及原理

### 代码及数据生成

- Unity 菜单 `Tools/配置表工具/生成全部数据(数据压缩)` 生成全部
- `Tools/配置表工具/数据生成窗口` 可指定文件/单 sheet 导出
- 亦可调用 `TEngine.Editor.ExcelTool.ExcelDataTool` 的 API 导出

### 生成产物

```
GameScripts/HotFix/GameProto/GameConfig/
├── GoodsItem.cs               # Goods 表数据类（sheet 名 Goods 时）
└── ConfigTableRegistry.cs     # 表工厂注册表（合并式生成，替代反射）

Assets/AssetRaw/Configs/Bytes/
└── GoodsConfig.bytes          # GoodsConfig 表二进制数据（sheet 名 + Config）
```

命名规则：**表类名 = bytes 名 = `{sheet}Config`；数据类 = `{sheet}Item`**。

**重要**：`GameConfig/` 目录全部是**自动生成**代码（含 `ConfigTableRegistry.cs`），禁止手动修改，下次生成时覆盖。

### 生成原理

#### 多语言实现
`##lb-target##` 标注 `l` 的字符串统一写入多语言工作簿（`Configs/Language/Localization.xlsx`）并分配 int index；二进制中只写 int index，运行时经 `GetLanguageString(id)` 查多语言表。

> 注意：多语言 index 有漂移风险——生成多语言表后未及时提交 Language 工作簿，其他人再导表会复用 index 导致错乱。**Language 工作簿必须与表数据同批提交。**

#### 懒加载实现
- 游戏初始化阶段统一加载配置表二进制（TextAsset）并解析头部（key→offset 索引）
- 首次 `GetItem` 时按 offset 反序列化并实例化 item 入缓存
- 全表缓存满后自动释放 TextAsset 句柄与字节缓冲

#### 共享缓存优化（数据去重）
- 自动检测 `string`/`list`/`dict`/RewardObject/ConditionObject/ValueTuple 等类型的数据重复率
- 字段重复率 ≥ 8% 自动开启：唯一值存静态共享字典，每行只存索引
- 空值（-1）安全返回默认值；只优化顶级字段；string 重复率计算跳过空串

#### 组合索引
`##lb-labels##` 中 `~` 前缀的列为组合索引成员。生成的表类带 `GetIdByCompoundKey_Xxx(...)` / `TryGetIdByCompoundKey_Xxx(...)` / `EnumerateCompoundIndex_Xxx()` 方法。

#### 二进制布局
1. **共享缓存段**（可选）：字段数 → 每字段（字段名 + 唯一值数 + N 个值）
2. **数据头**：int TotalCount → offset[]（key + 偏移）
3. **数据内容**：每行数据，先基础类型后集合

---

## ConfigSystem 加载器

`GameProto/ConfigSystem.cs` 桥接生成代码与 YooAsset 资源系统（本项目适配版：无 UIParticle/LocalizedAddressResolver 依赖，日志走 TEngine 参数化重载）：

```csharp
public class ConfigSystem
{
    public static ConfigSystem Instance => _instance ??= new ConfigSystem();
    private LanguageConfig m_languageConfig;                       // 多语言表
    private Dictionary<string, ConfigTableBase> m_tableCache;      // 表缓存

    public async UniTask InitLanguage(Language? targetLanguage = null);
    public async UniTask InitConfig(object param);                 // GetAssetInfos("Configs") 并发加载全部表
    public T GetConfig<T>() where T : ConfigTableBase;             // typeof(T).Name 查缓存
    public string GetLanguageString(int id);                       // 多语言查询（miss 负缓存）
    public int ConfigLoadVersion { get; }                          // 每次重载自增，供派生缓存失效
}
```

要点：
- **InitConfig** 经 `ConfigTableRegistry.TryCreate(asset.name)` 免反射建表，miss 回落 `Assembly.GetType("TEngine.{name}")` 并告警（新表未更新注册表时兜底）
- 单表解析失败不整体崩溃；加载完成后整体替换缓存并 `ConfigLoadVersion++`
- **TableBaseTemp<TKey,TItem>**（`TableBase.cs`）：懒加载基类——`GetItem(id)` 命中缓存零 GC，未命中按 offset 解码；缺失 key 负缓存；`GetAllKeys()` 缓存列表

### 初始化时机

`GameApp.StartGameLogic`（`Assets/GameScripts/HotFix/GameLogic/GameApp.cs`）：

```csharp
await ConfigSystem.Instance.InitLanguage();
await ConfigSystem.Instance.InitConfig(null);
// 配置就绪后再显示 UI
GameModule.UI.ShowUIAsync<LoginUI>();
```

---

## 配置数据访问

```csharp
// 单例访问
var tables = ConfigSystem.Instance.GetConfig<GoodsConfig>();

// 按 ID 查询
var itemCfg = tables.GetItem(id);
if (itemCfg != null) { }

// 遍历所有行（⚠️ 仅用于冷启动一次性构建索引；运行时热点禁止照抄——会击穿懒加载）
foreach (var key in tables.GetAllKeys()) { var item = tables.GetItem(key); }
// 运行时访问的性能治理见 config-access-patterns.md
```

---

## 配置管理器封装（推荐）

对有一定复杂度的模块，**不应在业务代码中直接调用 `ConfigSystem.Instance.GetConfig<xxx>()`**，应封装 `XxxConfigMgr`（或 CfgLoader）集中查询逻辑。

### 好处

- 业务与表结构解耦，字段改名只改管理器一处
- 可在管理器做预处理、预建索引（强 Init 模式）、校验
- 接口语义清晰

### 封装模板

```csharp
// GameLogic/Config/GoodsConfigMgr.cs
public class GoodsConfigMgr
{
    private static GoodsConfigMgr _instance;
    public static GoodsConfigMgr Instance => _instance ??= new GoodsConfigMgr();

    /// <summary>获取商品配置，找不到返回 null</summary>
    public GoodsItem GetGoods(int goodsId)
    {
        return ConfigSystem.Instance.GetConfig<GoodsConfig>().GetItem(goodsId);
    }
}
```

```csharp
// ✅ 推荐：经管理器
var cfg = GoodsConfigMgr.Instance.GetGoods(goodsId);
// ❌ 不推荐：业务代码散落
var cfg = ConfigSystem.Instance.GetConfig<GoodsConfig>().GetItem(goodsId);
```

简单只读一次的场景可直接访问；复杂模块/多处查询同一张表时封装管理器。

---

## 添加新配置表流程

```
1. 在 Configs/Tables/ 下新建 xlsx。单 sheet 建议 sheet 名与文件名一致
2. 按规范添加表头（标签行结构见上文）
3. 确认版本控制为最新（多语言 index 依赖 Language 工作簿同步）
4. 运行菜单 Tools/配置表工具/生成全部数据(数据压缩)
5. 验证生成结果：
   - GameScripts/HotFix/GameProto/GameConfig/ 下新增 {sheet}Item.cs
   - Assets/AssetRaw/Configs/Bytes/ 下新增 {sheet}Config.bytes
6. 在 Unity Editor 确认 .bytes 被 YooAsset Configs 组正确收集
7. （可选）在 GameLogic/Config/ 封装 XxxConfigMgr：
   - 需要预处理/分组缓存/自定义查询时推荐创建
   - 仅简单按键查询可不创建，直接 GetConfig<{sheet}Config>().GetItem(id)
```

**注意事项**：
- 字段名和类型一旦确定不轻易修改（热更包兼容性）
- 新增字段可前向兼容，删除字段不可
- 先在 Editor 模式验证，再发布热更包
- 自动共享缓存超阈值自动开启，无需手动配置

---

## 配套工具

- **数据生成窗口**（`Tools/配置表工具/数据生成窗口`）：列表/搜索 Excel，单文件导出、单 sheet 导出
- **Bytes 查看器**（`Assets/Editor/ExcelTool/BytesViewer/`）：查看 .bytes 内容、导出 JSON/CSV、Excel diff
- **共享缓存报告**（`Assets/Editor/ExcelTool/Report/SharedCacheSavingsReport.cs`）：压缩率分析报告
