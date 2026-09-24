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
            sessionA.Close();

            var portB = new FakeGsCanPort();
            var sessionB = new ViewSession(portB, store);

            Assert.Equal(@"\\?\usb#remembered", sessionB.SelectedDevice!.Path);
            Assert.False(sessionB.DisplayFilter.ShowEcho);
            Assert.Equal(0x123u, sessionB.TxSlots[0].Id);
            Assert.Equal(10, sessionB.TxSlots[0].PeriodMs);
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
