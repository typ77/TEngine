// Assets/TEngine/Tests/EditMode/DataBinding/DataBindingTestBase.cs
using System.Reflection;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    /// <summary>
    /// 数据绑定测试基类。
    /// 提供 Singleton 重置辅助方法和 BatchScheduler Flush 辅助。
    /// </summary>
    public abstract class DataBindingTestBase
    {
        [SetUp]
        public virtual void SetUp()
        {
            ResetSingleton<BatchScheduler>();
        }

        [TearDown]
        public virtual void TearDown()
        {
            ResetSingleton<BatchScheduler>();
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

        /// <summary>
        /// 触发 BatchScheduler 的 OnLateUpdate，等价于模拟一帧刷新。
        /// </summary>
        protected static void FlushScheduler()
        {
            BatchScheduler.Instance.OnLateUpdate();
        }
    }
}
