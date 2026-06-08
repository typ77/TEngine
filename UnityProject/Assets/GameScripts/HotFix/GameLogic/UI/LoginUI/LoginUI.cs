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
    /// - SetupBindings：用 BindText 等便捷方法订阅 DataContext 属性
    /// - 用户交互：只调用 Service，不直接操作 Model
    ///
    /// MVE 分层交互：
    ///   用户点击 [登录按钮]
    ///     → View 调用 Service.RandomLogin()        （View → Service）
    ///       → Service 修改 Model 数据               （Service → Model）
    ///         → DataContext MapProperty 自动映射     （Model → DataContext）
    ///           → BindText 自动刷新 UI              （DataContext → View）
    ///
    /// 规范：
    /// - View 不直接读写 Model（通过 Service 操作）
    /// - View 不包含业务逻辑（由 Service 负责）
    /// - View 通过 BindText 等便捷方法响应数据变化，一行搞定
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
        /// 数据绑定：用 BindText 一行绑定组件。
        /// 对比 Phase 1 的 4 行 lambda 写法，现在是 1 行搞定。
        /// </summary>
        protected override void SetupBindings()
        {
            var dc = GetDataContext<LoginDataContext>();
            var model = Singleton<LoginModel>.Instance;

            // Phase 2 便捷绑定：一行绑定 Text 组件
            BindText(m_textAccount, dc.DisplayAccount);

            // 一行绑定 InputField（同一 Model 可绑定多个 View）
            BindText(m_inputAccount, model.AccountName);

            // 金币显示：converter 在 DataContext 层处理 "破产" 逻辑
            // View 完全不知道"破产"是什么，只管显示 string
            BindText(m_inputPassword, dc.DisplayGold);

            // 按钮交互状态绑定（演示 BindInteractable）
            BindInteractable(m_btnLogin, model.IsLoggedIn);

            // 复杂场景仍可用 Bind + lambda（日志输出等）
            Bind(dc.DisplayStatus, status =>
            {
                Log.Info($"[MVE Demo] 状态变更: {status}");
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
