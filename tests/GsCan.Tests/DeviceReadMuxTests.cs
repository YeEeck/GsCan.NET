using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using GsCan;

namespace GsCan.Tests
{
    public class DeviceReadMuxTests
    {
        [Fact]
        public void Foreign_frame_is_queued_for_the_other_channel()
        {
            var mux = new DeviceReadMux(2);
            var usb = new FakeUsb();
            usb.Push(1, Frame(0x11, CanFrameKind.Rx));
            usb.Push(0, Frame(0x10, CanFrameKind.Echo));

            Assert.True(Read(mux, usb, 0, 50, out var echo));
            Assert.Equal(CanFrameKind.Echo, echo.Kind);
            Assert.Equal(0x10u, echo.Id);

            Assert.True(Read(mux, usb, 1, 0, out var rx));
            Assert.Equal(CanFrameKind.Rx, rx.Kind);
            Assert.Equal(0x11u, rx.Id);
        }

        [Fact]
        public void Queued_rx_is_readable_while_peer_channel_waits_on_usb()
        {
            var mux = new DeviceReadMux(2);
            var usb = new FakeUsb { BlockUntilPulse = true };
            usb.Push(1, Frame(0x11, CanFrameKind.Rx));
            Assert.False(Read(mux, usb, 0, 0, out _));

            var cts = new CancellationTokenSource();
            var ch0 = new Thread(() => Read(mux, usb, 0, 5000, cts.Token, out _));
            ch0.IsBackground = true;
            ch0.Start();
            Assert.True(usb.Waiting.Wait(1000), "ch0 did not enter USB wait");

            bool got = false;
            var drain = Stopwatch.StartNew();
            var ch1 = new Thread(() =>
            {
                got = Read(mux, usb, 1, 5000, CancellationToken.None, out _);
                drain.Stop();
            });
            ch1.IsBackground = true;
            ch1.Start();

            bool finished = ch1.Join(300);
            usb.Pulse();
            cts.Cancel();
            Assert.True(ch0.Join(1000), "ch0 USB wait did not unblock");
            Assert.True(ch1.Join(1000), "ch1 did not finish after USB wait ended");

            Assert.True(finished, "ch1 dequeue blocked behind ch0 USB wait");
            Assert.True(got, "ch1 did not receive the queued RX");
            Assert.True(
                drain.ElapsedMilliseconds < 80,
                "ch1 dequeue waited on ch0 USB, drainMs=" + drain.ElapsedMilliseconds);
        }

        private static bool Read(
            DeviceReadMux mux,
            FakeUsb usb,
            int channel,
            int timeoutMs,
            out CanFrame frame)
        {
            return Read(mux, usb, channel, timeoutMs, CancellationToken.None, out frame);
        }

        private static bool Read(
            DeviceReadMux mux,
            FakeUsb usb,
            int channel,
            int timeoutMs,
            CancellationToken token,
            out CanFrame frame)
        {
            return mux.TryRead(
                channel,
                timeoutMs,
                usb.Read,
                _ => true,
                () => token.IsCancellationRequested,
                () => { },
                () => { },
                out frame);
        }

        private static CanFrame Frame(uint id, CanFrameKind kind)
        {
            return new CanFrame(id, new byte[] { 0xAA }, kind);
        }

        private sealed class FakeUsb
        {
            private readonly Queue<Item> _pending = new Queue<Item>();
            private readonly object _gate = new object();

            public bool BlockUntilPulse { get; set; }

            public ManualResetEventSlim Waiting { get; } = new ManualResetEventSlim(false);

            private bool _released;

            public void Push(int channel, CanFrame frame)
            {
                lock (_gate)
                {
                    _pending.Enqueue(new Item(channel, frame));
                    Monitor.PulseAll(_gate);
                }
            }

            public void Pulse()
            {
                lock (_gate)
                {
                    _released = true;
                    Monitor.PulseAll(_gate);
                }
            }

            public bool Read(int timeoutMilliseconds, out CanFrame frame, out int channel)
            {
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        if (timeoutMilliseconds <= 0 || _released)
                        {
                            frame = default;
                            channel = -1;
                            return false;
                        }

                        Waiting.Set();
                        if (BlockUntilPulse)
                        {
                            while (!_released)
                            {
                                Monitor.Wait(_gate);
                            }
                        }
                        else
                        {
                            Monitor.Wait(_gate, timeoutMilliseconds);
                        }
                        if (_pending.Count == 0 || _released)
                        {
                            frame = default;
                            channel = -1;
                            return false;
                        }
                    }

                    var item = _pending.Dequeue();
                    channel = item.Channel;
                    frame = item.Frame;
                    return true;
                }
            }

            private readonly struct Item
            {
                public Item(int channel, CanFrame frame)
                {
                    Channel = channel;
                    Frame = frame;
                }

                public int Channel { get; }
                public CanFrame Frame { get; }
            }
        }
    }
}
