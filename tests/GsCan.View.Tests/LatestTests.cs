using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class LatestTests
    {
        [Fact]
        public void Latest_keeps_Echo_and_Rx_as_separate_rows()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Echo, timestamp: 1000, data: 0x11));
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Rx, timestamp: 1100, data: 0x11));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Latest.Count);
            Assert.Equal("Echo", session.Latest[0].Kind);
            Assert.Equal("Rx", session.Latest[1].Kind);
            Assert.Equal(0x123u, session.Latest[0].Id);
            Assert.Equal(0x123u, session.Latest[1].Id);
            Assert.Equal(1, session.Latest[0].Count);
            Assert.Equal(1, session.Latest[1].Count);
        }

        [Fact]
        public void Latest_keeps_Error_and_Rx_as_separate_rows()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(
                0,
                new CanFrame(
                    0x123,
                    new byte[] { 0xEE },
                    CanFrameKind.Error,
                    overflow: true,
                    timestampMicroseconds: 1000));
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Rx, timestamp: 1100, data: 0x11));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Latest.Count);
            Assert.Equal("Error", session.Latest[0].Kind);
            Assert.Equal("Rx", session.Latest[1].Kind);
            Assert.Equal(0x123u, session.Latest[0].Id);
            Assert.Equal(0x123u, session.Latest[1].Id);
            Assert.True(session.Latest[0].Overflow);
            Assert.False(session.Latest[1].Overflow);
        }

        [Fact]
        public void Latest_keeps_same_id_on_different_channels_as_separate_rows()
        {
            var (session, opened) = OpenStarted();
            session.StartChannel(1);
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(1, Frame(0x100, CanFrameKind.Rx, timestamp: 1100, data: 0x02));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Latest.Count);
            Assert.Equal(0, session.Latest[0].Channel);
            Assert.Equal(1, session.Latest[1].Channel);
            Assert.Equal("01", session.Latest[0].DataHex);
            Assert.Equal("02", session.Latest[1].DataHex);
        }

        [Fact]
        public void Latest_classic_and_fd_same_id_are_separate_rows()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(
                0,
                new CanFrame(
                    0x100,
                    new byte[] { 0x01 },
                    CanFrameKind.Rx,
                    fd: true,
                    timestampMicroseconds: 1100));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Latest.Count);
            Assert.False(session.Latest[0].IsFd);
            Assert.True(session.Latest[1].IsFd);
        }

        [Fact]
        public void Latest_increments_count_and_takes_latest_data_and_flags()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(
                0,
                new CanFrame(
                    0x100,
                    new byte[] { 0x01 },
                    CanFrameKind.Rx,
                    fd: true,
                    timestampMicroseconds: 1000));
            opened.Enqueue(
                0,
                new CanFrame(
                    0x100,
                    new byte[] { 0xAA, 0xBB },
                    CanFrameKind.Rx,
                    fd: true,
                    bitRateSwitch: true,
                    errorStateIndicator: true,
                    overflow: true,
                    timestampMicroseconds: 2500));

            session.PumpUntilIdle();

            Assert.Single(session.Latest);
            var row = session.Latest[0];
            Assert.Equal("Rx", row.Kind);
            Assert.Equal(0x100u, row.Id);
            Assert.Equal(2, row.Count);
            Assert.Equal("AA BB", row.DataHex);
            Assert.Equal(2, row.Length);
            Assert.True(row.IsFd);
            Assert.True(row.BitRateSwitch);
            Assert.True(row.ErrorStateIndicator);
            Assert.True(row.Overflow);
            Assert.Equal(1.5, row.RelativeMilliseconds);
            Assert.Equal(2500u, row.TimestampMicroseconds);
        }

        [Fact]
        public void ReceiveMode_defaults_to_Trace_and_switching_does_not_drop_rows()
        {
            var (session, opened) = OpenStarted();
            Assert.Equal(ReceiveMode.Trace, session.ReceiveMode);
            Assert.True(session.IsTraceMode);
            Assert.False(session.IsLatestMode);

            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 2000, data: 0x02));
            session.PumpUntilIdle();

            session.ReceiveMode = ReceiveMode.Latest;

            Assert.Equal(ReceiveMode.Latest, session.ReceiveMode);
            Assert.False(session.IsTraceMode);
            Assert.True(session.IsLatestMode);
            Assert.Equal(2, session.Trace.Count);
            Assert.Equal(2, session.Latest.Count);

            session.ReceiveMode = ReceiveMode.Trace;
            Assert.Equal(2, session.Latest.Count);
            Assert.Equal(2, session.Trace.Count);
        }

        [Fact]
        public void Pause_does_not_update_Latest()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            session.PumpUntilIdle();
            Assert.Single(session.Latest);
            Assert.Equal(1, session.Latest[0].Count);

            session.Paused = true;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 2000, data: 0x02));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 3000, data: 0x03));
            session.PumpUntilIdle();

            Assert.Single(session.Latest);
            Assert.Equal(0x100u, session.Latest[0].Id);
            Assert.Equal(1, session.Latest[0].Count);
            Assert.Equal("01", session.Latest[0].DataHex);
            Assert.Equal(2, session.PauseDroppedCount);
            Assert.Single(session.Trace);
        }

        [Fact]
        public void Clear_clears_Latest_and_Trace()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            session.PumpUntilIdle();

            session.Clear();

            Assert.Empty(session.Latest);
            Assert.Empty(session.Trace);
            Assert.True(session.Channels[0].IsRunning);
        }

        private static (ViewSession Session, FakeOpenedDevice Opened) OpenStarted()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);
            session.StartChannel(0);
            return (session, port.LastOpenedDevice!);
        }

        private static CanFrame Frame(uint id, CanFrameKind kind, uint timestamp, byte data)
        {
            return new CanFrame(id, new[] { data }, kind, timestampMicroseconds: timestamp);
        }
    }
}
