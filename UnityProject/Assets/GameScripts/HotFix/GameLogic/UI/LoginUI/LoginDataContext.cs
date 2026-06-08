using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>
    /// LoginUI 的数据上下文（MVE — DataContext 层）。
    ///
    /// 职责：
    /// - 从 Model 层读取原始数据，映射为 View 友好的格式
    /// - 通过 MapProperty 建立 Model→View 的响应式绑定链
    /// - Model 数据变更时自动触发 View 属性更新
    ///
    /// 数据流：
    ///   LoginModel.AccountName (原始数据)
    ///     → MapProperty 转换
    ///       → DisplayAccount (格式化文本 "当前账号: Hero_001")
    ///         → LoginUI.Bind() 订阅
    ///           → m_text_Account.text 自动更新
    ///
    /// 规范：
    /// - DataContext 只读 Model，不修改 Model
    /// - DataContext 不引用 UI 组件（View 通过 Bind() 消费）
    /// - 通过 [DataContext] 特性标记，由 DataContextFactory 自动创建
    /// - 无参构造 + Singleton&lt;T&gt;.Instance 访问 Model（HybridCLR 安全）
    /// </summary>
    [DataContext(typeof(LoginUI))]
    public class LoginDataContext : DataContext<LoginUI>
    {
        /// <summary>View 输出：格式化的账号文本。</summary>
        public readonly BindableProperty<string> DisplayAccount = new BindableProperty<string>("");

        /// <summary>View 输出：登录状态文本。</summary>
        public readonly BindableProperty<string> DisplayStatus = new BindableProperty<string>("");

        public LoginDataContext()
        {
            var model = Singleton<LoginModel>.Instance;

            // Model → View 映射：AccountName → "当前账号: {name}"
            MapProperty(model.AccountName, DisplayAccount, FormatAccount);

            // Model → View 映射：LoginCount → 状态文本
            MapProperty(model.LoginCount, DisplayStatus, FormatStatus);
        }

        private static string FormatAccount(string name) => $"当前账号: {name}";

        private static string FormatStatus(int count) => count > 0 ? $"已登录 {count} 次" : "未登录";
    }
}
