// Assets/TEngine/Tests/EditMode/DataBinding/DataBindingTestBase.cs
using System.Reflection;
using NUnit.Framework;

namespace TEngine.Tests
{
    /// <summary>
    /// 数据绑定测试基类。
    /// 提供 Singleton 重置辅助方法。
    /// BatchScheduler Flush 辅助将在 BatchScheduler 实现后添加。
    /// </summary>
    public abstract class DataBindingTestBase
    {
        [SetUp]
        public virtual void SetUp()
        {
        }

        [TearDown]
        public virtual void TearDown()
        {
        }

        /// <summary>
        /// 通过反射重置 Singleton 静态实例，确保测试间隔离。
        /// </summary>
        protected static void ResetSingleton<T>() where T : class
        {
            var field = typeof(T).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, null);
        }
    }
}
