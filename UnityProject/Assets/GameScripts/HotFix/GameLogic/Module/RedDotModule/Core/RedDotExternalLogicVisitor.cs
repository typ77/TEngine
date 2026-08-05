using System.Collections.Generic;

namespace GameLogic.RedDot.Core
{
    /// <summary>
    /// 红点叶子节点外部判断逻辑委托（不带 id）。
    /// </summary>
    /// <param name="status">输出：红点状态</param>
    /// <param name="num">输出：红点数值（仅 Number 状态有意义）</param>
    public delegate void RedDotExternalLogic(out RedDotStatus status, out int num);

    /// <summary>
    /// 红点叶子节点外部判断逻辑委托（带 id，用于列表项红点）。
    /// </summary>
    /// <param name="id">业务 id（如邮件 id）</param>
    /// <param name="status">输出：红点状态</param>
    /// <param name="num">输出：红点数值</param>
    public delegate void RedDotExternalLogicWithId(uint id, out RedDotStatus status, out int num);

    /// <summary>
    /// 红点外部逻辑访问器。叶子节点的红点状态由业务方注册的委托实时计算得到。
    /// <para>移植自 fermsi/RedDotGraph，改为普通类（由 <see cref="RedDotSystem"/> 持有，不再独立单例）。
    /// 修复了原 <c>CheckHasExternalId</c> 只查无 id 字典的缺陷。</para>
    /// </summary>
    public class RedDotExternalLogicVisitor
    {
        private readonly Dictionary<int, RedDotExternalLogic> _visitors = new Dictionary<int, RedDotExternalLogic>();
        private readonly Dictionary<int, RedDotExternalLogicWithId> _visitorsWithId = new Dictionary<int, RedDotExternalLogicWithId>();

        /// <summary>
        /// 注册不带 id 的叶子判断逻辑。
        /// </summary>
        public void RegExternalLogic(int externalId, RedDotExternalLogic externalLogic)
        {
            _visitors[externalId] = externalLogic;
        }

        /// <summary>
        /// 注册带 id 的叶子判断逻辑（列表项红点用）。
        /// </summary>
        public void RegExternalLogic(int externalId, RedDotExternalLogicWithId externalLogic)
        {
            _visitorsWithId[externalId] = externalLogic;
        }

        /// <summary>
        /// 注销指定 externalId 的所有逻辑。
        /// </summary>
        public void UnRegExternalLogic(int externalId)
        {
            _visitors.Remove(externalId);
            _visitorsWithId.Remove(externalId);
        }

        /// <summary>
        /// 调用叶子节点的外部逻辑计算当前状态。
        /// <para>id==0 时优先用无 id 版本，否则用带 id 版本。</para>
        /// </summary>
        public void Visit(int externalId, uint id, out RedDotStatus status, out int num)
        {
            if (id == 0)
            {
                if (_visitors.TryGetValue(externalId, out var visitor))
                {
                    visitor(out status, out num);
                    return;
                }
            }
            if (_visitorsWithId.TryGetValue(externalId, out var visitorId))
            {
                visitorId(id, out status, out num);
                return;
            }

            status = 0;
            num = 0;
        }

        /// <summary>
        /// 是否已注册指定 externalId 的逻辑（修复原实现只查无 id 字典的缺陷）。
        /// </summary>
        public bool CheckHasExternalId(int externalId)
        {
            return _visitors.ContainsKey(externalId) || _visitorsWithId.ContainsKey(externalId);
        }

        /// <summary>
        /// 清理所有注册逻辑（模块释放时调用）。
        /// </summary>
        public void Clear()
        {
            _visitors.Clear();
            _visitorsWithId.Clear();
        }
    }
}
