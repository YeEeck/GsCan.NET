using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using GsCan.View.Session;

namespace OrderTest
{
    /// <summary>
    /// 验证顺序发送：严格按列表顺序、每步按该条自己的周期推进。
    /// 时间戳统一用同一个 Stopwatch，避免测量误差。
    /// </summary>
    internal static class Program
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<(double At, string Name)> Fired = new();
        private static readonly object Gate = new();

        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.WriteLine("=== 顺序发送：A、B 各 2.5ms 步进（模拟 200Hz 方波）===");
            RunSequence(new[] { ("A", 2.5), ("B", 2.5) }, 30);

            Console.WriteLine("\n=== 顺序发送：A 用 10ms、B 用 5ms ===");
            RunSequence(new[] { ("A", 10.0), ("B", 5.0) }, 16);

            Console.WriteLine("\n=== 顺序发送：三条 A/B/C，各 5ms ===");
            RunSequence(new[] { ("A", 5.0), ("B", 5.0), ("C", 5.0) }, 15);
        }

        private static void RunSequence((string Name, double Period)[] steps, int total)
        {
            var clock = new RealClock();
            using var seq = new TxSequenceScheduler(clock);

            var list = new List<TxSequenceScheduler.Step>();
            double expectedStep = steps[0].Period;
            foreach (var (name, period) in steps)
            {
                var captured = name;
                list.Add(new TxSequenceScheduler.Step(0, period, () =>
                {
                    lock (Gate) { Fired.Add((Clock.Elapsed.TotalMilliseconds, captured)); }
                }));
            }
            seq.SetSteps(list);

            lock (Gate) { Fired.Clear(); }

            seq.Start();
            var waitUntil = Environment.TickCount64 + 4000;
            while (Environment.TickCount64 < waitUntil)
            {
                lock (Gate) { if (Fired.Count >= total) break; }
                Thread.Sleep(2);
            }
            seq.Stop();

            lock (Gate)
            {
                if (Fired.Count < 3)
                {
                    Console.WriteLine($"  只收到 {Fired.Count} 次，无法判断");
                    return;
                }

                // 顺序
                var sb = new System.Text.StringBuilder("  顺序: ");
                for (int i = 0; i < Math.Min(18, Fired.Count); i++) sb.Append(Fired[i].Name);
                Console.WriteLine(sb.ToString());

                // 间隔统计（跳过第 0 个）
                var gaps = new List<double>();
                for (int i = 1; i < Fired.Count; i++) gaps.Add(Fired[i].At - Fired[i - 1].At);
                double min = double.MaxValue, max = 0, sum = 0;
                foreach (var g in gaps) { if (g < min) min = g; if (g > max) max = g; sum += g; }
                double avg = sum / gaps.Count;

                Console.WriteLine($"  间隔：最小 {min:0.000}  最大 {max:0.000}  平均 {avg:0.000} ms");
                Console.WriteLine($"  目标步进 {expectedStep:0.0}ms  偏差 {Math.Abs(avg - expectedStep) / expectedStep * 100:0.0}%");

                // 严格交替/循环检查
                bool ordered = true;
                for (int i = 1; i < Fired.Count; i++)
                {
                    int prev = IndexOf(steps, Fired[i - 1].Name);
                    int cur = IndexOf(steps, Fired[i].Name);
                    int expect = (prev + 1) % steps.Length;
                    if (cur != expect) { ordered = false; break; }
                }

                Console.WriteLine(ordered ? "  顺序正确（严格按列表循环）✓" : "  顺序错误 ✗");
            }
        }

        private static int IndexOf((string Name, double Period)[] steps, string name)
        {
            for (int i = 0; i < steps.Length; i++) if (steps[i].Name == name) return i;
            return -1;
        }

        private sealed class RealClock : IClock
        {
            public IDisposable Schedule(TimeSpan delay, Action callback)
            {
                return new Timer(_ => callback(), null, delay, Timeout.InfiniteTimeSpan);
            }
        }
    }
}
