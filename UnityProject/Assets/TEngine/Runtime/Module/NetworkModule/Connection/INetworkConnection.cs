using System;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 单条网络连接的接口契约。独立持有 Transport / Codec / Dispatch / Interceptor / Rpc 等
    /// 子系统实例，对外提供连接控制、收发、订阅、RPC 与诊断能力。
    /// </summary>
    public interface INetworkConnection
    {
        /// <summary>连接名称（模块内的唯一标识）。</summary>
        string Name { get; }

        /// <summary>当前连接状态。</summary>
        ConnectionState State { get; }

        // ---- 连接控制 ----

        /// <summary>建立连接。</summary>
        UniTask ConnectAsync();

        /// <summary>主动断开（可被 <c>ReconnectAsync</c> 恢复）。</summary>
        void Disconnect();

        /// <summary>重连。</summary>
        UniTask ReconnectAsync();

        // ---- 收发 ----

        /// <summary>发送单向消息（事件消息）。</summary>
        void Send<T>(ushort msgId, T message) where T : IMessage;

        /// <summary>发送 RPC 请求并等待响应（RPC 主体已实现：RequestId 编排、响应匹配、超时、断线处置）。</summary>
        /// <remarks>
        /// 仅在 <see cref="ConnectionState.Connected"/> 状态可调用，否则抛 <see cref="InvalidOperationException"/>。
        /// <para><see cref="RpcPolicy.SafeRecover"/> 当前版本等价于 <see cref="RpcPolicy.FailOnDisconnect"/>：
        /// 断线（含重连成功后）以 <see cref="RpcAbortException"/> 中止 pending RPC，而非继续等待原响应。
        /// 真正的断线恢复协商协议待后续版本。</para>
        /// </remarks>
        UniTask<TResp> SendAsync<TReq, TResp>(
            ushort msgId, TReq message,
            RpcPolicy policy = RpcPolicy.SafeRecover,
            int timeoutMs = 10000)
            where TReq : IMessage where TResp : IMessage;

        // ---- 订阅 ----

        /// <summary>订阅指定 msgId 的消息。</summary>
        void On<T>(ushort msgId, Action<T> handler) where T : IMessage;

        /// <summary>取消指定 msgId 的所有订阅。</summary>
        void Off(ushort msgId);

        /// <summary>取消本连接全部订阅。</summary>
        void OffAll();

        // ---- 拦截器（连接级） ----

        void AddInterceptor(IMessageInterceptor interceptor);
        void RemoveInterceptor(string name);

        // ---- 诊断 ----

        /// <summary>本连接的实时诊断数据。</summary>
        NetworkDiagnostics Diagnostics { get; }

        // ---- 连接级事件 ----

        event Action OnConnected;
        event Action OnDisconnected;
        event Action<ConnectionState> OnStateChanged;
        event Action<Exception> OnError;
    }
}
