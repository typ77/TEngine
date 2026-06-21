using System;

namespace TEngine
{
    /// <summary>
    /// 重连策略。指数退避：每次重连间隔 = baseInterval × backoff^attempts，
    /// 上限 maxIntervalMs；超过 maxAttempts 则不再重试。
    /// </summary>
    internal class ReconnectPolicy
    {
        private readonly int _intervalMs;
        private readonly int _maxAttempts;
        private readonly float _backoff;
        private readonly int _maxIntervalMs;

        /// <summary>已尝试次数。</summary>
        public int Attempts { get; private set; }

        /// <summary>是否还能重试。</summary>
        public bool CanRetry => Attempts < _maxAttempts;

        public ReconnectPolicy(int intervalMs, int maxAttempts, float backoff, int maxIntervalMs)
        {
            _intervalMs = intervalMs;
            _maxAttempts = maxAttempts;
            _backoff = backoff;
            _maxIntervalMs = maxIntervalMs;
        }

        /// <summary>计算下一次重连等待时长（毫秒）。</summary>
        public int GetNextIntervalMs()
        {
            double interval = _intervalMs * Math.Pow(_backoff, Attempts);
            return (int)Math.Min(interval, _maxIntervalMs);
        }

        /// <summary>记录一次重连尝试。</summary>
        public void RecordAttempt()
        {
            Attempts++;
        }

        /// <summary>重置计数（连接成功后调用）。</summary>
        public void Reset()
        {
            Attempts = 0;
        }
    }
}
