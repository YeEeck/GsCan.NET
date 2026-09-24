using System;
using System.Collections.Generic;
using System.Threading;

namespace GsCan.View.Session
{
    internal sealed class TxScheduler
    {
        private readonly IClock _clock;
        private readonly Action<int> _send;
        private readonly Dictionary<int, Running> _running = new Dictionary<int, Running>();
        private readonly object _gate = new object();

        public TxScheduler(IClock clock, Action<int> send)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public void Start(int slotIndex, int periodMs)
        {
            lock (_gate)
            {
                StopUnlocked(slotIndex);
                if (periodMs <= 0)
                {
                    return;
                }

                var state = new Running(periodMs);
                _running[slotIndex] = state;
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
            }
        }

        private void StopUnlocked(int slotIndex)
        {
            if (_running.TryGetValue(slotIndex, out var state))
            {
                _running.Remove(slotIndex);
                state.Cancel();
            }
        }

        private void Arm(int slotIndex, Running state)
        {
            state.Handle = _clock.Schedule(TimeSpan.FromMilliseconds(state.PeriodMs), () => Tick(slotIndex, state));
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
                if (IsCurrent(slotIndex, state))
                {
                    Arm(slotIndex, state);
                }
            }
        }

        private bool IsCurrent(int slotIndex, Running state)
        {
            return _running.TryGetValue(slotIndex, out var current) && ReferenceEquals(current, state);
        }

        private sealed class Running
        {
            public Running(int periodMs)
            {
                PeriodMs = periodMs;
            }

            public int PeriodMs { get; }

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
