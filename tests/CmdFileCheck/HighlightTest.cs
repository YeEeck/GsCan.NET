using System;
using System.Collections.Generic;
using GsCan;
using GsCan.View.Session;

namespace CmdFileCheck
{
    /// <summary>端到端验证：发送 -> Echo -> 应答 的高亮是否真的落到行上。</summary>
    internal static class HighlightTest
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) _fail++;
        }

        private static CanFrame Frame(uint id, CanFrameKind kind, uint ts, params byte[] data) =>
            new CanFrame(id, data, kind, extended: true, remote: false, fd: false,
                         bitRateSwitch: false, errorStateIndicator: false, overflow: false,
                         timestampMicroseconds: ts);

        public static int Run()
        {
            _fail = 0;
            Console.WriteLine("\n[高亮端到端]");

            var clock = new RealClock();
            var filter = new DisplayFilter();
            var ingest = new FrameIngest(clock, filter, ui: null);   // 无 UI: 同步路径

            // 1) 先跑一堆心跳，把高亮缓存灌满
            uint ts = 100_000;
            for (int i = 0; i < 60; i++)   // 少于 TraceDisplay 窗口(128)，保证高亮行还在窗口内
            {
                ingest.Accept(1, Frame(0x01100001u, CanFrameKind.Rx, ts += 300, 1, 2, 3), paused: false);
            }

            Check(ingest.Latest.Count >= 1, $"心跳进 Latest（{ingest.Latest.Count} 行）");

            // 2) 登记一次发送，然后模拟 Echo + 应答
            ingest.PairTracker.Begin(new SendPairRequest(0, 0, 0x01020002u, true, "工作模式"), ts);

            Check(ingest.PairTracker.PendingCount == 1, $"登记后待配对 {ingest.PairTracker.PendingCount}");

            uint echoTs = ts + 1000;
            var echoFrame = Frame(0x01020002u, CanFrameKind.Echo, echoTs);
            ingest.Accept(0, echoFrame, paused: false);
            for (int i = 0; i < ingest.Trace.Count; i++)
            {
                var tr = ingest.Trace[i];
                if (tr.Id == 0x01020002u)
                {
                }
            }
            for (int i = 0; i < ingest.TraceDisplay.Count; i++)
            {
                Console.WriteLine($"        行{i}: Kind={ingest.TraceDisplay[i].Kind} Id={ingest.TraceDisplay[i].IdHex}");
            }

            uint replyTs = echoTs + 2000;
            ingest.Accept(0, Frame(0x01020002u, CanFrameKind.Rx, replyTs, 0x00, 0x00), paused: false);

            // 无 UI 路径下 TraceDisplay 需要主动刷新（真实界面由 FlushPending 负责）
            ingest.FlushUntilIdle();
            Check(ingest.TraceDisplay.Count > 0, $"心跳进 TraceDisplay（{ingest.TraceDisplay.Count} 行）");

            // 3) 检查 Echo 是否被标记为「发送」
            bool echoMarked = ingest.TryGetHighlight(echoTs, out var ek, out var etag);
            Check(echoMarked && ek == RowHighlight.Sent, $"Echo 帧标为发送（{ek} / {etag}）");

            // 4) 检查应答是否被标记为「配对」
            bool replyMarked = ingest.TryGetHighlight(replyTs, out var rk, out var rtag);
            Check(replyMarked && rk == RowHighlight.Paired, $"应答帧标为配对（{rk} / {rtag}）");

            // 5) Latest 行上真的带上了高亮
            LatestRow? echoRow = null, replyRow = null;
            for (int i = 0; i < ingest.Latest.Count; i++)
            {
                var r = ingest.Latest[i];
                if (r.LastTimestampMicroseconds == echoTs) echoRow = r;
                if (r.LastTimestampMicroseconds == replyTs) replyRow = r;
            }

            // Latest 按 (通道, Kind, ID) 分行，Echo 与 Rx 各占一行
            Check(echoRow != null, "Echo 在 Latest 里有独立一行");
            Check(replyRow != null, "应答在 Latest 里有独立一行");

            int hlRows = 0, pairedRows = 0;
            for (int k = 0; k < ingest.Latest.Count; k++)
            {
                if (ingest.Latest[k].IsHighlighted) hlRows++;
                if (ingest.Latest[k].IsPaired) pairedRows++;
            }

            Check(hlRows >= 1, $"Latest 有高亮行（{hlRows}）");
            Check(pairedRows == 1, $"Latest 有 1 行配对（{pairedRows}）");

            // 6) 心跳行不该被高亮
            bool heartbeatHighlighted = false;
            for (int i = 0; i < ingest.Latest.Count; i++)
            {
                var r = ingest.Latest[i];
                if (r.Id == 0x01100001u && r.IsHighlighted) heartbeatHighlighted = true;
            }
            Check(!heartbeatHighlighted, "心跳行没有被误高亮");

            // 7) Trace 行也带高亮
            bool traceHasSent = false, traceHasPaired = false;
            for (int i = 0; i < ingest.TraceDisplay.Count; i++)
            {
                if (ingest.TraceDisplay[i].Highlight == RowHighlight.Sent) traceHasSent = true;
                if (ingest.TraceDisplay[i].Highlight == RowHighlight.Paired) traceHasPaired = true;
            }
            Check(traceHasSent, "Trace 里有「发送」高亮行");
            Check(traceHasPaired, "Trace 里有「应答」高亮行");

            // ===== 两轮发送：只应留下最后一对 =====
            SpawnNewRound(ingest, clock);

            int hl = 0, pr = 0;
            for (int i = 0; i < ingest.Latest.Count; i++)
            {
                if (ingest.Latest[i].IsHighlighted) hl++;
                if (ingest.Latest[i].IsPaired) pr++;
            }

            Check(hl <= 2, $"两轮之后 Latest 高亮行不超过 2（实际 {hl}）");
            Check(pr <= 1, $"两轮之后配对行不超过 1（实际 {pr}）");
            Check(ingest.HighlightGeneration >= 1, $"代次号已递增（{ingest.HighlightGeneration}）");

            // Trace 里也不该残留上一轮
            int traceHl = 0;
            for (int i = 0; i < ingest.TraceDisplay.Count; i++)
            {
                if (ingest.TraceDisplay[i].IsHighlighted) traceHl++;
            }

            Check(traceHl <= 2, $"Trace 高亮行不超过 2（实际 {traceHl}）");

            return _fail;
        }

        /// <summary>模拟再点一次发送：新一轮 Echo + 应答。</summary>
        private static void SpawnNewRound(FrameIngest ingest, IClock clock)
        {
            uint now = 500_000;
            ingest.ResetHighlightsForNewSend();
            ingest.PairTracker.Begin(new SendPairRequest(1, 0, 0x01020002u, true, "第二轮"), now);

            uint echoTs = now + 1000;
            ingest.Accept(0, Frame(0x01020002u, CanFrameKind.Echo, echoTs), paused: false);
            ingest.Accept(0, Frame(0x01020002u, CanFrameKind.Rx, echoTs + 1500, 0x11, 0x22), paused: false);
            ingest.FlushUntilIdle();
        }

        private sealed class RealClock : IClock
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
