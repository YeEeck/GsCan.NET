using System;
using System.Threading;
using GsCan;
using GsCan.View.Session;
using Xunit;

namespace GsCan.View.Tests
{
    public class HardwareChannelStartStopTests
    {
        [Fact]
        public void Hardware_can_start_and_stop_one_channel_independently()
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                Console.WriteLine("SKIP hardware Start/Stop: no gs_usb device present.");
                return;
            }

            var session = new ViewSession(new GsCanPort());
            try
            {
                session.Open(list[0]);
                if (session.Channels.Count == 0)
                {
                    Console.WriteLine("SKIP hardware Start/Stop: Open failed: " + session.LastError);
                    return;
                }

                Assert.True(session.Channels.Count >= 1);
                Assert.DoesNotContain(session.Channels, channel => channel.IsRunning);

                session.Channels[0].Loopback = true;
                session.StartChannel(0);
                Assert.Null(session.LastError);
                Assert.True(session.Channels[0].IsRunning);
                if (session.Channels.Count > 1)
                {
                    Assert.False(session.Channels[1].IsRunning);
                }

                if (session.Channels.Count >= 2)
                {
                    session.Channels[1].Loopback = true;
                    session.StartChannel(1);
                    Assert.Null(session.LastError);
                    Assert.True(session.Channels[0].IsRunning);
                    Assert.True(session.Channels[1].IsRunning);

                    session.StopChannel(1);
                    Thread.Sleep(250);
                    Assert.Equal(list[0].Path, session.OpenedPath);
                    Assert.False(session.Channels[1].IsRunning);
                    Assert.True(session.Channels[0].IsRunning);
                    Assert.Null(session.LastError);

                    session.StartChannel(1);
                    Thread.Sleep(250);
                    Assert.Equal(list[0].Path, session.OpenedPath);
                    Assert.True(session.Channels[0].IsRunning);
                    Assert.True(session.Channels[1].IsRunning);
                    Assert.Null(session.LastError);
                }

                session.StopChannel(0);
                session.StartChannel(0);
                Thread.Sleep(250);
                Assert.Equal(list[0].Path, session.OpenedPath);
                Assert.True(session.Channels[0].IsRunning);
                Assert.Null(session.LastError);

                session.StopChannel(0);
                Assert.False(session.Channels[0].IsRunning);
                Assert.Null(session.LastError);
            }
            finally
            {
                session.Close();
            }
        }
    }
}
