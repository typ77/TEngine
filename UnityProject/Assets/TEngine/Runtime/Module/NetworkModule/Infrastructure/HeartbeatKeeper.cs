using System;

namespace TEngine
{
    /// <summary>
    /// 心跳维护（由 <c>NetworkConnection.Update</c> 统一驱动，单线程同帧，无定时器回调竞争）。
    /// <para><b>状态机</b>：</para>
    /// <list type="bullet">
    /// <item>空闲累计达 <c>intervalSec</c> → 触发 <see cref="OnSendHeartbeat"/> 并进入“等待回复”状态（重置计时）；</item>
    /// <item>等待回复期间累计时长，超过 <c>timeoutSec</c> 仍未收到回复 → 触发 <see cref="OnTimeout"/> 并 <see cref="Reset"/>；</item>
    /// <item>收到回复（<see cref="OnHeartbeatResponse"/>）→ 重置计时，回到空闲。</item>
    /// </list>
    /// </summary>
    internal class HeartbeatKeeper
    {
        private readonly int _intervalSec;
        private readonly int _timeoutSec;
        private float _elapsed;
        private bool _waitingReply;

        /// <summary>到达心跳间隔时触发（调用方发送心跳包）。</summary>
        public event Action OnSendHeartbeat;

        /// <summary>心跳超时（发心跳后 timeoutSec 内无回复）时触发。</summary>
        public event Action OnTimeout;

        public HeartbeatKeeper(int intervalSec, int timeoutSec)
        {
            _intervalSec = intervalSec;
            _timeoutSec = timeoutSec;
        }

        /// <summary>重置计时与等待状态（连接建立 / 超时后 / 手动取消时调用）。</summary>
        public void Reset()
        {
            _elapsed = 0;
            _waitingReply = false;
        }

        /// <summary>收到心跳回复时调用，重置计时并回到空闲。</summary>
        public void OnHeartbeatResponse()
        {
            _waitingReply = false;
            _elapsed = 0;
        }

        public void Update(float elapseSeconds)
        {
            _elapsed += elapseSeconds;

            if (_waitingReply)
            {
                // 已发心跳等待回复：累计等待时长，超过 timeoutSec 仍无回复 → 判定连接超时
                if (_elapsed >= _timeoutSec)
                {
                    OnTimeout?.Invoke();
                    Reset();
                }
                return;
            }

            // 空闲：到 interval 触发一次心跳
            if (_elapsed >= _intervalSec)
            {
                _elapsed = 0;
                _waitingReply = true;
                OnSendHeartbeat?.Invoke();
            }
        }
    }
}
