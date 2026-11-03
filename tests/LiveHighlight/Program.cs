using System;
using System.Collections.Generic;
using System.Threading;
using GsCan;
using GsCan.View.Session;

namespace LiveHighlight
{
    /// <summary>
    /// 真机验证：打开设备、启动 CH0，发一条查询，看 Echo 与应答是否都被高亮。
    /// 直接驱动 FrameIngest，绕过界面，但走的是同一条接收/配对路径。
    /// </summary>
    internal static class Program
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) _fail++;
        }

        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var port = new GsCanPort();
            var devices = port.List();
            Console.WriteLine($"设备数 = {devices.Count}");
            if (devices.Count == 0) { Console.WriteLine("没有 gs_usb 设备"); Environment.Exit(2); }

            IOpenedDevice? dev = null;
            try
            {
                dev = port.Open(devices[0]);
                Console.WriteLine($"已打开：{dev.ChannelCount} 路通道");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[占用] " + ex.Message);
                Environment.Exit(3);
            }

            var clock = new UiClock();
            var filter = new DisplayFilter();
            var ingest = new FrameIngest(clock, filter, ui: null);

            try
            {
                dev.Start(0, new ChannelOptions { Bitrate = 500_000 });
                dev.Start(1, new ChannelOptions { Bitrate = 500_000, ListenOnly = true });
                Console.WriteLine("CH0 已启动（正常） / CH1 已启动（只听，有心跳）");

                // 先收一点背景流量
                DrainFor(dev, ingest, 300);
                Console.WriteLine($"背景帧 {ingest.Trace.Count} 条，Latest {ingest.Latest.Count} 行");

                // 登记发送（用最近帧时间戳作为配对起点，与实际运行一致）
                ulong before = (ulong)ingest.LastFrameTimestampUs;
                Console.WriteLine($"发送前 LastFrameTs={ingest.LastFrameTimestampUs}");
                ingest.PairTracker.Begin(
                    new SendPairRequest(0, 0, 0x02000002u, true, "查询版本"),
                    (long)before);

                // 真的发出去
                dev.Send(0, CanFrame.Classic(0x02000002u, Array.Empty<byte>(), extended: true));
                Console.WriteLine("已发送 0x02000002（查询版本）");

                DrainFor(dev, ingest, 1200);
                ingest.FlushUntilIdle();

                Console.WriteLine($"\n收集到 {ingest.Trace.Count} 条，Latest {ingest.Latest.Count} 行");

                // 找高亮行
                int sentRows = 0, pairedRows = 0;
                for (int i = 0; i < ingest.Trace.Count; i++)
                {
                    var r = ingest.Trace[i];
                    if (r.Kind == "Echo" && ingest.TryGetHighlight(r.TimestampMicroseconds, out var k, out _)
                        && k == RowHighlight.Sent)
                    {
                        sentRows++;
                    }

                    if (r.Kind == "Rx" && r.Id == 0x02000002u
                        && ingest.TryGetHighlight(r.TimestampMicroseconds, out var k2, out var t2)
                        && k2 == RowHighlight.Paired)
                    {
                        pairedRows++;
                        Console.WriteLine($"  应答行：{r.IdHex} [{r.DataHex}]  角标={t2}");
                    }
                }

                // 诊断：把 0x02000002 的所有帧打印出来
                for (int i = 0; i < ingest.Trace.Count; i++)
                {
                    var r = ingest.Trace[i];
                    if (r.Id == 0x02000002u)
                    {
                        bool hv = ingest.TryGetHighlight(r.TimestampMicroseconds, out var hk, out var ht);
                        Console.WriteLine($"      {r.Kind,-4} ts={r.TimestampMicroseconds} 高亮={hv} kind={hk} tag={ht}");
                    }
                }

                Check(sentRows >= 1, $"Echo 帧被标为「发送」（{sentRows} 条）");
                Check(pairedRows >= 1, $"应答帧被标为「配对」（{pairedRows} 条）");

                // Latest 上也要有
                int latestHighlighted = 0, latestPaired = 0;
                for (int i = 0; i < ingest.Latest.Count; i++)
                {
                    if (ingest.Latest[i].IsHighlighted) latestHighlighted++;
                    if (ingest.Latest[i].IsPaired) latestPaired++;
                }

                Check(latestHighlighted >= 1, $"Latest 有高亮行（{latestHighlighted}）");
                Check(latestPaired >= 1, $"Latest 有配对行（{latestPaired}）");

                // 心跳不应被高亮
                int heartbeatHighlighted = 0;
                for (int i = 0; i < ingest.Latest.Count; i++)
                {
                    var r = ingest.Latest[i];
                    if (r.Id == 0x01100001u && r.IsHighlighted) heartbeatHighlighted++;
                }

                Check(heartbeatHighlighted == 0, $"心跳行未被误高亮（{heartbeatHighlighted}）");

                // ===== 第二轮：再发一次，应该只剩一对 =====
                Console.WriteLine("\n--- 第二次发送 ---");
                ingest.ResetHighlightsForNewSend();
                ingest.PairTracker.Begin(new SendPairRequest(1, 0, 0x01020002u, true, "第二轮"), ingest.LastFrameTimestampUs);
                dev.Send(0, CanFrame.Classic(0x01020002u, Array.Empty<byte>(), extended: true));
                DrainFor(dev, ingest, 1200);
                ingest.FlushUntilIdle();

                int hl2 = 0, pr2 = 0, traceHl = 0;
                for (int i = 0; i < ingest.Latest.Count; i++)
                {
                    if (ingest.Latest[i].IsHighlighted) hl2++;
                    if (ingest.Latest[i].IsPaired) pr2++;
                }

                for (int i = 0; i < ingest.TraceDisplay.Count; i++)
                {
                    if (ingest.TraceDisplay[i].IsHighlighted) traceHl++;
                }

                Check(hl2 <= 2, $"第二轮后 Latest 高亮行 ≤2（{hl2}）");
                Check(pr2 <= 1, $"第二轮后配对行 ≤1（{pr2}）");
                Check(traceHl <= 2, $"第二轮后 Trace 高亮行 ≤2（{traceHl}）");
                Check(ingest.Trace.Count > 500, $"确实有大量心跳流量（{ingest.Trace.Count} 帧）");
            }
            finally
            {
                try { dev.Stop(0); } catch { }
                dev.Dispose();
            }

            Console.WriteLine(_fail == 0 ? "\n真机验证通过" : $"\n失败 {_fail} 项");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }

        private static void DrainFor(IOpenedDevice dev, FrameIngest ingest, int ms)
        {
            var until = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < until)
            {
                // 两路都要读，否则 CH1 的心跳帧会积压
                for (int ch = 0; ch < 2; ch++)
                {
                    while (dev.TryRead(ch, 0, out var frame))
                    {
                        ingest.Accept(ch, frame, paused: false);
                    }
                }

                Thread.Sleep(2);
            }
        }

        private sealed class UiClock : IClock
        {
            public IDisposable Schedule(TimeSpan delay, Action callback)
            {
                callback();
                return new Noop();
            }

            private sealed class Noop : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
