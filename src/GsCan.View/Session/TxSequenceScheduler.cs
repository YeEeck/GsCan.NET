using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GsCan.View.Session
{
    /// <summary>
    /// 顺序发送调度器：用一个定时器按列表顺序轮流发送，而不是每行各起一个定时器。
    ///
    /// 为什么需要它：原来的实现里每个 TxSlot 都有自己的定时器，两条命令同时使能时
    /// 谁先谁后是随机的、间隔还会互相挤（实测 10ms 周期能挤出 0.5ms 的间隔）。
    /// 想做 PWM 或固定顺序的执行序列，就必须由单个调度器统一发号。
    ///
    /// 每步之间的间隔取「当前这一条命令自己的周期」。所以：
    ///   两条都填 2.5ms  -> 严格交替，每 2.5ms 一帧（等效 200Hz 方波）
    ///   第一条 10ms、第二条 5ms -> A 发完等 10ms 发 B，B 发完等 5ms 再发 A
    /// </summary>
    internal sealed class TxSequenceScheduler : IDisposable
    {
        private readonly IClock _clock;
        private readonly object _gate = new object();
        private readonly List<Step> _steps = new List<Step>();

        private IDisposable? _resolution;
        private IDisposable? _pending;
        private bool _running;
        private int _cursor;
        private double _nextDueMs;
        private double _lastPeriodMs;

        public TxSequenceScheduler(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>当前是否在顺序发送。</summary>
        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _running;
                }
            }
        }

        /// <summary>已经发到第几步（用于状态显示）。</summary>
        public int Cursor
        {
            get
            {
                lock (_gate)
                {
                    return _cursor;
                }
            }
        }

        public int StepCount
        {
            get
            {
                lock (_gate)
                {
                    return _steps.Count;
                }
            }
        }

        /// <summary>
        /// 设置参与顺序发送的命令（按列表顺序）。
        /// 传空列表等价于停止。
        /// </summary>
        public void SetSteps(IReadOnlyList<Step> steps)
        {
            lock (_gate)
            {
                _steps.Clear();
                if (steps != null)
                {
                    for (int i = 0; i < steps.Count; i++)
                    {
                        _steps.Add(steps[i]);
                    }
                }
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_running || _steps.Count == 0)
                {
                    return;
                }

                _running = true;
                _cursor = 0;
                _lastPeriodMs = 0;
                _nextDueMs = NowMs;

                if (_resolution == null)
                {
                    _resolution = TimerResolution.Acquire();
                }

                Arm();
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                StopUnlocked();
            }
        }

        /// <summary>停掉并清空步骤。</summary>
        public void Reset()
        {
            lock (_gate)
            {
                StopUnlocked();
                _steps.Clear();
            }
        }

        public void Dispose() => Reset();

        private void StopUnlocked()
        {
            _running = false;

            var handle = _pending;
            _pending = null;
            handle?.Dispose();

            var resolution = _resolution;
            _resolution = null;
            resolution?.Dispose();
        }

        private static double NowMs => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency * 1000.0;

        private void Arm()
        {
            if (!_running || _steps.Count == 0)
            {
                return;
            }

            double delay = _nextDueMs - NowMs;
            if (delay < 0)
            {
                delay = 0;
            }

            _pending = _clock.Schedule(TimeSpan.FromMilliseconds(delay), OnTick);
        }

        private void OnTick()
        {
            Step? step;
            double period;

            lock (_gate)
            {
                if (!_running || _steps.Count == 0)
                {
                    return;
                }

                _pending = null;

                if (_cursor >= _steps.Count)
                {
                    _cursor = 0;
                }

                step = _steps[_cursor];
                period = step.PeriodMs;

                // 按「当前这条的周期」推进下一次的时间点
                _nextDueMs += period > 0 ? period : 1.0;
                _lastPeriodMs = period;

                // 落后太多就不追了，避免积压后疯狂补发
                double now = NowMs;
                if (_nextDueMs < now - 50)
                {
                    _nextDueMs = now + (period > 0 ? period : 1.0);
                }

                _cursor = (_cursor + 1) % _steps.Count;
            }

            try
            {
                step.Send();
            }
            catch
            {
                // 单步失败不终止序列，交给上层状态显示
            }

            lock (_gate)
            {
                if (_running)
                {
                    Arm();
                }
            }
        }

        /// <summary>一步：发送哪一条、用它的周期做步进间隔。</summary>
        public sealed class Step
        {
            public Step(int slotIndex, double periodMs, Action send)
            {
                SlotIndex = slotIndex;
                PeriodMs = periodMs;
                Send = send ?? throw new ArgumentNullException(nameof(send));
            }

            public int SlotIndex { get; }
            public double PeriodMs { get; }
            public Action Send { get; }
        }
    }
}
