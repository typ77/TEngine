---
name: custom-config
description: TEngine 自研 Excel 配置表系统（Custom Config）使用规范。当用户要编辑游戏配置数据、新增/修改配置表、调整表结构（字段/类型）、导表生成代码、使用 ConfigSystem/GetConfig 访问配置、配置多语言文案（##lb-target##=l）、排查配置加载/懒加载/共享缓存问题、或使用 Tools/配置表工具 菜单时使用。即使用户没有提到"配置表"或"Luban"，只要涉及编辑游戏数值/文案数据（Excel）也应使用。
---

# TEngine Custom 配置表系统

自研 Excel → C# 代码 + 二进制 bytes 的配置管线（源自 LjdmwMiniClient 移植），不依赖 Luban 生成器。

## 项目核心约定（速览）

- **数据源**：`Configs/Tables/*.xlsx`（仓库内，相对 Assets 路径 `../../Configs/Tables`，可在 UpdateSetting 配置）
- **生成代码**：`Assets/GameScripts/HotFix/GameProto/GameConfig/`（热更程序集，**全自动生成，禁手改**）
- **生成数据**：`Assets/AssetRaw/Configs/Bytes/*.bytes`（YooAsset tag `Configs`）
- **多语言**：`Configs/Language/Localization.xlsx` → `Assets/AssetRaw/Configs/LanguageBytes/Language_*.bytes`
- **运行时入口**：`ConfigSystem.Instance`（单例），初始化在 `GameApp.StartGameLogic`
- **导出菜单**：`Tools/配置表工具/生成全部数据(数据压缩)`；单表用 `数据生成窗口`

## 命名规则（sheet 名驱动）

| sheet 名（如 `Goods`） | 产物 |
|---|---|
| 表类名 / bytes 名 | `GoodsConfig` / `GoodsConfig.bytes` |
| 数据类 | `GoodsItem`（文件 `GoodsItem.cs`） |
| 注册 | 自动并入 `GameConfig/ConfigTableRegistry.cs` |

## 新增配置表流程（7 步）

1. 在 `Configs/Tables/` 新建 xlsx（sheet 名 = 表名，勿用中文/特殊字符）
2. 按规范写表头：1-4 行 `##lb-row##` 保留行 → `##lb-labels##`（`#`开头=主键）→ `##lb-target##`（c/s/l/cs）→ `##lb-keys##`（类型）→ `##lb-names##`（字段名），9 行起数据
3. 确认版本控制最新（多语言 index 依赖 Language 工作簿同步）
4. 运行菜单 `Tools/配置表工具/生成全部数据(数据压缩)`
5. 验证产物：`GameConfig/GoodsItem.cs` + `ConfigTableRegistry.cs` 更新 + `AssetRaw/Configs/Bytes/GoodsConfig.bytes`
6. 确认 .bytes 被 YooAsset Configs 组收集（已配好，新表自动收）
7. 复杂模块在 `GameLogic/Config/` 封装 `XxxConfigMgr`（简单按键查询可直接访问）

## 红线

1. **不改 `GameConfig/` 下任何生成代码**——下次导表覆盖；`ConfigTableRegistry.cs` 同为生成物
2. **业务经 ConfigMgr 访问**，不散落 `ConfigSystem.Instance.GetConfig<>()`（只读一次的简单场景除外）
3. **字段前向兼容**：新增字段安全，删除/改名不兼容（热更包）；字段名含空格工具会报 Error
4. **运行时禁止热点全量遍历**：`foreach GetAllKeys { GetItem }` 会击穿懒加载，见 references/config-access-patterns.md
5. 主键仅支持 int/string 单列，第一个数据列，`##lb-labels##` 中 `#` 开头标识

## 常用命令

```
# 导出全部（AI/脚本方式）
Unity 菜单: Tools/配置表工具/生成全部数据(数据压缩)
# 或 MCP: EditorApplication.ExecuteMenuItem 同上路径
# 或反射: TEngine.Editor.ExcelTool.ExcelDataTool.BuildConfigBytes()
```

## 参考文档（按需加载）

| 文档 | 内容 |
|-----|------|
| [references/custom-config.md](references/custom-config.md) | **主文档**：表定义规范/类型系统/生成原理/ConfigSystem 全文/访问示例/多语言 |
| [references/config-access-patterns.md](references/config-access-patterns.md) | 运行时访问性能治理：懒加载击穿/强 Init 模式/反模式清单 |
| [examples/goods-sample.md](examples/goods-sample.md) | 本项目 Goods 示例表结构与产物对照 |

## 排障速查

| 现象 | 原因/处理 |
|------|----------|
| 生成代码找不到（ConfigSystem 报未注册） | 未导表，先跑生成菜单 |
| .bytes 加载失败 | 检查收集器 Configs 组（Bytes 目录 + tag `Configs`） |
| GetItem 返回 null 且已报"配置X中不存在Key" | 确认主键值；表未加载时属时序现象，等 InitConfig 完成 |
| 多语言串读出为空 | Language_*.bytes 未生成或语言枚举名不匹配（`Language_{枚举名}`） |
| 导表弹"导表失败"对话框 | 详情看 Console；多为单元格格式/类型不匹配 |
