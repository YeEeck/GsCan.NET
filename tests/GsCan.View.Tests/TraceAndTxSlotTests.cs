using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class TraceAndTxSlotTests
    {
        [Fact]
        public void TxSlots_starts_with_one_oneshot_row()
        {
            var session = new ViewSession(new FakeGsCanPort());
            var slot = Assert.Single(session.TxSlots);
            Assert.Equal(0, slot.PeriodMs);
            Assert.Equal(TxSlot.DefaultLength, slot.Length);
            Assert.Equal(new byte[TxSlot.DefaultLength], slot.Data);
            Assert.True(session.CanAddTxSlot);
            Assert.False(session.CanRemoveTxSlot);
            Assert.False(slot.CanRemove);
        }

        [Fact]
        public void AddTxSlot_appends_until_max_and_Remove_keeps_one()
        {
            var session = new ViewSession(new FakeGsCanPort());

            for (int i = 1; i < TxSlot.MaxSlotCount; i++)
            {
                session.AddTxSlot();
            }

            Assert.Equal(TxSlot.MaxSlotCount, session.TxSlots.Count);
            Assert.False(session.CanAddTxSlot);
            Assert.True(session.CanRemoveTxSlot);
            session.AddTxSlot();
            Assert.Equal(TxSlot.MaxSlotCount, session.TxSlots.Count);

            session.RemoveTxSlot(session.TxSlots[3]);
            Assert.Equal(TxSlot.MaxSlotCount - 1, session.TxSlots.Count);
            for (int i = 0; i < session.TxSlots.Count; i++)
            {
                Assert.Equal(i, session.TxSlots[i].Index);
            }

            while (session.TxSlots.Count > 1)
            {
                session.RemoveTxSlot(session.TxSlots[0]);
            }

            Assert.Single(session.TxSlots);
            Assert.False(session.CanRemoveTxSlot);
            session.RemoveTxSlot(session.TxSlots[0]);
            Assert.Single(session.TxSlots);
        }

        [Fact]
        public void TxSlot_clears_BRS_when_FD_is_unchecked()
        {
            var slot = new ViewSession(new FakeGsCanPort()).TxSlots[0];
            slot.Fd = true;
            slot.BitRateSwitch = true;

            slot.Fd = false;

            Assert.False(slot.BitRateSwitch);
            slot.BitRateSwitch = true;
            Assert.False(slot.BitRateSwitch);
        }

        [Fact]
        public void TxSlot_Length_pads_and_truncates_data()
        {
            var slot = new ViewSession(new FakeGsCanPort()).TxSlots[0];
            slot.DataHex = "AA BB";

            Assert.Equal(8, slot.Length);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0, 0, 0, 0, 0, 0 }, slot.Data);

            slot.Length = 2;
            Assert.Equal(new byte[] { 0xAA, 0xBB }, slot.Data);
            Assert.Equal("AA BB", slot.DataHex);

            slot.Length = 4;
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0, 0 }, slot.Data);
        }

        [Fact]
        public void TxSlot_DataHex_grows_DLC_and_does_not_shrink_it()
        {
            var slot = new ViewSession(new FakeGsCanPort()).TxSlots[0];
            slot.Length = 2;
            slot.DataHex = "11 22 33";
            Assert.Equal(3, slot.Length);
            Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, slot.Data);

            slot.Fd = true;
            slot.DataHex = "11 22 33 44 55 66 77 88 99";
            Assert.Equal(12, slot.Length);
            Assert.Equal(12, slot.Data.Length);
            Assert.Equal(0x99, slot.Data[8]);

            slot.DataHex = "AA";
            Assert.Equal(12, slot.Length);
            Assert.Equal(0xAA, slot.Data[0]);
            Assert.Equal(0, slot.Data[1]);
        }

        [Fact]
        public void TxSlot_invalid_DataHex_does_not_change_data()
        {
            var slot = new ViewSession(new FakeGsCanPort()).TxSlots[0];
            slot.Length = 2;
            slot.DataHex = "AA BB";
            slot.DataHex = "A";
            Assert.Equal(new byte[] { 0xAA, 0xBB }, slot.Data);
            slot.DataHex = "GG";
            Assert.Equal(new byte[] { 0xAA, 0xBB }, slot.Data);
        }

        [Fact]
        public void TxSlot_unchecking_FD_clamps_length_to_8()
        {
            var slot = new ViewSession(new FakeGsCanPort()).TxSlots[0];
            slot.Fd = true;
            slot.Length = 64;
            Assert.Equal(64, slot.Length);
            Assert.Equal(64, slot.Data.Length);

            slot.Fd = false;

            Assert.Equal(8, slot.Length);
            Assert.Equal(8, slot.Data.Length);
            Assert.Same(TxSlot.ClassicLengths, slot.LengthChoices);
        }

        [Fact]
        public void SendOnce_uses_DLC_length()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            var slot = session.TxSlots[0];
            slot.Channel = 0;
            slot.Id = 0x123;
            slot.Length = 4;
            slot.DataHex = "AA BB";

            session.SendOnce(0);

            Assert.Single(opened.Sent);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0, 0 }, opened.Sent[0].Frame.Data);
        }

        [Fact]
        public void PumpUntilIdle_appends_injected_frames_in_arrival_order()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 2000, data: 0x02));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Trace.Count);
            Assert.Equal(0x100u, session.Trace[0].Id);
            Assert.Equal(0x200u, session.Trace[1].Id);
            Assert.Equal(0, session.Trace[0].Channel);
            Assert.Equal(0, session.Trace[1].Channel);
        }

        [Fact]
        public void PumpUntilIdle_maps_Echo_and_Rx_kind_as_english_tokens()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Echo, timestamp: 1000, data: 0x11));
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Rx, timestamp: 1100, data: 0x11));

            session.PumpUntilIdle();

            Assert.Equal("Echo", session.Trace[0].Kind);
            Assert.Equal("Rx", session.Trace[1].Kind);
            Assert.Equal("11", session.Trace[0].DataHex);
            Assert.Equal("11", session.Trace[1].DataHex);
            Assert.DoesNotContain("成功", session.Trace[0].Kind);
            Assert.DoesNotContain("总线", session.Trace[0].Kind);
        }

        [Fact]
        public void Pause_drops_frames_from_Trace_but_pump_still_reads()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            session.Paused = true;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 2000, data: 0x02));

            session.PumpUntilIdle();

            Assert.Empty(session.Trace);
            Assert.Equal(2, session.PauseDroppedCount);
            Assert.True(session.Channels[0].IsRunning);
            Assert.False(opened.TryRead(0, 0, out _));

            session.Paused = false;
            opened.Enqueue(0, Frame(0x300, CanFrameKind.Rx, timestamp: 3000, data: 0x03));
            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal(0x300u, session.Trace[0].Id);
            Assert.Equal(2, session.PauseDroppedCount);

            session.Clear();
            Assert.Empty(session.Trace);
            Assert.True(session.Channels[0].IsRunning);
            Assert.Equal(2, session.PauseDroppedCount);
        }

        [Fact]
        public void AppendMany_under_capacity_adds_without_resetting_the_list()
        {
            var rows = new TraceRows();
            var buffer = new TraceBuffer(rows);
            for (uint i = 0; i < 5_000; i++)
            {
                buffer.Append(Row(i));
            }

            int resets = 0;
            rows.CollectionChanged += (_, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    resets++;
                }
            };

            var sw = Stopwatch.StartNew();
            for (uint i = 0; i < 200; i++)
            {
                buffer.AppendMany(new[] { Row(5_000 + i) });
            }

            sw.Stop();
            Assert.Equal(0, resets);
            Assert.Equal(5_200, rows.Count);
            Assert.Equal(0u, rows[0].Id);
            Assert.Equal(5_199u, rows[5_199].Id);
            Assert.True(sw.ElapsedMilliseconds < 500, "append took " + sw.ElapsedMilliseconds + "ms");
        }

        [Fact]
        public void TraceDisplay_keeps_only_the_newest_window()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            int total = TraceBuffer.DisplayCapacity + 500;
            for (uint i = 0; i < total; i++)
            {
                opened.Enqueue(0, new CanFrame(i, Array.Empty<byte>(), CanFrameKind.Rx, timestampMicroseconds: i));
            }

            session.PumpUntilIdle();

            Assert.Equal(total, session.Trace.Count);
            Assert.Equal(TraceBuffer.DisplayCapacity, session.TraceDisplay.Count);
            Assert.Equal((uint)(total - TraceBuffer.DisplayCapacity), session.TraceDisplay[0].Id);
            Assert.Equal((uint)(total - 1), session.TraceDisplay[session.TraceDisplay.Count - 1].Id);
        }

        [Fact]
        public void AppendMany_at_capacity_overwrites_oldest_without_copying_the_buffer()
        {
            var rows = new TraceRows();
            var buffer = new TraceBuffer(rows);
            for (uint i = 0; i < TraceBuffer.Capacity; i++)
            {
                buffer.Append(Row(i));
            }

            var sw = Stopwatch.StartNew();
            for (uint i = 0; i < 500; i++)
            {
                buffer.AppendMany(new[] { Row((uint)TraceBuffer.Capacity + i) });
            }

            sw.Stop();
            Assert.Equal(TraceBuffer.Capacity, rows.Count);
            Assert.Equal(500u, rows[0].Id);
            Assert.Equal((uint)(TraceBuffer.Capacity + 499), rows[TraceBuffer.Capacity - 1].Id);
            Assert.True(sw.ElapsedMilliseconds < 250, "overwrite took " + sw.ElapsedMilliseconds + "ms");
        }

        [Fact]
        public void Trace_drops_oldest_row_when_over_capacity()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            for (uint i = 0; i < 100_001; i++)
            {
                opened.Enqueue(0, new CanFrame(i, Array.Empty<byte>(), CanFrameKind.Rx, timestampMicroseconds: i));
            }

            session.PumpUntilIdle();

            Assert.Equal(100_000, session.Trace.Count);
            Assert.Equal(1u, session.Trace[0].Id);
            Assert.Equal(100_000u, session.Trace[99_999].Id);
        }

        [Fact]
        public void Pending_and_trace_stay_capped_when_ui_flush_lags()
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new QueuingSynchronizationContext());
            try
            {
                var info = new DeviceInfo(@"\\?\usb#a", 2);
                var port = new FakeGsCanPort();
                var session = new ViewSession(port, runBackgroundPumps: false, marshalToUi: true);
                session.Open(info);
                session.StartChannel(0);
                var opened = port.LastOpenedDevice!;

                const int burst = 250_000;
                for (uint i = 0; i < burst; i++)
                {
                    opened.Enqueue(0, new CanFrame(i, Array.Empty<byte>(), CanFrameKind.Rx, timestampMicroseconds: i));
                }

                session.DrainRunningChannels();
                Assert.Equal(TraceBuffer.Capacity, session.PendingCount);
                Assert.Empty(session.Trace);

                var sw = Stopwatch.StartNew();
                session.PumpUntilIdle();
                sw.Stop();

                Assert.True(
                    sw.ElapsedMilliseconds < 2000,
                    "flush took " + sw.ElapsedMilliseconds + "ms");
                Assert.Equal(0, session.PendingCount);
                Assert.Equal(TraceBuffer.Capacity, session.Trace.Count);
                Assert.Equal((uint)(burst - TraceBuffer.Capacity), session.Trace[0].Id);
                Assert.Equal((uint)(burst - 1), session.Trace[TraceBuffer.Capacity - 1].Id);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Fact]
        public void Clear_drops_pending_rows_that_have_not_flushed()
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new QueuingSynchronizationContext());
            try
            {
                var info = new DeviceInfo(@"\\?\usb#a", 2);
                var port = new FakeGsCanPort();
                var session = new ViewSession(port, runBackgroundPumps: false, marshalToUi: true);
                session.Open(info);
                session.StartChannel(0);
                var opened = port.LastOpenedDevice!;
                opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
                opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 2000, data: 0x02));

                session.DrainRunningChannels();
                Assert.Equal(2, session.PendingCount);
                Assert.Empty(session.Trace);

                session.Clear();
                session.PumpUntilIdle();

                Assert.Equal(0, session.PendingCount);
                Assert.Empty(session.Trace);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [Fact]
        public void Relative_milliseconds_are_from_first_frame_after_this_Start()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 3500, data: 0x02));

            session.PumpUntilIdle();

            Assert.Equal(0.0, session.Trace[0].RelativeMilliseconds);
            Assert.Equal(2.5, session.Trace[1].RelativeMilliseconds);
            Assert.Equal(1000u, session.Trace[0].TimestampMicroseconds);
            Assert.Equal(3500u, session.Trace[1].TimestampMicroseconds);

            session.StopChannel(0);
            session.StartChannel(0);
            opened.Enqueue(0, Frame(0x300, CanFrameKind.Rx, timestamp: 9000, data: 0x03));
            session.PumpUntilIdle();

            Assert.Equal(3, session.Trace.Count);
            Assert.Equal(0.0, session.Trace[2].RelativeMilliseconds);
            Assert.Equal(0x300u, session.Trace[2].Id);
        }

        [Fact]
        public void Overflow_is_flagged_on_the_row_and_Error_payload_is_hex()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            opened.Enqueue(
                0,
                new CanFrame(
                    0x000,
                    new byte[] { 0xAB, 0xCD },
                    CanFrameKind.Error,
                    overflow: true,
                    timestampMicroseconds: 1000));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal("Error", session.Trace[0].Kind);
            Assert.True(session.Trace[0].Overflow);
            Assert.Equal("AB CD", session.Trace[0].DataHex);
            Assert.Equal(2, session.Trace[0].Length);
        }

        [Fact]
        public void SendOnce_on_listen_only_channel_does_not_send()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false, listenOnly: true);
            session.TxSlots[0].Channel = 0;
            session.TxSlots[0].Id = 0x123;
            session.TxSlots[0].Data = new byte[] { 0x11 };

            session.SendOnce(0);

            Assert.Empty(opened.Sent);
            Assert.Equal("只听通道不能发送。", session.LastError);
        }

        [Fact]
        public void SendOnce_records_oneshot_frame_on_the_opened_device()
        {
            var (session, opened) = OpenStarted(runBackgroundPumps: false);
            var slot = session.TxSlots[0];
            slot.Channel = 0;
            slot.Id = 0x123;
            slot.Extended = true;
            slot.Remote = false;
            slot.Fd = true;
            slot.BitRateSwitch = true;
            slot.PeriodMs = 0;
            slot.Data = new byte[] { 0xAA, 0xBB };

            session.SendOnce(0);

            Assert.Single(opened.Sent);
            Assert.Equal(0, opened.Sent[0].Channel);
            var sent = opened.Sent[0].Frame;
            Assert.Equal(0x123u, sent.Id);
            Assert.True(sent.Extended);
            Assert.False(sent.Remote);
            Assert.True(sent.IsFd);
            Assert.True(sent.BitRateSwitch);
            Assert.Equal(new byte[] { 0xAA, 0xBB }, sent.Data);

            opened.Enqueue(0, new CanFrame(0x123, new byte[] { 0xAA, 0xBB }, CanFrameKind.Echo, extended: true, fd: true, bitRateSwitch: true, timestampMicroseconds: 1000));
            opened.Enqueue(0, new CanFrame(0x123, new byte[] { 0xAA, 0xBB }, CanFrameKind.Rx, extended: true, fd: true, bitRateSwitch: true, timestampMicroseconds: 1100));
            session.PumpUntilIdle();

            Assert.Equal(2, session.Trace.Count);
            Assert.Equal("Echo", session.Trace[0].Kind);
            Assert.Equal("Rx", session.Trace[1].Kind);
        }

        [Fact]
        public void SendOnce_on_a_stopped_channel_does_not_send()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);

            session.SendOnce(0);

            Assert.Empty(port.LastOpenedDevice!.Sent);
        }

        private static (ViewSession Session, FakeOpenedDevice Opened) OpenStarted(
            bool runBackgroundPumps,
            bool listenOnly = false,
            bool loopback = false)
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps);
            session.Open(info);
            session.Channels[0].ListenOnly = listenOnly;
            session.Channels[0].Loopback = loopback;
            session.StartChannel(0);
            return (session, port.LastOpenedDevice!);
        }

        private sealed class QueuingSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
            }

            public override void Send(SendOrPostCallback d, object? state)
            {
                d(state);
            }
        }

        private static FrameRow Row(uint id)
        {
            return new FrameRow(0, 0, "Rx", id, false, false, false, false, false, false, 0, string.Empty, 0);
        }

        private static CanFrame Frame(
            uint id,
            CanFrameKind kind,
            uint timestamp,
            byte data,
            bool overflow = false)
        {
            return new CanFrame(
                id,
                new[] { data },
                kind,
                overflow: overflow,
                timestampMicroseconds: timestamp);
        }
    }
}
