using System.Collections.Generic;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    /// <summary>
    /// 红点外部方法访问器
    /// </summary>
    public partial class RedDotExternalLogicVisitor
    {
        public delegate void RedDotExternalLogic(out RedDotStatus status, out int num);
        public delegate void RedDotExternalLogicWithId(uint id, out RedDotStatus status, out int num);
        private static RedDotExternalLogicVisitor s_ins;
        public static RedDotExternalLogicVisitor Ins()
        {
            if (s_ins == null)
            {
                s_ins = new RedDotExternalLogicVisitor();
            }
            return s_ins;
        }

        private RedDotExternalLogicVisitor()
        {
        }

        private Dictionary<int, RedDotExternalLogic> _visitors = new Dictionary<int, RedDotExternalLogic>();
        private Dictionary<int, RedDotExternalLogicWithId> _visitorsWithId = new Dictionary<int, RedDotExternalLogicWithId>();

        public void RegExternalLogic(int externalId, RedDotExternalLogic externalLogic)
        {
            _visitors[externalId] = externalLogic;
        }

        public void RegExternalLogic(int externalId, RedDotExternalLogicWithId externalLogic)
        {
            _visitorsWithId[externalId] = externalLogic;
        }

        public void Visit(int externalId, uint id, out RedDotStatus status, out int num)
        {
            if (id == 0)
            {
                if (_visitors.TryGetValue(externalId, out RedDotExternalLogic visitor))
                {
                    visitor(out status, out num);
                    return;
                }
            }
            if (_visitorsWithId.TryGetValue(externalId, out RedDotExternalLogicWithId visitorId))
            {
                visitorId(id, out status, out num);
                return;
            }

            status = 0;
            num = 0;
            Log.Warning("RedDotExternalFunVisitor.visitors里没有找到externalId : {0}", externalId);
        }

        public bool CheckHasExternalId(int externalId)
        {
            //P3-4(B4):无 id 与带 id 两张委托表都要查,漏查带 id 表会对合法注册误报未注册
            return _visitors.ContainsKey(externalId) || _visitorsWithId.ContainsKey(externalId);
        }

        /// <summary>
        /// P0-2:按 externalId 注销(两表同清)。动态节点删除时同步摘除陈旧 checker 闭包,
        /// 防其持已删顶点引用继续被 Visit 调用;未注册的 id 静默忽略。
        /// </summary>
        public void UnregisterExternalLogic(int externalId)
        {
            _visitors.Remove(externalId);
            _visitorsWithId.Remove(externalId);
        }

        /// <summary>
        /// R1(审查 2026-09-12):带 id 委托表是否已注册该 externalId。
        /// SummarizeCells 的现算/读缓存路由判据:有业务 WithId checker 才逐 cell 现算,
        /// 否则(externalId=-1 系统段直写节点)保持读 runtime info 缓存契约。
        /// </summary>
        public bool HasWithIdExternal(int externalId)
        {
            return _visitorsWithId.ContainsKey(externalId);
        }

        /// <summary>
        /// P4-1(A6):清空两张委托表——重登后旧闭包指向已销毁对象,必须随 Reset 一并清空
        /// </summary>
        public void Clear()
        {
            _visitors.Clear();
            _visitorsWithId.Clear();
        }
    }
}