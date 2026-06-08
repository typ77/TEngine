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
    ///         → LoginUI.BindText() 订阅
    ///           → m_text_Account.text 自动更新
    ///
    /// Converter 演示：
    ///   LoginModel.Gold = -500 (long)
    ///     → MapProperty converter 转换
    ///       → DisplayGold = "破产" (string)
    ///         → View 无需知道"破产"逻辑，只负责渲染 string
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

        /// <summary>View 输出：格式化的金币文本（含"破产" converter 演示）。</summary>
        public readonly BindableProperty<string> DisplayGold = new BindableProperty<string>("");

        public LoginDataContext()
        {
            var model = Singleton<LoginModel>.Instance;

            // Model → View 映射：AccountName → "当前账号: {name}"
            MapProperty(model.AccountName, DisplayAccount, FormatAccount);

            // Model → View 映射：LoginCount → 状态文本
            MapProperty(model.LoginCount, DisplayStatus, FormatStatus);

            // Model → View 映射：Gold (long) → 格式化文本
            // Converter 职责：负数显示"破产"，正数显示金币数
            MapProperty(model.Gold, DisplayGold, FormatGold);
        }

        private static string FormatAccount(string name) => $"当前账号: {name}";

        private static string FormatStatus(int count) => count > 0 ? $"已登录 {count} 次" : "未登录";

        /// <summary>
        /// 金币格式化 converter：负数 → "破产"，正数 → 带单位简写。
        /// 此逻辑属于 DataContext 层（类型转换 + 显示语义），不在 Model 也不在 View。
        /// </summary>
        private static string FormatGold(long gold)
        {
            if (gold < 0) return "破产";
            if (gold >= 1_000_000_000) return $"{gold / 1_000_000_000.0:F1}B";
            if (gold >= 1_000_000) return $"{gold / 1_000_000.0:F1}M";
            if (gold >= 10_000) return $"{gold / 1_000.0:F1}K";
            return $"金币: {gold}";
        }
    }
}
