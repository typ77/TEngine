using System;
using System.Collections.Generic;

namespace GameLogic.RedDot.Core
{
    /// <summary>
    /// JSON 配置：红点节点定义。
    /// </summary>
    [Serializable]
    public class RedDotNodeJson
    {
        /// <summary>红点节点全局 key。</summary>
        public string key;

        /// <summary>红点允许的状态位掩码（Normal=1|Number=2|New=4|Full=8 任意组合）。</summary>
        public int redDotType;

        /// <summary>外部逻辑 ID；-1 表示非叶子聚合节点。</summary>
        public int externalId;
    }

    /// <summary>
    /// JSON 配置：红点边定义（父→子）。
    /// </summary>
    [Serializable]
    public class RedDotEdgeJson
    {
        /// <summary>父节点 key。</summary>
        public string key;

        /// <summary>子节点 key 列表。</summary>
        public List<string> outKeys = new List<string>();
    }

    /// <summary>
    /// JSON 配置：红点图导出结构。
    /// <para>注意：字段名 <see cref="vertexs"/> 保持与 fermsi 编辑器导出一致（少一个 e），
    /// 兼容已存在的 JSON 资源。</para>
    /// </summary>
    [Serializable]
    public class RedDotExportJson
    {
        /// <summary>所有红点节点。</summary>
        public List<RedDotNodeJson> vertexs = new List<RedDotNodeJson>();

        /// <summary>所有红点边。</summary>
        public List<RedDotEdgeJson> edges = new List<RedDotEdgeJson>();
    }
}
