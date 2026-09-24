using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class TraceAndTxSlotTests
    {
        [Fact]
        public void TxSlots_has_sixteen_oneshot_rows()
        {
            var session = new ViewSession(new FakeGsCanPort());
            Assert.Equal(16, session.TxSlots.Count);
            Assert.All(session.TxSlots, slot => Assert.Equal(0, slot.PeriodMs));
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
            Assert.Equal("Cannot send on a listen-only channel.", session.LastError);
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
