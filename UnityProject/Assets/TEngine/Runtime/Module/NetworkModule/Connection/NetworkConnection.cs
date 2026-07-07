using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 单条网络连接的实现：持有并协调 Transport/Codec/PacketFormatter/SendQueue/Dispatcher/
    /// InterceptorPipeline/HeartbeatKeeper/ReconnectPolicy/ConnectionStateMachine/RpcTracker/
    /// RpcRecovery/PingMonitor/QualityEstimator/NetworkDiagnostics 全部子系统，实现 <see cref="INetworkConnection"/>。
    /// <para>Transport 事件驱动：Receive 在 <see cref="ITransport.OnDataReceived"/> 回调里处理（非轮询）。</para>
    /// </summary>
    internal sealed class NetworkConnection : INetworkConnection
    {
        // ---- 配置 & 注入依赖 ----
        private readonly ConnectionConfig _config;
        private readonly ITransport _transport;
        private readonly ICodec _codec;
        private readonly IPacketFormatter _formatter;
        private readonly Dictionary<ushort, MessagePriority> _priorityRegistry;

        // ---- 子系统 ----
        private readonly SendQueue _sendQueue;
        private readonly MessageDispatcher _dispatcher;
        private readonly InterceptorPipeline _interceptors;
        private readonly HeartbeatKeeper _heartbeat;
        private readonly ReconnectPolicy _reconnectPolicy;   // 阶段2 接线
        private readonly ConnectionStateMachine _stateMachine;
        private readonly RpcTracker _rpcTracker;
        private readonly RpcRecovery _rpcRecovery;           // 阶段2 接线
        private readonly NetworkDiagnostics _diagnostics;
        private readonly PingMonitor _pingMonitor;
        private readonly QualityEstimator _qualityEstimator;

        // ---- 接收缓冲（粘包/半包累积）----
        private byte[] _recvBuffer;
        private int _recvCount;

        // ---- 收发序号 ----
        private uint _sendSeq;
        private uint _recvSeq;

        // ---- 可测性钩子 ----
        // 退避等待工厂：生产环境用 UniTask.Delay（依赖 Unity PlayerLoop 推进）；
        // EditMode 单元测试因主线程无 PlayerLoop 推进，注入同步实现绕过（见 SetDelayFactoryForTests）。
        private Func<int, UniTask> _delayFactory = ms => UniTask.Delay(ms);

        /// <inheritdoc />
        public event Action OnConnected;
        /// <inheritdoc />
        public event Action OnDisconnected;
        /// <inheritdoc />
        public event Action<ConnectionState> OnStateChanged;
        /// <inheritdoc />
        public event Action<Exception> OnError;

        /// <inheritdoc />
        public string Name => _config.Name;
        /// <inheritdoc />
        public ConnectionState State => _stateMachine.Current;
        /// <inheritdoc />
        public NetworkDiagnostics Diagnostics => _diagnostics;

        /// <summary>
        /// 依赖注入构造。T18 NetworkModule 负责创建 transport/codec 并注入；测试注入 fake。
        /// </summary>
        internal NetworkConnection(
            ConnectionConfig config,
            ITransport transport,
            ICodec codec,
            IPacketFormatter formatter,
            IReadOnlyList<IMessageInterceptor> globalInterceptors,
            IDictionary<ushort, Type> messageTypeRegistry,
            IDictionary<ushort, MessagePriority> priorityRegistry)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            // 防御性拷贝为独立 Dictionary：字段需具体 Dictionary（TryGetValue 可用），且隔离外部修改
            _priorityRegistry = priorityRegistry != null
                ? new Dictionary<ushort, MessagePriority>(priorityRegistry)
                : new Dictionary<ushort, MessagePriority>();

            config.Validate(); // 校验必填项（Name/Transport/Codec/Endpoint）

            // 子系统实例化
            _sendQueue = new SendQueue();
            _dispatcher = new MessageDispatcher(config.MaxDispatchPerFrame, config.MaxDispatchTimeMs);
            _interceptors = new InterceptorPipeline();
            _heartbeat = new HeartbeatKeeper(config.HeartbeatInterval, config.HeartbeatTimeout);
            _reconnectPolicy = new ReconnectPolicy(config.ReconnectInterval, config.MaxReconnectAttempts,
                config.BackoffMultiplier, config.MaxReconnectInterval);
            _stateMachine = new ConnectionStateMachine();
            _rpcTracker = new RpcTracker(config.Name);
            _diagnostics = new NetworkDiagnostics();
            _pingMonitor = new PingMonitor(_diagnostics, SendPing);
            _qualityEstimator = new QualityEstimator(_diagnostics);
            _rpcRecovery = new RpcRecovery(config.Name, SendControlPacket);

            // 全局拦截器（INetworkModule 下发）
            _interceptors.SetGlobalInterceptors(globalInterceptors ?? Array.Empty<IMessageInterceptor>());

            // 注册消息类型到 codec（codec.RegisterMessage<T> 为泛型方法，反射调用）
            if (messageTypeRegistry != null)
            {
                foreach (var kv in messageTypeRegistry)
                {
                    RegisterCodecMessageType(kv.Key, kv.Value);
                }
            }

            // 接收缓冲（至少 1KB，按配置 RecvBufferSize KB 上取整）
            _recvBuffer = new byte[Math.Max(1024, config.RecvBufferSize * 1024)];

            // 订阅 transport 事件（事件驱动 Receive / 状态变化 / 错误）
            _transport.OnDataReceived += HandleReceive;
            _transport.OnConnected += HandleTransportConnected;
            _transport.OnDisconnected += HandleTransportDisconnected;
            _transport.OnError += RaiseError;

            // 状态机事件转发到 INetworkConnection.OnStateChanged
            _stateMachine.OnStateChanged += s => OnStateChanged?.Invoke(s);

            // 订阅心跳事件（阶段2：发心跳包 + 超时判定断线）
            _heartbeat.OnSendHeartbeat += SendHeartbeatPacket;
            _heartbeat.OnTimeout += HandleHeartbeatTimeout;
        }

        /// <summary>反射调用 codec.RegisterMessage&lt;T&gt;(msgId)，注册消息类型映射。</summary>
        private void RegisterCodecMessageType(ushort msgId, Type type)
        {
            var method = typeof(ICodec).GetMethod(nameof(ICodec.RegisterMessage));
            method.MakeGenericMethod(type).Invoke(_codec, new object[] { msgId });
        }

        // ---- 生命周期（阶段1 简化版；断开/重连完整逻辑见阶段2）----

        /// <inheritdoc />
        public async UniTask ConnectAsync()
        {
            var current = _stateMachine.Current;
            if (current != ConnectionState.None
                && current != ConnectionState.Failed
                && current != ConnectionState.Closed)
            {
                throw new InvalidOperationException($"Cannot Connect from state {current}.");
            }

            _stateMachine.TransitionTo(ConnectionState.Connecting);
            try
            {
                await _transport.ConnectAsync(_config.Host, _config.Port);
                // transport.ConnectAsync 完成后通常由 OnConnected 回调切到 Connected；
                // 若 transport 未触发回调（同步完成），这里兜底。
                if (_stateMachine.Current != ConnectionState.Connected)
                {
                    EnterConnected();
                }
            }
            catch (Exception ex)
            {
                _stateMachine.TransitionTo(ConnectionState.Failed);
                RaiseError(ex);
                throw new ConnectionException(_config.Name, "Connect failed");
            }
        }

        private void HandleTransportConnected()
        {
            if (_stateMachine.Current != ConnectionState.Connected)
            {
                EnterConnected();
            }
        }

        private void EnterConnected()
        {
            _stateMachine.TransitionTo(ConnectionState.Connected);
            _sendQueue.Resume();
            _heartbeat.Reset();
            OnConnected?.Invoke();
        }

        private void HandleTransportDisconnected()
        {
            // 防重入：Closed/Disconnected 已处理
            if (_stateMachine.Current == ConnectionState.Closed
                || _stateMachine.Current == ConnectionState.Disconnected)
            {
                return;
            }
            _stateMachine.TransitionTo(ConnectionState.Disconnected);
            _sendQueue.Pause();
            OnDisconnected?.Invoke();

            if (_config.AutoReconnect)
            {
                TriggerAutoReconnect().Forget();
            }
        }

        private void HandleHeartbeatTimeout()
        {
            // 心跳超时：连接被认为已断（仅在已连接时触发）
            if (_stateMachine.Current != ConnectionState.Connected)
            {
                return;
            }
            _stateMachine.TransitionTo(ConnectionState.Disconnected);
            _sendQueue.Pause();
            try { _transport.Close(); } catch (Exception ex) { RaiseError(ex); }
            OnDisconnected?.Invoke();

            if (_config.AutoReconnect)
            {
                TriggerAutoReconnect().Forget();
            }
        }

        private async UniTaskVoid TriggerAutoReconnect()
        {
            try
            {
                await ReconnectAsync();
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            // 幂等：已 Closed 直接返回
            if (_stateMachine.Current == ConnectionState.Closed)
            {
                return;
            }

            // RPC 断线处置：FailOnDisconnect 立即 Abort；SafeRecover 保留（待重连恢复）
            foreach (var rpc in _rpcTracker.GetAllPending())
            {
                if (rpc.Policy == RpcPolicy.FailOnDisconnect)
                {
                    rpc.Tcs.TrySetException(new RpcAbortException(_config.Name, rpc.MsgId, rpc.RequestId, "Disconnected"));
                    _rpcTracker.Remove(rpc.RequestId);
                    _diagnostics.RecordRpcFailed();
                }
            }

            _stateMachine.TransitionTo(ConnectionState.Closed);
            _sendQueue.Clear();
            _heartbeat.Reset();
            try
            {
                _transport.Close();
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
            OnDisconnected?.Invoke();
        }

        /// <inheritdoc />
        public async UniTask ReconnectAsync()
        {
            if (_stateMachine.Current != ConnectionState.Disconnected)
            {
                throw new InvalidOperationException($"Reconnect requires Disconnected state, current={_stateMachine.Current}.");
            }

            _stateMachine.TransitionTo(ConnectionState.Reconnecting);

            while (_reconnectPolicy.CanRetry)
            {
                // 退避等待：指数退避 base × backoff^attempts（上限 maxInterval）。
                // 必须在 RecordAttempt 之前读取，使首次（attempts=0）等待 base interval，之后递增，
                // 避免断线时紧密重连轰炸对端。
                await _delayFactory(_reconnectPolicy.GetNextIntervalMs());
                _reconnectPolicy.RecordAttempt();
                try
                {
                    await _transport.ConnectAsync(_config.Host, _config.Port);
                    // 重连成功
                    _reconnectPolicy.Reset();
                    _stateMachine.TransitionTo(ConnectionState.Connected);
                    _sendQueue.Resume();
                    _heartbeat.Reset();
                    _diagnostics.ResetConnectionMetrics();
                    var toRecover = _rpcTracker.GetNeedsRecovery();
                    if (toRecover.Count > 0)
                    {
                        await _rpcRecovery.RecoverAsync(toRecover);
                    }
                    OnConnected?.Invoke();
                    return;
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                    // 继续重试（下一轮循环顶部按退避等待）
                }
            }

            _stateMachine.TransitionTo(ConnectionState.Failed);
            throw new ReconnectFailedException(_config.Name, _reconnectPolicy.Attempts);
        }

        // ---- 发送：事件消息 ----

        /// <inheritdoc />
        public void Send<T>(ushort msgId, T message) where T : IMessage
        {
            byte[] payload;
            try
            {
                payload = _codec.Serialize(message);
            }
            catch (Exception ex)
            {
                // 序列化失败 → OnError，不抛（事件模式语义）
                RaiseError(new SerializationException(_config.Name, msgId, "Send", ex));
                return;
            }

            _sendSeq++;
            // 普通事件包 flags=0（RPC 包见阶段3，置 PacketFlags.Rpc）
            byte[] packet = _formatter.BuildPacket(msgId, 0, payload);
            MessageTrace trace = MessageTrace.Create(_sendSeq, MessageDirection.Send, packet.Length);

            // 拦截器发送方向处理：false 表示被拒绝，静默丢弃
            if (!_interceptors.ProcessSending(msgId, ref packet, in trace))
            {
                return;
            }

            _diagnostics.RecordSend(packet.Length);
            _sendQueue.Enqueue(packet);
        }

        // ---- 接收（transport 事件驱动）----

        private void HandleReceive(ReadOnlyMemory<byte> data)
        {
            AppendRecv(data);

            int offset = 0;
            while (offset < _recvCount)
            {
                PacketParseResult? parsed = _formatter.TryParse(_recvBuffer, offset, _recvCount - offset, _config.MaxPacketSize, out int consumed);
                if (parsed == null || consumed <= 0)
                {
                    break; // 半包，等待更多数据到达
                }

                PacketParseResult pkt = parsed.Value;

                // 超限保护：声明长度超过 MaxPacketSize 或导致整数溢出 → 字节流不可信，断连。
                // 流对齐已破坏，后续字节不再解析；采用与心跳超时相同的断连处置。
                if (pkt.IsOversized)
                {
                    RaiseError(new TransportException(_config.Name, "receive",
                        new InvalidOperationException(
                            $"收到超限包 msgId={pkt.MsgId}（声明负载超过 MaxPacketSize={_config.MaxPacketSize}），断开连接。")));
                    _stateMachine.TransitionTo(ConnectionState.Disconnected);
                    _sendQueue.Pause();
                    try { _transport.Close(); } catch (Exception ex) { RaiseError(ex); }
                    OnDisconnected?.Invoke();
                    if (_config.AutoReconnect)
                    {
                        TriggerAutoReconnect().Forget();
                    }
                    return;
                }

                offset += consumed;

                _recvSeq++;
                MessageTrace trace = MessageTrace.Create(_recvSeq, MessageDirection.Receive, consumed);

                byte[] payload = pkt.Payload;
                // 拦截器接收方向处理：false 表示被拒绝，丢弃不分发
                if (!_interceptors.ProcessReceiving(pkt.MsgId, ref payload, in trace))
                {
                    continue;
                }

                object message = null;
                try
                {
                    message = _codec.Deserialize(pkt.MsgId, payload, 0, payload.Length);
                }
                catch (Exception ex)
                {
                    RaiseError(new SerializationException(_config.Name, pkt.MsgId, "Receive", ex));
                    continue;
                }

                _diagnostics.RecordReceive(consumed);

                // 心跳响应：重置心跳计时（阶段2 完整接线 OnSendHeartbeat 发心跳）
                if (pkt.MsgId == SystemMessageIds.HeartbeatResponse)
                {
                    _heartbeat.OnHeartbeatResponse();
                }

                // 注意：MessagePriority.High=0，TryGetValue 失败时 out 默认值为 0(High)，
                // 故未注册的 msgId 须显式回退 Normal，避免误得 High 优先级。
                if (!_priorityRegistry.TryGetValue(pkt.MsgId, out MessagePriority priority))
                {
                    priority = MessagePriority.Normal;
                }

                _dispatcher.Enqueue(pkt.MsgId, message, priority, in trace);
            }

            // 压缩缓冲：移除已消费部分，保留剩余半包
            if (offset > 0)
            {
                int remaining = _recvCount - offset;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(_recvBuffer, offset, _recvBuffer, 0, remaining);
                }
                _recvCount = remaining;
            }
        }

        private void AppendRecv(ReadOnlyMemory<byte> data)
        {
            int len = data.Length;
            if (_recvCount + len > _recvBuffer.Length)
            {
                int newSize = Math.Max(_recvBuffer.Length * 2, _recvCount + len);
                byte[] newBuf = new byte[newSize];
                Buffer.BlockCopy(_recvBuffer, 0, newBuf, 0, _recvCount);
                _recvBuffer = newBuf;
            }
            data.Span.CopyTo(new Span<byte>(_recvBuffer, _recvCount, len));
            _recvCount += len;
        }

        // ---- Update 时序（每帧由 NetworkModule 驱动）----

        internal void Update(float elapseSeconds)
        {
            // Transport 事件驱动，无 Poll
            _heartbeat.Update(elapseSeconds);
            if (_diagnostics.PingEnabled)
            {
                _pingMonitor.Update(elapseSeconds);
            }
            _rpcTracker.CheckTimeouts();

            if (_stateMachine.Current == ConnectionState.Connected)
            {
                List<byte[]> packets = _sendQueue.DequeueAll();
                for (int i = 0; i < packets.Count; i++)
                {
                    _transport.Send(packets[i]);
                }
            }

            _dispatcher.Dispatch(elapseSeconds);
            _qualityEstimator.MaybeUpdate();

            // 回填分发统计到诊断
            _diagnostics.Dispatch = _dispatcher.Stats;
        }

        // ---- 订阅 ----

        /// <inheritdoc />
        public void On<T>(ushort msgId, Action<T> handler) where T : IMessage
        {
            _dispatcher.RegisterHandler(msgId, handler);
        }

        /// <inheritdoc />
        public void Off(ushort msgId)
        {
            _dispatcher.UnregisterHandler(msgId);
        }

        /// <inheritdoc />
        public void OffAll()
        {
            _dispatcher.UnregisterAll();
        }

        // ---- 拦截器（连接级）----

        /// <inheritdoc />
        public void AddInterceptor(IMessageInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
        }

        /// <inheritdoc />
        public void RemoveInterceptor(string name)
        {
            _interceptors.Remove(name);
        }

        // ---- RPC（阶段3）----

        /// <inheritdoc />
        public UniTask<TResp> SendAsync<TReq, TResp>(ushort msgId, TReq message,
            RpcPolicy policy = RpcPolicy.SafeRecover, int timeoutMs = 10000)
            where TReq : IMessage where TResp : IMessage
        {
            throw new NotSupportedException("RPC 待阶段 3 实现，当前 SendAsync 为占位。");
        }

        // ---- 控制包发送回调（心跳/Ping/恢复，阶段2 完整接线）----

        private void SendHeartbeatPacket()
        {
            if (_stateMachine.Current != ConnectionState.Connected)
            {
                return;
            }
            byte[] packet = _formatter.BuildPacket(SystemMessageIds.HeartbeatRequest, 0, Array.Empty<byte>());
            try
            {
                _transport.Send(packet);
                _diagnostics.RecordSend(packet.Length);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        private void SendPing()
        {
            // PingMonitor 回调：发 PingRequest。阶段3 实现。
        }

        private void SendControlPacket(ushort msgId, byte[] payload)
        {
            // RpcRecovery 回调：发恢复包。阶段2/3 实现。
        }

        private void RaiseError(Exception ex)
        {
            OnError?.Invoke(ex);
        }

        /// <summary>
        /// 供单元测试注入退避等待实现。生产用默认 <see cref="UniTask.Delay"/>（依赖 PlayerLoop）；
        /// EditMode 测试注入同步实现（如 <c>_ =&gt; UniTask.CompletedTask</c>）绕过 PlayerLoop 依赖，
        /// 使 <see cref="ReconnectAsync"/> 全程同步可在主线程 <c>GetResult</c> 完成。
        /// </summary>
        internal void SetDelayFactoryForTests(Func<int, UniTask> factory)
        {
            _delayFactory = factory ?? (ms => UniTask.Delay(ms));
        }
    }
}
