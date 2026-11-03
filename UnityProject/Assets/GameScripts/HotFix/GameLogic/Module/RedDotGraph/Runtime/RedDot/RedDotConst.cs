namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// P5-2(T10)刷新模式:
    /// Immediate=DataChange 同帧标记+通知(现状语义);Batched=DataChange 仅入队,帧末由项目侧驱动
    /// FlushPendingChanges 统一 BFS+通知(每帧上限 RedDotManager.MaxDispatchPerFrame)。
    /// </summary>
    public enum RedDotRefreshMode
    {
        Immediate = 0,
        Batched = 1,
    }

    /// <summary>
    /// 红点状态。位值为固定兼容值(2026-09-20 起与优先级解耦),覆盖优先级唯一权威 = <see cref="RedDotStatusRules"/>。
    /// </summary>
    /// <remarks>
    /// 覆盖优先级(高→低,2026-09-20 重定):满 &gt; 红点 &gt; 蓝点 &gt; 红数字 &gt; 蓝数字 &gt; 新。
    /// 位值(1/2/4/8/16/32)保持不变——节点掩码已按位值序列化于图资产/导出 JSON,重排位值需迁移数据;
    /// 聚合/归一/汇总的优先级比较一律走 <see cref="RedDotStatusRules.PriorityOf"/>,禁止直接比较位值。
    /// 运行时聚合结果(info.RedDotStatus)恒为单态;节点掩码(v.RedDotType)可为组合。
    /// 历史名对照(2026-09-17 纯改名,位值未变):RedDot 原名 Default、BlueDot 原名 BlueNoNumber、RedNumber 原名 New。
    /// 注意:与旧系统 RedDotFlags 无数值对应关系,旧→新迁移必须按语义查表,不可按数值直通。
    /// </remarks>
    public enum RedDotStatus
    {
        RedDotStatusNull = 0,
        RedDotStatusRedDot = 1 << 0,        // 红点(无数字,编辑器标签"红点";原名 Default)
        RedDotStatusBlueDot = 1 << 1,       // 蓝点(无数字,编辑器标签"蓝点";原名 BlueNoNumber)
        RedDotStatusRedNumber = 1 << 2,     // 红点(有数字,编辑器标签"红数字";原名 New)
        RedDotStatusBlueNumber = 1 << 3,    // 蓝点(有数字,编辑器标签"蓝数字")
        RedDotStatusTips = 1 << 4,          // 新标签位(编辑器标签"新")
        RedDotStatusFull = 1 << 5,          // 满
    }

    /// <summary>
    /// 状态规则集中地:覆盖优先级表、数字性判断等,禁止散落在聚合算法里硬编码位值或位值比较。
    /// 覆盖优先级若再调整,只改 PriorityByBit 一处,聚合算法零感知。
    /// </summary>
    public static class RedDotStatusRules
    {
        /// <summary>
        /// 覆盖优先级表(2026-09-20 重定,高→低):满 &gt; 红点 &gt; 蓝点 &gt; 红数字 &gt; 蓝数字 &gt; 新。
        /// 下标=状态位序(枚举位 0~5),值=优先级秩,大者胜;位值本身不承载优先级。
        /// </summary>
        private static readonly int[] PriorityByBit = { 5, 4, 3, 2, 1, 6 };

        /// <summary>
        /// 全部数字态掩码(同数字态叠加 Value;跨态/跨类按覆盖优先级取高)
        /// </summary>
        public const int NumberStatusMask =
            (int)RedDotStatus.RedDotStatusRedNumber | (int)RedDotStatus.RedDotStatusBlueNumber;

        /// <summary>
        /// 全部六态掩码(P6 审核 M5:动态节点等"放行全部子贡献"场景的具名常量,防增态遗漏)
        /// </summary>
        public const int AllStatusMask =
            (int)RedDotStatus.RedDotStatusRedDot | (int)RedDotStatus.RedDotStatusRedNumber |
            (int)RedDotStatus.RedDotStatusBlueDot | (int)RedDotStatus.RedDotStatusBlueNumber |
            (int)RedDotStatus.RedDotStatusTips | (int)RedDotStatus.RedDotStatusFull;

        /// <summary>
        /// 是否数字态。仅对单态合法值调用;组合值行为未定义。
        /// </summary>
        public static bool IsNumber(RedDotStatus s)
        {
            return ((int)s & NumberStatusMask) != 0;
        }

        /// <summary>
        /// 覆盖优先级(高→低:满&gt;红点&gt;蓝点&gt;红数字&gt;蓝数字&gt;新)。仅对单态合法值调用;Null 返 0,组合值行为未定义。
        /// </summary>
        public static int PriorityOf(RedDotStatus s)
        {
            var b = (int)s;
            if (b == 0)
            {
                return 0;
            }
            var idx = 0;
            while ((b >>= 1) != 0)
            {
                idx++;
            }
            return idx < PriorityByBit.Length ? PriorityByBit[idx] : 0;
        }

        /// <summary>
        /// 诊断格式化:掩码/状态位值转可读名(如"红点|红数字";0 返"无")。低频诊断路径(告警/GM Dump)专用。
        /// </summary>
        public static string Describe(int mask)
        {
            if (mask == 0)
            {
                return "无";
            }
            var sb = new System.Text.StringBuilder();
            AppendFlag(sb, mask, (int)RedDotStatus.RedDotStatusRedDot, "红点");
            AppendFlag(sb, mask, (int)RedDotStatus.RedDotStatusBlueDot, "蓝点");
            AppendFlag(sb, mask, (int)RedDotStatus.RedDotStatusRedNumber, "红数字");
            AppendFlag(sb, mask, (int)RedDotStatus.RedDotStatusBlueNumber, "蓝数字");
            AppendFlag(sb, mask, (int)RedDotStatus.RedDotStatusTips, "新");
            AppendFlag(sb, mask, (int)RedDotStatus.RedDotStatusFull, "满");
            return sb.ToString();
        }

        private static void AppendFlag(System.Text.StringBuilder sb, int mask, int bit, string name)
        {
            if ((mask & bit) == 0)
            {
                return;
            }
            if (sb.Length > 0)
            {
                sb.Append('|');
            }
            sb.Append(name);
        }

        /// <summary>
        /// 组合值归一:返回优先级最高的单个置位态(防御配置失误的多位组合;0 返 Null)。
        /// </summary>
        public static RedDotStatus HighestPriorityBit(int combined)
        {
            var best = RedDotStatus.RedDotStatusNull;
            var bestRank = 0;
            for (var i = 0; i < PriorityByBit.Length; i++)
            {
                if ((combined & (1 << i)) == 0 || PriorityByBit[i] <= bestRank)
                {
                    continue;
                }
                bestRank = PriorityByBit[i];
                best = (RedDotStatus)(1 << i);
            }
            return best;
        }
    }
}
