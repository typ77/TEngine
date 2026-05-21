using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine
{
    public enum TimeMode { Scaled, Unscaled }

    public readonly struct TimerHandle
    {
        public readonly int Id;
        public readonly int Version;

        internal TimerHandle(int id, int version) { Id = id; Version = version; }

        public static readonly TimerHandle Invalid = default;

        public bool IsValid => TimerModule.GlobalInstance?.IsHandleValid(this) ?? false;

        public float Remaining => TimerModule.GlobalInstance?.GetRemaining(this) ?? 0f;

        public float Progress => TimerModule.GlobalInstance?.GetProgress(this) ?? 0f;

        public void Cancel() => TimerModule.GlobalInstance?.Cancel(this);

        public void Pause() => TimerModule.GlobalInstance?.PauseTimer(this);

        public void Resume() => TimerModule.GlobalInstance?.ResumeTimer(this);
    }

    public readonly struct TimerTickArgs
    {
        public readonly int TickIndex;
        public readonly int TotalTicks;
        public readonly int TicksRemaining;
        public readonly float Progress;
        public readonly float ElapsedTime;
        public readonly TimerHandle Handle;

        internal TimerTickArgs(int tickIndex, int totalTicks, float elapsedTime, TimerHandle handle)
        {
            TickIndex = tickIndex;
            TotalTicks = totalTicks;
            TicksRemaining = totalTicks < 0 ? -1 : totalTicks - tickIndex - 1;
            Progress = totalTicks < 0 ? float.NaN : (tickIndex + 1f) / totalTicks;
            ElapsedTime = elapsedTime;
            Handle = handle;
        }
    }

    public struct TimerDiagnosticInfo
    {
        public int Id;
        public float RemainingSeconds;
        public int TickIndex;
        public int TotalTicks;
        public TimeMode TimeMode;
        public bool IsPaused;
        public bool IsDeleted;
    }
}
