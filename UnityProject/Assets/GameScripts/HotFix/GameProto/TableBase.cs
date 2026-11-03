using System.Collections.Generic;
using Luban;
using TEngine;
using UnityEngine;

// Non-generic base class (if needed for backward compatibility)
public abstract class ConfigTableBase
{
    public int TotalCount { get; set; }
    protected abstract void ReadHead(ByteBuf byteBuf);
    public abstract void SetTextAsset(TextAsset textAsset);
}
/// <summary>
/// key item
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TItem"></typeparam>
public abstract class TableBaseTemp<TKey, TItem> : ConfigTableBase
{
    protected readonly Dictionary<TKey, TItem> m_cache = new Dictionary<TKey, TItem>();
    protected Dictionary<TKey, int> m_offsetMap;
    protected List<TKey> m_allKeys;
    protected ByteBuf m_byteBuf;
    // 缺失 key 负缓存：确认不存在的 id 只查一次 offsetMap、只打一次日志（config-system-reflection-analysis P1）
    private HashSet<TKey> m_missCache;

    /// <summary>
    /// 泛型行类名静态缓存：typeof(TItem).Name 每次访问都会新建字符串，
    /// miss 日志改走 format 参数化重载后此项保证 miss 路径零反射字符串分配。
    /// </summary>
    private static class ItemTypeNameCache
    {
        // ReSharper disable once StaticMemberInGenericType
        public static readonly string Value = typeof(TItem).Name;
    }

    /// <summary>
    /// Gets all keys in the table.
    /// </summary>
    public List<TKey> GetAllKeys()
    {
        if (m_allKeys == null)
        {
            m_allKeys = new List<TKey>(m_offsetMap.Keys);
        }
        return m_allKeys;
    }

    /// <summary>
    /// Retrieves an item by its key, using cached data or reading from binary buffer.
    /// </summary>
    public TItem GetItem(TKey id, bool silentIfAbsent = false)
    {
        // Return cached item if available
        if (m_cache.TryGetValue(id, out var item))
            return item;

        // 表已加载且该 key 已确认缺失：直接返回，不重复查 offsetMap、不重复打日志
        if (m_offsetMap != null && m_missCache != null && m_missCache.Contains(id))
            return default;

        // Return null if key/byte buffer is invalid
        if (m_offsetMap == null || !m_offsetMap.TryGetValue(id, out var value) || m_byteBuf == null)
        {
            // 仅表头已解析（offsetMap 就绪）时才认定 key 真缺失；
            // 表未加载时的 miss 是时序现象，不可记入负缓存
            if (m_offsetMap != null)
            {
                if (m_missCache == null)
                {
                    m_missCache = new HashSet<TKey>();
                }
                m_missCache.Add(id);
            }

            if (!silentIfAbsent)
            {
                Log.Error("配置{0}中不存在Key={1}", ItemTypeNameCache.Value, id);
            }
            return default;
        }

        // Read and cache new item from binary buffer
        var offset = value;
        m_byteBuf.ReaderIndex = offset;
        var newItem = CreateItem(m_byteBuf);
        m_cache[id] = newItem;

        // Cleanup temporary data when all items are cached
        if (m_cache.Count >= m_offsetMap.Count)
        {
            GetAllKeys();
            m_offsetMap.Clear();
            m_byteBuf = null;
        }

        return newItem;
    }

    /// <summary>
    /// Parses the binary header to populate the offset map (key-to-buffer-position mapping).
    /// </summary>
    protected override void ReadHead(ByteBuf byteBuf)
    {
        if (byteBuf == null)
            return;

        // 同实例重解析（SetTextAsset/SetByteBuf 二次调用）时，旧负缓存可能遮蔽新表头的 key，随表头重建一并失效
        m_missCache?.Clear();

        TotalCount = byteBuf.ReadInt();
        m_offsetMap = new Dictionary<TKey, int>(TotalCount);
        for (var i = 0; i < TotalCount; i++)
        {
            var key = ReadKey(byteBuf); // Type-specific key reading
            m_offsetMap[key] = byteBuf.ReadInt(); // Store offset for later item reading
        }
        m_byteBuf = new ByteBuf(byteBuf.ReadBytes()); // Remaining bytes for item data
    }

    /// <summary>
    /// Abstract method to read a key from the binary buffer (implemented by subclasses for specific key types).
    /// </summary>
    protected abstract TKey ReadKey(ByteBuf byteBuf);

    /// <summary>
    /// Abstract method to create an item from the binary buffer (implemented by subclasses for specific item types).
    /// </summary>
    protected abstract TItem CreateItem(ByteBuf byteBuf);

    /// <summary>
    /// Loads and parses the configuration data from a text asset.
    /// </summary>
    public override void SetTextAsset(TextAsset textAsset)
    {
        if (textAsset)
        {
            //解析头信息
            var bytes = new ByteBuf(textAsset.bytes);
            ReadHead(bytes);
            
            ConfigSystem.Instance.ResourceModule.UnloadAsset(textAsset);
        }
    }
    
    public void SetByteBuf(ByteBuf byteBuf)
    {
        ReadHead(byteBuf);
    }
}
