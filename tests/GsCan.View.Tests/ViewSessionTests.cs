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
        }
    }
}
