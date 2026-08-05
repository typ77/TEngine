using System.Collections.Generic;

namespace GameLogic.RedDot.Core
{
    /// <summary>
    /// 红点节点运行时信息（按业务 id 分桶，支持列表项红点）。
    /// </summary>
    public class RedDotVertexRuntimeInfo
    {
        /// <summary>
        /// 状态改变时间戳（全局递增计数器快照），用于 UI 层判断状态是否自上次回调后变过。
        /// </summary>
        public uint StatusChangeTime = 0;

        /// <summary>
        /// 红点当前状态（运行时计算得到）。
        /// </summary>
        public RedDotStatus RedDotStatus = 0;

        /// <summary>
        /// 红点数值（仅 <see cref="RedDotStatus.Number"/> 状态有意义）。
        /// </summary>
        public int Value = 0;

        /// <summary>
        /// 数据是否已变更（脏标记）。true 表示需要重新计算。
        /// </summary>
        public bool IsDataChange;

        /// <summary>
        /// 重置为初始状态，便于对象池复用。
        /// </summary>
        public void Reset()
        {
            StatusChangeTime = 0;
            RedDotStatus = 0;
            Value = 0;
            IsDataChange = false;
        }
    }

    /// <summary>
    /// 红点顶点（图节点）。
    /// </summary>
    public class RedDotVertex
    {
        /// <summary>红点全局唯一 key。</summary>
        public string Key;

        /// <summary>
        /// 红点允许的状态位掩码（来自配置），如 Normal|Number = 3。
        /// </summary>
        public int RedDotType = 0;

        /// <summary>
        /// 外部逻辑 ID；-1 表示非叶子（聚合节点），由子节点聚合得到状态。
        /// </summary>
        public int ExternalId = -1;

        /// <summary>
        /// 是否为运行时动态添加的叶子节点（软删除标记用）。
        /// </summary>
        public bool IsDynamicLeaf;

        /// <summary>
        /// 关联的 <see cref="RedDotNode"/> 门面（MVE 绑定入口），建图时设置。
        /// </summary>
        public object UserData;

        private readonly Dictionary<uint, RedDotVertexRuntimeInfo> _runTimeInfos = new Dictionary<uint, RedDotVertexRuntimeInfo>();

        public RedDotVertex(string key, int redDotType, int externalId)
        {
            Key = key;
            RedDotType = redDotType;
            ExternalId = externalId;
        }

        /// <summary>
        /// 获取指定业务 id 的运行时信息（不存在则懒创建）。
        /// </summary>
        public RedDotVertexRuntimeInfo GetRuntimeInfo(uint id)
        {
            if (_runTimeInfos.TryGetValue(id, out var info))
            {
                return info;
            }
            info = new RedDotVertexRuntimeInfo();
            _runTimeInfos[id] = info;
            return info;
        }

        /// <summary>
        /// 是否存在指定业务 id 的运行时信息。
        /// </summary>
        public bool HasRuntimeInfo(uint id) => _runTimeInfos.ContainsKey(id);

        /// <summary>
        /// 清理指定业务 id 的运行时信息（修复原 fermsi 实现无清理路径的缺陷）。
        /// </summary>
        public void ClearRuntimeInfo(uint id) => _runTimeInfos.Remove(id);

        /// <summary>
        /// 清理所有运行时信息（节点回收前调用）。
        /// </summary>
        public void ClearAllRuntimeInfo() => _runTimeInfos.Clear();

        /// <summary>
        /// 重置为可复用状态（池化归还前调用）。
        /// </summary>
        public void Reset()
        {
            Key = null;
            RedDotType = 0;
            ExternalId = -1;
            IsDynamicLeaf = false;
            UserData = null;
            ClearAllRuntimeInfo();
        }
    }
}
