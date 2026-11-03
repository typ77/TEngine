using System.Collections.Generic;

namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 邮件 Demo 的模拟数据层（Singleton）。
    /// 持有五个叶子节点的数据源,供 ProcedureRedDotDemo 注册的 checker 查询;
    /// 本类只存数据不碰红点 API,报脏由 UI 按钮触发 DataChange。
    /// </summary>
    public sealed class MailDemoData : Singleton<MailDemoData>
    {
        /// <summary>系统公告状态(对应 Mail.Announce 叶子:红点/蓝点/关 三态)。</summary>
        public enum AnnounceStateEnum
        {
            Off = 0,
            Red = 1,
            Blue = 2,
        }

        /// <summary>未读邮件数(对应 Mail.Unread 叶子,红数字态)。</summary>
        public int UnreadCount { get; set; } = 2;

        /// <summary>公告状态(对应 Mail.Announce 叶子,默认红点态)。</summary>
        public AnnounceStateEnum AnnounceMode { get; set; } = AnnounceStateEnum.Red;

        /// <summary>限时商店是否满溢(对应 Shop.LimitTime 叶子,满态)。</summary>
        public bool ShopFull { get; set; } = true;

        /// <summary>今日签到是否未领取(对应 Activity.SignIn 叶子,新标签态)。</summary>
        public bool SignInUnclaimed { get; set; } = true;

        /// <summary>可领取福利数(对应 Activity.Welfare 叶子,蓝数字态)。</summary>
        public int WelfareCount { get; set; } = 3;

        /// <summary>动态节点模拟:活动期次数据(ownerId→可领取奖励数,>0 亮红点)。</summary>
        private readonly Dictionary<int, int> _dynamicRewards = new Dictionary<int, int>();
        private int _nextDynamicOwnerId = 1;

        /// <summary>新增一个动态活动期次,返回 owner id(奖励数默认 1,可领取)。</summary>
        public int AddDynamicReward()
        {
            int ownerId = _nextDynamicOwnerId++;
            _dynamicRewards[ownerId] = 1;
            return ownerId;
        }

        /// <summary>查询动态活动期次是否有可领取奖励(动态叶子 checker 数据源)。</summary>
        public bool HasDynamicReward(int ownerId)
        {
            return _dynamicRewards.TryGetValue(ownerId, out int count) && count > 0;
        }

        /// <summary>移除动态活动期次。</summary>
        public bool RemoveDynamicReward(int ownerId)
        {
            return _dynamicRewards.Remove(ownerId);
        }

        protected override void OnRelease()
        {
            _dynamicRewards.Clear();
            base.OnRelease();
        }
    }
}
