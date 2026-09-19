using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GsCan.Tests
{
    public class CanFdTests
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
        public void Loopback_Send_Fd_yields_IsFd_frame_with_BRS_and_timestamp()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP FD loopback: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions
                {
                    Bitrate = 500000,
                    DataBitrate = 2000000,
                    Loopback = true
                });
                try
                {
                    var payload = new byte[64];
                    for (int i = 0; i < payload.Length; i++)
                    {
                        payload[i] = (byte)i;
                    }

                    ch.Send(CanFrame.Fd(0x123, payload));

                    var got = Drain(ch, expectedMin: 1, timeoutMs: 500);
                    Assert.True(got.Count >= 1, "expected at least one Echo or Rx FD frame, got " + got.Count);

                    var fdFrames = got.Where(f => f.IsFd).ToList();
                    Assert.True(fdFrames.Count >= 1, "expected at least one frame with IsFd=true");

                    Assert.Contains(fdFrames, f => f.Id == 0x123u && f.Data.SequenceEqual(payload));
                    Assert.All(fdFrames.Where(f => f.Id == 0x123u), f => Assert.True(f.BitRateSwitch));

                    // HW timestamps should be non-zero on FlintCAN-FD when HW_TIMESTAMP is on.
                    Assert.Contains(got, f => f.TimestampMicroseconds != 0);
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Start_without_DataBitrate_classic_Send_TryRead_still_works()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP classic without DataBitrate: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    var payload = new byte[] { 0x11, 0x22 };
                    ch.Send(CanFrame.Classic(0x123, payload));

                    var got = Drain(ch, expectedMin: 2, timeoutMs: 500);
                    Assert.True(got.Count >= 2, "expected Echo+Rx for classic send, got " + got.Count);
                    Assert.Contains(got, f => f.Kind == CanFrameKind.Echo && f.Id == 0x123u && !f.IsFd);
                    Assert.Contains(got, f => f.Kind == CanFrameKind.Rx && f.Id == 0x123u && !f.IsFd);
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Start_with_unsupported_DataBitrate_throws_GsCanException()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP unsupported DataBitrate: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                Assert.Throws<GsCanException>(() =>
                    ch.Start(new ChannelOptions
                    {
                        Bitrate = 500000,
                        DataBitrate = 123456,
                        Loopback = true
                    }));
            }
        }

        [Fact]
        public void Public_API_has_no_TDC_or_raw_bit_timing_types()
        {
            var publicTypes = typeof(Device).Assembly.GetExportedTypes();
            Assert.DoesNotContain(publicTypes, t =>
                t.Name.IndexOf("Tdc", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Name.IndexOf("Bittiming", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Name.IndexOf("BitTiming", StringComparison.OrdinalIgnoreCase) >= 0);
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
