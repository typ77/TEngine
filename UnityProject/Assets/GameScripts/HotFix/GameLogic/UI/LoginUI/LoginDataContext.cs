using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>
    /// LoginUI 的数据上下文（MVE 演示）。
    /// 演示 Model → DataContext → View 完整数据绑定流程。
    ///
    /// Model 层：AccountName（原始账号名）、LoginCount（登录计数）
    /// View 层：DisplayAccount（格式化账号文本）、DisplayStatus（登录状态文本）
    /// </summary>
    [DataContext(typeof(LoginUI))]
    public class LoginDataContext : DataContext<LoginUI>
    {
        /// <summary>Model：账号名。</summary>
        public readonly BindableProperty<string> AccountName = new BindableProperty<string>("Player_001");

        /// <summary>Model：登录次数。</summary>
        public readonly BindableProperty<int> LoginCount = new BindableProperty<int>(0);

        /// <summary>View 输出：格式化的账号文本。</summary>
        public readonly BindableProperty<string> DisplayAccount = new BindableProperty<string>("");

        /// <summary>View 输出：登录状态文本。</summary>
        public readonly BindableProperty<string> DisplayStatus = new BindableProperty<string>("");

        private static readonly string[] RandomNames =
        {
            "Hero_001", "Knight_X", "Mage_99",
            "Rogue_7", "Paladin_42", "Archer_Z",
            "Bard_88", "Cleric_3"
        };

        public LoginDataContext()
        {
            // Model → View 映射：AccountName → "当前账号: {name}"
            MapProperty(AccountName, DisplayAccount, FormatAccount);

            // Model → View 映射：LoginCount → 状态文本
            MapProperty(LoginCount, DisplayStatus, FormatStatus);
        }

        /// <summary>
        /// 随机切换账号名并增加登录计数（模拟登录操作）。
        /// </summary>
        public void RandomLogin()
        {
            AccountName.Value = RandomNames[UnityEngine.Random.Range(0, RandomNames.Length)];
            LoginCount.Value++;
        }

        private static string FormatAccount(string name) => $"当前账号: {name}";

        private static string FormatStatus(int count) => count > 0 ? $"已登录 {count} 次" : "未登录";
    }
}
