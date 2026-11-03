using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using GsCan;
using GsCan.View.Session;

namespace PwmTest
{
    internal static class Program
    {
        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var port = new GsCanPort();
            var devices = port.List();
            if (devices.Count == 0) { Console.WriteLine("没有设备"); Environment.Exit(2); }

            IOpenedDevice? dev = null;
            try { dev = port.Open(devices[0]); }
            catch (Exception ex) { Console.WriteLine("[占用] " + ex.Message); Environment.Exit(3); }

            var payload = new byte[] { 0, 0, 0, 0, 0, 0, 0, 8 };
            const uint Id = 0x0AF00011u;

            try
            {
                dev!.Start(0, new ChannelOptions { Bitrate = 500_000 });

                Console.WriteLine("=== 单帧 send 的耗时（USB 往返）===");
                MeasureSingleSend(dev, Id, payload);

                Console.WriteLine("\n=== 按目标间隔发送，看能否稳住 ===");
                foreach (var targetMs in new[] { 10.0, 5.0, 2.5 })
                {
                    MeasurePaced(dev, Id, payload, targetMs, 60);
                }
            }
            finally
            {
                try { dev?.Stop(0); } catch { }
                dev?.Dispose();
            }
        }

        private static void MeasureSingleSend(IOpenedDevice dev, uint id, byte[] payload)
        {
            var frame = new CanFrame(id, payload, CanFrameKind.Rx, extended: true,
                                     remote: false, fd: false, bitRateSwitch: false,
                                     errorStateIndicator: false, overflow: false, timestampMicroseconds: 1);

            var times = new List<double>();
            int fail = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++)
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                try { dev.Send(0, frame); times.Add(sw.Elapsed.TotalMilliseconds - t0); }
                catch (GsCanException) { fail++; }
                Thread.Sleep(5);   // 给 USB 一点喘息，避免测到队列满
            }

            double min = double.MaxValue, max = 0, sum = 0;
            foreach (var t in times) { if (t < min) min = t; if (t > max) max = t; sum += t; }
            Console.WriteLine($"  成功 {times.Count} 失败 {fail}");
            if (times.Count > 0)
            {
                Console.WriteLine($"  单次 send：最小 {min:0.000} 最大 {max:0.000} 平均 {sum / times.Count:0.000} ms");
                Console.WriteLine($"  => 若每次都要等这么久，理论上限 {1000 / (sum / times.Count):0} 帧/秒");
            }
        }

        private static void MeasurePaced(IOpenedDevice dev, uint id, byte[] payload, double targetMs, int count)
        {
            var frame = new CanFrame(id, payload, CanFrameKind.Rx, extended: true,
                                     remote: false, fd: false, bitRateSwitch: false,
                                     errorStateIndicator: false, overflow: false, timestampMicroseconds: 1);

            var stamps = new List<double>();
            var sendCost = new List<double>();
            int failed = 0;

            using (TimerResolution.Acquire())
            {
                var sw = Stopwatch.StartNew();
                double next = 0;

                for (int i = 0; i < count; i++)
                {
                    next += targetMs;
                    while (true)
                    {
                        double remain = next - sw.Elapsed.TotalMilliseconds;
                        if (remain <= 0) break;
                        if (remain > 1.2) Thread.Sleep(1);
                        else Thread.SpinWait(300);
                    }

                    var at = sw.Elapsed.TotalMilliseconds;
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    try
                    {
                        dev.Send(0, frame);
                        stamps.Add(at);
                        sendCost.Add(sw.Elapsed.TotalMilliseconds - t0);
                    }
                    catch (GsCanException)
                    {
                        failed++;
                    }
                }
            }

            if (stamps.Count < 2)
            {
                Console.WriteLine($"  目标 {targetMs,4:0.0}ms：能发的太少（失败 {failed}）");
                return;
            }

            var gaps = new List<double>();
            for (int i = 1; i < stamps.Count; i++) gaps.Add(stamps[i] - stamps[i - 1]);
            double gmin = double.MaxValue, gmax = 0, gsum = 0;
            foreach (var g in gaps) { if (g < gmin) gmin = g; if (g > gmax) gmax = g; gsum += g; }
            double avg = gsum / gaps.Count;

            double cmin = double.MaxValue, cmax = 0, csum = 0;
            foreach (var c in sendCost) { if (c < cmin) cmin = c; if (c > cmax) cmax = c; csum += c; }

            string ok = Math.Abs(avg - targetMs) < targetMs * 0.2 ? "达标" : "偏差大";
            Console.WriteLine($"  目标 {targetMs,4:0.0}ms -> 实际平均 {avg,6:0.000}ms 抖动±{(max0(gmax) - gmin) / 2,5:0.000} " +
                              $"失败{failed,3}  send耗时{csum / sendCost.Count,5:0.000}ms  {ok}  (≈{1000 / (avg * 2):0}Hz PWM)");
        }

        private static double max0(double v) => v;
    }
}
