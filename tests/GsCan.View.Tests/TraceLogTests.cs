using System.Reflection;
using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;

namespace GsCan.View.Tests
{
    public class TraceLogTests
    {
        [Fact]
        public void SaveLog_writes_trace_as_csv_with_raw_microseconds_and_kinds()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Echo, timestamp: 1000, data: new byte[] { 0x11 }));
            opened.Enqueue(0, Frame(0x123, CanFrameKind.Rx, timestamp: 1100, data: new byte[] { 0x11 }));
            session.PumpUntilIdle();

            var path = TempCsv();
            try
            {
                session.SaveLog(path);

                var lines = File.ReadAllLines(path);
                Assert.Equal(3, lines.Length);
                Assert.Equal(
                    "TimestampMicroseconds,Channel,Kind,Id,Extended,Remote,IsFd,BitRateSwitch,ErrorStateIndicator,Overflow,Length,Data",
                    lines[0]);
                Assert.Equal("1000,0,Echo,291,False,False,False,False,False,False,1,11", lines[1]);
                Assert.Equal("1100,0,Rx,291,False,False,False,False,False,False,1,11", lines[2]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SaveLog_omits_pause_dropped_frames_and_writes_trace_as_is()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, Frame(0x100, CanFrameKind.Rx, timestamp: 1000, data: new byte[] { 0x01 }));
            session.PumpUntilIdle();

            session.Paused = true;
            opened.Enqueue(0, Frame(0x200, CanFrameKind.Rx, timestamp: 2000, data: new byte[] { 0x02 }));
            session.PumpUntilIdle();

            session.Paused = false;
            opened.Enqueue(0, Frame(0x300, CanFrameKind.Echo, timestamp: 3000, data: new byte[] { 0x03 }));
            session.PumpUntilIdle();

            var path = TempCsv();
            try
            {
                session.SaveLog(path);

                var text = File.ReadAllText(path);
                Assert.Contains("1000,0,Rx,256,", text);
                Assert.Contains("3000,0,Echo,768,", text);
                Assert.DoesNotContain(",512,", text);
                Assert.DoesNotContain("2000,", text);
                Assert.Equal(1, session.PauseDroppedCount);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SaveLog_writes_flags_and_hex_data_from_the_trace_row()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(
                0,
                new CanFrame(
                    0x1ABCDEF,
                    new byte[] { 0xAA, 0xBB },
                    CanFrameKind.Rx,
                    extended: true,
                    remote: false,
                    fd: true,
                    bitRateSwitch: true,
                    errorStateIndicator: true,
                    overflow: true,
                    timestampMicroseconds: 424242));
            session.PumpUntilIdle();

            var path = TempCsv();
            try
            {
                session.SaveLog(path);

                var lines = File.ReadAllLines(path);
                Assert.Equal(2, lines.Length);
                Assert.Equal("424242,0,Rx,28036591,True,False,True,True,True,True,2,AA BB", lines[1]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ViewSession_has_no_open_log_api()
        {
            var methods = typeof(ViewSession).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            Assert.Contains(methods, method => method.Name == nameof(ViewSession.SaveLog));
            Assert.DoesNotContain(methods, method => method.Name.Contains("OpenLog", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(methods, method => method.Name.Contains("LoadLog", StringComparison.OrdinalIgnoreCase));
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

        private static CanFrame Frame(uint id, CanFrameKind kind, uint timestamp, byte[] data)
        {
            return new CanFrame(id, data, kind, timestampMicroseconds: timestamp);
        }

        private static string TempCsv()
        {
            return Path.Combine(Path.GetTempPath(), $"gscan-log-{Guid.NewGuid():N}.csv");
        }
    }
}
