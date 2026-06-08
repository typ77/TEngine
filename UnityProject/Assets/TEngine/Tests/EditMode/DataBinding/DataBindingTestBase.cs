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
            // 不重置 BatchScheduler 单例：重置会导致 Instance 重建，
            // 触发 SingletonSystem.CheckInit → DontDestroyOnLoad，
            // 在 EditMode 下抛异常。改为只清理脏状态。
            if (SingletonHasInstance<BatchScheduler>())
            {
                BatchScheduler.Instance.OnLateUpdate(); // 清空待处理脏标记
            }
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (SingletonHasInstance<BatchScheduler>())
            {
                BatchScheduler.Instance.OnLateUpdate(); // 确保测试结束时无残留
            }
        }

        /// <summary>
        /// 通过反射重置 Singleton 静态实例。
        /// 注意：不要对依赖 SingletonSystem 初始化链的类型使用此方法（如 BatchScheduler）。
        /// </summary>
        protected static void ResetSingleton<T>() where T : class
        {
            var field = typeof(T).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, null);
        }

        /// <summary>
        /// 检查 Singleton 是否已创建实例（不触发创建）。
        /// </summary>
        protected static bool SingletonHasInstance<T>() where T : class
        {
            var field = typeof(T).GetField("_instance",
                BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null) != null;
        }

        /// <summary>
        /// 触发 BatchScheduler 的 OnLateUpdate，等价于模拟一帧刷新。
        /// 如果 BatchScheduler 尚未初始化则安全跳过。
        /// </summary>
        protected static void FlushScheduler()
        {
            if (SingletonHasInstance<BatchScheduler>())
            {
                BatchScheduler.Instance.OnLateUpdate();
            }
        }
    }
}
