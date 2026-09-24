using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class StatusAndUnplugTests
    {
        [Fact]
        public void Injected_frames_count_Rx_Echo_Error_and_Overflow()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Echo, timestamp: 1100, data: 0x02));
            opened.Enqueue(
                0,
                new CanFrame(
                    0x000,
                    new byte[] { 0xAB },
                    CanFrameKind.Error,
                    overflow: true,
                    timestampMicroseconds: 1200));
            opened.Enqueue(0, Frame(0x300, CanFrameKind.Rx, timestamp: 1300, data: 0x03, overflow: true));

            session.PumpUntilIdle();

            Assert.Equal(2, session.Status.RxCount);
            Assert.Equal(1, session.Status.TxCount);
            Assert.Equal(1, session.Status.ErrorCount);
            Assert.Equal(2, session.Status.OverflowCount);
            Assert.Equal(4, session.Trace.Count);

            session.Clear();
            Assert.Empty(session.Trace);
            Assert.Equal(2, session.Status.RxCount);
            Assert.Equal(1, session.Status.TxCount);
            Assert.Equal(1, session.Status.ErrorCount);
            Assert.Equal(2, session.Status.OverflowCount);
        }

        [Fact]
        public void Pause_still_counts_pumped_frames_and_records_drops()
        {
            var (session, opened) = OpenStarted();
            session.Paused = true;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Echo, timestamp: 1100, data: 0x02));
            opened.Enqueue(
                0,
                new CanFrame(
                    0x000,
                    new byte[] { 0xEE },
                    CanFrameKind.Error,
                    overflow: true,
                    timestampMicroseconds: 1200));

            session.PumpUntilIdle();

            Assert.Empty(session.Trace);
            Assert.Equal(3, session.PauseDroppedCount);
            Assert.Equal(1, session.Status.RxCount);
            Assert.Equal(1, session.Status.TxCount);
            Assert.Equal(1, session.Status.ErrorCount);
            Assert.Equal(1, session.Status.OverflowCount);
            Assert.True(session.Channels[0].IsRunning);
        }

        [Fact]
        public void Tx_count_comes_from_Echo_not_from_SendOnce()
        {
            var (session, opened) = OpenStarted();
            session.TxSlots[0].Channel = 0;
            session.TxSlots[0].Id = 0x123;
            session.TxSlots[0].Data = new byte[] { 0x11 };

            session.SendOnce(0);

            Assert.Single(opened.Sent);
            Assert.Equal(0, session.Status.TxCount);
            Assert.Empty(session.Trace);

            opened.Enqueue(0, Frame(0x123, CanFrameKind.Echo, timestamp: 1000, data: 0x11));
            session.PumpUntilIdle();

            Assert.Equal(1, session.Status.TxCount);
            Assert.Equal("Echo", session.Trace[0].Kind);
            Assert.DoesNotContain("成功", session.Trace[0].Kind);
            Assert.DoesNotContain("总线", session.Trace[0].Kind);
        }

        [Fact]
        public void Pump_GsCanException_closes_device_keeps_Trace_and_does_not_reopen()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);
            session.StartChannel(0);
            var opened = port.LastOpenedDevice!;
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: 0x01));
            session.PumpUntilIdle();
            Assert.Single(session.Trace);
            Assert.Equal(1, session.Status.RxCount);

            opened.TryReadException = new GsCanException("The device was disconnected.");
            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal(0x100u, session.Trace[0].Id);
            Assert.Equal(1, session.Status.RxCount);
            Assert.Null(session.OpenedPath);
            Assert.Empty(session.Channels);
            Assert.Equal("The device was disconnected.", session.LastError);
            Assert.Equal(1, port.OpenCallCount);
            Assert.Equal(1, port.DisposeCallCount);
            Assert.Equal(1, opened.StartCallCount);

            session.Open(info);

            Assert.Equal(2, port.OpenCallCount);
            Assert.Equal(@"\\?\usb#a", session.OpenedPath);
            Assert.DoesNotContain(session.Channels, channel => channel.IsRunning);
            Assert.Single(session.Trace);
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
