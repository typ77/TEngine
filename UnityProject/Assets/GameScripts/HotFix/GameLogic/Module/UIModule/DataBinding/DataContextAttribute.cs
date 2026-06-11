using System;

namespace GameLogic.DataBinding
{
    /// <summary>
    /// 标记 View 类对应的 DataContext 类型。
    /// DataContextFactory 通过此 Attribute 自动关联 View 和 DataContext。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DataContextAttribute : Attribute
    {
        /// <summary>
        /// DataContext 类型。
        /// </summary>
        public Type DataContextType { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="dataContextType">DataContext 子类型</param>
        /// <exception cref="ArgumentNullException">dataContextType 为 null</exception>
        /// <exception cref="ArgumentException">类型不是 DataContext 子类</exception>
        public DataContextAttribute(Type dataContextType)
        {
            if (dataContextType == null)
                throw new ArgumentNullException(nameof(dataContextType));
            if (!typeof(DataContext).IsAssignableFrom(dataContextType))
                throw new ArgumentException(
                    $"Type must derive from DataContext, got {dataContextType.FullName}");
            DataContextType = dataContextType;
        }
    }
}
