namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 邮件 Demo 的叶子 externalId 常量（对应 RedDotGraphConfig.json 配置里的 externalId,与 RedDotKeys 常量表一致）。
    /// <para>正式业务零魔法数收口走 RedDotKeys.XXX（key 常量自带 externalId）,本表仅保留给
    /// 动态节点 checker 与 Demo 数据层做注释对照。</para>
    /// <para>权威来源:值以 RedDotKeys / RedDotGraphConfig.json 为准,本表仅为阅读辅助,无对账保护,
    /// 图结构变更时须人工同步（不得在本表新增业务入口）。</para>
    /// </summary>
    public static class RedDotDemoLogicIds
    {
        /// <summary>未读邮件数（红数字态,对应 Mail.Unread 叶子）。</summary>
        public const int Unread = 1;

        /// <summary>系统公告开关（红点/蓝点双态,对应 Mail.Announce 叶子）。</summary>
        public const int Announce = 2;

        /// <summary>限时商店满溢（满态,对应 Shop.LimitTime 叶子）。</summary>
        public const int ShopLimitTime = 3;

        /// <summary>每日签到（新标签态,对应 Activity.SignIn 叶子）。</summary>
        public const int ActivitySignIn = 4;

        /// <summary>福利领取数（蓝数字态,对应 Activity.Welfare 叶子）。</summary>
        public const int ActivityWelfare = 5;
    }
}
