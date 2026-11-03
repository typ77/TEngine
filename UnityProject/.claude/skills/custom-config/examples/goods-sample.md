# Goods 示例表（本项目移植验证样板）

> 位置：`Configs/Tables/Goods.xlsx`（仓库内）。用于验证 Excel → 导表 → 运行时全链路，也是新表的抄写模板。

## 表头结构（A 列标签行驱动）

| 行 | A | B | C | D | E | F | G | H |
|---|---|---|---|---|---|---|---|---|
| 1-4 | `##lb-row##` | | | | | | | |
| 5 | `##lb-labels##` | `#索引id` | 名称 | 类型 | 血量加成 | 攻击加成 | 奖励 | 描述 |
| 6 | `##lb-target##` | | `l` | | | | | `l` |
| 7 | `##lb-keys##` | `int` | `string` | `int` | `int` | `int` | `RewardObject` | `string` |
| 8 | `##lb-names##` | `id` | `name` | `type` | `hp` | `atk` | `rewards` | `desc` |
| 9+ | | 1001 | 铁剑 | 1 | 10 | 5 | `[[1,1001,10]]` | 新手铁剑 |

覆盖的类型点：int 主键（`#` 前缀）、多语言列（`l` 标记的 name/desc）、RewardObject 复合对象。

> **⚠️ 列对齐**：A 列是标签列，**数据行的值必须从 B 列起与标签行的字段列一一对齐**（主键列兜底为 B 列）。数据行错位（如从 A 列起填）会导致列类型映射错乱、bytes 索引错位。

## 产物对照（sheet 名 `Goods`）

| 产物 | 路径 |
|---|---|
| 数据类 | `Assets/GameScripts/HotFix/GameProto/GameConfig/GoodsItem.cs`（类 `GoodsItem`） |
| 表类 | 同目录 `ConfigTableRegistry.cs` 内注册（类 `GoodsConfig`） |
| 表数据 | `Assets/AssetRaw/Configs/Bytes/GoodsConfig.bytes` |
| 多语言 | `Configs/Language/Localization.xlsx`（导出时回写）→ `Assets/AssetRaw/Configs/LanguageBytes/Language_*.bytes` |

## 运行时验证

```csharp
// InitConfig 之后：
var goods = ConfigSystem.Instance.GetConfig<GoodsConfig>();
var item = goods.GetItem(1001);            // 铁剑
var text = ConfigSystem.Instance.GetLanguageString(item.nameKey);  // nameKey 为多语言 int id，name 属性已内置还原
```

## 复制为新表的步骤

1. 复制 Goods.xlsx，改 sheet 名（= 新表名）
2. 保留 5-8 标签行结构，按需增删列
3. 跑 `Tools/配置表工具/生成全部数据(数据压缩)`
