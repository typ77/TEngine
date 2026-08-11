namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 邮件 Demo 的红点叶子逻辑 ID 常量（对应 JSON 配置里的 externalId）。
    /// <para>集中定义避免多处魔法数字重复，防止改一处漏改另一处。</para>
    /// </summary>
    public static class RedDotDemoLogicIds
    {
        /// <summary>未读普通邮件计数（数量型，对应 Mail.Unread 叶子）。</summary>
        public const int Unread = 1;

        /// <summary>系统公告开关（布尔型，对应 Mail.Announce 叶子）。</summary>
        public const int Announce = 2;

        /// <summary>新功能邮件（带 id，对应动态叶子 Mail.Item.{id}）。</summary>
        public const int NewMail = 3;
    }
}
