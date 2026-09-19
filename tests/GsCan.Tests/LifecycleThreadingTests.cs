using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace GsCan.Tests
{
    public class LifecycleThreadingTests
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
        public void Public_surface_has_no_State_events_Overflow_type_or_BERR_API()
        {
            var assembly = typeof(Device).Assembly;
            var exported = assembly.GetExportedTypes();

            Assert.DoesNotContain(exported, t => t.Name == "Overflow");
            Assert.Null(exported.FirstOrDefault(t =>
                t.Name.IndexOf("Berr", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Name.IndexOf("BusOff", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Name.IndexOf("Recovery", StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (var type in new[] { typeof(Device), typeof(Channel) })
            {
                Assert.Null(type.GetProperty("State", BindingFlags.Instance | BindingFlags.Public));
                Assert.Empty(type.GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static));
            }

            var overflow = typeof(CanFrame).GetProperty("Overflow", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(overflow);
            Assert.Equal(typeof(bool), overflow!.PropertyType);

            Assert.True(Enum.IsDefined(typeof(CanFrameKind), CanFrameKind.Error));
        }

        [Fact]
        public void CanFrame_Overflow_is_bool_flag_not_separate_kind()
        {
            var withOverflow = new CanFrame(0x40, Array.Empty<byte>(), CanFrameKind.Error, overflow: true);
            Assert.True(withOverflow.Overflow);
            Assert.Equal(CanFrameKind.Error, withOverflow.Kind);

            var clean = CanFrame.Classic(0x100, new byte[] { 1 });
            Assert.False(clean.Overflow);
        }

        [Fact]
        public void Stop_does_not_throw_when_not_started_or_after_Dispose()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP Stop never throws: no gs_usb device present.");
                return;
            }

            var ch = device!.Channels[0];
            ch.Stop();
            ch.Stop();
            device.Dispose();
            ch.Stop();
        }

        [Fact]
        public void Cross_thread_Send_while_TryRead_both_complete()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP cross-thread Send/TryRead: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    CanFrame readFrame = default;
                    var readOk = false;
                    Exception? readEx = null;
                    Exception? sendEx = null;
                    var readerDone = new ManualResetEventSlim(false);
                    var senderDone = new ManualResetEventSlim(false);

                    var reader = new Thread(() =>
                    {
                        try
                        {
                            readOk = ch.TryRead(out readFrame, 500);
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

                    Thread.Sleep(50);

                    var sender = new Thread(() =>
                    {
                        try
                        {
                            ch.Send(CanFrame.Classic(0x321, new byte[] { 0xDE, 0xAD }));
                        }
                        catch (Exception ex)
                        {
                            sendEx = ex;
                        }
                        finally
                        {
                            senderDone.Set();
                        }
                    });
                    sender.IsBackground = true;
                    sender.Start();

                    Assert.True(readerDone.Wait(2000), "TryRead timed out");
                    Assert.True(senderDone.Wait(2000), "Send timed out");
                    Assert.Null(sendEx);
                    Assert.Null(readEx);
                    Assert.True(readOk, "TryRead should observe Echo or Rx from concurrent Send");
                    Assert.True(
                        readFrame.Kind == CanFrameKind.Echo || readFrame.Kind == CanFrameKind.Rx,
                        "expected Echo or Rx, got " + readFrame.Kind);
                    Assert.Equal(0x321u, readFrame.Id);
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Stop_without_drain_then_Start_does_not_yield_Echo_from_discarded_Send()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP Stop leftover Echo: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    // Burst larger than a typical gs_usb TX queue so some Host TX stay unfinished.
                    for (uint i = 0; i < 64; i++)
                    {
                        ch.Send(CanFrame.Classic(0x700 + i, new byte[] { (byte)i }));
                    }

                    ch.Stop();
                    ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });

                    var echoes = new List<CanFrame>();
                    var deadline = DateTime.UtcNow.AddMilliseconds(100);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (ch.TryRead(out var frame, 20))
                        {
                            if (frame.Kind == CanFrameKind.Echo
                                && frame.Id >= 0x700
                                && frame.Id < 0x700 + 64)
                            {
                                echoes.Add(frame);
                            }
                        }
                    }

                    Assert.Empty(echoes);
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void ListenOnly_Start_succeeds_and_Send_fails_or_yields_no_Echo()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP ListenOnly: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions
                {
                    Bitrate = 500000,
                    ListenOnly = true,
                    Loopback = false
                });
                try
                {
                    GsCanException? sendEx = null;
                    try
                    {
                        ch.Send(CanFrame.Classic(0x111, new byte[] { 0x01 }));
                    }
                    catch (GsCanException ex)
                    {
                        sendEx = ex;
                    }

                    if (sendEx != null)
                    {
                        return;
                    }

                    var deadline = DateTime.UtcNow.AddMilliseconds(200);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (ch.TryRead(out var frame, 50))
                        {
                            Assert.NotEqual(CanFrameKind.Echo, frame.Kind);
                        }
                    }
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void OneShot_Start_succeeds_on_loopback()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP OneShot Start: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions
                {
                    Bitrate = 500000,
                    Loopback = true,
                    OneShot = true
                });
                try
                {
                    ch.Send(CanFrame.Classic(0x222, new byte[] { 0x02 }));
                    Assert.True(ch.TryRead(out var frame, 500));
                    Assert.True(frame.Kind == CanFrameKind.Echo || frame.Kind == CanFrameKind.Rx);
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Stop_unblocks_blocked_TryRead_with_GsCanException()
        {
            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP Stop unblocks TryRead: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                try
                {
                    Exception? readEx = null;
                    var readerDone = new ManualResetEventSlim(false);
                    var reader = new Thread(() =>
                    {
                        try
                        {
                            ch.TryRead(out _, 5000);
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
                    ch.Stop();

                    Assert.True(readerDone.Wait(2000), "blocked TryRead did not unblock after Stop");
                    Assert.IsType<GsCanException>(readEx);
                }
                finally
                {
                    ch.Stop();
                }
            }
        }

        [Fact]
        public void Unplug_during_use_fails_with_GsCanException()
        {
            if (!string.Equals(
                    Environment.GetEnvironmentVariable("GSCAN_UNPLUG_TEST"),
                    "1",
                    StringComparison.Ordinal))
            {
                Console.WriteLine(
                    "SKIP unplug: set GSCAN_UNPLUG_TEST=1 and unplug the adapter during the wait.");
                return;
            }

            if (!TryOpenFirst(out var device))
            {
                Console.WriteLine("SKIP unplug: no gs_usb device present.");
                return;
            }

            using (device!)
            {
                var ch = device.Channels[0];
                ch.Start(new ChannelOptions { Bitrate = 500000, Loopback = true });
                Console.WriteLine("Unplug the adapter now (10s window)...");

                Action readUntilFailure = () =>
                {
                    // Surprise unplug must surface as GsCanException, not a fake frame.
                    while (true)
                    {
                        ch.TryRead(out _, 1000);
                    }
                };

                Assert.Throws<GsCanException>(readUntilFailure);
            }
        }
    }
}
