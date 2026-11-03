using System;
using System.Collections.Generic;
using System.Text;
using RedDot.Runtime.RedDot;
using TEngine;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic.RedDotDemo
{
    /// <summary>
    /// 红点 Demo 主界面（RedDotGraph 新图版）。
    /// 展示:聚合链(Root→Mail/Shop/Activity 分组) + 五叶六态(RedNumber/RedDot|BlueDot/Full/Tips/BlueNumber)
    /// + 动态节点增删(RegisterDynamicNode/SetDynamicParent/BindDynamicChecker/RemoveDynamicNode)
    /// + SetNodeEnabled 门控。
    /// 红点刷新走 RedDotWidget 六态渲染 + RedDotGraphModule.AddUI 回调模型:
    /// 固定行由 prefab 上 RedDotKeyBinder 序列化的 key 自动绑定(RedDotWidget.RegisterEvent 内 BindNewGraph),
    /// 动态行经 SetKey 运行时换绑;回调内 GetRedDotStatus 读时刷新取态。
    /// </summary>
    [Window(UILayer.UI)]
    public class RedDotDemoUI : UIWindow
    {
        // ── 红点行(widget 节点,RedDotWidget 六态渲染) ──
        private RedDotWidget _rootRow;
        private RedDotWidget _mailRow;
        private RedDotWidget _unreadRow;
        private RedDotWidget _announceRow;
        private RedDotWidget _limitTimeRow;
        private RedDotWidget _signInRow;
        private RedDotWidget _welfareRow;
        private RedDotWidget _dynamicRow;

        // ── 动态行标签(显示当前绑定的 dynKey) ──
        private Text _dynamicLabel;

        // ── 按钮引用 ──
        private Button _btnAddUnread;
        private Button _btnReadOne;
        private Button _btnToggleAnnounce;
        private Button _btnToggleShopFull;
        private Button _btnToggleSignIn;
        private Button _btnAddWelfare;
        private Button _btnClaimWelfare;
        private Button _btnAddDynamic;
        private Button _btnRemoveDynamic;
        private Button _btnToggleWelfareEnabled;

        // ── 动态节点登记(ownerKey/dynKey 平行栈,后进先出) ──
        private readonly List<string> _dynamicOwnerKeys = new List<string>();
        private readonly List<string> _dynamicKeys = new List<string>();

        // ── Welfare 门控状态(用于按钮翻转) ──
        private bool _welfareEnabled = true;

        // ── 操作日志 ──
        private Text _textLog;
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private const int MaxLogLines = 10;

        // ── 行路径表(与 RedDotDemoBuilder 生成的 prefab 结构一致) ──
        private const string RowRootPath = "DisplayArea/RowRoot";
        private const string RowMailPath = "DisplayArea/RowMail";
        private const string RowUnreadPath = "DisplayArea/RowUnread";
        private const string RowAnnouncePath = "DisplayArea/RowAnnounce";
        private const string RowLimitTimePath = "DisplayArea/RowLimitTime";
        private const string RowSignInPath = "DisplayArea/RowSignIn";
        private const string RowWelfarePath = "DisplayArea/RowWelfare";
        private const string RowDynamicPath = "DisplayArea/RowDynamic";
        private const string DynamicLabelPath = "DisplayArea/RowDynamic/Label";

        protected override void ScriptGenerator()
        {
            _dynamicLabel = FindChildComponent<Text>(DynamicLabelPath);
            _btnAddUnread = FindChildComponent<Button>("BtnArea/m_btn_AddUnread");
            _btnReadOne = FindChildComponent<Button>("BtnArea/m_btn_ReadOne");
            _btnToggleAnnounce = FindChildComponent<Button>("BtnArea/m_btn_ToggleAnnounce");
            _btnToggleShopFull = FindChildComponent<Button>("BtnArea/m_btn_ToggleShopFull");
            _btnToggleSignIn = FindChildComponent<Button>("BtnArea/m_btn_ToggleSignIn");
            _btnAddWelfare = FindChildComponent<Button>("BtnArea/m_btn_AddWelfare");
            _btnClaimWelfare = FindChildComponent<Button>("BtnArea/m_btn_ClaimWelfare");
            _btnAddDynamic = FindChildComponent<Button>("BtnArea/m_btn_AddDynamic");
            _btnRemoveDynamic = FindChildComponent<Button>("BtnArea/m_btn_RemoveDynamic");
            _btnToggleWelfareEnabled = FindChildComponent<Button>("BtnArea/m_btn_ToggleWelfareEnabled");
            _textLog = FindChildComponent<Text>("TextLogContainer/m_text_Log");
        }

        protected override void OnCreate()
        {
            EnsureTmpFont();
            CreateRowWidgets();
            BindButton(_btnAddUnread, OnAddUnread);
            BindButton(_btnReadOne, OnReadOne);
            BindButton(_btnToggleAnnounce, OnToggleAnnounce);
            BindButton(_btnToggleShopFull, OnToggleShopFull);
            BindButton(_btnToggleSignIn, OnToggleSignIn);
            BindButton(_btnAddWelfare, OnAddWelfare);
            BindButton(_btnClaimWelfare, OnClaimWelfare);
            BindButton(_btnAddDynamic, OnAddDynamic);
            BindButton(_btnRemoveDynamic, OnRemoveDynamic);
            BindButton(_btnToggleWelfareEnabled, OnToggleWelfareEnabled);
            AppendLog("红点 Demo 就绪(RedDotGraph 新图)。点击右侧按钮触发 DataChange。");
        }

        protected override void OnDestroy()
        {
            UnbindButton(_btnAddUnread, OnAddUnread);
            UnbindButton(_btnReadOne, OnReadOne);
            UnbindButton(_btnToggleAnnounce, OnToggleAnnounce);
            UnbindButton(_btnToggleShopFull, OnToggleShopFull);
            UnbindButton(_btnToggleSignIn, OnToggleSignIn);
            UnbindButton(_btnAddWelfare, OnAddWelfare);
            UnbindButton(_btnClaimWelfare, OnClaimWelfare);
            UnbindButton(_btnAddDynamic, OnAddDynamic);
            UnbindButton(_btnRemoveDynamic, OnRemoveDynamic);
            UnbindButton(_btnToggleWelfareEnabled, OnToggleWelfareEnabled);
            // widget 随窗口 ListChild 统一销毁(UIWidget.OnDestroyWidget→OnDestroy→UnbindNewGraph),此处不重复释放
            _rootRow = null;
            _mailRow = null;
            _unreadRow = null;
            _announceRow = null;
            _limitTimeRow = null;
            _signInRow = null;
            _welfareRow = null;
            _dynamicRow = null;
        }

        // ── 行 widget 创建 ──

        /// <summary>
        /// 逐行创建 RedDotWidget:固定行走 prefab 上 RedDotKeyBinder 序列化 key(RegisterEvent 自动绑定),
        /// 动态行 binder 留空,由 SetKey 换绑。
        /// </summary>
        private void CreateRowWidgets()
        {
            _rootRow = CreateRowWidget(RowRootPath);
            _mailRow = CreateRowWidget(RowMailPath);
            _unreadRow = CreateRowWidget(RowUnreadPath);
            _announceRow = CreateRowWidget(RowAnnouncePath);
            _limitTimeRow = CreateRowWidget(RowLimitTimePath);
            _signInRow = CreateRowWidget(RowSignInPath);
            _welfareRow = CreateRowWidget(RowWelfarePath);
            _dynamicRow = CreateRowWidget(RowDynamicPath);
        }

        private RedDotWidget CreateRowWidget(string rowPath)
        {
            var rowGo = FindChild(rowPath)?.gameObject;
            if (rowGo == null)
            {
                Log.Warning("[RedDotDemoUI] 红点行节点不存在:{0}", rowPath);
                return null;
            }
            var widget = new RedDotWidget();
            if (!widget.Create(this, rowGo))
            {
                Log.Warning("[RedDotDemoUI] RedDotWidget 创建失败:{0}", rowPath);
                return null;
            }
            return widget;
        }

        /// <summary>
        /// Demo 特例:工程未导入 TMP Essentials 时(TMP Settings 资产不存在),
        /// 红数字/蓝数字的 TextMeshProUGUI 无字体可渲染。运行时用内置 LegacyRuntime.ttf
        /// 生成动态字体资产,进程级静态缓存一份供全部行复用;导入 TMP Essentials 后本兜底自动失效。
        /// 注意:TMP Essentials 缺失时 TMP_Settings.defaultFontAsset 的 getter 本身会抛 NRE
        /// (内部 instance 为 null 直接解引用),必须 try/catch 兜住后走兜底分支。
        /// </summary>
        private static TMP_FontAsset s_fallbackFontAsset;

        private void EnsureTmpFont()
        {
            TMP_FontAsset settingsFont = null;
            try
            {
                settingsFont = TMP_Settings.defaultFontAsset;
            }
            catch (Exception)
            {
                // TMP Settings 资产不存在(TMP Essentials 未导入),按无字体处理,走下方兜底
            }
            if (settingsFont != null)
            {
                return;
            }
            var fontAsset = GetOrCreateFallbackFontAsset();
            if (fontAsset == null)
            {
                Log.Warning("[RedDotDemoUI] 无法生成 TMP 兜底字体,数字态文本将不渲染;请导入 TMP Essentials 后重建 Demo");
                return;
            }
            FixRowFont(RowUnreadPath, fontAsset);
            FixRowFont(RowWelfarePath, fontAsset);
        }

        private static TMP_FontAsset GetOrCreateFallbackFontAsset()
        {
            if (s_fallbackFontAsset != null)
            {
                return s_fallbackFontAsset;
            }
            var builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtinFont == null)
            {
                return null;
            }
            s_fallbackFontAsset = TMP_FontAsset.CreateFontAsset(builtinFont);
            return s_fallbackFontAsset;
        }

        private void FixRowFont(string rowPath, TMP_FontAsset fontAsset)
        {
            FixNumFont(FindChildComponent<TextMeshProUGUI>(rowPath + "/RedDotContainer/m_goReddotNum"), fontAsset);
            FixNumFont(FindChildComponent<TextMeshProUGUI>(rowPath + "/RedDotContainer/m_goBluedotNum"), fontAsset);
        }

        private static void FixNumFont(TextMeshProUGUI tmp, TMP_FontAsset fontAsset)
        {
            if (tmp != null && tmp.font == null)
            {
                tmp.font = fontAsset;
            }
        }

        // ── 按钮交互(只发命令/报脏,UI 不改红点图状态) ──

        /// <summary>+1 未读邮件:红数字态,Root/Mail/Mail.Unread 数字 +1。</summary>
        private void OnAddUnread()
        {
            MailDemoData.Instance.UnreadCount++;
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Mail_Unread);
            AppendLog(Utility.Text.Format("+1 未读邮件,当前未读 {0}", MailDemoData.Instance.UnreadCount));
        }

        /// <summary>读取一封:未读数 -1,归零后红点熄灭。</summary>
        private void OnReadOne()
        {
            if (MailDemoData.Instance.UnreadCount <= 0)
            {
                AppendLog("已无未读邮件可读");
                return;
            }
            MailDemoData.Instance.UnreadCount--;
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Mail_Unread);
            AppendLog(Utility.Text.Format("读取一封,剩余未读 {0}", MailDemoData.Instance.UnreadCount));
        }

        /// <summary>公告 红→蓝→关 循环:演示同节点 RedDot/BlueDot 双态切换(节点掩码 3)。</summary>
        private void OnToggleAnnounce()
        {
            switch (MailDemoData.Instance.AnnounceMode)
            {
                case MailDemoData.AnnounceStateEnum.Red:
                    MailDemoData.Instance.AnnounceMode = MailDemoData.AnnounceStateEnum.Blue;
                    AppendLog("公告切换为蓝点态");
                    break;
                case MailDemoData.AnnounceStateEnum.Blue:
                    MailDemoData.Instance.AnnounceMode = MailDemoData.AnnounceStateEnum.Off;
                    AppendLog("公告关闭(蓝点熄灭)");
                    break;
                default:
                    MailDemoData.Instance.AnnounceMode = MailDemoData.AnnounceStateEnum.Red;
                    AppendLog("公告开启(红点态)");
                    break;
            }
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Mail_Announce);
        }

        /// <summary>限时商店 满/空:满态(Full 优先级最高,Root 被它覆盖)。</summary>
        private void OnToggleShopFull()
        {
            MailDemoData.Instance.ShopFull = !MailDemoData.Instance.ShopFull;
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Shop_LimitTime);
            AppendLog(Utility.Text.Format("限时商店{0}", MailDemoData.Instance.ShopFull ? "满溢(满态点亮)" : "清空"));
        }

        /// <summary>签到 领取/重置:新标签(Tips)态。</summary>
        private void OnToggleSignIn()
        {
            MailDemoData.Instance.SignInUnclaimed = !MailDemoData.Instance.SignInUnclaimed;
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Activity_SignIn);
            AppendLog(Utility.Text.Format("签到{0}", MailDemoData.Instance.SignInUnclaimed ? "重置为新标签态" : "已领取"));
        }

        /// <summary>+1 福利:蓝数字态。</summary>
        private void OnAddWelfare()
        {
            MailDemoData.Instance.WelfareCount++;
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Activity_Welfare);
            AppendLog(Utility.Text.Format("+1 福利,当前可领 {0}", MailDemoData.Instance.WelfareCount));
        }

        /// <summary>-1 福利:蓝数字态,归零熄灭。</summary>
        private void OnClaimWelfare()
        {
            if (MailDemoData.Instance.WelfareCount <= 0)
            {
                AppendLog("无可领取福利");
                return;
            }
            MailDemoData.Instance.WelfareCount--;
            RedDotGraphModule.Instance.DataChange(RedDotKeys.Activity_Welfare);
            AppendLog(Utility.Text.Format("领取一份福利,剩余 {0}", MailDemoData.Instance.WelfareCount));
        }

        /// <summary>
        /// 新增动态节点:RegisterDynamicNode(幂等分配 dyn:) → SetDynamicParent(挂到 Mail) →
        /// BindDynamicChecker(绑该期次 checker) → DataChange(触发首算) → 演示行 SetKey 换绑展示。
        /// </summary>
        private void OnAddDynamic()
        {
            int ownerId = MailDemoData.Instance.AddDynamicReward();
            string ownerKey = Utility.Text.Format("act:{0}", ownerId);
            string dynKey;
            RedDotGraphModule.Instance.RegisterDynamicNode(ownerKey, out dynKey);
            RedDotGraphModule.Instance.SetDynamicParent(dynKey, RedDotKeys.Mail);
            int capturedId = ownerId;
            RedDotGraphModule.Instance.BindDynamicChecker(dynKey, (out RedDotStatus status, out int num) =>
            {
                status = MailDemoData.Instance.HasDynamicReward(capturedId)
                    ? RedDotStatus.RedDotStatusRedDot
                    : RedDotStatus.RedDotStatusNull;
                num = 0;
            });
            RedDotGraphModule.Instance.DataChange(dynKey);
            _dynamicOwnerKeys.Add(ownerKey);
            _dynamicKeys.Add(dynKey);
            BindDynamicRow(dynKey);
            AppendLog(Utility.Text.Format("新增动态节点 {0}(挂 Mail 下,红点态)", dynKey));
        }

        /// <summary>移除最新动态节点:RemoveDynamicNode(摘边/摘 checker/对父补发),演示行回退绑定上一节点。</summary>
        private void OnRemoveDynamic()
        {
            if (_dynamicOwnerKeys.Count == 0)
            {
                AppendLog("无动态节点可移除");
                return;
            }
            int last = _dynamicOwnerKeys.Count - 1;
            string ownerKey = _dynamicOwnerKeys[last];
            string dynKey = _dynamicKeys[last];
            _dynamicOwnerKeys.RemoveAt(last);
            _dynamicKeys.RemoveAt(last);
            MailDemoData.Instance.RemoveDynamicReward(ParseOwnerId(ownerKey));
            RedDotGraphModule.Instance.RemoveDynamicNode(ownerKey);
            if (_dynamicKeys.Count > 0)
            {
                BindDynamicRow(_dynamicKeys[_dynamicKeys.Count - 1]);
            }
            else
            {
                _dynamicRow?.ClearKey();
                if (_dynamicLabel != null)
                {
                    _dynamicLabel.text = "动态节点 (无)";
                }
            }
            AppendLog(Utility.Text.Format("移除动态节点 {0},父链已补发重算", dynKey));
        }

        /// <summary>ownerKey("act:{id}") 还原 id。</summary>
        private static int ParseOwnerId(string ownerKey)
        {
            return int.TryParse(ownerKey.Substring(ownerKey.IndexOf(':') + 1), out var id) ? id : 0;
        }

        /// <summary>门控演示:禁用节点吞 DataChange/读回 Null,启用后恢复参与聚合。</summary>
        private void OnToggleWelfareEnabled()
        {
            _welfareEnabled = !_welfareEnabled;
            RedDotGraphModule.Instance.SetNodeEnabled(RedDotKeys.Activity_Welfare, _welfareEnabled);
            AppendLog(Utility.Text.Format("Activity.Welfare 门控{0}({1})",
                _welfareEnabled ? "启用" : "禁用", _welfareEnabled ? "报脏恢复传播" : "吞报脏+读回 Null"));
        }

        private void BindDynamicRow(string dynKey)
        {
            _dynamicRow?.SetKey(dynKey);
            if (_dynamicLabel != null)
            {
                _dynamicLabel.text = Utility.Text.Format("动态节点 {0}", dynKey);
            }
        }

        // ── 辅助 ──

        private void AppendLog(string line)
        {
            _logBuilder.AppendLine(line);
            var lines = _logBuilder.ToString().Split('\n');
            if (lines.Length > MaxLogLines + 2)
            {
                _logBuilder.Clear();
                for (int i = lines.Length - MaxLogLines - 1; i < lines.Length; i++)
                {
                    if (i >= 0 && !string.IsNullOrEmpty(lines[i]))
                    {
                        _logBuilder.AppendLine(lines[i]);
                    }
                }
            }
            if (_textLog != null)
            {
                _textLog.text = _logBuilder.ToString();
            }
        }

        private static void BindButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn != null)
            {
                btn.onClick.AddListener(action);
            }
        }

        private static void UnbindButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn != null)
            {
                btn.onClick.RemoveListener(action);
            }
        }
    }
}
