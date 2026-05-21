// Assets/TEngine/Runtime/Module/TimerModule/TimerNode.cs
using System;
using System.Threading;
using UnityEngine;

namespace TEngine
{
    internal class TimerNode
    {
        public int Id;
        public int Version;
        public double FireTime;
        public double Interval;
        public double StartTime;
        public TimeMode TimeMode;
        public int TotalTicks;       // -1 = 无限
        public int CompletedTicks;
        public bool IsDeleted;
        public bool IsPaused;
        public double PauseRemaining;

        public Action OnTickNoArg;
        public Action<TimerTickArgs> OnTickWithArg;
        public Action OnComplete;

        public UnityEngine.Object Owner;
        public CancellationToken Token;

        public void Reset()
        {
            FireTime = 0;
            Interval = 0;
            StartTime = 0;
            TotalTicks = 0;
            CompletedTicks = 0;
            IsDeleted = false;
            IsPaused = false;
            PauseRemaining = 0;
            OnTickNoArg = null;
            OnTickWithArg = null;
            OnComplete = null;
            Owner = null;
            Token = default;
        }
    }

    internal struct FrameTimerNode
    {
        public int Id;
        public int Version;
        public int TargetFrame;
        public Action Callback;
        public bool IsDeleted;
    }
}
