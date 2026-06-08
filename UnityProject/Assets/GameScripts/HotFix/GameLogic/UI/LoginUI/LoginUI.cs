using GameLogic.DataBinding;
using UnityEngine.UI;
using TEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    /// <summary>
    /// 登录界面（MVE — View 层）。
    ///
    /// 职责：
    /// - ScriptGenerator：绑定 Prefab 节点引用
    /// - SetupBindings：订阅 DataContext 属性 → 刷新 UI 元素
    /// - 用户交互：只调用 Service，不直接操作 Model
    ///
    /// MVE 分层交互：
    ///   用户点击 [登录按钮]
    ///     → View 调用 Service.RandomLogin()        （View → Service）
    ///       → Service 修改 Model.AccountName       （Service → Model）
    ///         → DataContext MapProperty 自动映射     （Model → DataContext）
    ///           → Bind() 回调刷新 m_text_Account    （DataContext → View）
    ///
    /// 规范：
    /// - View 不直接读写 Model（通过 Service 操作）
    /// - View 不包含业务逻辑（由 Service 负责）
    /// - View 通过 Bind() 响应数据变化，不手动刷新
    /// - View 通过 [DataContext] 特性关联 DataContext 类型
    /// </summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(LoginDataContext))]
    public class LoginUI : UIWindow
    {
        // ── Prefab 节点引用 ──────────────────────────
        private InputField m_inputAccount;
        private InputField m_inputPassword;
        private Button m_btnLogin;
        private Text m_textAccount;

        // ── 生命周期 ────────────────────────────────

        protected override void ScriptGenerator()
        {
            m_inputAccount = FindChildComponent<InputField>("m_inputAccount");
            m_inputPassword = FindChildComponent<InputField>("m_inputPassword");
            m_btnLogin = FindChildComponent<Button>("m_btnLogin");
            m_textAccount = FindChildComponent<Text>("m_text_Account");

            m_btnLogin.onClick.AddListener(OnLoginClicked);
        }

        /// <summary>
        /// 数据绑定：订阅 DataContext 属性 → 刷新 UI 元素。
        /// 由 UIModule 在 InternalCreate 中自动调用（在 RegisterEvent 之前）。
        /// Bind() 首次调用时会立即同步当前值。
        /// </summary>
        protected override void SetupBindings()
        {
            var dc = GetDataContext<LoginDataContext>();

            // 绑定 1：格式化账号文本 → 显示标签
            Bind(dc.DisplayAccount, text =>
            {
                if (m_textAccount != null)
                    m_textAccount.text = text;
            });

            // 绑定 2：原始账号名 → 输入框（演示同一 Model 可绑定多个 View）
            var model = Singleton<LoginModel>.Instance;
            Bind(model.AccountName, name =>
            {
                if (m_inputAccount != null)
                    m_inputAccount.text = name;
            });

            // 绑定 3：登录状态 → 日志输出（演示 Bind 不限于 UI 组件）
            Bind(dc.DisplayStatus, status =>
            {
                Log.Info($"[MVE Demo] 登录状态变更: {status}");
            });
        }

        // ── 用户交互 ────────────────────────────────

        /// <summary>
        /// 登录按钮点击：只调用 Service，不直接操作 Model。
        /// </summary>
        private void OnLoginClicked()
        {
            Singleton<LoginService>.Instance.RandomLogin();
        }
    }
}
