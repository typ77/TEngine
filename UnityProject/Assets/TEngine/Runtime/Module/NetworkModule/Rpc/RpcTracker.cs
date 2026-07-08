using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// RPC 终结类型。决定 <see cref="RpcTracker.Complete"/> 回填的诊断计数。
    /// </summary>
    internal enum RpcOutcome
    {
        /// <summary>正常收到响应完成（计入 RpcsCompleted）。</summary>
        Completed,

        /// <summary>反序列化失败等异常终结（计入 RpcsFailed）。</summary>
        Failed,

        /// <summary>等待响应超时（计入 RpcsFailed）。</summary>
        Timeout,

        /// <summary>断线中止或拦截器拒绝（计入 RpcsFailed）。</summary>
        Aborted,
    }

    /// <summary>
    /// RPC 追踪器（内部类型）。管理单条连接上所有进行中的 RPC：
    /// 分配 RequestId、记录 pending、超时检测、断线恢复筛选。
    /// </summary>
    internal class RpcTracker
    {
        private readonly string _connectionName;
        private readonly NetworkDiagnostics _diagnostics;
        private uint _nextId;
        private readonly Dictionary<uint, PendingRpc> _pending = new Dictionary<uint, PendingRpc>();

        /// <summary>当前进行中的 RPC 数。</summary>
        public int PendingCount => _pending.Count;

        public RpcTracker(string connectionName, NetworkDiagnostics diagnostics)
        {
            _connectionName = connectionName;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        /// <summary>分配连接级唯一、递增的 RequestId（从 1 开始）。</summary>
        public uint AllocateId()
        {
            return ++_nextId;
        }

        /// <summary>登记一个 pending RPC。</summary>
        public void Add(uint requestId, ushort msgId, byte[] payload, RpcPolicy policy,
            UniTaskCompletionSource<object> tcs, Type responseType, int timeoutMs)
        {
            _pending[requestId] = new PendingRpc
            {
                RequestId = requestId,
                MsgId = msgId,
                OriginalPayload = payload,
                Policy = policy,
                Tcs = tcs,
                ResponseType = responseType,
                StartTimestamp = Stopwatch.GetTimestamp(),
                TimeoutMs = timeoutMs,
                Sent = false,
            };
        }

        /// <summary>查询 pending RPC。</summary>
        public bool TryGet(uint requestId, out PendingRpc rpc)
        {
            return _pending.TryGetValue(requestId, out rpc);
        }

        /// <summary>移除 pending RPC（正常完成或主动取消）。</summary>
        public void Remove(uint requestId)
        {
            _pending.Remove(requestId);
        }

        /// <summary>标记请求已发出（struct 值语义，需写回字典）。</summary>
        public void MarkSent(uint requestId)
        {
            if (_pending.TryGetValue(requestId, out var rpc))
            {
                rpc.Sent = true;
                _pending[requestId] = rpc;
            }
        }

        /// <summary>获取全部 pending RPC（快照副本）。</summary>
        public List<PendingRpc> GetAllPending()
        {
            var list = new List<PendingRpc>(_pending.Count);
            foreach (var kvp in _pending) list.Add(kvp.Value);
            return list;
        }

        /// <summary>获取需要断线恢复的 RPC（策略 SafeRecover 且已发出）。</summary>
        public List<PendingRpc> GetNeedsRecovery()
        {
            var list = new List<PendingRpc>();
            foreach (var kvp in _pending)
            {
                if (kvp.Value.Policy == RpcPolicy.SafeRecover && kvp.Value.Sent)
                    list.Add(kvp.Value);
            }
            return list;
        }

        /// <summary>
        /// 检查所有 pending RPC 的超时，超时则触发 <see cref="RpcTimeoutException"/> 并移除。
        /// </summary>
        public void CheckTimeouts()
        {
            long now = Stopwatch.GetTimestamp();
            var timedOut = new List<uint>();

            foreach (var kvp in _pending)
            {
                var elapsed = (float)(now - kvp.Value.StartTimestamp) / Stopwatch.Frequency * 1000;
                if (elapsed >= kvp.Value.TimeoutMs)
                    timedOut.Add(kvp.Key);
            }

            // 超时统一走 Complete(outcome=Timeout)：自动计入 RpcsFailed，杜绝漏计数（修 M1）。
            foreach (var id in timedOut)
            {
                Complete(id, RpcOutcome.Timeout, rpc => rpc.Tcs.TrySetException(new RpcTimeoutException(
                    _connectionName, rpc.MsgId, rpc.RequestId, rpc.TimeoutMs)));
            }
        }

        /// <summary>
        /// RPC 终结单一出口。所有让 pending 结束的路径（正常完成 / 反序列化失败 / 超时 / 断线 Abort /
        /// 拦截器拒绝）MUST 经此：统一「执行 finalize（完成 TCS）→ 移除 → 按 outcome 回填诊断计数」，
        /// 从结构上消灭「pending 已移除但诊断计数未更新」的泄漏（修 M1）。
        /// </summary>
        /// <param name="requestId">待终结的 RequestId；不存在则无操作。</param>
        /// <param name="outcome">终结类型，决定回填 <see cref="NetworkDiagnostics.RecordRpcCompleted"/> 还是 <see cref="NetworkDiagnostics.RecordRpcFailed"/>。</param>
        /// <param name="finalize">完成 TCS 的动作（TrySetResult/TrySetException）；拦截器拒绝等无需完成 TCS 的路径传 null。</param>
        internal void Complete(uint requestId, RpcOutcome outcome, Action<PendingRpc> finalize)
        {
            if (!_pending.TryGetValue(requestId, out var rpc))
            {
                return;
            }

            finalize?.Invoke(rpc);
            _pending.Remove(requestId);

            // 单一出口回填诊断计数：Completed → completed；Failed/Timeout/Aborted → failed
            if (outcome == RpcOutcome.Completed)
            {
                _diagnostics.RecordRpcCompleted();
            }
            else
            {
                _diagnostics.RecordRpcFailed();
            }
        }
    }
}
