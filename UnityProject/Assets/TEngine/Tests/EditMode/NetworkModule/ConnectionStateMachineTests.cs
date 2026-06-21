using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class ConnectionStateMachineTests
    {
        private ConnectionStateMachine _sm;

        [SetUp]
        public void SetUp()
        {
            _sm = new ConnectionStateMachine();
        }

        [Test]
        public void InitialState_IsNone()
        {
            Assert.AreEqual(ConnectionState.None, _sm.Current);
        }

        [Test]
        public void Transition_None_ToConnecting()
        {
            _sm.TransitionTo(ConnectionState.Connecting);
            Assert.AreEqual(ConnectionState.Connecting, _sm.Current);
        }

        [Test]
        public void Transition_Connecting_ToConnected()
        {
            _sm.TransitionTo(ConnectionState.Connecting);
            _sm.TransitionTo(ConnectionState.Connected);
            Assert.AreEqual(ConnectionState.Connected, _sm.Current);
        }

        [Test]
        public void Transition_Connected_ToDisconnected()
        {
            _sm.TransitionTo(ConnectionState.Connecting);
            _sm.TransitionTo(ConnectionState.Connected);
            _sm.TransitionTo(ConnectionState.Disconnected);
            Assert.AreEqual(ConnectionState.Disconnected, _sm.Current);
        }

        [Test]
        public void OnStateChanged_FiredOnTransition()
        {
            ConnectionState? changed = null;
            _sm.OnStateChanged += state => changed = state;
            _sm.TransitionTo(ConnectionState.Connecting);
            Assert.AreEqual(ConnectionState.Connecting, changed);
        }

        [Test]
        public void Disconnect_AlwaysSafe()
        {
            // 从 None 断开
            Assert.DoesNotThrow(() => _sm.TransitionTo(ConnectionState.Closed));
            Assert.AreEqual(ConnectionState.Closed, _sm.Current);
        }
    }
}
