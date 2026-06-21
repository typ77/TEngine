using System;

namespace TEngine
{
    /// <summary>
    /// Ping 监测（内部骨架）。按 <see cref="NetworkDiagnostics.PingIntervalMs"/> 周期触发发送 Ping，
    /// 收到响应时回填 RTT 到 <see cref="NetworkDiagnostics"/>。T17 接入真实传输后驱动。
    /// </summary>
    internal class PingMonitor
    {
        private readonly NetworkDiagnostics _diagnostics;
        private readonly Action _sendPing;
        private float _elapsed;

        public PingMonitor(NetworkDiagnostics diagnostics, Action sendPing)
        {
            _diagnostics = diagnostics;
            _sendPing = sendPing;
        }

        public void Update(float elapseSeconds)
        {
            if (!_diagnostics.PingEnabled) return;
            _elapsed += elapseSeconds;
            float interval = _diagnostics.PingIntervalMs / 1000f;
            if (_elapsed >= interval)
            {
                _elapsed = 0;
                _sendPing?.Invoke();
            }
        }

        public void OnPingResponse(float rttMs)
        {
            _diagnostics.UpdatePing(rttMs);
        }
    }
}
