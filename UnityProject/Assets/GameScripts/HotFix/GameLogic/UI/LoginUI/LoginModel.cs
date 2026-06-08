using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>
    /// 登录模块数据模型（MVE — Model 层）。
    ///
    /// 职责：
    /// - 仅持有响应式数据属性（BindableProperty），不包含任何业务逻辑
    /// - 作为 Singleton 由 SingletonSystem 管理生命周期
    /// - 由 Service 层读写，由 DataContext 层只读映射
    ///
    /// 规范：
    /// - Model 永远不引用 View 或 UI 类型
    /// - Model 永远不包含业务操作方法（由 Service 负责）
    /// - Model 属性通过 BindableProperty 暴露，支持响应式订阅
    /// </summary>
    public class LoginModel : Singleton<LoginModel>
    {
        /// <summary>当前账号名。</summary>
        public readonly BindableProperty<string> AccountName = new BindableProperty<string>("Player_001");

        /// <summary>累计登录次数。</summary>
        public readonly BindableProperty<int> LoginCount = new BindableProperty<int>(0);

        /// <summary>是否已登录。</summary>
        public readonly BindableProperty<bool> IsLoggedIn = new BindableProperty<bool>(false);

        /// <summary>金币数量（可为负数，由 DataContext 层决定显示策略）。</summary>
        public readonly BindableProperty<long> Gold = new BindableProperty<long>(1000);
    }
}
