using GsCan;
using GsCan.View;
using Xunit;

namespace GsCan.View.Tests
{
    public class DeviceLabelTests
    {
        [Fact]
        public void Parses_candle_winusb_path()
        {
            var path = @"\\?\usb#vid_1d50&pid_606f&mi_00#7&328fb755&0&0000#{c15b4308-04d3-11e6-b3ea-6057189e6443}";
            var info = new DeviceInfo(path, 2);

            Assert.Equal("gs_usb", DeviceLabel.Title(info));
            Assert.Equal("1D50:606F · 328fb755", DeviceLabel.Detail(info));
            Assert.Equal("2 路通道", DeviceLabel.ChannelText(info));
            Assert.Equal("gs_usb · 2 路通道 · 1D50:606F · 328fb755", DeviceLabel.Summary(info));
        }

        [Fact]
        public void Parses_short_vid_pid_path()
        {
            var info = new DeviceInfo(@"\\?\usb#vid_1d50&pid_606f#1", 2);

            Assert.Equal("gs_usb", DeviceLabel.Title(info));
            Assert.Equal("1D50:606F · 1", DeviceLabel.Detail(info));
        }

        [Fact]
        public void Falls_back_when_path_has_no_vid_pid()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 1);

            Assert.Equal("gs_usb 设备", DeviceLabel.Title(info));
            Assert.Equal("a", DeviceLabel.Detail(info));
            Assert.Equal("1 路通道", DeviceLabel.ChannelText(info));
        }

        [Fact]
        public void Summary_omits_channel_count_when_unknown()
        {
            var path = @"\\?\usb#vid_1d50&pid_606f&mi_00#7&328fb755&0&0000#{c15b4308-04d3-11e6-b3ea-6057189e6443}";
            var info = new DeviceInfo(path, 0);

            Assert.Equal("gs_usb · 1D50:606F · 328fb755", DeviceLabel.Summary(info));
        }

        [Fact]
        public void ChannelText_is_empty_when_channel_count_is_unknown()
        {
            var path = @"\\?\usb#vid_1d50&pid_606f&mi_00#7&328fb755&0&0000#{c15b4308-04d3-11e6-b3ea-6057189e6443}";
            var info = new DeviceInfo(path, 0);

            Assert.Equal(string.Empty, DeviceLabel.ChannelText(info));
            Assert.Equal(string.Empty, DeviceLabel.ChannelText(0));
        }
    }
}
