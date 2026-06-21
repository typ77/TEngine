namespace TEngine
{
    /// <summary>
    /// 网络质量评估（内部骨架）。每 30 帧根据 RTT / 丢包率估算 <see cref="NetworkQualityLevel"/>，
    /// 变化时通过 <see cref="NetworkDiagnostics.UpdateQuality"/> 回写并触发事件。
    /// </summary>
    internal class QualityEstimator
    {
        private readonly NetworkDiagnostics _diagnostics;
        private int _frameCount;

        public QualityEstimator(NetworkDiagnostics diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public void MaybeUpdate()
        {
            _frameCount++;
            if (_frameCount < 30) return;
            _frameCount = 0;

            var prev = _diagnostics.Quality;
            var next = Estimate();
            if (next != prev)
            {
                _diagnostics.UpdateQuality(next);
            }
        }

        private NetworkQualityLevel Estimate()
        {
            float rtt = _diagnostics.RTT;
            float loss = _diagnostics.PacketLoss;

            if (rtt < 50 && loss < 0.01f) return NetworkQualityLevel.Excellent;
            if (rtt < 100 && loss < 0.03f) return NetworkQualityLevel.Good;
            if (rtt < 200 && loss < 0.10f) return NetworkQualityLevel.Fair;
            if (rtt < 500 && loss < 0.30f) return NetworkQualityLevel.Poor;
            return NetworkQualityLevel.Bad;
        }
    }
}
