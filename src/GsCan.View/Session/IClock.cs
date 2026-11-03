using System;

namespace GsCan.View.Session
{
    /// <summary>
    /// Clock for cyclic TxSlot scheduling.
    /// </summary>
    public interface IClock
    {
        IDisposable Schedule(TimeSpan delay, Action callback);
    }

    /// <summary>
    /// 把当前线程/进程的定时器精度提到 1ms。
    ///
    /// Windows 默认定时器粒度是 15.6ms，直接 Sleep(2) 会真的睡 15ms，
    /// 想发 200Hz（2.5ms 一帧）根本不可能。timeBeginPeriod(1) 之后
    /// 实测 2ms 的抖动从 ±9ms 降到 ±0.85ms。
    ///
    /// 按引用计数：多次调用只生效一次，避免反复改动系统全局状态。
    /// </summary>
    public static class TimerResolution
    {
        private static readonly object Gate = new object();
        private static int _refCount;
        private static IDisposable? _current;

        public static IDisposable Acquire()
        {
            lock (Gate)
            {
                if (_refCount++ == 0)
                {
                    try
                    {
                        _current = new PeriodHandle(1);
                    }
                    catch
                    {
                        _current = null;
                    }
                }

                return new Release();
            }
        }

        private sealed class Release : IDisposable
        {
            private bool _done;

            public void Dispose()
            {
                if (_done)
                {
                    return;
                }

                _done = true;
                lock (Gate)
                {
                    if (--_refCount <= 0)
                    {
                        _refCount = 0;
                        _current?.Dispose();
                        _current = null;
                    }
                }
            }
        }

        private sealed class PeriodHandle : IDisposable
        {
            private readonly uint _period;
            private bool _done;

            public PeriodHandle(uint period)
            {
                _period = period;
                if (NativeMethods.timeBeginPeriod(period) != 0)
                {
                    throw new InvalidOperationException("timeBeginPeriod failed");
                }
            }

            public void Dispose()
            {
                if (_done)
                {
                    return;
                }

                _done = true;
                NativeMethods.timeEndPeriod(_period);
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("winmm.dll")]
            public static extern uint timeBeginPeriod(uint period);

            [System.Runtime.InteropServices.DllImport("winmm.dll")]
            public static extern uint timeEndPeriod(uint period);
        }
    }
}
