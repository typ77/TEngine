using System;

namespace TEngine
{
    /// <summary>
    /// 心跳维护。周期性触发发送心跳，累计丢失次数，超阈值判定超时。
    /// </summary>
    /// <remarks>
    /// <b>已知局限（T17 集成时完善）</b>：当前 OnTimeout 判定位于“发送心跳”分支内，
    /// 依赖 <c>_missedCount</c> 在每次发心跳时自增。一旦 <c>_waitingReply=true</c> 且持续无响应，
    /// 后续 Update 不会再进入发送分支，<c>_missedCount</c> 停滞，OnTimeout 无法触发。
    /// T17 NetworkConnection 接入时应改为基于“等待回复的累计时长”判定超时，
    /// 或在等待期间持续计时并重置 _waitingReply。
    /// </remarks>
    internal class HeartbeatKeeper
    {
        private readonly int _intervalSec;
        private readonly int _timeoutSec;
        private float _elapsed;
        private int _missedCount;
        private bool _waitingReply;

        /// <summary>到达心跳间隔时触发（调用方发送心跳包）。</summary>
        public event Action OnSendHeartbeat;

        /// <summary>心跳超时（连续丢失达阈值）时触发。</summary>
        public event Action OnTimeout;

        public HeartbeatKeeper(int intervalSec, int timeoutSec)
        {
            _intervalSec = intervalSec;
            _timeoutSec = timeoutSec;
        }

        /// <summary>重置计时与丢失计数（连接建立 / 收到回复时调用）。</summary>
        public void Reset()
        {
            _elapsed = 0;
            _missedCount = 0;
            _waitingReply = false;
        }

        /// <summary>收到心跳回复时调用。</summary>
        public void OnHeartbeatResponse()
        {
            _missedCount = 0;
            _waitingReply = false;
        }

        public void Update(float elapseSeconds)
        {
            _elapsed += elapseSeconds;

            if (!_waitingReply && _elapsed >= _intervalSec)
            {
                _elapsed = 0;
                _waitingReply = true;
                _missedCount++;
                OnSendHeartbeat?.Invoke();

                if (_missedCount >= _timeoutSec / _intervalSec)
                {
                    OnTimeout?.Invoke();
                }
            }
        }
    }
}
