using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace TEngine
{
    /// <summary>
    /// RPC 断线恢复。
    /// <para><b>第一版（框架）</b>：不实现完整的服务端恢复协议（RpcRecoverRequest 0xFFFE /
    /// RpcRecoverResponse 0xFFFF 的协商），直接将所有未确认的 pending RPC 以
    /// <see cref="RpcAbortException"/> 中止，由业务层重新发起。</para>
    /// <para>T17 接入真实传输后，再扩展为：发送恢复请求 → 等待回复 →
    /// supported&amp;found 完成 TCS / supported&amp;!found 补发原始请求 / !supported 全部 Abort。</para>
    /// </summary>
    internal class RpcRecovery
    {
        private readonly string _connectionName;
        private readonly Action<ushort, byte[]> _sendAction;

        public RpcRecovery(string connectionName, Action<ushort, byte[]> sendAction)
        {
            _connectionName = connectionName;
            _sendAction = sendAction;
        }

        public async UniTask RecoverAsync(List<PendingRpc> pendingRpcs)
        {
            // 第一版：直接中止所有未确认 RPC
            foreach (var rpc in pendingRpcs)
            {
                rpc.Tcs.TrySetException(new RpcAbortException(
                    _connectionName, rpc.MsgId, rpc.RequestId,
                    "Server recovery not supported"));
            }

            await UniTask.CompletedTask;
        }
    }
}
