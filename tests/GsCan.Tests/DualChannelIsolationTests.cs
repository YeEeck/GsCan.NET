using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
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
        public void Stop_on_one_channel_does_not_throw_on_the_other_channels_blocked_TryRead()
        {
            if (!TryOpenDualChannel(out var device))
            {
                Console.WriteLine("SKIP concurrent Stop isolation: no dual-channel gs_usb device present.");
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
                    Exception? readEx = null;
                    var readReturned = false;
                    var readerDone = new ManualResetEventSlim(false);
                    var reader = new Thread(() =>
                    {
                        try
                        {
                            readReturned = ch0.TryRead(out _, 400);
                        }
                        catch (Exception ex)
                        {
                            readEx = ex;
                        }
                        finally
                        {
                            readerDone.Set();
                        }
                    });
                    reader.IsBackground = true;
                    reader.Start();
                    Thread.Sleep(80);
                    ch1.Stop();

                    Assert.True(readerDone.Wait(2000), "ch0 TryRead did not complete after ch1 Stop");
                    Assert.Null(readEx);
                    Assert.False(readReturned);
                }
                finally
                {
                    ch0.Stop();
                    ch1.Stop();
                }
            }
        }

        [Fact]
        public void Concurrent_Send_on_both_channels_does_not_fail()
        {
            Device? device;
            try
            {
                if (!TryOpenDualChannel(out device))
                {
                    Console.WriteLine("SKIP concurrent dual-channel Send: no dual-channel gs_usb device present.");
                    return;
                }
            }
            catch (GsCanException ex)
            {
                Console.WriteLine("SKIP concurrent dual-channel Send: " + ex.Message);
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
                    const int perThread = 100;
                    var errors = new ConcurrentBag<Exception>();
                    var barrier = new Barrier(3);
                    var stopReader = new ManualResetEventSlim(false);

                    var reader = new Thread(() =>
                    {
                        barrier.SignalAndWait();
                        while (!stopReader.IsSet)
                        {
                            try
                            {
                                ch0.TryRead(out _, 5);
                                ch1.TryRead(out _, 5);
                            }
                            catch (GsCanException)
                            {
                                break;
                            }
                        }
                    });
                    reader.IsBackground = true;
                    reader.Start();

                    void RunSender(Channel ch, byte tag)
                    {
                        barrier.SignalAndWait();
                        for (int i = 0; i < perThread; i++)
                        {
                            try
                            {
                                ch.Send(CanFrame.Classic(0x200, new byte[] { tag, (byte)i }));
                            }
                            catch (Exception ex)
                            {
                                errors.Add(ex);
                            }
                        }
                    }

                    var sender0 = new Thread(() => RunSender(ch0, 0));
                    var sender1 = new Thread(() => RunSender(ch1, 1));
                    sender0.IsBackground = true;
                    sender1.IsBackground = true;
                    sender0.Start();
                    sender1.Start();

                    Assert.True(sender0.Join(15000), "ch0 sender did not finish");
                    Assert.True(sender1.Join(15000), "ch1 sender did not finish");
                    stopReader.Set();
                    Assert.True(reader.Join(2000), "reader did not finish");
                    Assert.True(
                        errors.IsEmpty,
                        "concurrent dual-channel Send failed: "
                            + string.Join("; ", errors.Select(e => e.Message)));
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

        [Fact]
        public void Dual_pump_rx_does_not_lag_after_ch0_send_stops()
        {
            if (!TryOpenDualChannel(out var device))
            {
                Console.WriteLine("SKIP dual-pump RX lag: no dual-channel gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch0 = device.Channels[0];
                var ch1 = device.Channels[1];
                ch0.Start(new ChannelOptions { Bitrate = 500000 });
                ch1.Start(new ChannelOptions { Bitrate = 500000 });
                try
                {
                    var cts = new CancellationTokenSource();
                    int echo = 0;
                    int rx = 0;
                    int overflow = 0;
                    Exception? pumpError = null;
                    var pump0 = StartPump(ch0, cts.Token, frame =>
                    {
                        if (frame.Overflow)
                        {
                            Interlocked.Increment(ref overflow);
                        }

                        if (frame.Kind == CanFrameKind.Echo)
                        {
                            Interlocked.Increment(ref echo);
                        }
                    }, ex => Interlocked.CompareExchange(ref pumpError, ex, null));
                    var pump1 = StartPump(ch1, cts.Token, frame =>
                    {
                        if (frame.Overflow)
                        {
                            Interlocked.Increment(ref overflow);
                        }

                        if (frame.Kind == CanFrameKind.Rx)
                        {
                            Interlocked.Increment(ref rx);
                        }
                    }, ex => Interlocked.CompareExchange(ref pumpError, ex, null));

                    ch0.Send(CanFrame.Classic(0x100, new byte[] { 0x01 }));
                    var sawCross = WaitFor(() => Volatile.Read(ref rx) >= 1, 400);
                    if (!sawCross)
                    {
                        cts.Cancel();
                        pump0.Join(1000);
                        pump1.Join(1000);
                        Console.WriteLine("SKIP dual-pump RX lag: ch0 is not wired to ch1.");
                        return;
                    }

                    const int count = 80;
                    for (int i = 0; i < count; i++)
                    {
                        ch0.Send(CanFrame.Classic(0x100, new byte[] { (byte)i }));
                        Thread.Sleep(10);
                    }

                    Assert.True(
                        WaitFor(() => Volatile.Read(ref echo) >= count, 1000),
                        "Echo did not catch up after send, echo=" + Volatile.Read(ref echo));

                    int rxAtStop = Volatile.Read(ref rx);
                    var drain = Stopwatch.StartNew();
                    bool rxCaughtUp = WaitFor(() => Volatile.Read(ref rx) >= count, 500);
                    drain.Stop();

                    cts.Cancel();
                    pump0.Join(1000);
                    pump1.Join(1000);
                    Assert.Null(pumpError);

                    Assert.True(rxCaughtUp, "RX did not catch up after TX stop, rx=" + Volatile.Read(ref rx)
                        + " echo=" + Volatile.Read(ref echo)
                        + " overflow=" + Volatile.Read(ref overflow));
                    Assert.True(
                        drain.ElapsedMilliseconds < 200,
                        "RX trickled after TX stop: rxAtStop=" + rxAtStop
                            + " rx=" + Volatile.Read(ref rx)
                            + " drainMs=" + drain.ElapsedMilliseconds);
                    Assert.Equal(0, Volatile.Read(ref overflow));
                }
                finally
                {
                    ch0.Stop();
                    ch1.Stop();
                }
            }
        }

        private static Thread StartPump(
            Channel channel,
            CancellationToken token,
            Action<CanFrame> onFrame,
            Action<Exception>? onError = null)
        {
            var thread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!channel.TryRead(out var frame, 50))
                        {
                            continue;
                        }

                        onFrame(frame);
                        while (!token.IsCancellationRequested && channel.TryRead(out frame, 0))
                        {
                            onFrame(frame);
                        }
                    }
                    catch (GsCanException ex)
                    {
                        if (token.IsCancellationRequested
                            || ex.Message == "Channel is not started.")
                        {
                            break;
                        }

                        onError?.Invoke(ex);
                        break;
                    }
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(5);
            }

            return condition();
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
