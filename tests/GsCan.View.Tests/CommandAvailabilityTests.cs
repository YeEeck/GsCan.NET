using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class CommandAvailabilityTests
    {
        [Fact]
        public void Closed_without_a_selection_cannot_open_or_close_and_can_refresh()
        {
            var session = new ViewSession(new FakeGsCanPort());

            Assert.False(session.CanOpenSelected);
            Assert.Equal("未选择设备", session.OpenUnavailableReason);
            Assert.False(session.CanClose);
            Assert.Equal("未打开 Device", session.CloseUnavailableReason);
            Assert.True(session.CanRefreshDevices);
            Assert.Null(session.RefreshUnavailableReason);
            Assert.True(session.CanSelectDevice);
            Assert.Null(session.DeviceSelectUnavailableReason);
        }

        [Fact]
        public void Closed_with_a_selection_can_open()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            port.Devices.Add(info);
            var session = new ViewSession(port);
            session.RefreshDevices();
            session.SelectedDevice = session.DeviceList[0];

            Assert.True(session.CanOpenSelected);
            Assert.Null(session.OpenUnavailableReason);
            Assert.False(session.CanClose);
        }

        [Fact]
        public void Open_locks_device_identity_until_Close()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var other = new DeviceInfo(@"\\?\usb#b", 1);
            var port = new FakeGsCanPort();
            port.Devices.Add(info);
            var session = new ViewSession(port);
            session.RefreshDevices();
            session.SelectedDevice = session.DeviceList[0];
            session.Open(info);

            Assert.False(session.CanSelectDevice);
            Assert.Equal("请先关闭当前 Device", session.DeviceSelectUnavailableReason);
            Assert.False(session.CanRefreshDevices);
            Assert.Equal("请先关闭当前 Device", session.RefreshUnavailableReason);
            Assert.False(session.CanOpenSelected);
            Assert.Equal("请先关闭当前 Device", session.OpenUnavailableReason);
            Assert.True(session.CanClose);
            Assert.Null(session.CloseUnavailableReason);

            session.SelectedDevice = other;
            Assert.Equal(info.Path, session.SelectedDevice!.Path);

            session.Open(other);
            Assert.Null(session.LastError);
            Assert.Equal(info.Path, session.OpenedPath);

            session.Close();

            Assert.True(session.CanSelectDevice);
            Assert.True(session.CanRefreshDevices);
            Assert.True(session.CanOpenSelected);
            Assert.False(session.CanClose);
            Assert.Equal("未打开 Device", session.CloseUnavailableReason);
        }

        [Fact]
        public void Stopped_channel_can_start_and_edit_options()
        {
            var (session, _) = OpenDevice();
            var channel = session.Channels[0];

            Assert.True(channel.CanStart);
            Assert.Null(channel.StartUnavailableReason);
            Assert.False(channel.CanStop);
            Assert.Equal("通道未启动", channel.StopUnavailableReason);
            Assert.True(channel.CanEditOptions);
        }

        [Fact]
        public void Running_channel_locks_options_and_only_that_channel()
        {
            var (session, _) = OpenDevice();
            session.StartChannel(0);
            var ch0 = session.Channels[0];
            var ch1 = session.Channels[1];

            Assert.False(ch0.CanStart);
            Assert.Equal("通道已在运行", ch0.StartUnavailableReason);
            Assert.True(ch0.CanStop);
            Assert.Null(ch0.StopUnavailableReason);
            Assert.False(ch0.CanEditOptions);

            ch0.Bitrate = 1_000_000;
            ch0.FdEnabled = true;
            ch0.ListenOnly = true;
            ch0.Loopback = true;
            ch0.OneShot = true;
            Assert.Equal(500_000, ch0.Bitrate);
            Assert.False(ch0.FdEnabled);
            Assert.False(ch0.ListenOnly);
            Assert.False(ch0.Loopback);
            Assert.False(ch0.OneShot);

            Assert.True(ch1.CanStart);
            Assert.False(ch1.CanStop);
            Assert.True(ch1.CanEditOptions);
            ch1.Bitrate = 250_000;
            Assert.Equal(250_000, ch1.Bitrate);

            session.StopChannel(0);
            Assert.True(ch0.CanStart);
            Assert.False(ch0.CanStop);
            Assert.True(ch0.CanEditOptions);
            ch0.Bitrate = 125_000;
            Assert.Equal(125_000, ch0.Bitrate);
        }

        [Fact]
        public void SaveLog_is_available_only_when_Trace_has_rows()
        {
            var (session, port) = OpenDevice();
            Assert.False(session.CanSaveLog);
            Assert.Equal("Trace 为空", session.SaveLogUnavailableReason);

            session.StartChannel(0);
            port.LastOpenedDevice!.Enqueue(0, new CanFrame(0x100, new byte[] { 0x01 }, CanFrameKind.Rx, timestampMicroseconds: 1000));
            session.PumpUntilIdle();

            Assert.True(session.CanSaveLog);
            Assert.Null(session.SaveLogUnavailableReason);

            session.Close();
            Assert.True(session.CanSaveLog);
            Assert.False(session.CanClose);

            session.Clear();
            Assert.False(session.CanSaveLog);
            Assert.Equal("Trace 为空", session.SaveLogUnavailableReason);
        }

        [Fact]
        public void ChannelChoices_are_zero_and_one_until_Open_then_follow_ChannelCount()
        {
            var closed = new ViewSession(new FakeGsCanPort());
            Assert.Equal(new[] { 0, 1 }, closed.ChannelChoices);
            Assert.True(closed.ShowFilterChannel1);
            Assert.Equal(new[] { 0, 1 }, closed.TxSlots[0].ChannelChoices);

            var (two, _) = OpenDevice(channelCount: 2);
            Assert.Equal(new[] { 0, 1 }, two.ChannelChoices);
            Assert.True(two.ShowFilterChannel1);

            var info = new DeviceInfo(@"\\?\usb#a", 1);
            var port = new FakeGsCanPort();
            var one = new ViewSession(port, runBackgroundPumps: false);
            one.TxSlots[0].Channel = 1;
            one.Open(info);

            Assert.Equal(new[] { 0 }, one.ChannelChoices);
            Assert.False(one.ShowFilterChannel1);
            Assert.Equal(new[] { 0 }, one.TxSlots[0].ChannelChoices);
            Assert.Equal(0, one.TxSlots[0].Channel);

            one.Close();
            Assert.Equal(new[] { 0, 1 }, one.ChannelChoices);
            Assert.True(one.ShowFilterChannel1);
        }

        [Fact]
        public void Send_and_enable_are_disabled_until_the_target_channel_is_running()
        {
            var session = new ViewSession(new FakeGsCanPort());
            var slot = session.TxSlots[0];
            Assert.False(slot.CanSend);
            Assert.False(slot.CanEnable);
            Assert.Equal("未打开 Device", slot.SendUnavailableReason);

            var (open, _) = OpenDevice();
            slot = open.TxSlots[0];
            slot.Channel = 0;
            Assert.False(slot.CanSend);
            Assert.Equal("通道未启动", slot.SendUnavailableReason);

            slot.Enabled = true;
            Assert.False(slot.Enabled);

            open.StartChannel(0);
            Assert.True(slot.CanSend);
            Assert.True(slot.CanEnable);
            Assert.Null(slot.SendUnavailableReason);
            Assert.False(slot.Enabled);

            open.Paused = true;
            Assert.True(slot.CanSend);

            open.StopChannel(0);
            Assert.False(slot.CanSend);
            Assert.Equal("通道未启动", slot.SendUnavailableReason);
        }

        [Fact]
        public void ListenOnly_running_channel_cannot_send_and_does_not_write_LastError()
        {
            var (session, _) = OpenDevice();
            session.Channels[0].ListenOnly = true;
            session.StartChannel(0);
            var slot = session.TxSlots[0];
            slot.Channel = 0;

            Assert.False(slot.CanSend);
            Assert.Equal("只听通道不能发送", slot.SendUnavailableReason);

            session.SendOnce(0);
            Assert.Null(session.LastError);
            Assert.False(slot.Enabled);
        }

        [Fact]
        public void Send_key_follows_the_slot_channel_independently()
        {
            var (session, _) = OpenDevice();
            session.StartChannel(0);
            var slot = session.TxSlots[0];
            slot.Channel = 0;
            Assert.True(slot.CanSend);

            slot.Channel = 1;
            Assert.False(slot.CanSend);
            Assert.Equal("通道未启动", slot.SendUnavailableReason);

            session.StartChannel(1);
            Assert.True(slot.CanSend);
        }

        [Fact]
        public void Start_failure_leaves_the_channel_startable()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port);
            session.Open(info);
            port.LastOpenedDevice!.StartException = new GsCanException("start failed");

            session.StartChannel(0);

            Assert.False(session.Channels[0].IsRunning);
            Assert.True(session.Channels[0].CanStart);
            Assert.Equal("start failed", session.LastError);
        }

        private static (ViewSession Session, FakeGsCanPort Port) OpenDevice(int channelCount = 2)
        {
            var info = new DeviceInfo(@"\\?\usb#a", channelCount);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);
            return (session, port);
        }
    }
}
