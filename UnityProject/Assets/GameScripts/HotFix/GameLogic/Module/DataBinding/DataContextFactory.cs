using System;
using System.Collections.Generic;
using System.Reflection;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// DataContext 工厂。
    /// 通过 DataContextAttribute 反射查找 View 对应的 DataContext 类型，
    /// 使用 Activator.CreateInstance 创建实例，并缓存工厂委托。
    /// </summary>
    internal static class DataContextFactory
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
    }
}
