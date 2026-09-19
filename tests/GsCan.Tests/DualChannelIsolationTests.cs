using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GsCan.Tests
{
    public class DualChannelIsolationTests
    {
        private static bool TryOpenDualChannel(out Device? device)
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                device = null;
                return false;
            }

            device = Device.Open(list[0]);
            if (device.Channels.Count < 2)
            {
                device.Dispose();
                device = null;
                return false;
            }

            return true;
        }

        [Fact]
        public void Device_public_surface_has_no_device_wide_read_API()
        {
            var deviceMethods = typeof(Device)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .ToArray();

            Assert.DoesNotContain("ReadAll", deviceMethods);
            Assert.DoesNotContain("TryReadDevice", deviceMethods);
            Assert.DoesNotContain("TryRead", deviceMethods);

            var allowedDevice = new HashSet<string>
            {
                "List",
                "Open",
                "get_Channels",
                "Dispose",
            };
            foreach (var name in deviceMethods)
            {
                Assert.Contains(name, allowedDevice);
            }

            var channelTryReads = typeof(Channel)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "TryRead")
                .ToArray();

            Assert.Single(channelTryReads);
            var parameters = channelTryReads[0].GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal(typeof(CanFrame).MakeByRefType(), parameters[0].ParameterType);
            Assert.Equal(typeof(int), parameters[1].ParameterType);
            Assert.Equal(typeof(bool), channelTryReads[0].ReturnType);
        }

        [Fact]
        public void Both_channels_Start_Stop_independently_at_different_bitrates()
        {
            if (!TryOpenDualChannel(out var device))
            {
                Console.WriteLine("SKIP dual Start/Stop: no dual-channel gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch0 = device.Channels[0];
                var ch1 = device.Channels[1];

                ch0.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                ch1.Start(new ChannelOptions { Bitrate = 250000, Loopback = true });
                try
                {
                    ch1.Stop();
                    ch0.Send(CanFrame.Classic(0x100, new byte[] { 0x01 }));
                    var got = Drain(ch0, expectedMin: 2, timeoutMs: 500);
                    Assert.True(got.Count >= 2, "ch0 should still Echo/Rx after ch1 Stop, got " + got.Count);

                    ch1.Start(new ChannelOptions { Bitrate = 250000, Loopback = true });
                    Assert.False(ch1.TryRead(out _, 50));
                }
                finally
                {
                    ch0.Stop();
                    ch1.Stop();
                }
            }
        }

        [Fact]
        public void Send_on_ch0_does_not_appear_on_ch1_TryRead_but_remains_on_ch0()
        {
            if (!TryOpenDualChannel(out var device))
            {
                Console.WriteLine("SKIP ch0->ch1 isolation: no dual-channel gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch0 = device.Channels[0];
                var ch1 = device.Channels[1];

                ch0.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                ch1.Start(new ChannelOptions { Bitrate = 250000, Loopback = true });
                try
                {
                    var payload = new byte[] { 0x11, 0x22 };
                    ch0.Send(CanFrame.Classic(0x123, payload));

                    // ch1 must not see ch0 frames even while it is the one blocked on USB IN.
                    Assert.False(ch1.TryRead(out _, 200));

                    var got = Drain(ch0, expectedMin: 2, timeoutMs: 500);
                    Assert.True(got.Count >= 2, "ch0 Echo/Rx must remain queued after ch1 TryRead, got " + got.Count);
                    Assert.Contains(got, f => f.Kind == CanFrameKind.Echo && f.Id == 0x123u && f.Data.SequenceEqual(payload));
                    Assert.Contains(got, f => f.Kind == CanFrameKind.Rx && f.Id == 0x123u && f.Data.SequenceEqual(payload));
                }
                finally
                {
                    ch0.Stop();
                    ch1.Stop();
                }
            }
        }

        [Fact]
        public void Send_on_ch1_does_not_appear_on_ch0_TryRead_but_remains_on_ch1()
        {
            if (!TryOpenDualChannel(out var device))
            {
                Console.WriteLine("SKIP ch1->ch0 isolation: no dual-channel gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch0 = device.Channels[0];
                var ch1 = device.Channels[1];

                ch0.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                ch1.Start(new ChannelOptions { Bitrate = 250000, Loopback = true });
                try
                {
                    var payload = new byte[] { 0xAA, 0xBB };
                    ch1.Send(CanFrame.Classic(0x456, payload));

                    Assert.False(ch0.TryRead(out _, 200));

                    var got = Drain(ch1, expectedMin: 2, timeoutMs: 500);
                    Assert.True(got.Count >= 2, "ch1 Echo/Rx must remain queued after ch0 TryRead, got " + got.Count);
                    Assert.Contains(got, f => f.Kind == CanFrameKind.Echo && f.Id == 0x456u && f.Data.SequenceEqual(payload));
                    Assert.Contains(got, f => f.Kind == CanFrameKind.Rx && f.Id == 0x456u && f.Data.SequenceEqual(payload));
                }
                finally
                {
                    ch0.Stop();
                    ch1.Stop();
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
