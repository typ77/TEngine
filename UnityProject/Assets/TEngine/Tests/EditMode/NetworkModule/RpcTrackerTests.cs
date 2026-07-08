using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class RpcTrackerTests
    {
        private RpcTracker _tracker;
        private NetworkDiagnostics _diag;

        [SetUp]
        public void SetUp()
        {
            _diag = new NetworkDiagnostics();
            _tracker = new RpcTracker("test", _diag);
        }

        [Test]
        public void Allocate_ReturnsIncrementingIds()
        {
            uint id1 = _tracker.AllocateId();
            uint id2 = _tracker.AllocateId();
            Assert.AreEqual(1u, id1);
            Assert.AreEqual(2u, id2);
        }

        [Test]
        public void TryGet_Existing_ReturnsTrue()
        {
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                tcs, typeof(object), 10000);
            Assert.IsTrue(_tracker.TryGet(1, out _));
        }

        [Test]
        public void TryGet_NonExisting_ReturnsFalse()
        {
            Assert.IsFalse(_tracker.TryGet(999, out _));
        }

        [Test]
        public void Remove_Existing()
        {
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                tcs, typeof(object), 10000);
            _tracker.Remove(1);
            Assert.IsFalse(_tracker.TryGet(1, out _));
        }

        [Test]
        public void PendingCount_TracksCorrectly()
        {
            Assert.AreEqual(0, _tracker.PendingCount);
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                tcs, typeof(object), 10000);
            Assert.AreEqual(1, _tracker.PendingCount);
        }

        // 超时的 RPC 经 Complete(Timeout) 计入 RpcsFailed，且从 tracker 移除（修 M1）。
        [Test]
        public void CheckTimeouts_TimedOutRpc_CountedAsFailed()
        {
            var tcs = new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>();
            // timeoutMs=0：登记后立即可被 CheckTimeouts 判超时
            _tracker.Add(1, 1001, new byte[] { 0x01 }, RpcPolicy.FailOnDisconnect,
                tcs, typeof(object), 0);
            _diag.RecordRpcStarted(); // 与 SendAsync 成对（生产路径在 NetworkConnection 内成对调用）

            _tracker.CheckTimeouts();

            Assert.IsFalse(_tracker.TryGet(1, out _), "超时 RPC 应已从 tracker 移除");
            Assert.AreEqual(0, _diag.RpcsPending, "RpcsPending 应递减归零");
            Assert.AreEqual(1, _diag.RpcsFailed, "超时应计入 RpcsFailed");
        }

        // 多条终结路径（完成/超时/中止）计数守恒：pending 递减数 == completed + failed 递增数（修 M1）。
        [Test]
        public void Complete_MultipleOutcomes_CountsConserved()
        {
            // 登记 3 个 pending，各配一次 RecordRpcStarted（与 SendAsync 成对）
            for (uint id = 1; id <= 3; id++)
            {
                _tracker.Add(id, 1001, new byte[] { 0x01 }, RpcPolicy.SafeRecover,
                    new Cysharp.Threading.Tasks.UniTaskCompletionSource<object>(),
                    typeof(object), 10000);
                _diag.RecordRpcStarted();
            }
            Assert.AreEqual(3, _diag.RpcsPending);

            _tracker.Complete(1, RpcOutcome.Completed, null);
            _tracker.Complete(2, RpcOutcome.Timeout, null);
            _tracker.Complete(3, RpcOutcome.Aborted, null);

            Assert.AreEqual(0, _diag.RpcsPending, "RpcsPending 应全部递减");
            Assert.AreEqual(1, _diag.RpcsCompleted, "仅 Completed 路径计入 completed");
            Assert.AreEqual(2, _diag.RpcsFailed, "Timeout + Aborted 计入 failed");
            // 守恒：pending 递减(3) == completed(1) + failed(2)
            Assert.AreEqual(3, _diag.RpcsCompleted + _diag.RpcsFailed);
        }

        // 未登记的 RequestId 调 Complete 安全无操作、不污染计数。
        [Test]
        public void Complete_UnknownRequestId_NoOp()
        {
            _tracker.Complete(999, RpcOutcome.Completed, r => r.Tcs.TrySetResult(null));

            Assert.AreEqual(0, _diag.RpcsCompleted, "未知 RequestId 不应回填计数");
            Assert.AreEqual(0, _diag.RpcsFailed);
        }
    }
}
