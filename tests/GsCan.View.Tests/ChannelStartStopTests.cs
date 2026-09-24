using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class ChannelStartStopTests
    {
        [Fact]
        public void Open_leaves_channels_with_default_options_and_not_running()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port);

            session.Open(info);

            Assert.Equal(2, session.Channels.Count);
            Assert.Equal(0, port.LastOpenedDevice!.StartCallCount);
            foreach (var channel in session.Channels)
            {
                Assert.False(channel.IsRunning);
                Assert.Equal(500_000, channel.Bitrate);
                Assert.False(channel.FdEnabled);
                Assert.Equal(2_000_000, channel.DataBitrate);
                Assert.False(channel.ListenOnly);
                Assert.False(channel.Loopback);
                Assert.False(channel.OneShot);
            }
        }

        [Fact]
        public void Arbitration_bitrates_are_the_native_recognized_rates()
        {
            Assert.Equal(
                new[] { 10_000, 20_000, 50_000, 83_333, 100_000, 125_000, 250_000, 500_000, 800_000, 1_000_000 },
                ChannelState.ArbitrationBitrates);
        }

        [Fact]
        public void Data_bitrates_are_1M_2M_4M()
        {
            Assert.Equal(new[] { 1_000_000, 2_000_000, 4_000_000 }, ChannelState.DataBitrates);
        }

        [Fact]
        public void StartChannel_starts_only_that_channel()
        {
            var (session, opened) = OpenTwoChannels();

            session.StartChannel(0);

            Assert.True(session.Channels[0].IsRunning);
            Assert.False(session.Channels[1].IsRunning);
            Assert.Equal(new[] { 0 }, opened.StartedIndexes);
        }

        [Fact]
        public void StartChannel_classic_passes_arbitration_bitrate_and_null_data_bitrate()
        {
            var (session, opened) = OpenTwoChannels();
            session.Channels[0].Bitrate = 250_000;
            session.Channels[0].FdEnabled = false;
            session.Channels[0].DataBitrate = 4_000_000;

            session.StartChannel(0);

            var options = opened.LastStartOptions[0];
            Assert.Equal(250_000, options.Bitrate);
            Assert.Null(options.DataBitrate);
        }

        [Fact]
        public void StartChannel_fd_defaults_data_bitrate_to_2M()
        {
            var (session, opened) = OpenTwoChannels();
            session.Channels[0].FdEnabled = true;

            session.StartChannel(0);

            Assert.Equal(2_000_000, opened.LastStartOptions[0].DataBitrate);
        }

        [Fact]
        public void StartChannel_fd_passes_data_bitrate()
        {
            var (session, opened) = OpenTwoChannels();
            session.Channels[0].FdEnabled = true;
            session.Channels[0].DataBitrate = 4_000_000;

            session.StartChannel(0);

            var options = opened.LastStartOptions[0];
            Assert.Equal(500_000, options.Bitrate);
            Assert.Equal(4_000_000, options.DataBitrate);
        }

        [Fact]
        public void StartChannel_passes_listen_only_loopback_and_one_shot()
        {
            var (session, opened) = OpenTwoChannels();
            session.Channels[0].ListenOnly = true;
            session.Channels[0].Loopback = true;
            session.Channels[0].OneShot = true;

            session.StartChannel(0);

            var options = opened.LastStartOptions[0];
            Assert.True(options.ListenOnly);
            Assert.True(options.Loopback);
            Assert.True(options.OneShot);
        }

        [Fact]
        public void StartChannel_on_second_channel_does_not_stop_or_start_the_first()
        {
            var (session, opened) = OpenTwoChannels();
            session.StartChannel(0);

            session.StartChannel(1);

            Assert.True(session.Channels[0].IsRunning);
            Assert.True(session.Channels[1].IsRunning);
            Assert.Equal(new[] { 0, 1 }, opened.StartedIndexes);
        }

        [Fact]
        public void StartChannel_failure_sets_LastError_and_leaves_that_channel_stopped()
        {
            var (session, opened) = OpenTwoChannels();
            session.StartChannel(1);
            opened.StartException = new GsCanException("Failed to start channel (native error 4).");

            session.StartChannel(0);

            Assert.Equal("Failed to start channel (native error 4).", session.LastError);
            Assert.False(session.Channels[0].IsRunning);
            Assert.True(session.Channels[1].IsRunning);
        }

        [Fact]
        public void StopChannel_stops_only_that_channel()
        {
            var (session, opened) = OpenTwoChannels();
            session.StartChannel(0);
            session.StartChannel(1);

            session.StopChannel(0);

            Assert.False(session.Channels[0].IsRunning);
            Assert.True(session.Channels[1].IsRunning);
            Assert.Equal(new[] { 0 }, opened.StoppedIndexes);
            Assert.Null(session.LastError);
        }

        [Fact]
        public void StopChannel_on_a_stopped_channel_does_not_set_LastError()
        {
            var (session, opened) = OpenTwoChannels();

            session.StopChannel(0);

            Assert.False(session.Channels[0].IsRunning);
            Assert.False(session.Channels[1].IsRunning);
            Assert.Equal(new[] { 0 }, opened.StoppedIndexes);
            Assert.Null(session.LastError);
        }

        [Fact]
        public void Close_stops_running_channels_then_disposes()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);
            session.StartChannel(0);
            var opened = port.LastOpenedDevice!;

            session.Close();

            Assert.Equal(new[] { 0 }, opened.StoppedIndexes);
            Assert.Equal(1, port.DisposeCallCount);
            Assert.Null(session.OpenedPath);
            Assert.Empty(session.Channels);
        }

        [Fact]
        public void Channel_bar_Start_and_Stop_delegate_to_the_session()
        {
            var (session, opened) = OpenTwoChannels();

            session.Channels[0].Start();
            Assert.True(session.Channels[0].IsRunning);
            Assert.Equal(new[] { 0 }, opened.StartedIndexes);

            session.Channels[0].Stop();
            Assert.False(session.Channels[0].IsRunning);
            Assert.Equal(new[] { 0 }, opened.StoppedIndexes);
        }

        private static (ViewSession Session, FakeOpenedDevice Opened) OpenTwoChannels()
        {
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false);
            session.Open(info);
            return (session, port.LastOpenedDevice!);
        }
    }
}
