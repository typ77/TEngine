using System;
using System.Collections.Generic;
using System.Reflection;

namespace TEngine
{
    /// <summary>
    /// Codec 注册表：按 <see cref="CodecType"/> 解析 <see cref="ICodec"/> 实例。
    /// 替代原计划的 CodecFactory —— 具体协议实现下沉到可选扩展程序集，
    /// 通过 <see cref="CodecProviderAttribute"/> 在运行时反射自注册，
    /// 使框架核心（TEngine.Runtime）零三方序列化依赖。
    /// </summary>
    /// <remarks>
    /// codec 实例为单例：首次 <see cref="Get"/> 时由工厂创建并缓存，
    /// 以保留 <see cref="ICodec.RegisterMessage{T}"/> 注册的类型映射状态。
    /// </remarks>
    public static class CodecRegistry
    {
        private static readonly Dictionary<CodecType, Func<ICodec>> _factories = new Dictionary<CodecType, Func<ICodec>>();
        private static readonly Dictionary<CodecType, ICodec> _instances = new Dictionary<CodecType, ICodec>();
        private static bool _initialized;
        private static readonly object _lock = new object();

        /// <summary>手动注册一个 codec 工厂。</summary>
        public static void Register(CodecType type, Func<ICodec> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            lock (_lock)
            {
                _factories[type] = factory;
                _instances.Remove(type); // 重新注册时丢弃旧单例，下次 Get 重建
            }
        }

        /// <summary>手动注册一个 codec 实现类型（须有无参构造）。</summary>
        public static void Register<T>(CodecType type) where T : ICodec, new()
        {
            Register(type, () => new T());
        }

        /// <summary>
        /// 按 <see cref="CodecType"/> 获取 codec 实例（单例）。
        /// 未注册时抛 <see cref="InvalidOperationException"/>（含排查提示）。
        /// </summary>
        public static ICodec Get(CodecType type)
        {
            EnsureInitialized();
            lock (_lock)
            {
                if (_instances.TryGetValue(type, out var cached))
                {
                    return cached;
                }
                if (_factories.TryGetValue(type, out var factory))
                {
                    ICodec instance = factory();
                    _instances[type] = instance;
                    return instance;
                }
            }
            throw new InvalidOperationException(
                $"未注册 codec {type}。请确认：(1) 已安装对应协议库 dll；" +
                $"(2) 已定义编译符号（MessagePack=TENGINE_CODEC_MESSAGEPACK，Protobuf=TENGINE_CODEC_PROTOBUF）；" +
                $"(3) 业务程序集已 references 对应扩展；或运行期调用 INetworkModule.RegisterCodec 手动注册。");
        }

        /// <summary>
        /// 扫描所有已加载程序集，发现标注 <see cref="CodecProviderAttribute"/> 的类型并注册。
        /// 容错 <see cref="ReflectionTypeLoadException"/>：部分类型加载失败时跳过坏类型，继续扫描其余。
        /// 首次 <see cref="Get"/> 时由 <see cref="EnsureInitialized"/> 自动触发。
        /// </summary>
        internal static void Scan()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    var attr = (CodecProviderAttribute)Attribute.GetCustomAttribute(type, typeof(CodecProviderAttribute));
                    if (attr == null)
                    {
                        continue;
                    }

                    Type implType = attr.CodecImplementationType;
                    if (implType == null || !typeof(ICodec).IsAssignableFrom(implType))
                    {
                        continue;
                    }

                    Register(attr.CodecType, () => (ICodec)Activator.CreateInstance(implType));
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }
            lock (_lock)
            {
                if (_initialized)
                {
                    return;
                }
                Scan();
                _initialized = true;
            }
        }

        /// <summary>仅供单元测试重置注册表状态，保证用例间隔离。</summary>
        internal static void ResetForTests()
        {
            lock (_lock)
            {
                _factories.Clear();
                _instances.Clear();
                _initialized = false;
            }
        }
    }
}