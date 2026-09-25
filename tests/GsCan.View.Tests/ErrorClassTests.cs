using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;

namespace GsCan.View.Tests
{
    public class ErrorClassTests
    {
        private const uint ErrCrtl = 0x00000004;
        private const uint ErrProt = 0x00000008;
        private const uint ErrAck = 0x00000020;
        private const uint ErrBusOff = 0x00000040;
        private const uint ErrBusError = 0x00000080;
        private const uint ErrRestarted = 0x00000100;
        private const uint ErrCnt = 0x00000200;

        [Fact]
        public void Ack_error_shows_ErrorClass_and_hint_on_the_trace_row()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, ErrorFrame(ErrAck | ErrProt | ErrBusError | ErrCnt, Payload(tec: 5, rec: 0)));
            session.PumpUntilIdle();

            Assert.Single(session.Trace);
            Assert.Equal("Error", session.Trace[0].Kind);
            Assert.Equal("ACK", session.Trace[0].ErrorClass);
            Assert.Equal("ACK", session.TraceDisplay[0].IdHex);
            Assert.Contains("未收到应答", session.Trace[0].ErrorHint);
            Assert.Contains("TEC=5 REC=0", session.Trace[0].ErrorHint);
            Assert.Equal("00 00 00 00 00 00 05 00", session.Trace[0].DataHex);
            Assert.Equal("2A8", session.Trace[0].IdHex);
            Assert.Equal("CH0 ACK：未收到应答。常见原因：没有第二节点、波特率不一致、或对端只听。 TEC=5 REC=0", session.LastError);
        }

        [Theory]
        [InlineData(0x00000008u, 0x00, 0x08, 0x00, "Bit0")]
        [InlineData(0x00000008u, 0x00, 0x10, 0x00, "Bit1")]
        [InlineData(0x00000008u, 0x00, 0x00, 0x08, "CRC")]
        [InlineData(0x00000004u, 0x10, 0x00, 0x00, "Error-passive")]
        [InlineData(0x00000004u, 0x40, 0x00, 0x00, "Error-active")]
        [InlineData(0x00000100u, 0x00, 0x00, 0x00, "Restarted")]
        [InlineData(0x00000080u, 0x00, 0x00, 0x00, "Bus error")]
        [InlineData(0x00000000u, 0x00, 0x00, 0x00, "Unknown")]
        public void Remaining_classes_match_the_locked_catalog(
            uint id,
            byte ctrl,
            byte prot,
            byte location,
            string expected)
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, ErrorFrame(id, Payload(ctrl: ctrl, prot: prot, location: location)));
            session.PumpUntilIdle();

            Assert.Equal(expected, session.Trace[0].ErrorClass);
            Assert.Equal(expected, session.TraceDisplay[0].IdHex);
        }

        [Theory]
        [InlineData(0x08, "CRC")]
        [InlineData(0x0A, "Bus error")]
        [InlineData(0x0B, "Bus error")]
        [InlineData(0x19, "Bus error")]
        public void Protocol_location_is_an_enum_not_a_crc_bitfield(byte location, string expected)
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, ErrorFrame(ErrProt | ErrBusError, Payload(location: location)));
            session.PumpUntilIdle();

            Assert.Equal(expected, session.Trace[0].ErrorClass);
        }

        [Fact]
        public void Bus_off_wins_over_ack_on_the_same_frame()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, ErrorFrame(ErrBusOff | ErrAck, Payload()));
            session.PumpUntilIdle();

            Assert.Equal("Bus-off", session.Trace[0].ErrorClass);
            Assert.Contains("暂时不能上总线", session.Trace[0].ErrorHint);
            Assert.DoesNotContain("Stop", session.Trace[0].ErrorHint);
            Assert.DoesNotContain("Start", session.Trace[0].ErrorHint);
        }

        [Fact]
        public void Latest_splits_Stuff_and_Form_and_merges_Ack_variants()
        {
            var (session, opened) = OpenStarted();
            uint lecId = ErrProt | ErrBusError | ErrCnt;
            opened.Enqueue(0, ErrorFrame(lecId, Payload(prot: 0x04, tec: 1)));
            opened.Enqueue(0, ErrorFrame(lecId, Payload(prot: 0x02, tec: 2)));
            opened.Enqueue(0, ErrorFrame(ErrAck, Payload()));
            opened.Enqueue(0, ErrorFrame(ErrAck | lecId, Payload(tec: 8, rec: 1)));
            session.PumpUntilIdle();

            Assert.Equal(3, session.Latest.Count);
            Assert.Equal("Stuff", session.Latest[0].ErrorClass);
            Assert.Equal("Stuff", session.Latest[0].IdHex);
            Assert.Equal(1, session.Latest[0].Count);
            Assert.Equal("Form", session.Latest[1].ErrorClass);
            Assert.Equal(1, session.Latest[1].Count);
            Assert.Equal("ACK", session.Latest[2].ErrorClass);
            Assert.Equal(2, session.Latest[2].Count);
            Assert.Contains("TEC=8 REC=1", session.Latest[2].ErrorHint);
        }

        [Fact]
        public void Display_filter_shows_Error_despite_FD_and_ID_constraints()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowClassic = false;
            session.DisplayFilter.IdText = "100-1FF";
            opened.Enqueue(0, ErrorFrame(ErrBusOff, Payload()));
            opened.Enqueue(0, new CanFrame(0x100, new byte[] { 0x01 }, CanFrameKind.Rx, fd: true, timestampMicroseconds: 1100));
            session.PumpUntilIdle();

            Assert.Equal(2, session.Trace.Count);
            Assert.Equal("Error", session.Trace[0].Kind);
            Assert.Equal("Bus-off", session.Trace[0].ErrorClass);
            Assert.Equal("Rx", session.Trace[1].Kind);
        }

        [Fact]
        public void Display_filter_still_hides_Error_by_Kind_or_Channel()
        {
            var (session, opened) = OpenStarted();
            session.DisplayFilter.ShowError = false;
            opened.Enqueue(0, ErrorFrame(ErrAck, Payload()));
            session.PumpUntilIdle();
            Assert.Empty(session.Trace);
            Assert.Contains("ACK", session.LastError);

            session.Clear();
            session.DisplayFilter.ShowError = true;
            session.DisplayFilter.ShowChannel1 = false;
            opened.Enqueue(1, ErrorFrame(ErrBusOff, Payload()));
            session.PumpUntilIdle();
            Assert.Empty(session.Trace);
            Assert.Contains("CH1 Bus-off", session.LastError);
        }

        [Fact]
        public void Warning_does_not_stop_cyclic_send_but_ack_does()
        {
            var (session, opened, clock) = OpenStartedWithClock();
            var slot = session.TxSlots[0];
            slot.Channel = 0;
            slot.Id = 0x100;
            slot.PeriodMs = 10;
            slot.Enabled = true;
            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Single(opened.Sent);

            opened.Enqueue(0, ErrorFrame(ErrCrtl | ErrCnt, Payload(ctrl: 0x08, tec: 96)));
            session.PumpUntilIdle();
            Assert.True(session.TxSlots[0].Enabled);
            Assert.Equal("Error-warning", session.Trace[0].ErrorClass);
            Assert.DoesNotContain("已停止该路周期发送", session.LastError);

            opened.Enqueue(0, ErrorFrame(ErrAck, Payload()));
            session.PumpUntilIdle();
            Assert.False(session.TxSlots[0].Enabled);
            Assert.Contains("已停止该路周期发送", session.LastError);
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(opened.Sent);
        }

        [Fact]
        public void Pause_still_updates_LastError_and_does_not_append_the_row()
        {
            var (session, opened) = OpenStarted();
            session.Paused = true;
            opened.Enqueue(0, ErrorFrame(ErrAck, Payload()));
            session.PumpUntilIdle();

            Assert.Empty(session.Trace);
            Assert.Empty(session.Latest);
            Assert.Equal(1, session.PauseDroppedCount);
            Assert.Contains("ACK", session.LastError);
        }

        [Fact]
        public void SaveLog_still_writes_numeric_Id_for_Error()
        {
            var (session, opened) = OpenStarted();
            opened.Enqueue(0, ErrorFrame(ErrBusOff, new byte[] { 0x00 }));
            session.PumpUntilIdle();

            var path = Path.Combine(Path.GetTempPath(), $"gscan-error-{Guid.NewGuid():N}.csv");
            try
            {
                session.SaveLog(path);
                var lines = File.ReadAllLines(path);
                Assert.Equal(2, lines.Length);
                Assert.StartsWith("1000,0,Error,040,", lines[1]);
                Assert.DoesNotContain("Bus-off", lines[1]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static (ViewSession Session, FakeOpenedDevice Opened, FakeClock Clock) OpenStartedWithClock()
        {
            var clock = new FakeClock();
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false, clock: clock);
            session.Open(info);
            session.StartChannel(0);
            return (session, port.LastOpenedDevice!, clock);
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

        private static CanFrame ErrorFrame(uint id, byte[] data)
        {
            return new CanFrame(id, data, CanFrameKind.Error, timestampMicroseconds: 1000);
        }

        private static byte[] Payload(
            byte ctrl = 0,
            byte prot = 0,
            byte location = 0,
            byte tec = 0,
            byte rec = 0)
        {
            return new byte[] { 0, ctrl, prot, location, 0, 0, tec, rec };
        }
    }
}
