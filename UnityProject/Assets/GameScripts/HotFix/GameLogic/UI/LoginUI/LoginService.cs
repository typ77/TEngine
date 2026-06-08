using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 登录模块业务操作层（MVE — Service 层）。
    ///
    /// 职责：
    /// - 封装登录相关的业务逻辑（模拟登录、登出、金币变动等）
    /// - 操作 Model 层数据，触发响应式更新链
    /// - View 层只调用 Service，不直接操作 Model
    ///
    /// 数据流：
    ///   View.OnLoginClicked()
    ///     → Service.RandomLogin()
    ///       → Model.AccountName.Value = "Hero_001"
    ///         → DataContext MapProperty 自动转换
    ///           → Bind() 回调自动刷新 UI
    ///
    /// 规范：
    /// - Service 不引用任何 UI 类型
    /// - Service 不引用 DataContext
    /// - Service 只读写 Model 数据
    /// </summary>
    public class LoginService : Singleton<LoginService>
    {
        private static readonly string[] RandomNames =
        {
            "Hero_001", "Knight_X", "Mage_99",
            "Rogue_7", "Paladin_42", "Archer_Z",
            "Bard_88", "Cleric_3"
        };

        /// <summary>
        /// 模拟随机登录：随机分配账号名、扣减金币、增加登录计数。
        /// </summary>
        public void RandomLogin()
        {
            var model = Singleton<LoginModel>.Instance;
            model.AccountName.Value = RandomNames[Random.Range(0, RandomNames.Length)];
            model.LoginCount.Value++;
            model.IsLoggedIn.Value = true;

            // 模拟金币随机变动（可能变为负数，触发 DataContext converter 演示）
            model.Gold.Value += Random.Range(-1500, 500);
        }

        /// <summary>
        /// 模拟登出：重置登录状态。
        /// </summary>
        public void Logout()
        {
            var model = Singleton<LoginModel>.Instance;
            model.IsLoggedIn.Value = false;
        }
    }
}
