using System.Collections.Generic;

namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// 红点节点运行时信息
    /// </summary>
    public class RedDotVertexRuntimeInfo
    {
        /// <summary>
        /// 状态改变时间
        /// </summary>
        public uint StatusChangeTime = 0;
        /// <summary>
        /// 红点当前状态，运行时才知道的
        /// </summary>
        public RedDotStatus RedDotStatus = 0;
        /// <summary>
        /// 红点值
        /// </summary>
        public int Value = 0;
        /// <summary>
        /// 数据是否有更新
        /// </summary>
        public bool IsDataChange;
    }
    /// <summary>
    /// 红点顶点
    /// </summary>
    public class RedDotVertex
    {
        public string Key;
        /// <summary>
        /// 红点类型，红点需要显示的状态
        /// </summary>
        public int RedDotType = 0;
        /// <summary>
        /// 外部接口ID
        /// </summary>
        public int ExternalId = -1;

        /// <summary>
        /// P0-1:节点启用门控(默认 true=启用)。禁用节点:DataChange 吞、不作 BFS 中继、聚合跳过、
        /// 读回 Null、巡检跳过。未解锁系统的红点经此与父聚合隔离,解锁置 true 后自动补发父链。
        /// </summary>
        public bool Enabled = true;

        private Dictionary<uint, RedDotVertexRuntimeInfo> _runTimeInfos = new Dictionary<uint, RedDotVertexRuntimeInfo>();

        /// <summary>
        /// 获取运行时信息
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
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

        /// <summary>P4-2(N6):当前 per-id 运行时信息条目数(验证/诊断用)</summary>
        public int RuntimeInfoCount => _runTimeInfos.Count;

        /// <summary>P0-2:收集当前全部 per-id 槽位(快照,供节点删除/禁用后对父逐 id 补发)</summary>
        public void CollectIds(List<uint> buffer)
        {
            foreach (var id in _runTimeInfos.Keys)
            {
                buffer.Add(id);
            }
        }

        /// <summary>P4-2(N6):释放单个 id 的运行时信息</summary>
        public void ReleaseRuntimeInfo(uint id)
        {
            _runTimeInfos.Remove(id);
        }

        /// <summary>
        /// P4-2(N6):批量收敛——只保留 keep 集内的 id,其余释放。
        /// 接口形状对齐列表场景:列表刷新后传入本次全部 cellId。keep 为空集等同全清。
        /// </summary>
        public void ReleaseIdsExcept(HashSet<uint> keep)
        {
            if (keep == null)
            {
                return;
            }
            if (keep.Count == 0)
            {
                _runTimeInfos.Clear();
                return;
            }
            List<uint> stale = null;
            foreach (var kv in _runTimeInfos)
            {
                if (!keep.Contains(kv.Key))
                {
                    stale ??= new List<uint>();
                    stale.Add(kv.Key);
                }
            }
            if (stale == null)
            {
                return;
            }
            foreach (var id in stale)
            {
                _runTimeInfos.Remove(id);
            }
        }

        public RedDotVertex(string key, int redDotType, int externalId)
        {
            Key = key;
            RedDotType = redDotType;
            ExternalId = externalId;
        }
    }
}