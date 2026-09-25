using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class ViewSessionTests
    {
        [Fact]
        public void Constructing_ViewSession_does_not_throw()
        {
            var port = new FakeGsCanPort();
            var session = new ViewSession(port);
            Assert.NotNull(session);
        }

        [Fact]
        public void Constructing_ViewSession_does_not_open_a_Device()
        {
            var port = new FakeGsCanPort();
            _ = new ViewSession(port);
            Assert.Equal(0, port.OpenCallCount);
        }

        [Fact]
        public void WindowTitle_is_GsCan_View()
        {
            var session = new ViewSession(new FakeGsCanPort());
            Assert.Equal("GsCan View", session.WindowTitle);
        }

        [Fact]
        public void no_channel_is_running()
        {
            var session = new ViewSession(new FakeGsCanPort());
            Assert.DoesNotContain(session.Channels, channel => channel.IsRunning);
        }

        [Fact]
        public void Constructing_ViewSession_does_not_list_or_open_devices()
        {
            var port = new FakeGsCanPort();
            _ = new ViewSession(port);
            Assert.Equal(0, port.ListCallCount);
            Assert.Equal(0, port.OpenCallCount);
        }

        [Fact]
        public void OpenedPath_is_null_when_no_Device_is_open()
        {
            var session = new ViewSession(new FakeGsCanPort());
            Assert.Null(session.OpenedPath);
        }

        [Fact]
        public void DeviceList_is_empty_until_refresh()
        {
            var session = new ViewSession(new FakeGsCanPort());
            Assert.Empty(session.DeviceList);
            Assert.True(session.DeviceListIsEmpty);
        }

        [Fact]
        public void RefreshDevices_fills_DeviceList_from_port_List()
        {
            var port = new FakeGsCanPort();
            port.Devices.Add(new DeviceInfo(@"\\?\usb#vid_1d50&pid_606f#1", 2));
            var session = new ViewSession(port);

            session.RefreshDevices();

            Assert.Equal(1, port.ListCallCount);
            Assert.Single(session.DeviceList);
            Assert.Equal(@"\\?\usb#vid_1d50&pid_606f#1", session.DeviceList[0].Path);
            Assert.Equal(2, session.DeviceList[0].ChannelCount);
            Assert.False(session.DeviceListIsEmpty);
        }

        [Fact]
        public void DeviceList_is_a_snapshot_and_does_not_update_until_refresh()
        {
            var port = new FakeGsCanPort();
            port.Devices.Add(new DeviceInfo(@"\\?\usb#a", 2));
            var session = new ViewSession(port);
            session.RefreshDevices();

            port.Devices.Clear();
            port.Devices.Add(new DeviceInfo(@"\\?\usb#b", 1));

            Assert.Single(session.DeviceList);
            Assert.Equal(@"\\?\usb#a", session.DeviceList[0].Path);

            session.RefreshDevices();

            Assert.Equal(2, port.ListCallCount);
            Assert.Single(session.DeviceList);
            Assert.Equal(@"\\?\usb#b", session.DeviceList[0].Path);
            Assert.Equal(1, session.DeviceList[0].ChannelCount);
        }

        [Fact]
        public void RefreshDevices_with_no_devices_leaves_DeviceList_empty()
        {
            var session = new ViewSession(new FakeGsCanPort());

            session.RefreshDevices();

            Assert.Empty(session.DeviceList);
            Assert.True(session.DeviceListIsEmpty);
        }

        [Fact]
        public void RefreshDevices_from_a_list_to_no_devices_sets_DeviceListIsEmpty()
        {
            var port = new FakeGsCanPort();
            port.Devices.Add(new DeviceInfo(@"\\?\usb#a", 2));
            var session = new ViewSession(port);
            session.RefreshDevices();
            Assert.False(session.DeviceListIsEmpty);

            port.Devices.Clear();
            session.RefreshDevices();

            Assert.Empty(session.DeviceList);
            Assert.True(session.DeviceListIsEmpty);
        }

        [Fact]
        public void RefreshDevices_keeps_the_same_list_instance_when_the_snapshot_is_unchanged()
        {
            var port = new FakeGsCanPort();
            port.Devices.Add(new DeviceInfo(@"\\?\usb#a", 2));
            var session = new ViewSession(port);
            session.RefreshDevices();
            session.SelectedDevice = session.DeviceList[0];
            var first = session.DeviceList;

            session.RefreshDevices();

            Assert.Equal(2, port.ListCallCount);
            Assert.Same(first, session.DeviceList);
            Assert.Same(first[0], session.SelectedDevice);
        }

        [Fact]
        public void RefreshDevices_does_not_List_while_a_Device_is_open()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            port.Devices.Add(info);
            var session = new ViewSession(port);
            session.RefreshDevices();
            session.Open(info);
            int listsAfterOpen = port.ListCallCount;

            session.RefreshDevices();

            Assert.Equal(listsAfterOpen, port.ListCallCount);
            Assert.Single(session.DeviceList);
            Assert.Equal(info.Path, session.DeviceList[0].Path);
        }

        [Fact]
        public void Open_writes_discovered_ChannelCount_onto_the_listed_Device()
        {
            var path = @"\\?\usb#a";
            var port = new FakeGsCanPort();
            port.Devices.Add(new DeviceInfo(path, 0));
            port.OpenChannelCount = 2;
            var session = new ViewSession(port);
            session.RefreshDevices();
            session.SelectedDevice = session.DeviceList[0];

            session.OpenSelected();

            Assert.Equal(2, session.DeviceList[0].ChannelCount);
            Assert.Equal(2, session.SelectedDevice!.ChannelCount);
            Assert.Same(session.DeviceList[0], session.SelectedDevice);
            Assert.Equal(2, session.Channels.Count);
        }

        [Fact]
        public void Open_sets_OpenedPath_and_creates_stopped_Channels()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port);

            session.Open(info);

            Assert.Equal(1, port.OpenCallCount);
            Assert.Equal(@"\\?\usb#a", session.OpenedPath);
            Assert.Equal(2, session.Channels.Count);
            Assert.Equal(0, session.Channels[0].Index);
            Assert.Equal(1, session.Channels[1].Index);
            Assert.False(session.Channels[0].IsRunning);
            Assert.False(session.Channels[1].IsRunning);
            Assert.Equal(0, port.LastOpenedDevice!.StartCallCount);
        }

        [Fact]
        public void Open_creates_one_Channel_bar_when_ChannelCount_is_1()
        {
            var info = new DeviceInfo(@"\\?\usb#single", 1);
            var session = new ViewSession(new FakeGsCanPort());

            session.Open(info);

            Assert.Single(session.Channels);
            Assert.Equal(0, session.Channels[0].Index);
            Assert.False(session.Channels[0].IsRunning);
        }

        [Fact]
        public void Open_does_not_open_a_second_Device_while_one_is_open()
        {
            var first = new DeviceInfo(@"\\?\usb#a", 2);
            var second = new DeviceInfo(@"\\?\usb#b", 1);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port);

            session.Open(first);
            session.Open(second);

            Assert.Equal(1, port.OpenCallCount);
            Assert.Equal(@"\\?\usb#a", session.OpenedPath);
            Assert.Equal(2, session.Channels.Count);
        }

        [Fact]
        public void Close_releases_Device_so_the_same_info_can_be_opened_again()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port);

            session.Open(info);
            session.Close();

            Assert.Null(session.OpenedPath);
            Assert.Empty(session.Channels);
            Assert.Equal(1, port.DisposeCallCount);

            session.Open(info);

            Assert.Equal(@"\\?\usb#a", session.OpenedPath);
            Assert.Equal(2, session.Channels.Count);
            Assert.Equal(2, port.OpenCallCount);
            Assert.DoesNotContain(session.Channels, channel => channel.IsRunning);
        }

        [Fact]
        public void Open_failure_surfaces_GsCanException_and_does_not_open()
        {
            var info = new DeviceInfo(@"\\?\usb#missing", 2);
            var port = new FakeGsCanPort
            {
                OpenException = new GsCanException("Device not found: \\\\?\\usb#missing")
            };
            var session = new ViewSession(port);

            session.Open(info);

            Assert.Null(session.OpenedPath);
            Assert.Empty(session.Channels);
            Assert.Equal("Device not found: \\\\?\\usb#missing", session.LastError);
            Assert.Equal(1, port.OpenCallCount);
            Assert.Equal(0, port.DisposeCallCount);
        }

        [Fact]
        public void Close_when_no_Device_is_open_does_not_throw()
        {
            var session = new ViewSession(new FakeGsCanPort());
            session.Close();
            Assert.Null(session.OpenedPath);
        }
    }
}
