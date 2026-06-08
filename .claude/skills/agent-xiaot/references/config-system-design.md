# 自定义 Config System 设计参考

> 小T的技术预研笔记 — 替换 Luban 的轻量级配置方案

## 一、为什么替换 Luban

| 维度 | Luban | 自研 Config System |
|------|-------|-------------------|
| 依赖 | 外部工具链 | 纯 C#，零外部依赖 |
| 加载策略 | 较固定 | 灵活（同步/异步/懒加载） |
| 热更新集成 | 需额外配置 | 原生支持 YooAsset 热更 |
| 编辑器集成 | 独立工具 | 深度集成 Unity Inspector |
| 自定义扩展 | 有限 | 完全可控 |

## 二、技术选型对比

| 方案 | 性能 | 可读性 | 编辑器友好 | 热更支持 |
|------|------|--------|-----------|---------|
| ScriptableObject | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| JSON | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| Binary (MessagePack) | ⭐⭐⭐⭐⭐ | ⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ |
| Excel 直读 | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ |

推荐：**ScriptableObject + JSON 双模式**（编辑器用 SO，运行时加载 JSON/Binary）

## 三、接口设计草案

```csharp
public interface IConfigSystem
{
    // 单表加载
    UniTask<T> LoadTableAsync<T>(string tableName) where T : class;
    T LoadTable<T>(string tableName) where T : class;
    
    // 懒加载（首次访问时自动加载）
    T GetTable<T>() where T : class, new();
    
    // 热更重载
    UniTask ReloadAsync(string tableName);
    
    // 数据访问
    T GetById<T, TKey>(TKey id) where T : IConfigRow<TKey>;
    List<T> GetAll<T>() where T : class;
    T GetByPredicate<T>(Func<T, bool> predicate) where T : class;
}

// 示例：配置表数据结构
[ConfigTable("items")] // 绑定到 items.json / items.bytes
public class ItemConfig : IConfigRow<int>
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int Type { get; set; }
    public Dictionary<string, int> Attributes { get; set; }
    public List<int> CombineIds { get; set; }
    
    public int GetKey() => Id;
}
```