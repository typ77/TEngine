// Assets/TEngine/Runtime/Module/TimerModule/ITimerModule.cs
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TEngine
{
    // 旧 API 回调委托（保留向后兼容）
    public delegate void TimerHandler(params object[] args);

    public interface ITimerModule
    {
        // ——— 新 API ———

        TimerHandle Delay(float delay, Action callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Delay(float delay, Action<TimerTickArgs> callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Repeat(float interval, Action callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Repeat(float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Schedule(float delay, float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle Countdown(int count, float interval,
            Action<TimerTickArgs> onTick,
            Action onComplete = null,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default);

        TimerHandle NextFrame(Action callback);

        TimerHandle WaitFrames(int frames, Action callback);

        void Configure(int maxCatchUpSteps = 5, int initialPoolCapacity = 128);

        void Compact();

        // ——— 诊断属性 ———

        int ActiveTimerCount { get; }
        int PoolCapacity { get; }
        int PoolUsed { get; }
        int ZombieCount { get; }

        void GetDiagnostics(List<TimerDiagnosticInfo> output);

        // ——— 旧 API（Obsolete，保留向后兼容）———

        [Obsolete("请使用 Delay / Repeat / Schedule")]
        int AddTimer(TimerHandler callback, float time, bool isLoop = false,
            bool isUnscaled = false, params object[] args);

        [Obsolete("请使用 TimerHandle.Cancel()")]
        void RemoveTimer(int timerId);

        [Obsolete("请使用 TimerHandle.Cancel()")]
        void RemoveAllTimer();

        [Obsolete("请使用 TimerHandle.Pause()")]
        void Stop(int timerId);

        [Obsolete("请使用 TimerHandle.Resume()")]
        void Resume(int timerId);

        [Obsolete("请使用 TimerHandle.IsValid")]
        bool IsRunning(int timerId);

        [Obsolete("请使用 TimerHandle.Remaining")]
        float GetLeftTime(int timerId);

        [Obsolete("请使用 Schedule 替代")]
        void Restart(int timerId);

        [Obsolete("请使用 Schedule 替代")]
        void ResetTimer(int timerId, TimerHandler callback, float time,
            bool isLoop = false, bool isUnscaled = false);

        [Obsolete("请使用 Schedule 替代")]
        void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled);

        [Obsolete("请使用 UniTask.Delay 或 UniTask.DelayFrame")]
        System.Timers.Timer AddSystemTimer(
            Action<object, System.Timers.ElapsedEventArgs> callBack);
    }
}
