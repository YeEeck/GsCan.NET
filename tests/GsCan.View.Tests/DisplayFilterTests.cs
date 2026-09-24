using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class DisplayFilterTests
    {
        [Fact]
        public void Turning_off_Kind_Echo_hides_Echo_but_keeps_Rx()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowEcho = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Echo, timestamp: 1000));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal("Rx", session.Trace[0].Kind);
            Assert.Equal(0x100u, session.Trace[0].Id);
        }

        [Fact]
        public void Restricting_to_one_Channel_hides_the_other_Channel()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowChannel1 = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000));
            opened.Enqueue(1, Frame(0x200, CanFrameKind.Rx, timestamp: 1100));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal(0, session.Trace[0].Channel);
            Assert.Equal(0x100u, session.Trace[0].Id);
        }

        [Fact]
        public void Single_hex_ID_shows_only_that_ID()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.IdText = "100";
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000));
            opened.Enqueue(0, Frame(0x101, CanFrameKind.Rx, timestamp: 1100));
            opened.Enqueue(0, Frame(0x1FF, CanFrameKind.Rx, timestamp: 1200));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal(0x100u, session.Trace[0].Id);
            Assert.Null(session.DisplayFilter.FilterError);
        }

        [Fact]
        public void Closed_hex_ID_range_is_inclusive()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.IdText = "100-1FF";
            opened.Enqueue(0, Frame(0x0FF, CanFrameKind.Rx, timestamp: 1000));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100));
            opened.Enqueue(0, Frame(0x1FF, CanFrameKind.Rx, timestamp: 1200));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 1300));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Trace.Count);
            Assert.Equal(0x100u, session.Trace[0].Id);
            Assert.Equal(0x1FFu, session.Trace[1].Id);
            Assert.Null(session.DisplayFilter.FilterError);
        }

        [Fact]
        public void Illegal_ID_text_is_explained_and_does_not_throw()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.IdText = "100-1FF";
            session.DisplayFilter.IdText = "not-hex";
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 1100));

            session.PumpUntilIdle();

            Assert.False(string.IsNullOrWhiteSpace(session.DisplayFilter.FilterError));
            Assert.Contains("100-1FF", session.DisplayFilter.FilterError);
            Assert.Single(session.Trace);
            Assert.Equal(0x100u, session.Trace[0].Id);
        }

        [Fact]
        public void Hidden_frames_are_still_drained_from_the_pump()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowEcho = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Echo, timestamp: 1000));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Echo, timestamp: 1100));

            session.PumpUntilIdle();

            Assert.Empty(session.Trace);
            Assert.Equal(0, session.PauseDroppedCount);
            Assert.False(opened.TryRead(0, 0, out _));
        }

        [Fact]
        public void Turning_off_Kind_Rx_hides_Rx_but_keeps_Echo()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowRx = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Echo, timestamp: 1000));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal("Echo", session.Trace[0].Kind);
        }

        [Fact]
        public void Turning_off_standard_hides_11bit_and_keeps_extended()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowStandard = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, extended: false));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100, extended: true));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.True(session.Trace[0].Extended);
        }

        [Fact]
        public void Turning_off_classic_hides_classic_and_keeps_FD()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowClassic = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, fd: false));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100, fd: true));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.True(session.Trace[0].IsFd);
        }

        [Fact]
        public void Turning_off_Remote_hides_remote_frames()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowRemote = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, remote: false));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100, remote: true));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.False(session.Trace[0].Remote);
        }

        [Fact]
        public void Turning_off_Kind_Error_hides_Error_frames()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowError = false;
            opened.Enqueue(0, Frame(0x000, CanFrameKind.Error, timestamp: 1000));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal("Rx", session.Trace[0].Kind);
        }

        [Fact]
        public void Turning_off_data_keeps_only_Remote_frames()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowData = false;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, remote: false));
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1100, remote: true));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.True(session.Trace[0].Remote);
        }

        [Fact]
        public void Empty_ID_text_clears_the_ID_constraint()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.IdText = "100";
            session.DisplayFilter.IdText = "";
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 1000));

            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal(0x200u, session.Trace[0].Id);
            Assert.Null(session.DisplayFilter.FilterError);
        }

        private static (ViewSession Session, FakeOpenedDevice Opened) OpenStarted()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);
            session.StartChannel(0);
            session.StartChannel(1);
            return (session, port.LastOpenedDevice!);
        }

        private static CanFrame Frame(
            uint id,
            CanFrameKind kind,
            uint timestamp,
            bool extended = false,
            bool remote = false,
            bool fd = false)
        {
            return new CanFrame(
                id,
                new byte[] { 0x01 },
                kind,
                extended: extended,
                remote: remote,
                fd: fd,
                timestampMicroseconds: timestamp);
        }
    }
}
