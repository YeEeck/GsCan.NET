using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class RememberConfigTests
    {
        [Fact]
        public void New_session_restores_form_without_opening_or_starting()
        {
            var store = new FakeConfigStore();
            var info = new DeviceInfo(@"\\?\usb#remembered", 2);
            var portA = new FakeGsCanPort();
            var sessionA = new ViewSession(portA, store);
            sessionA.Open(info);
            sessionA.Channels[0].Bitrate = 500_000;
            sessionA.Channels[0].FdEnabled = true;
            sessionA.Channels[0].ListenOnly = true;
            sessionA.Channels[0].Loopback = true;
            sessionA.Channels[0].OneShot = true;
            sessionA.Channels[1].Bitrate = 250_000;
            sessionA.DisplayFilter.ShowEcho = false;
            sessionA.TxSlots[0].Id = 0x123;
            sessionA.TxSlots[0].PeriodMs = 10;
            sessionA.AddTxSlot();
            sessionA.TxSlots[1].Id = 0x200;
            sessionA.TxSlots[1].Channel = 1;
            sessionA.Close();

            var portB = new FakeGsCanPort();
            var sessionB = new ViewSession(portB, store);

            Assert.Equal(@"\\?\usb#remembered", sessionB.SelectedDevice!.Path);
            Assert.False(sessionB.DisplayFilter.ShowEcho);
            Assert.Equal(2, sessionB.TxSlots.Count);
            Assert.Equal(0x123u, sessionB.TxSlots[0].Id);
            Assert.Equal(10, sessionB.TxSlots[0].PeriodMs);
            Assert.Equal(0x200u, sessionB.TxSlots[1].Id);
            Assert.Equal(1, sessionB.TxSlots[1].Channel);
            Assert.Null(sessionB.OpenedPath);
            Assert.DoesNotContain(sessionB.Channels, channel => channel.IsRunning);
            Assert.Equal(0, portB.OpenCallCount);
            Assert.Equal(0, portB.ListCallCount);

            sessionB.Open(info);

            Assert.Equal(500_000, sessionB.Channels[0].Bitrate);
            Assert.True(sessionB.Channels[0].FdEnabled);
            Assert.True(sessionB.Channels[0].ListenOnly);
            Assert.True(sessionB.Channels[0].Loopback);
            Assert.True(sessionB.Channels[0].OneShot);
            Assert.Equal(250_000, sessionB.Channels[1].Bitrate);
            Assert.False(sessionB.Channels[1].FdEnabled);
            Assert.False(sessionB.Channels[0].IsRunning);
            Assert.False(sessionB.Channels[1].IsRunning);
            Assert.Equal(0, portB.LastOpenedDevice!.StartCallCount);
        }

        [Fact]
        public void Restored_Enabled_does_not_send_until_the_channel_is_started()
        {
            var store = new FakeConfigStore();
            var info = new DeviceInfo(@"\\?\usb#remembered", 2);
            var clockA = new FakeClock();
            var portA = new FakeGsCanPort();
            var sessionA = new ViewSession(portA, runBackgroundPumps: false, clock: clockA, configStore: store);
            sessionA.Open(info);
            sessionA.StartChannel(0);
            sessionA.TxSlots[0].Id = 0x123;
            sessionA.TxSlots[0].PeriodMs = 10;
            sessionA.TxSlots[0].Enabled = true;
            sessionA.Close();

            var clockB = new FakeClock();
            var portB = new FakeGsCanPort();
            var sessionB = new ViewSession(portB, runBackgroundPumps: false, clock: clockB, configStore: store);

            Assert.True(sessionB.TxSlots[0].Enabled);
            Assert.Null(sessionB.OpenedPath);
            Assert.Equal(0, portB.OpenCallCount);

            sessionB.Open(info);
            clockB.Advance(System.TimeSpan.FromMilliseconds(50));
            Assert.Empty(portB.LastOpenedDevice!.Sent);
            Assert.False(sessionB.Channels[0].IsRunning);

            sessionB.StartChannel(0);
            clockB.Advance(System.TimeSpan.FromMilliseconds(10));
            Assert.Single(portB.LastOpenedDevice.Sent);
            Assert.Equal(0x123u, portB.LastOpenedDevice.Sent[0].Frame.Id);
        }

        [Fact]
        public void Restoring_sixteen_blank_TxSlots_keeps_one_row()
        {
            var store = new FakeConfigStore();
            var blanks = new List<TxSlotConfig>();
            for (int i = 0; i < 16; i++)
            {
                blanks.Add(new TxSlotConfig());
            }

            store.Save(new ViewConfig { TxSlots = blanks });

            var session = new ViewSession(new FakeGsCanPort(), store);

            Assert.Equal(0u, Assert.Single(session.TxSlots).Id);
        }

        [Fact]
        public void Restoring_filled_TxSlots_drops_trailing_blanks()
        {
            var store = new FakeConfigStore();
            store.Save(new ViewConfig
            {
                TxSlots = new List<TxSlotConfig>
                {
                    new TxSlotConfig { Id = 0x100, PeriodMs = 10 },
                    new TxSlotConfig { Id = 0x200, Channel = 1 },
                    new TxSlotConfig(),
                    new TxSlotConfig { Length = 8, DataHex = "00 00 00 00 00 00 00 00" }
                }
            });

            var session = new ViewSession(new FakeGsCanPort(), store);

            Assert.Equal(2, session.TxSlots.Count);
            Assert.Equal(0x100u, session.TxSlots[0].Id);
            Assert.Equal(10, session.TxSlots[0].PeriodMs);
            Assert.Equal(0x200u, session.TxSlots[1].Id);
            Assert.Equal(1, session.TxSlots[1].Channel);
        }

        [Fact]
        public void Restoring_TxSlot_uses_saved_Length_and_infers_when_missing()
        {
            var store = new FakeConfigStore();
            store.Save(new ViewConfig
            {
                TxSlots = new List<TxSlotConfig>
                {
                    new TxSlotConfig { Id = 0x100, Length = 4, DataHex = "AA BB" },
                    new TxSlotConfig { Id = 0x200, Fd = true, DataHex = "11 22 33 44 55 66 77 88 99" }
                }
            });

            var session = new ViewSession(new FakeGsCanPort(), store);

            Assert.Equal(2, session.TxSlots.Count);
            Assert.Equal(4, session.TxSlots[0].Length);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0, 0 }, session.TxSlots[0].Data);
            Assert.True(session.TxSlots[1].Fd);
            Assert.Equal(12, session.TxSlots[1].Length);
            Assert.Equal(12, session.TxSlots[1].Data.Length);
            Assert.Equal(0x99, session.TxSlots[1].Data[8]);
        }

        [Fact]
        public void Restoring_TxSlot_saved_Length_truncates_longer_DataHex()
        {
            var store = new FakeConfigStore();
            store.Save(new ViewConfig
            {
                TxSlots = new List<TxSlotConfig>
                {
                    new TxSlotConfig { Id = 0x100, Length = 4, DataHex = "AA BB CC DD EE" }
                }
            });

            var session = new ViewSession(new FakeGsCanPort(), store);

            var slot = Assert.Single(session.TxSlots);
            Assert.Equal(4, slot.Length);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }, slot.Data);
        }

        [Fact]
        public void RefreshDevices_selects_the_remembered_path_from_the_list()
        {
            var store = new FakeConfigStore();
            var info = new DeviceInfo(@"\\?\usb#remembered", 2);
            var sessionA = new ViewSession(new FakeGsCanPort(), store);
            sessionA.Open(info);
            sessionA.Close();

            var portB = new FakeGsCanPort();
            portB.Devices.Add(info);
            var sessionB = new ViewSession(portB, store);

            Assert.Equal(@"\\?\usb#remembered", sessionB.SelectedDevice!.Path);
            Assert.NotSame(info, sessionB.SelectedDevice);
            Assert.Equal(0, portB.ListCallCount);

            sessionB.RefreshDevices();

            Assert.Same(sessionB.DeviceList[0], sessionB.SelectedDevice);
            Assert.Equal(1, portB.ListCallCount);
            Assert.Equal(0, portB.OpenCallCount);
        }
    }
}
