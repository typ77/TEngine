using System;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// 一个进行中的 RPC 请求的追踪信息（内部类型）。
    /// <para>注：<see cref="Tcs"/> 使用 <c>UniTaskCompletionSource&lt;object&gt;</c>，
    /// 响应在分发层以 object 承载（Codec 反序列化结果），由上层 await 后 cast 到具体 TResp。
    /// 不能用非泛型 <c>UniTaskCompletionSource</c>——它不承载返回值。</para>
    /// </summary>
    internal struct PendingRpc
    {
        /// <summary>请求 Id（连接级唯一，由 <see cref="RpcTracker.AllocateId"/> 分配）。</summary>
        public uint RequestId;

        /// <summary>RPC 消息 Id。</summary>
        public ushort MsgId;

        /// <summary>原始请求负载（断线重发用）。</summary>
        public byte[] OriginalPayload;

        /// <summary>断线处置策略。</summary>
        public RpcPolicy Policy;

        /// <summary>等待响应的 TCS。</summary>
        public UniTaskCompletionSource<object> Tcs;

        /// <summary>期望的响应类型。</summary>
        public Type ResponseType;

        /// <summary>发起时间戳（<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>）。</summary>
        public long StartTimestamp;

        /// <summary>超时阈值（毫秒）。</summary>
        public int TimeoutMs;

        /// <summary>请求是否已发出（用于断线恢复筛选）。</summary>
        public bool Sent;
    }
}
