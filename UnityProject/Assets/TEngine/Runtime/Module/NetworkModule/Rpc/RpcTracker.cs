using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// RPC 追踪器（内部类型）。管理单条连接上所有进行中的 RPC：
    /// 分配 RequestId、记录 pending、超时检测、断线恢复筛选。
    /// </summary>
    internal class RpcTracker
    {
        private readonly string _connectionName;
        private uint _nextId;
        private readonly Dictionary<uint, PendingRpc> _pending = new Dictionary<uint, PendingRpc>();

        /// <summary>当前进行中的 RPC 数。</summary>
        public int PendingCount => _pending.Count;

        public RpcTracker(string connectionName)
        {
            _connectionName = connectionName;
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

            foreach (var id in timedOut)
            {
                if (_pending.TryGetValue(id, out var rpc))
                {
                    rpc.Tcs.TrySetException(new RpcTimeoutException(
                        _connectionName, rpc.MsgId, rpc.RequestId, rpc.TimeoutMs));
                    _pending.Remove(id);
                }
            }
        }
    }
}
