using System.Collections.Generic;

namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 邮件 Demo 的模拟数据层（Singleton）。
    /// 持有未读数、公告开关、新功能邮件列表，供 RedDotSystem 的叶子逻辑查询。
    /// </summary>
    public sealed class MailDemoData : Singleton<MailDemoData>
    {
        /// <summary>未读普通邮件数（对应 Mail.Unread 叶子）。</summary>
        public int UnreadCount { get; set; } = 2;

        /// <summary>是否有新系统公告（对应 Mail.Announce 叶子）。</summary>
        public bool HasAnnounce { get; set; } = true;

        /// <summary>新功能邮件 id 列表（对应动态叶子 Mail.Item.{id}）。</summary>
        private readonly List<int> _newMailIds = new List<int>();
        private int _nextNewMailId = 1000;

        /// <summary>当前新功能邮件数量。</summary>
        public int NewMailCount => _newMailIds.Count;

        /// <summary>新增一个新功能邮件，返回分配的 id。</summary>
        public int AddNewMail()
        {
            int id = _nextNewMailId++;
            _newMailIds.Add(id);
            return id;
        }

        /// <summary>获取所有新功能邮件 id（只读快照）。</summary>
        public List<int>.Enumerator GetNewMailIds() => _newMailIds.GetEnumerator();

        /// <summary>清空所有新功能邮件。</summary>
        public void ClearNewMails()
        {
            _newMailIds.Clear();
        }

        protected override void OnRelease()
        {
            _newMailIds.Clear();
            base.OnRelease();
        }
    }
}
