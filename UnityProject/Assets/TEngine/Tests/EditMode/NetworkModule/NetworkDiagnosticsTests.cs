using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class NetworkDiagnosticsTests
    {
        private NetworkDiagnostics _diag;

        [SetUp]
        public void SetUp()
        {
            _diag = new NetworkDiagnostics();
        }

        [Test]
        public void InitialState_AllZero()
        {
            Assert.AreEqual(0, _diag.BytesSent);
            Assert.AreEqual(0, _diag.MessagesSent);
            Assert.AreEqual(0, _diag.RpcsPending);
        }

        [Test]
        public void RecordSend_IncrementsCounters()
        {
            _diag.RecordSend(100);
            _diag.RecordSend(200);
            Assert.AreEqual(300, _diag.BytesSent);
            Assert.AreEqual(2, _diag.MessagesSent);
        }

        [Test]
        public void RecordReceive_IncrementsCounters()
        {
            _diag.RecordReceive(50);
            Assert.AreEqual(50, _diag.BytesReceived);
            Assert.AreEqual(1, _diag.MessagesReceived);
        }

        [Test]
        public void RecordRpcStarted_DecrementsPending()
        {
            _diag.RecordRpcStarted();
            _diag.RecordRpcStarted();
            Assert.AreEqual(2, _diag.RpcsPending);
        }

        [Test]
        public void RecordRpcCompleted_AdjustsCounters()
        {
            _diag.RecordRpcStarted();
            _diag.RecordRpcCompleted();
            Assert.AreEqual(0, _diag.RpcsPending);
            Assert.AreEqual(1, _diag.RpcsCompleted);
        }
    }
}
