using System;
using System.Collections.Generic;
using GsCan.View.Session;

namespace GsCan.View.Tests.Fakes
{
    internal sealed class FakeClock : IClock
    {
        private readonly List<Scheduled> _scheduled = new List<Scheduled>();
        private long _nowMs;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            var item = new Scheduled(this, _nowMs + (long)delay.TotalMilliseconds, callback);
            _scheduled.Add(item);
            return item;
        }

        public void Advance(TimeSpan delta)
        {
            var target = _nowMs + (long)delta.TotalMilliseconds;
            while (true)
            {
                Scheduled? next = null;
                foreach (var item in _scheduled)
                {
                    if (item.DueMs > target)
                    {
                        continue;
                    }

                    if (next == null || item.DueMs < next.DueMs)
                    {
                        next = item;
                    }
                }

                if (next == null)
                {
                    break;
                }

                _nowMs = next.DueMs;
                _scheduled.Remove(next);
                next.Invoke();
            }

            _nowMs = target;
        }

        private void Cancel(Scheduled item)
        {
            _scheduled.Remove(item);
        }

        private sealed class Scheduled : IDisposable
        {
            private readonly FakeClock _clock;
            private readonly Action _callback;
            private bool _disposed;

            public Scheduled(FakeClock clock, long dueMs, Action callback)
            {
                _clock = clock;
                DueMs = dueMs;
                _callback = callback;
            }

            public long DueMs { get; }

            public void Invoke()
            {
                if (!_disposed)
                {
                    _callback();
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _clock.Cancel(this);
            }
        }
    }
}
