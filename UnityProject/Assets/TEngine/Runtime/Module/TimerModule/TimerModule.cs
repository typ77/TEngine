// Assets/TEngine/Runtime/Module/TimerModule/TimerModule.cs
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TEngine
{
    internal sealed class TimerModule : Module, IUpdateModule, ITimerModule
    {
        // ——— 静态全局实例（供 TimerHandle 访问）———
        internal static TimerModule GlobalInstance { get; private set; }

        // ——— 配置 ———
        private int _maxCatchUpSteps = 5;

        // ——— 对象池 ———
        private TimerNodePool _pool;

        // ——— 双堆 ———
        private IndexedMinHeap _scaledHeap;
        private IndexedMinHeap _unscaledHeap;

        // ——— 帧队列 ———
        private readonly List<FrameTimerNode> _frameTimers = new List<FrameTimerNode>();
        private int _currentFrame;

        // ——— 时间累积（不依赖 Time.timeAsDouble，由 Update 驱动，利于单元测试）———
        private double _scaledTime;
        private double _unscaledTime;

        // ——— 旧 API 兼容映射（timerId → nodeId）———
        private readonly Dictionary<int, int> _legacyIdMap = new Dictionary<int, int>();

        // ——— 诊断用：zombie 警告节流（同一帧只警告一次）———
        private int _lastZombieWarnFrame = -1;

        // ——— 诊断属性 ———
        public int ActiveTimerCount
        {
            get
            {
                int count = 0;
                count += CountActive(_scaledHeap);
                count += CountActive(_unscaledHeap);
                return count;
            }
        }

        public int PoolCapacity => _pool.Capacity;
        public int PoolUsed => _pool.UsedCount;

        public int ZombieCount
        {
            get
            {
                int z = 0;
                z += CountZombie(_scaledHeap);
                z += CountZombie(_unscaledHeap);
                return z;
            }
        }

        // ——— 初始化 ———
        public override void OnInit()
        {
            GlobalInstance = this;
            InitPool(128);
        }

        private void InitPool(int capacity)
        {
            _pool = new TimerNodePool(capacity);
            _scaledHeap = new IndexedMinHeap(_pool.Nodes, capacity);
            _unscaledHeap = new IndexedMinHeap(_pool.Nodes, capacity);
            _pool.OnExpand += newCap =>
            {
                _scaledHeap.Expand(newCap);
                _unscaledHeap.Expand(newCap);
            };
        }

        public void Configure(int maxCatchUpSteps = 5, int initialPoolCapacity = 128)
        {
            _maxCatchUpSteps = maxCatchUpSteps;
            _scaledTime = 0;
            _unscaledTime = 0;
            _currentFrame = 0;
            _frameTimers.Clear();
            InitPool(initialPoolCapacity);
        }

        public override void Shutdown()
        {
            GlobalInstance = null;
            _pool = null;
            _legacyIdMap.Clear();
            _frameTimers.Clear();
        }

        // ——— Update 驱动 ———
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            _scaledTime += elapseSeconds;
            _unscaledTime += realElapseSeconds;
            _currentFrame++;

            DrainHeap(_scaledHeap, _scaledTime);
            DrainHeap(_unscaledHeap, _unscaledTime);
            DrainFrameQueue(_currentFrame);
        }

        // ——— 核心调度循环 ———
        private void DrainHeap(IndexedMinHeap heap, double currentTime)
        {
            while (heap.Count > 0)
            {
                int topId = heap.Peek();
                TimerNode top = _pool.Nodes[topId];
                if (top.FireTime > currentTime) break;

                heap.Pop();

                if (top.IsDeleted)
                {
                    _pool.Return(topId);
                    continue;
                }

                if (top.Owner != null && top.Owner.Equals(null)) // Unity null check
                {
                    _pool.Return(topId);
                    continue;
                }

                if (top.Token.CanBeCanceled && top.Token.IsCancellationRequested)
                {
                    _pool.Return(topId);
                    continue;
                }

                // zombie 比例警告（每帧最多一次）
                if (_currentFrame != _lastZombieWarnFrame)
                {
                    int total = heap.Count + 1;
                    if (ZombieCount > total * 0.3f)
                    {
                        _lastZombieWarnFrame = _currentFrame;
                        Log.Warning("[Timer] Zombie 比例超过 30%，建议调用 Compact() 清理");
                    }
                }

                bool isLoop = top.TotalTicks != 1;
                bool isFinite = top.TotalTicks > 0;

                FireNode(top, currentTime);

                if (top.IsDeleted)
                {
                    _pool.Return(topId);
                    continue;
                }

                if (isLoop)
                {
                    if (isFinite && top.CompletedTicks >= top.TotalTicks)
                    {
                        CallOnComplete(top);
                        _pool.Return(topId);
                        continue;
                    }

                    // 追赶逻辑：计算下次触发时间
                    double nextFireTime = top.FireTime + top.Interval;
                    int catchUp = 0;
                    while (nextFireTime <= currentTime)
                    {
                        if (++catchUp > _maxCatchUpSteps)
                        {
                            Log.Warning("[Timer] 追赶步数超限，跳过积压触发");
                            nextFireTime = currentTime + top.Interval;
                            break;
                        }

                        FireNode(top, currentTime);

                        if (top.IsDeleted)
                        {
                            _pool.Return(topId);
                            goto ContinueOuter;
                        }

                        if (isFinite && top.CompletedTicks >= top.TotalTicks)
                        {
                            CallOnComplete(top);
                            _pool.Return(topId);
                            goto ContinueOuter;
                        }

                        nextFireTime += top.Interval;
                    }

                    top.FireTime = nextFireTime;
                    heap.Push(topId);
                }
                else
                {
                    CallOnComplete(top);
                    _pool.Return(topId);
                }

                ContinueOuter:;
            }
        }

        private void FireNode(TimerNode node, double currentTime)
        {
            var handle = new TimerHandle(node.Id, node.Version);
            float elapsed = (float)(currentTime - node.StartTime);
            var args = new TimerTickArgs(node.CompletedTicks, node.TotalTicks, elapsed, handle);

            try
            {
                node.CompletedTicks++;

                if (node.OnTickWithArg != null)
                    node.OnTickWithArg(args);
                else
                    node.OnTickNoArg?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error($"[Timer] 回调异常（Id={node.Id}）: {ex}");
                // 回调异常时标记为删除，避免影响其他定时器
                node.IsDeleted = true;
            }
        }

        private void CallOnComplete(TimerNode node)
        {
            if (node.OnComplete == null) return;
            try { node.OnComplete(); }
            catch (Exception ex) { Log.Error($"[Timer] OnComplete 异常（Id={node.Id}）: {ex}"); }
        }

        // ——— 帧队列 ———
        private void DrainFrameQueue(int currentFrame)
        {
            for (int i = _frameTimers.Count - 1; i >= 0; i--)
            {
                FrameTimerNode fn = _frameTimers[i];
                // 通过 Pool 节点检查取消状态（Cancel() 会标记 IsDeleted=true）
                TimerNode poolNode = _pool.Nodes[fn.Id];
                bool cancelled = poolNode.Version != fn.Version || poolNode.IsDeleted;

                if (cancelled || fn.TargetFrame <= currentFrame)
                {
                    _frameTimers.RemoveAt(i);
                    if (!cancelled)
                    {
                        try { fn.Callback?.Invoke(); }
                        catch (Exception ex) { Log.Error($"[Timer] FrameTimer 异常: {ex}"); }
                    }
                    _pool.Return(fn.Id); // 释放池槽，令 Handle.IsValid → false
                }
            }
        }

        // ——— 公开 API ———
        public TimerHandle Delay(float delay, Action callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal((float)delay, (float)delay, null, callback, 1,
                timeMode, null, owner, cancellationToken);
        }

        public TimerHandle Delay(float delay, Action<TimerTickArgs> callback,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(delay, delay, callback, null, 1,
                timeMode, null, owner, cancellationToken);
        }

        public TimerHandle Repeat(float interval, Action callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(interval, interval, null, callback, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle Repeat(float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(interval, interval, callback, null, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle Schedule(float delay, float interval, Action<TimerTickArgs> callback,
            int count = -1,
            TimeMode timeMode = TimeMode.Scaled,
            Action onComplete = null,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(delay, interval, callback, null, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle Countdown(int count, float interval,
            Action<TimerTickArgs> onTick,
            Action onComplete = null,
            TimeMode timeMode = TimeMode.Scaled,
            Object owner = null,
            CancellationToken cancellationToken = default)
        {
            return ScheduleInternal(interval, interval, onTick, null, count,
                timeMode, onComplete, owner, cancellationToken);
        }

        public TimerHandle NextFrame(Action callback) => WaitFrames(1, callback);

        public TimerHandle WaitFrames(int frames, Action callback)
        {
            int nodeId = _pool.Rent();
            TimerNode node = _pool.Nodes[nodeId];
            var handle = new TimerHandle(nodeId, node.Version);
            _frameTimers.Add(new FrameTimerNode
            {
                Id = nodeId,
                Version = node.Version,
                TargetFrame = _currentFrame + frames,
                Callback = callback,
                IsDeleted = false
            });
            return handle;
        }

        private TimerHandle ScheduleInternal(
            float delay, float interval,
            Action<TimerTickArgs> onTickWithArg,
            Action onTickNoArg,
            int count,
            TimeMode timeMode,
            Action onComplete,
            Object owner,
            CancellationToken token)
        {
            int nodeId = _pool.Rent();
            TimerNode node = _pool.Nodes[nodeId];
            double now = timeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;

            node.FireTime = now + delay;
            node.Interval = interval;
            node.StartTime = now;
            node.TimeMode = timeMode;
            node.TotalTicks = count;
            node.CompletedTicks = 0;
            node.IsDeleted = false;
            node.IsPaused = false;
            node.OnTickWithArg = onTickWithArg;
            node.OnTickNoArg = onTickNoArg;
            node.OnComplete = onComplete;
            node.Owner = owner;
            node.Token = token;

            IndexedMinHeap heap = timeMode == TimeMode.Scaled ? _scaledHeap : _unscaledHeap;
            heap.Push(nodeId);

            return new TimerHandle(nodeId, node.Version);
        }

        // ——— TimerHandle 操作（供 TimerHandle 结构体回调）———
        internal bool IsHandleValid(TimerHandle handle)
        {
            if (_pool == null || handle.Id < 0 || handle.Id >= _pool.Capacity) return false;
            TimerNode node = _pool.Nodes[handle.Id];
            return node.Version == handle.Version && !node.IsDeleted;
        }

        internal float GetRemaining(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return 0f;
            TimerNode node = _pool.Nodes[handle.Id];
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            return (float)Math.Max(0, node.FireTime - now);
        }

        internal float GetProgress(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return 0f;
            TimerNode node = _pool.Nodes[handle.Id];
            if (node.TotalTicks < 0) return float.NaN;
            if (node.TotalTicks == 0) return 0f;
            return node.CompletedTicks / (float)node.TotalTicks;
        }

        internal void Cancel(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return;
            _pool.Nodes[handle.Id].IsDeleted = true;
        }

        internal void PauseTimer(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return;
            TimerNode node = _pool.Nodes[handle.Id];
            if (node.IsPaused) return;
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            node.PauseRemaining = node.FireTime - now;
            node.IsPaused = true;
            IndexedMinHeap heap = node.TimeMode == TimeMode.Scaled ? _scaledHeap : _unscaledHeap;
            heap.ChangeKey(handle.Id, double.MaxValue);
        }

        internal void ResumeTimer(TimerHandle handle)
        {
            if (!IsHandleValid(handle)) return;
            TimerNode node = _pool.Nodes[handle.Id];
            if (!node.IsPaused) return;
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            node.IsPaused = false;
            IndexedMinHeap heap = node.TimeMode == TimeMode.Scaled ? _scaledHeap : _unscaledHeap;
            heap.ChangeKey(handle.Id, now + node.PauseRemaining);
        }

        // ——— Compact ———
        public void Compact()
        {
            RebuildHeap(_scaledHeap, TimeMode.Scaled);
            RebuildHeap(_unscaledHeap, TimeMode.Unscaled);
        }

        private void RebuildHeap(IndexedMinHeap heap, TimeMode mode)
        {
            var active = new List<int>();
            while (heap.Count > 0)
            {
                int id = heap.Pop();
                if (!_pool.Nodes[id].IsDeleted)
                    active.Add(id);
                else
                    _pool.Return(id);
            }
            foreach (int id in active) heap.Push(id);
        }

        // ——— 诊断 ———
        public void GetDiagnostics(List<TimerDiagnosticInfo> output)
        {
            output.Clear();
            FillDiagnostics(_scaledHeap, output);
            FillDiagnostics(_unscaledHeap, output);
        }

        private void FillDiagnostics(IndexedMinHeap heap, List<TimerDiagnosticInfo> output)
        {
            // 遍历堆数组（不改变堆顺序）
            for (int i = 0; i < heap.Count; i++)
            {
                int id = heap.HeapAt(i);
                TimerNode node = _pool.Nodes[id];
                if (node.IsDeleted) continue;
                double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
                output.Add(new TimerDiagnosticInfo
                {
                    Id = id,
                    RemainingSeconds = (float)Math.Max(0, node.FireTime - now),
                    TickIndex = node.CompletedTicks,
                    TotalTicks = node.TotalTicks,
                    TimeMode = node.TimeMode,
                    IsPaused = node.IsPaused,
                    IsDeleted = node.IsDeleted
                });
            }
        }

        private int CountActive(IndexedMinHeap heap)
        {
            int count = 0;
            for (int i = 0; i < heap.Count; i++)
                if (!_pool.Nodes[heap.HeapAt(i)].IsDeleted) count++;
            return count;
        }

        private int CountZombie(IndexedMinHeap heap)
        {
            int count = 0;
            for (int i = 0; i < heap.Count; i++)
                if (_pool.Nodes[heap.HeapAt(i)].IsDeleted) count++;
            return count;
        }

        // ======== 向后兼容适配层 ========
#pragma warning disable CS0618

        [Obsolete("请使用 Delay / Repeat / Schedule")]
        public int AddTimer(TimerHandler callback, float time, bool isLoop = false,
            bool isUnscaled = false, params object[] args)
        {
            TimeMode mode = isUnscaled ? TimeMode.Unscaled : TimeMode.Scaled;
            int count = isLoop ? -1 : 1;
            Action wrappedCallback = () => callback?.Invoke(args);

            TimerHandle handle = isLoop
                ? Repeat(time, wrappedCallback, count, mode)
                : Delay(time, wrappedCallback, mode);

            _legacyIdMap[handle.Id] = handle.Id;
            return handle.Id;
        }

        [Obsolete("请使用 TimerHandle.Cancel()")]
        public void RemoveTimer(int timerId)
        {
            if (timerId <= 0 || timerId >= _pool.Capacity) return;
            _pool.Nodes[timerId].IsDeleted = true;
        }

        [Obsolete("请使用 TimerHandle.Cancel()")]
        public void RemoveAllTimer()
        {
            for (int i = 0; i < _pool.Capacity; i++)
                _pool.Nodes[i].IsDeleted = true;
        }

        [Obsolete("请使用 TimerHandle.Pause()")]
        public void Stop(int timerId) { /* stale access, safe ignore */ }

        [Obsolete("请使用 TimerHandle.Resume()")]
        public void Resume(int timerId) { /* stale access, safe ignore */ }

        [Obsolete("请使用 TimerHandle.IsValid")]
        public bool IsRunning(int timerId)
        {
            if (timerId <= 0 || timerId >= _pool.Capacity) return false;
            return !_pool.Nodes[timerId].IsDeleted;
        }

        [Obsolete("请使用 TimerHandle.Remaining")]
        public float GetLeftTime(int timerId)
        {
            if (timerId <= 0 || timerId >= _pool.Capacity) return 0f;
            TimerNode node = _pool.Nodes[timerId];
            double now = node.TimeMode == TimeMode.Scaled ? _scaledTime : _unscaledTime;
            return (float)Math.Max(0, node.FireTime - now);
        }

        [Obsolete("请使用 Schedule 替代")]
        public void Restart(int timerId) { }

        [Obsolete("请使用 Schedule 替代")]
        public void ResetTimer(int timerId, TimerHandler callback, float time,
            bool isLoop = false, bool isUnscaled = false) { }

        [Obsolete("请使用 Schedule 替代")]
        public void ResetTimer(int timerId, float time, bool isLoop, bool isUnscaled) { }

        [Obsolete("请使用 UniTask.Delay 或 UniTask.DelayFrame")]
        public System.Timers.Timer AddSystemTimer(
            Action<object, System.Timers.ElapsedEventArgs> callBack)
        {
            int interval = 1000;
            var timerTick = new System.Timers.Timer(interval);
            timerTick.AutoReset = true;
            timerTick.Enabled = true;
            timerTick.Elapsed += new System.Timers.ElapsedEventHandler(callBack);
            return timerTick;
        }

#pragma warning restore CS0618
    }
}
