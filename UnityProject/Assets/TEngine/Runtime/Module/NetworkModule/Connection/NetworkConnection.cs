using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 单条网络连接的实现：持有并协调 Transport/Codec/PacketFormatter/SendQueue/Dispatcher/
    /// InterceptorPipeline/HeartbeatKeeper/ReconnectPolicy/ConnectionStateMachine/RpcTracker/
    /// RpcRecovery/PingMonitor/QualityEstimator/NetworkDiagnostics 全部子系统，实现 <see cref="INetworkConnection"/>。
    /// <para><b>线程模型</b>：transport 回调（OnDataReceived/OnConnected/OnDisconnected/OnError）在 I/O 线程仅入队
    /// <c>_eventQueue</c>，主线程 <see cref="Update"/> 中 DrainTransportEvents 统一处理。Dispatcher/Diagnostics/
    /// Heartbeat/StateMachine/SendQueue 等共享子系统被主线程独占访问，无需加锁。接收处理延迟 ≤1 帧。</para>
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

        // 围栏3 测试钩子：置 true 后下一次 Update 直接抛异常（位于围栏2 try 之外），供 NetworkModule 围栏3 隔离测试。
        internal bool ThrowOnNextUpdateForTests;

        // ---- 线程安全：transport 事件队列 ----
        // transport 回调（OnDataReceived/OnConnected/OnDisconnected/OnError）在 I/O 线程触发，
        // 仅入队；主线程 Update 中 DrainTransportEvents 统一处理，使 Dispatcher/Diagnostics/
        // Heartbeat/StateMachine/SendQueue 等共享子系统被主线程独占访问，无需加锁。
        private enum TransportEventType { Data, Connected, Disconnected, Error }

        private struct TransportEvent
        {
            public TransportEventType Type;
            public ReadOnlyMemory<byte> Data;   // 仅 Type == Data 有效
            public Exception Error;             // 仅 Type == Error 有效
        }

        private readonly ConcurrentQueue<TransportEvent> _eventQueue = new ConcurrentQueue<TransportEvent>();

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

            // 订阅 transport 事件：I/O 线程仅入队 _eventQueue，主线程 Update 中 DrainTransportEvents 处理（线程安全）
            _transport.OnDataReceived += EnqueueReceivedData;
            _transport.OnConnected += EnqueueConnected;
            _transport.OnDisconnected += EnqueueDisconnected;
            _transport.OnError += EnqueueError;

            // 状态机事件转发到 INetworkConnection.OnStateChanged
            _stateMachine.OnStateChanged += s => OnStateChanged?.Invoke(s);

            // 订阅心跳事件（阶段2：发心跳包 + 超时判定断线）
            _heartbeat.OnSendHeartbeat += SendHeartbeatPacket;
            _heartbeat.OnTimeout += HandleHeartbeatTimeout;

            // 围栏1 可观测：订阅分发器/拦截器错误回调，转 OnError（带 msgId/拦截器名上下文，保留 inner）
            _dispatcher.OnDispatchError += (msgId, ex, trace) =>
                RaiseError(new NetworkException(_config.Name, $"dispatch msgId={msgId} handler 异常", ex));
            _interceptors.OnInterceptError += (name, msgId, ex) =>
                RaiseError(new NetworkException(_config.Name, $"interceptor '{name}' msgId={msgId} 异常", ex));
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

        /// <summary>
        /// 开启新连接会话：复位全部会话相关状态（接收缓冲写入位置、收发序号、transport 事件队列、诊断连接指标）。
        /// Connect/Reconnect 成功时调用，保证新会话从干净状态开始——这是重连不再协议错乱的根（修 S2）。
        /// </summary>
        private void BeginSession()
        {
            _recvCount = 0;
            _sendSeq = 0;
            _recvSeq = 0;
            _eventQueue.Clear();          // 清断线瞬间入队未 drain 的 transport 事件（防残留带入新会话）
            _diagnostics.ResetConnectionMetrics();
        }

        private void EnterConnected()
        {
            _stateMachine.TransitionTo(ConnectionState.Connected);
            BeginSession();              // 会话复位：清缓冲/序号/事件队列/诊断指标
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
                    BeginSession();              // 会话复位（含清缓冲/事件队列/ResetConnectionMetrics）
                    _sendQueue.Resume();
                    _heartbeat.Reset();
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

        // ---- Transport 事件入队（I/O 线程调用，仅入队不处理）----

        private void EnqueueReceivedData(ReadOnlyMemory<byte> data)
            => _eventQueue.Enqueue(new TransportEvent { Type = TransportEventType.Data, Data = data });

        private void EnqueueConnected()
            => _eventQueue.Enqueue(new TransportEvent { Type = TransportEventType.Connected });

        private void EnqueueDisconnected()
            => _eventQueue.Enqueue(new TransportEvent { Type = TransportEventType.Disconnected });

        private void EnqueueError(Exception ex)
            => _eventQueue.Enqueue(new TransportEvent { Type = TransportEventType.Error, Error = ex });

        /// <summary>
        /// 主线程 Update 调用：drain transport 事件队列，按类型分发处理。
        /// 所有共享子系统在此（及 Update 后续）被主线程独占访问，无需加锁。
        /// </summary>
        private void DrainTransportEvents()
        {
            while (_eventQueue.TryDequeue(out TransportEvent evt))
            {
                switch (evt.Type)
                {
                    case TransportEventType.Data:
                        ProcessReceivedData(evt.Data);
                        break;
                    case TransportEventType.Connected:
                        HandleTransportConnected();
                        break;
                    case TransportEventType.Disconnected:
                        HandleTransportDisconnected();
                        break;
                    case TransportEventType.Error:
                        RaiseError(evt.Error);
                        break;
                }
            }
        }

        // ---- 接收处理（主线程 drain 调用）----

        private void ProcessReceivedData(ReadOnlyMemory<byte> data)
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

                // RPC 响应：靠 RequestId 匹配 pending，完成 await，不走业务分发
                if ((pkt.Flags & PacketFlags.Rpc) != 0)
                {
                    HandleRpcResponse(pkt.RequestId, pkt.MsgId, payload);
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

        /// <summary>
        /// 处理 RPC 响应包：按 RequestId 匹配 pending，用 pending 的 ResponseType 反序列化响应，
        /// 完成 pending 的 TCS（TrySetResult）并移除。未知 RequestId 忽略。
        /// </summary>
        private void HandleRpcResponse(uint requestId, ushort msgId, byte[] payload)
        {
            if (!_rpcTracker.TryGet(requestId, out var rpc))
            {
                return; // 未知 RequestId（已超时/完成/伪造），忽略
            }

            object response;
            try
            {
                response = _codec.Deserialize(rpc.ResponseType, payload, 0, payload.Length);
            }
            catch (Exception ex)
            {
                rpc.Tcs.TrySetException(new SerializationException(_config.Name, msgId, "Receive", ex));
                _rpcTracker.Remove(requestId);
                _diagnostics.RecordRpcFailed();
                return;
            }

            rpc.Tcs.TrySetResult(response);
            _rpcTracker.Remove(requestId);
            _diagnostics.RecordRpcCompleted();
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
            // 可测性钩子：模拟 Update 抛异常（位于围栏2 try 之外），供 NetworkModule 围栏3 隔离测试。
            if (ThrowOnNextUpdateForTests)
            {
                ThrowOnNextUpdateForTests = false;
                throw new InvalidOperationException("test: update throws");
            }

            // 围栏2：Update 全部子系统操作异常收敛为 OnError，不冒泡到 NetworkModule.Update。
            try
            {
                // drain transport 事件队列（I/O 线程入队的事件在此主线程统一处理）
                DrainTransportEvents();

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
                        if (!TrySend(packets[i]))
                            break;   // 单包发送失败：触发断线处置，本轮停止，剩余包留队等重连
                    }
                }

                _dispatcher.Dispatch(elapseSeconds);
                _qualityEstimator.MaybeUpdate();

                // 回填分发统计到诊断
                _diagnostics.Dispatch = _dispatcher.Stats;
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        /// <summary>
        /// 围栏2（Send）：单包发送异常收敛为 RaiseError + 断线处置，绝不冒泡到 Update。
        /// 失败返回 false 使调用方停止本轮批量发送，剩余包留在 SendQueue 等待重连恢复。
        /// </summary>
        private bool TrySend(byte[] packet)
        {
            try
            {
                _transport.Send(packet);
                return true;
            }
            catch (Exception ex)
            {
                RaiseError(new TransportException(_config.Name, "send", ex));
                HandleTransportDisconnected();   // 触发断线/重连，避免继续 Send 已断 socket
                return false;
            }
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
        public async UniTask<TResp> SendAsync<TReq, TResp>(ushort msgId, TReq message,
            RpcPolicy policy = RpcPolicy.SafeRecover, int timeoutMs = 10000)
            where TReq : IMessage where TResp : IMessage
        {
            if (_stateMachine.Current != ConnectionState.Connected)
            {
                throw new InvalidOperationException("SendAsync requires Connected state.");
            }

            uint requestId = _rpcTracker.AllocateId();

            byte[] payload;
            try
            {
                payload = _codec.Serialize(message);
            }
            catch (Exception ex)
            {
                throw new SerializationException(_config.Name, msgId, "SendAsync", ex);
            }

            byte[] packet = _formatter.BuildPacket(msgId, PacketFlags.Rpc, payload);
            _formatter.WriteRequestId(packet, requestId);

            var tcs = new UniTaskCompletionSource<object>();
            _rpcTracker.Add(requestId, msgId, payload, policy, tcs, typeof(TResp), timeoutMs);
            _diagnostics.RecordRpcStarted();

            _sendSeq++;
            MessageTrace trace = MessageTrace.Create(_sendSeq, MessageDirection.Send, packet.Length);
            if (!_interceptors.ProcessSending(msgId, ref packet, in trace))
            {
                _rpcTracker.Remove(requestId);
                _diagnostics.RecordRpcFailed();
                throw new RpcAbortException(_config.Name, msgId, requestId, "Blocked by interceptor");
            }

            _diagnostics.RecordSend(packet.Length);
            _sendQueue.Enqueue(packet);
            _rpcTracker.MarkSent(requestId);

            object response = await tcs.Task;
            return (TResp)response;
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

        /// <summary>供测试读取当前 RPC pending 数（验证登记/完成）。</summary>
        internal int RpcPendingCountForTests => _rpcTracker.PendingCount;
    }
}
