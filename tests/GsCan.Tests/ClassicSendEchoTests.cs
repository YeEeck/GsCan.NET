using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GsCan.Tests
{
    public class ClassicSendEchoTests
    {
        private static bool TryOpenFirst(out Device? device)
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                device = null;
                return false;
            }

            device = Device.Open(list[0]);
            return true;
        }

        [Fact]
        public void Start_with_invalid_bitrate_throws_GsCanException()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP Start invalid bitrate: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                Assert.Throws<GsCanException>(() =>
                    ch.Start(new ChannelOptions { Bitrate = 1 }));
            }
        }

        [Fact]
        public void Start_Send_TryRead_after_Dispose_throw_GsCanException()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP after Dispose: no gs_usb device present.");
                return;
            }

            var ch = device!.Channels[0];
            device.Dispose();

            Assert.Throws<GsCanException>(() =>
                ch.Start(new ChannelOptions { Bitrate = 500000 }));
            Assert.Throws<GsCanException>(() =>
                ch.Send(CanFrame.Classic(0x100, new byte[] { 0x01 })));
            Assert.Throws<GsCanException>(() =>
            {
                ch.TryRead(out _, 10);
            });
        }

        [Fact]
        public void Send_and_TryRead_before_Start_throw_GsCanException()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP before Start: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                Assert.Throws<GsCanException>(() =>
                    ch.Send(CanFrame.Classic(0x100, new byte[] { 0x01 })));
                Assert.Throws<GsCanException>(() =>
                {
                    ch.TryRead(out _, 10);
                });
            }
        }

        [Fact]
        public void TryRead_timeout_returns_false_when_no_frame()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP TryRead timeout: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    Assert.False(ch.TryRead(out _, 50));
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Loopback_Send_classic_yields_Echo_and_Rx_FIFO()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP loopback Echo/Rx: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    var payload1 = new byte[] { 0x11, 0x22 };
                    var payload2 = new byte[] { 0xAA, 0xBB, 0xCC };
                    ch.Send(CanFrame.Classic(0x123, payload1));
                    ch.Send(CanFrame.Classic(0x456, payload2));

                    var got = Drain(ch, expectedMin: 4, timeoutMs: 500);
                    Assert.True(got.Count >= 4, "expected at least Echo+Rx for each of two sends, got " + got.Count);

                    var echoes = got.Where(f => f.Kind == CanFrameKind.Echo).ToList();
                    var rxs = got.Where(f => f.Kind == CanFrameKind.Rx).ToList();

                    Assert.True(echoes.Count >= 2, "expected >=2 Echo frames");
                    Assert.True(rxs.Count >= 2, "expected >=2 Rx frames");

                    Assert.Equal(0x123u, echoes[0].Id);
                    Assert.Equal(payload1, echoes[0].Data);
                    Assert.Equal(0x456u, echoes[1].Id);
                    Assert.Equal(payload2, echoes[1].Data);

                    Assert.Contains(rxs, f => f.Id == 0x123u && f.Data.SequenceEqual(payload1));
                    Assert.Contains(rxs, f => f.Id == 0x456u && f.Data.SequenceEqual(payload2));
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Stop_after_Start_allows_restart()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP Stop/restart: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                ch.Stop();
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    Assert.False(ch.TryRead(out _, 20));
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        private static List<CanFrame> Drain(Channel ch, int expectedMin, int timeoutMs)
        {
            var got = new List<CanFrame>();
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs * Math.Max(expectedMin, 1));
            while (got.Count < expectedMin && DateTime.UtcNow < deadline)
            {
                if (ch.TryRead(out var frame, 100))
                {
                    got.Add(frame);
                }
            }

            return got;
        }
    }
}
