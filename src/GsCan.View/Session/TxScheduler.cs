using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GsCan.View.Session
{
    /// <summary>
    /// 周期发送调度器。
    ///
    /// 两个要点：
    /// 1) 用「绝对时间累加」而不是「每次 sleep 一个周期」。后者会把每次的
    ///    调度误差累积起来，2.5ms 目标跑一会儿就飘到 3ms 以上。
    /// 2) 启动时把系统定时器精度提到 1ms（TimerResolution），否则 Windows
    ///    默认 15.6ms 粒度会让 200Hz 变成笑话。
    /// </summary>
    internal sealed class TxScheduler : IDisposable
    {
        private readonly IClock _clock;
        private readonly Action<int> _send;
        private readonly Dictionary<int, Running> _running = new Dictionary<int, Running>();
        private readonly object _gate = new object();
        private IDisposable? _resolution;
        private int _resolutionUsers;

        public TxScheduler(IClock clock, Action<int> send)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public void Start(int slotIndex, double periodMs)
        {
            lock (_gate)
            {
                StopUnlocked(slotIndex);
                if (periodMs <= 0)
                {
                    return;
                }

                AcquireResolutionUnlocked();

                var state = new Running(periodMs);
                _running[slotIndex] = state;
                state.NextDueMs = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0 + periodMs;
                Arm(slotIndex, state);
            }
        }

        public void Stop(int slotIndex)
        {
            lock (_gate)
            {
                StopUnlocked(slotIndex);
            }
        }

        public void StopAll()
        {
            lock (_gate)
            {
                if (_running.Count == 0)
                {
                    return;
                }

                var states = new List<Running>(_running.Values);
                _running.Clear();
                foreach (var state in states)
                {
                    state.Cancel();
                }

                ReleaseResolutionUnlocked();
            }
        }

        public void Dispose()
        {
            StopAll();
        }

        private void AcquireResolutionUnlocked()
        {
            if (_resolutionUsers++ == 0)
            {
                _resolution = TimerResolution.Acquire();
            }
        }

        private void ReleaseResolutionUnlocked()
        {
            if (_resolutionUsers > 0 && --_resolutionUsers == 0)
            {
                _resolution?.Dispose();
                _resolution = null;
            }
        }

        private void StopUnlocked(int slotIndex)
        {
            if (_running.TryGetValue(slotIndex, out var state))
            {
                _running.Remove(slotIndex);
                state.Cancel();
                ReleaseResolutionUnlocked();
            }
        }

        private static double NowMs => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0;

        private void Arm(int slotIndex, Running state)
        {
            // 按「距离下一个绝对时刻还有多久」来排下一次。
            // 已经超时（例如线程被抢占）就立刻补发，并把基准往前推一个周期。
            double delayMs = state.NextDueMs - NowMs;
            if (delayMs < 0)
            {
                delayMs = 0;
            }

            state.Handle = _clock.Schedule(TimeSpan.FromMilliseconds(delayMs), () => Tick(slotIndex, state));
        }

        private void Tick(int slotIndex, Running state)
        {
            lock (_gate)
            {
                if (!IsCurrent(slotIndex, state))
                {
                    return;
                }
            }

            _send(slotIndex);

            lock (_gate)
            {
                if (!IsCurrent(slotIndex, state))
                {
                    return;
                }

                state.NextDueMs += state.PeriodMs;

                // 落后超过两个周期就不追了，直接从当前时间重新起算，
                // 避免积压后疯狂补发把总线打满。
                double now = NowMs;
                if (state.NextDueMs < now - state.PeriodMs * 2)
                {
                    state.NextDueMs = now + state.PeriodMs;
                }

                Arm(slotIndex, state);
            }
        }

        private bool IsCurrent(int slotIndex, Running state)
        {
            return _running.TryGetValue(slotIndex, out var current) && ReferenceEquals(current, state);
        }

        private sealed class Running
        {
            public Running(double periodMs)
            {
                PeriodMs = periodMs;
            }

            public double PeriodMs { get; }

            /// <summary>下一次应该发送的绝对时刻（毫秒，Stopwatch 基准）。</summary>
            public double NextDueMs { get; set; }

            public IDisposable? Handle { get; set; }

            public void Cancel() => Handle?.Dispose();
        }
    }

    internal sealed class RealtimeClock : IClock
    {
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            return new TimerHandle(delay, callback);
        }

        private sealed class TimerHandle : IDisposable
        {
            private Timer? _timer;
            private Action? _callback;

            public TimerHandle(TimeSpan delay, Action callback)
            {
                _callback = callback;
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                _timer = new Timer(OnTick, null, delay, Timeout.InfiniteTimeSpan);
            }

            private void OnTick(object? _)
            {
                var callback = Interlocked.Exchange(ref _callback, null);
                var timer = Interlocked.Exchange(ref _timer, null);
                timer?.Dispose();
                callback?.Invoke();
            }

            public void Dispose()
            {
                var timer = Interlocked.Exchange(ref _timer, null);
                timer?.Dispose();
                Interlocked.Exchange(ref _callback, null);
            }
        }
    }
}
