using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// DataContext 工厂。
    /// 通过 DataContextAttribute 反射查找 View 对应的 DataContext 类型，
    /// 使用 Activator.CreateInstance 创建实例，并缓存工厂委托。
    /// <para>
    /// 缓存在 Domain Reload 时由 Unity 自动清空（静态字段重置）。
    /// 若使用 Enter Play Mode Options（禁用域重载），需在运行时手动调用 <see cref="ResetCache"/>。
    /// </para>
    /// </summary>
    public static class DataContextFactory
    {
        private static readonly Dictionary<Type, Func<DataContext>> _factories = new();

        /// <summary>
        /// 为指定 View 类型创建 DataContext 实例。
        /// View 类需标记 [DataContext(typeof(TContext))] 特性。
        /// </summary>
        /// <param name="viewType">View 类型</param>
        /// <returns>DataContext 实例，无特性时返回 null</returns>
        public static DataContext CreateFor(Type viewType)
        {
            var attr = viewType.GetCustomAttribute<DataContextAttribute>();
            if (attr == null) return null;

            if (!_factories.TryGetValue(attr.DataContextType, out var factory))
            {
                var contextType = attr.DataContextType;
                factory = () => (DataContext)Activator.CreateInstance(contextType);
                _factories[attr.DataContextType] = factory;
            }

            return factory();
        }

        /// <summary>
        /// 清空工厂缓存。
        /// 在 Enter Play Mode Options（禁用域重载）环境下，应在退出 Play Mode 时调用。
        /// 正常 Domain Reload 模式下无需手动调用（静态字段会自动重置）。
        /// </summary>
        public static void ResetCache()
        {
            _factories.Clear();
        }
    }
}
