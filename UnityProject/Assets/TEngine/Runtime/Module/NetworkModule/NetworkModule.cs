using System;
using System.Collections.Generic;

namespace TEngine
{
    /// <summary>
    /// 网络模块顶层。负责多连接生命周期管理、消息/编解码/传输/拦截器的全局注册，
    /// 按帧驱动各连接 <see cref="INetworkConnection"/>，并广播连接级状态/错误事件。
    /// 运行期通过 <c>GameModule.Network</c> 访问。
    /// </summary>
    /// <remarks>
    /// 连接池与 transport 生命周期：transport 由本模块创建并注入 <see cref="NetworkConnection"/>，
    /// 在 <see cref="DestroyConnection"/> 时由本模块负责 <see cref="IDisposable.Dispose"/>。
    /// 消息/拦截器/Formatter 的全局注册仅作用于【之后创建】的连接（构造时注入），
    /// 因此业务层应在创建连接前的初始化期完成注册。
    /// </remarks>
    internal sealed class NetworkModule : Module, INetworkModule, IUpdateModule
    {
        // ---- 连接池（名称 → 条目）----
        private readonly Dictionary<string, ConnectionEntry> _connections = new Dictionary<string, ConnectionEntry>();

        // ---- 全局消息注册表（注入新建连接的 codec 类型映射 / 优先级）----
        private readonly Dictionary<ushort, Type> _messageTypes = new Dictionary<ushort, Type>();
        private readonly Dictionary<ushort, MessagePriority> _priorities = new Dictionary<ushort, MessagePriority>();

        // ---- 全局拦截器（作用于新建连接）----
        private readonly List<IMessageInterceptor> _globalInterceptors = new List<IMessageInterceptor>();

        // ---- 全局 Formatter（默认 DefaultPacketFormatter，可被 SetPacketFormatter 替换）----
        private IPacketFormatter _formatter = new DefaultPacketFormatter();

        // ---- Platform Transport 注册表（按 PlatformId 选用）----
        private readonly Dictionary<string, ITransport> _platformTransports = new Dictionary<string, ITransport>();

        /// <summary>连接池条目：连接 + transport + 本模块订阅的事件委托（销毁时用于取消订阅）。</summary>
        private struct ConnectionEntry
        {
            public NetworkConnection Connection;
            public ITransport Transport;
            public Action<ConnectionState> StateHandler;
            public Action<Exception> ErrorHandler;
        }

        // ================================================================
        // Module 生命周期
        // ================================================================

        /// <inheritdoc />
        public override void OnInit()
        {
            // CodecRegistry.Get 时由 EnsureInitialized 自动 Scan 注册 [CodecProvider]，
            // 此处无需显式调用。
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            // 复制名称列表，避免遍历时 DestroyConnection 修改字典
            List<string> names = new List<string>(_connections.Keys);
            foreach (string name in names)
            {
                DestroyConnection(name);
            }
        }

        /// <inheritdoc />
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            foreach (ConnectionEntry entry in _connections.Values)
            {
                entry.Connection.Update(elapseSeconds);
            }
        }

        // ================================================================
        // 连接管理
        // ================================================================

        /// <inheritdoc />
        public INetworkConnection CreateConnection(ConnectionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            string name = config.Name;
            if (_connections.ContainsKey(name))
            {
                throw new ArgumentException($"连接 '{name}' 已存在", nameof(config));
            }

            // 创建前校验必填项，给出明确错误（先于 transport/codec 创建）
            config.Validate();

            ITransport transport = CreateTransport(config);
            ICodec codec = CodecRegistry.Get(config.Codec);

            NetworkConnection conn = new NetworkConnection(
                config, transport, codec, _formatter,
                _globalInterceptors, _messageTypes, _priorities);

            // 订阅连接级事件 → 广播到模块级事件
            Action<ConnectionState> stateHandler = state => OnConnectionStateChanged?.Invoke(name, state);
            Action<Exception> errorHandler = ex => OnError?.Invoke(name, ex);
            conn.OnStateChanged += stateHandler;
            conn.OnError += errorHandler;

            _connections[name] = new ConnectionEntry
            {
                Connection = conn,
                Transport = transport,
                StateHandler = stateHandler,
                ErrorHandler = errorHandler,
            };

            return conn;
        }

        /// <inheritdoc />
        public INetworkConnection GetConnection(string name)
        {
            return name != null && _connections.TryGetValue(name, out ConnectionEntry entry)
                ? entry.Connection
                : null;
        }

        /// <inheritdoc />
        public INetworkConnection GetConnectionOrDefault(string name)
        {
            if (name != null && _connections.TryGetValue(name, out ConnectionEntry entry))
            {
                return entry.Connection;
            }

            // 不存在则回退到任意一个可用连接（连接池为空返回 null）
            foreach (ConnectionEntry any in _connections.Values)
            {
                return any.Connection;
            }
            return null;
        }

        /// <inheritdoc />
        public void DestroyConnection(string name)
        {
            if (name == null || !_connections.TryGetValue(name, out ConnectionEntry entry))
            {
                return;
            }

            // 取消本模块对连接事件的订阅，避免继续广播已销毁连接
            entry.Connection.OnStateChanged -= entry.StateHandler;
            entry.Connection.OnError -= entry.ErrorHandler;

            // 主动断开（触发 transport 断线回调，连接转 Disconnected）
            entry.Connection.Disconnect();

            // 释放底层 transport（NetworkModule 拥有其生命周期）
            entry.Transport.Dispose();

            _connections.Remove(name);
        }

        /// <inheritdoc />
        public bool HasConnection(string name)
        {
            return name != null && _connections.ContainsKey(name);
        }

        // ================================================================
        // 消息注册（注入新建连接；已存在连接不回灌）
        // ================================================================

        /// <inheritdoc />
        public void RegisterMessage<T>(ushort msgId, MessagePriority priority = MessagePriority.Normal) where T : IMessage
        {
            _messageTypes[msgId] = typeof(T);
            _priorities[msgId] = priority;
        }

        /// <inheritdoc />
        public void RegisterMessages(IDictionary<ushort, (Type messageType, MessagePriority priority)> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            foreach (var kv in entries)
            {
                _messageTypes[kv.Key] = kv.Value.messageType;
                _priorities[kv.Key] = kv.Value.priority;
            }
        }

        /// <inheritdoc />
        public MessagePriority GetMessagePriority(ushort msgId)
        {
            return _priorities.TryGetValue(msgId, out MessagePriority priority) ? priority : MessagePriority.Normal;
        }

        // ================================================================
        // 全局注册
        // ================================================================

        /// <inheritdoc />
        public void RegisterCodec<T>(CodecType type) where T : ICodec, new()
        {
            CodecRegistry.Register<T>(type);
        }

        /// <inheritdoc />
        public void RegisterPlatformTransport(string platformId, ITransport transport)
        {
            if (string.IsNullOrEmpty(platformId))
            {
                throw new ArgumentException("platformId 不能为空", nameof(platformId));
            }
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            _platformTransports[platformId] = transport;
        }

        /// <inheritdoc />
        public void AddInterceptor(IMessageInterceptor interceptor)
        {
            if (interceptor == null)
            {
                throw new ArgumentNullException(nameof(interceptor));
            }

            _globalInterceptors.Add(interceptor);
        }

        /// <inheritdoc />
        public void RemoveInterceptor(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            for (int i = 0; i < _globalInterceptors.Count; i++)
            {
                if (_globalInterceptors[i].Name == name)
                {
                    _globalInterceptors.RemoveAt(i);
                    return;
                }
            }
        }

        /// <inheritdoc />
        public void SetPacketFormatter(IPacketFormatter formatter)
        {
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        // ================================================================
        // Transport 工厂
        // ================================================================

        /// <summary>
        /// 按 <see cref="ConnectionConfig.Transport"/> 创建传输层。
        /// Tcp 由 T9 的 <see cref="TcpTransport"/> 提供（基于 .NET Socket，无外部依赖）；
        /// KCP/WebSocket 待 T10/T11，当前抛 <see cref="NotSupportedException"/>；
        /// Platform 从 <see cref="RegisterPlatformTransport"/> 注册表按 PlatformId 取。
        /// </summary>
        private ITransport CreateTransport(ConnectionConfig config)
        {
            switch (config.Transport)
            {
                case TransportType.Platform:
                    if (!_platformTransports.TryGetValue(config.PlatformId, out ITransport platform))
                    {
                        throw new InvalidOperationException($"Platform transport '{config.PlatformId}' 未注册");
                    }
                    return platform;

                case TransportType.Tcp:
                    // T9 已实现 TcpTransport（基于 System.Net.Sockets.Socket，无外部依赖）。
                    // bufferSize 取接收缓冲（KB→字节），与 TcpTransport 默认接收缓冲语义一致。
                    return new TcpTransport(config.RecvBufferSize * 1024);

                case TransportType.Kcp:
                case TransportType.WebSocket:
                    throw new NotSupportedException(
                        $"{config.Transport} 传输尚未实现。TCP 已可用；KCP 待 T10（借 kcp2k）、WebSocket 待 T11（借 NativeWebSocket），见 docs/adr/network-kcp-strategy.md。");

                default:
                    throw new ArgumentOutOfRangeException(nameof(config), config.Transport, "未知传输类型");
            }
        }

        // ================================================================
        // 全局事件
        // ================================================================

        /// <inheritdoc />
        public event Action<string, ConnectionState> OnConnectionStateChanged;

        /// <inheritdoc />
        public event Action<string, Exception> OnError;
    }
}
