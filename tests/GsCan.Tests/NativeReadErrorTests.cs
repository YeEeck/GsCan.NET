using System;
using System.Threading;
using GsCan;

namespace GsCan.Tests
{
    public class NativeReadErrorTests
    {
        [Fact]
        public void Failed_read_with_native_error_zero_is_timeout_not_fatal()
        {
            Assert.True(
                Device.NativeReadErrorIsTimeout(0),
                "native error 0 after a failed read is Send overwriting last_error, not a fatal USB error");
            Assert.True(Device.NativeReadErrorIsTimeout(15));
            Assert.False(Device.NativeReadErrorIsTimeout(17));
        }

        [Fact]
        public void Concurrent_send_ok_must_not_turn_read_timeout_into_fatal_error()
        {
            int lastError = 0;
            int fatals = 0;
            int timeouts = 0;
            var barrier = new Barrier(2);
            var stop = DateTime.UtcNow.AddMilliseconds(250);

            var reader = new Thread(() =>
            {
                barrier.SignalAndWait();
                while (DateTime.UtcNow < stop)
                {
                    Volatile.Write(ref lastError, 15);
                    Thread.SpinWait(32);
                    int err = Volatile.Read(ref lastError);
                    if (Device.NativeReadErrorIsTimeout(err))
                    {
                        Interlocked.Increment(ref timeouts);
                    }
                    else
                    {
                        Interlocked.Increment(ref fatals);
                    }
                }
            });
            var sender = new Thread(() =>
            {
                barrier.SignalAndWait();
                while (DateTime.UtcNow < stop)
                {
                    Volatile.Write(ref lastError, 0);
                    Thread.SpinWait(32);
                }
            });

            reader.IsBackground = true;
            sender.IsBackground = true;
            reader.Start();
            sender.Start();
            Assert.True(reader.Join(2000), "reader did not finish");
            Assert.True(sender.Join(2000), "sender did not finish");

            Assert.True(timeouts > 0, "replica never observed a timeout");
            Assert.Equal(0, fatals);
        }
    }
}
