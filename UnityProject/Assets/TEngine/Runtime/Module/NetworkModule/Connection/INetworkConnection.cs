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

        /// <summary>发送 RPC 请求并等待响应。</summary>
        /// <remarks><b>当前为阶段 3 占位、尚未实现。</b>调用将抛 <see cref="NotSupportedException"/>。RPC 主体（RequestId 编排、响应匹配、超时、断线恢复）待阶段 3 实现，届时本方法拟从基础接口拆出为独立 RPC 接口。</remarks>
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
