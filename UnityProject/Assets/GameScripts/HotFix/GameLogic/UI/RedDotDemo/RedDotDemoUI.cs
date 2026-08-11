using System.Text;
using GameLogic.RedDot;
using GameLogic.RedDot.Core;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 红点 Demo 主界面。
    /// 展示 4 个红点节点（Root/Mail/Mail.Unread/Mail.Announce）+ 5 个交互按钮。
    ///
    /// 红点刷新采用 MVE 直接绑定模式：在 SetupBindings 里对每个 RedDotNode 的
    /// BindableProperty&lt;RedDotNodeState&gt; 调用 Bind，UIWindow 销毁时自动解绑。
    /// </summary>
    [Window(UILayer.UI)]
    public class RedDotDemoUI : UIWindow
    {
        // ── 红点显示节点引用（每行：红点 GameObject + 数字 Text） ──
        private GameObject _rootDotObj;
        private Text _rootNumText;
        private GameObject _mailDotObj;
        private Text _mailNumText;
        private GameObject _unreadDotObj;
        private Text _unreadNumText;
        private GameObject _announceDotObj;
        private Text _announceNumText;

        // ── 按钮引用 ──
        private Button _btnAddUnread;
        private Button _btnReadOne;
        private Button _btnAddNewMail;
        private Button _btnClearNewMail;
        private Button _btnToggleAnnounce;

        // ── 操作日志 ──
        private Text _textLog;
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private const int MAX_LOG_LINES = 8;

        private int _newMailCounter;

        // ── 节点绑定 ──
        protected override void ScriptGenerator()
        {
            // 4 行红点节点：每行有 m_go_Dot + m_text_Num 子节点
            _rootDotObj = FindChild("DisplayArea/RowRoot/RedDotContainer/m_go_Dot")?.gameObject;
            _rootNumText = FindChildComponent<Text>("DisplayArea/RowRoot/RedDotContainer/m_go_Dot/m_text_Num");
            _mailDotObj = FindChild("DisplayArea/RowMail/RedDotContainer/m_go_Dot")?.gameObject;
            _mailNumText = FindChildComponent<Text>("DisplayArea/RowMail/RedDotContainer/m_go_Dot/m_text_Num");
            _unreadDotObj = FindChild("DisplayArea/RowUnread/RedDotContainer/m_go_Dot")?.gameObject;
            _unreadNumText = FindChildComponent<Text>("DisplayArea/RowUnread/RedDotContainer/m_go_Dot/m_text_Num");
            _announceDotObj = FindChild("DisplayArea/RowAnnounce/RedDotContainer/m_go_Dot")?.gameObject;
            _announceNumText = FindChildComponent<Text>("DisplayArea/RowAnnounce/RedDotContainer/m_go_Dot/m_text_Num");

            _btnAddUnread = FindChildComponent<Button>("BtnArea/m_btn_AddUnread");
            _btnReadOne = FindChildComponent<Button>("BtnArea/m_btn_ReadOne");
            _btnAddNewMail = FindChildComponent<Button>("BtnArea/m_btn_AddNewMail");
            _btnClearNewMail = FindChildComponent<Button>("BtnArea/m_btn_ClearNewMail");
            _btnToggleAnnounce = FindChildComponent<Button>("BtnArea/m_btn_ToggleAnnounce");
            _textLog = FindChildComponent<Text>("TextLogContainer/m_text_Log");
        }

        protected override void SetupBindings()
        {
            base.SetupBindings();
            BindRedDotNode("Root", _rootDotObj, _rootNumText);
            BindRedDotNode("Mail", _mailDotObj, _mailNumText);
            BindRedDotNode("Mail.Unread", _unreadDotObj, _unreadNumText);
            BindRedDotNode("Mail.Announce", _announceDotObj, _announceNumText);
        }

        /// <summary>绑定一个红点节点：状态变化时刷新红点显隐与数字。</summary>
        private void BindRedDotNode(string key, GameObject dotObj, Text numText)
        {
            var node = RedDotSystem.Instance.GetNode(key);
            if (node == null)
            {
                Log.Warning($"[RedDotDemoUI] 红点节点不存在：{key}");
                return;
            }
            Bind(node.State, state =>
            {
                if (dotObj != null)
                {
                    bool show = state.IsActive;
                    dotObj.SetActive(show);
                }
                if (numText != null)
                {
                    bool showNum = state.Status == RedDotStatus.Number && state.Count > 0;
                    numText.gameObject.SetActive(showNum);
                    if (showNum) numText.text = state.Count > 99 ? "99+" : state.Count.ToString();
                }
            });
        }

        protected override void OnCreate()
        {
            BindButton(_btnAddUnread, OnAddUnread);
            BindButton(_btnReadOne, OnReadOne);
            BindButton(_btnAddNewMail, OnAddNewMail);
            BindButton(_btnClearNewMail, OnClearNewMail);
            BindButton(_btnToggleAnnounce, OnToggleAnnounce);

            AppendLog("红点 Demo 就绪。点击按钮触发红点变化。");
            AppendLog($"初始：未读 {MailDemoData.Instance.UnreadCount}，公告 {(MailDemoData.Instance.HasAnnounce ? "开" : "关")}，新功能 0");
        }

        protected override void OnDestroy()
        {
            UnbindButton(_btnAddUnread, OnAddUnread);
            UnbindButton(_btnReadOne, OnReadOne);
            UnbindButton(_btnAddNewMail, OnAddNewMail);
            UnbindButton(_btnClearNewMail, OnClearNewMail);
            UnbindButton(_btnToggleAnnounce, OnToggleAnnounce);
        }

        // ── 按钮交互（演示 4 大红点特性） ──

        /// <summary>+1 未读邮件：演示数量求和 + 父子聚合。</summary>
        private void OnAddUnread()
        {
            MailDemoData.Instance.UnreadCount++;
            RedDotSystem.Instance.MarkDirty("Mail.Unread");
            AppendLog($"+1 未读邮件，当前未读 {MailDemoData.Instance.UnreadCount}");
        }

        /// <summary>读取一封：未读数 -1。</summary>
        private void OnReadOne()
        {
            if (MailDemoData.Instance.UnreadCount <= 0)
            {
                AppendLog("已无未读邮件可读");
                return;
            }
            MailDemoData.Instance.UnreadCount--;
            RedDotSystem.Instance.MarkDirty("Mail.Unread");
            AppendLog($"读取一封，剩余未读 {MailDemoData.Instance.UnreadCount}");
        }

        /// <summary>新增 New 邮件：演示动态 AddLeaf（New 状态优先级高于 Number）。</summary>
        private void OnAddNewMail()
        {
            int id = MailDemoData.Instance.AddNewMail();
            _newMailCounter++;
            string key = $"Mail.Item.{id}";
            RedDotSystem.Instance.AddLeaf(key, "Mail", RedDotDemoLogicIds.NewMail, RedDotStatus.New);
            AppendLog($"新增 New 邮件 #{_newMailCounter}（{key}），Mail 应显示 New 优先级");
        }

        /// <summary>清空 New 邮件：演示 ClearLeavesUnder 批量动态删除。</summary>
        private void OnClearNewMail()
        {
            if (MailDemoData.Instance.NewMailCount == 0)
            {
                AppendLog("无 New 邮件可清空");
                return;
            }
            int removed = RedDotSystem.Instance.ClearLeavesUnder("Mail");
            MailDemoData.Instance.ClearNewMails();
            _newMailCounter = 0;
            AppendLog($"清空 {removed} 个 New 邮件，Mail 应回到数量/公告优先级");
        }

        /// <summary>切换公告开关：演示布尔红点（Normal 状态）。</summary>
        private void OnToggleAnnounce()
        {
            MailDemoData.Instance.HasAnnounce = !MailDemoData.Instance.HasAnnounce;
            RedDotSystem.Instance.MarkDirty("Mail.Announce");
            AppendLog($"公告已{(MailDemoData.Instance.HasAnnounce ? "开启" : "关闭")}");
        }

        // ── 辅助 ──

        private void AppendLog(string line)
        {
            _logBuilder.AppendLine(line);
            var lines = _logBuilder.ToString().Split('\n');
            if (lines.Length > MAX_LOG_LINES + 2)
            {
                _logBuilder.Clear();
                for (int i = lines.Length - MAX_LOG_LINES - 1; i < lines.Length; i++)
                {
                    if (i >= 0 && !string.IsNullOrEmpty(lines[i]))
                        _logBuilder.AppendLine(lines[i]);
                }
            }
            if (_textLog != null) _textLog.text = _logBuilder.ToString();
        }

        private static void BindButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn != null) btn.onClick.AddListener(action);
        }

        private static void UnbindButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn != null) btn.onClick.RemoveListener(action);
        }
    }
}
