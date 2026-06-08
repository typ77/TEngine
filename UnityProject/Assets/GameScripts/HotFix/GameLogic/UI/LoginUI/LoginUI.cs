using GameLogic.DataBinding;
using UnityEngine.UI;
using TEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    /// <summary>
    /// 登录界面（MVE 数据绑定演示）。
    ///
    /// 演示完整的 MVE 数据流：
    /// 1. 点击登录按钮 → Model 数据变更
    /// 2. DataContext 自动映射 → View 属性更新
    /// 3. Bind() 订阅 → UI 文本自动刷新
    ///
    /// 数据流：
    ///   LoginDataContext.RandomLogin()
    ///     → AccountName.Value 变更
    ///     → MapProperty 转换为 DisplayAccount
    ///     → Bind() 回调更新 m_text_Account.text
    /// </summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(LoginDataContext))]
    public class LoginUI : UIWindow
    {
        private InputField m_inputAccount;
        private InputField m_inputPassword;
        private Button m_btnLogin;
        private Text m_textAccount;

        protected override void ScriptGenerator()
        {
            m_inputAccount = FindChildComponent<InputField>("m_inputAccount");
            m_inputPassword = FindChildComponent<InputField>("m_inputPassword");
            m_btnLogin = FindChildComponent<Button>("m_btnLogin");
            m_textAccount = FindChildComponent<Text>("m_text_Account");
            m_btnLogin.onClick.AddListener(OnLoginClicked);
        }

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
            Bind(dc.AccountName, name =>
            {
                if (m_inputAccount != null)
                    m_inputAccount.text = name;
            });

            // 绑定 3：登录状态 → 密码输入框占位（演示多属性绑定）
            Bind(dc.DisplayStatus, status =>
            {
                Log.Info($"[MVE Demo] 登录状态变更: {status}");
            });
        }

        protected override void OnRefresh()
        {
            // 每次显示时刷新（Bind 初始化已在 SetupBindings 中完成首次同步）
        }

        private void OnLoginClicked()
        {
            // MVE 核心：只修改 Model，View 自动更新
            var dc = GetDataContext<LoginDataContext>();
            dc.RandomLogin();
        }
    }
}
