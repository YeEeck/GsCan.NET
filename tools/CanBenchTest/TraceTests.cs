using System;
using System.Diagnostics;
using CanBench;

namespace CanBenchTest
{
    /// <summary>Latest 聚合与 ID 过滤的测试。</summary>
    internal static class TraceTests
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) _fail++;
        }

        private static BusFrame Frame(byte ch, uint id, bool echo, params byte[] data) => new BusFrame
        {
            At = DateTime.Now,
            Channel = ch,
            Id = id,
            Extended = true,
            Echo = echo,
            Data = data,
        };

        public static int Run()
        {
            _fail = 0;
            LatestAggregation();
            Filtering();
            Period();
            Performance();
            return _fail;
        }

        private static void LatestAggregation()
        {
            Console.WriteLine("\n[Latest 聚合]");
            var agg = new LatestAggregator();

            // 心跳：同一个 ID 狂刷 5000 帧
            var t0 = DateTime.Now;
            for (int i = 0; i < 5000; i++)
            {
                var f = Frame(1, 0x01100001u, false, 0xFF, 0xFF, 0x03, 0xBE, 0, 0, 0, 0);
                f.At = t0.AddMilliseconds(i * 0.3);
                agg.Add(f);
            }

            Check(agg.Count == 1, $"5000 帧心跳只产生 1 行（实际 {agg.Count} 行）");

            var e = agg.Snapshot()[0];
            Check(e.Count == 5000, $"计数 = {e.Count}");
            Check(e.Channel == 1, $"CH 列有值 = {e.Channel}");
            Check(e.Kind == "Rx", $"Kind = {e.Kind}");
            Check(e.Direction == "← 接收", $"方向文本 = {e.Direction}");

            agg.Add(Frame(0, 0x01100001u, false));
            Check(agg.Count == 2, "同 ID 不同通道分成 2 行");

            agg.Add(Frame(1, 0x02000002u, true));
            agg.Add(Frame(1, 0x02000002u, false, 0x01, 0x00, 0x02, 0x01));
            Check(agg.Count == 4, "同 ID 的 发送/接收 分开计（共 4 行）");

            var echoEntry = agg.Snapshot().Find(x => x.Id == 0x02000002u && x.Kind == "Echo");
            Check(echoEntry != null && echoEntry.Direction == "发送 →", "发送方向文本正确");

            var err = new BusFrame { At = DateTime.Now, Channel = 1, Error = true, ErrorBits = 0x04, Id = 0x04 };
            agg.Add(err);
            var rxEntry = agg.Snapshot().Find(x => x.Id == 0x02000002u && x.Kind == "Rx");
            Check(rxEntry != null && rxEntry.Data.Length == 4, "错误帧没有把同 ID 的接收数据清空");

            var errEntry = agg.Snapshot().Find(x => x.Kind == "Error");
            Check(errEntry != null && errEntry.ErrorText.Contains("总线错误"), $"错误行有说明：{errEntry?.ErrorText}");
        }

        private static void Filtering()
        {
            Console.WriteLine("\n[ID 过滤]");

            var none = IdFilter.Parse("");
            Check(none.IsEmpty && none.Match(0x123), "空条件 = 全放行");

            var one = IdFilter.Parse("0x01100001");
            Check(one.Match(0x01100001u) && !one.Match(0x02000002u), "单个 ID 精确匹配");

            var range = IdFilter.Parse("0x100-0x1FF");
            Check(range.Match(0x100) && range.Match(0x1FF) && !range.Match(0x0FF) && !range.Match(0x200),
                  "范围 0x100-0x1FF 边界正确");

            Check(IdFilter.Parse("0x100..0x1FF").Match(0x150), "0x100..0x1FF 写法也认");

            var excl = IdFilter.Parse("!0x01100001");
            Check(!excl.Match(0x01100001u) && excl.Match(0x02000002u), "排除心跳帧后其它照常显示");

            var exclRange = IdFilter.Parse("!0x01100000-0x011000FF");
            Check(!exclRange.Match(0x01100001u) && exclRange.Match(0x02000002u), "排除范围");

            var mixed = IdFilter.Parse("0x02000002 0x01000002 !0x01000002");
            Check(mixed.Match(0x02000002u), "包含+排除混用：包含项保留");

            var multi = IdFilter.Parse("0x100,0x200;0x300");
            Check(multi.Match(0x100) && multi.Match(0x200) && multi.Match(0x300) && !multi.Match(0x150),
                  "逗号/分号分隔多个 ID");

            bool threw = false;
            try { IdFilter.Parse("ZZZZ"); } catch (FormatException) { threw = true; }
            Check(threw, "非法 ID 抛 FormatException");

            threw = false;
            try { IdFilter.Parse("0x30000000"); } catch (FormatException) { threw = true; }
            Check(threw, "超 29 位抛 FormatException");
        }

        private static void Period()
        {
            Console.WriteLine("\n[周期]");

            // 硬件时间戳
            var a1 = new LatestAggregator();
            for (int i = 0; i < 100; i++)
            {
                var f = Frame(1, 0x01100001u, false, 0xFF);
                f.TimestampUs = (uint)(i * 300);
                a1.Add(f);
            }
            var e1 = a1.Snapshot()[0];
            Check(Math.Abs(e1.PeriodMs - 0.3) < 0.01, $"硬件时间戳 300us -> {e1.PeriodText}");

            // 时间戳回绕（uint32 微秒约 71 分钟一圈）
            var a2 = new LatestAggregator();
            uint baseTs = 0xFFFFF000;
            for (int i = 0; i < 100; i++)
            {
                var f = Frame(1, 0x01100001u, false, 0xFF);
                f.TimestampUs = unchecked(baseTs + (uint)(i * 300));
                a2.Add(f);
            }
            var e2 = a2.Snapshot()[0];
            Check(Math.Abs(e2.PeriodMs - 0.3) < 0.01, $"跨回绕后周期仍 300us -> {e2.PeriodText}");

            // 无时间戳时退回本地时间
            var a3 = new LatestAggregator();
            var t0 = DateTime.Now;
            for (int i = 0; i < 50; i++)
            {
                var f = Frame(1, 0x02000002u, false, 0x01);
                f.At = t0.AddMilliseconds(i * 10);
                a3.Add(f);
            }
            var e3 = a3.Snapshot()[0];
            Check(Math.Abs(e3.PeriodMs - 10.0) < 0.5, $"无时间戳时用本地时间 -> {e3.PeriodText}");

            // 单帧不该瞎编周期
            var a4 = new LatestAggregator();
            a4.Add(Frame(0, 0x123u, false));
            Check(a4.Snapshot()[0].PeriodText == "—", "只有 1 帧时周期显示「—」");
        }

        private static void Performance()
        {
            Console.WriteLine("\n[性能：心跳帧场景]");
            var sw = Stopwatch.StartNew();
            var big = new LatestAggregator();
            var t0 = DateTime.Now;
            for (int i = 0; i < 200_000; i++)
            {
                var f = Frame(1, 0x01100001u, false, 0xFF);
                f.At = t0.AddTicks(i * 3);
                f.TimestampUs = (uint)(i * 300);
                big.Add(f);
            }
            sw.Stop();
            Check(big.Count == 1, $"20 万帧心跳仍是 1 行（{big.Count}）");
            Check(sw.ElapsedMilliseconds < 2000, $"聚合 20 万帧耗时 {sw.ElapsedMilliseconds}ms（<2000ms）");
            Console.WriteLine($"       平均 {sw.Elapsed.TotalMilliseconds / 200_000 * 1000:0.00}µs/帧");
        }
    }
}
