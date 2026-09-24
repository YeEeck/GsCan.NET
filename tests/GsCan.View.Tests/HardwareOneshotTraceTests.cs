using System;
using System.Linq;
using System.Threading;
using GsCan;
using GsCan.View.Session;
using Xunit;

namespace GsCan.View.Tests
{
    public class HardwareOneshotTraceTests
    {
        [Fact]
        public void Hardware_loopback_oneshot_shows_Echo_and_Rx_in_Trace()
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                Console.WriteLine("SKIP hardware loopback oneshot Trace: no gs_usb device present.");
                return;
            }

            var session = new ViewSession(new GsCanPort(), runBackgroundPumps: false);
            try
            {
                session.Open(list[0]);
                session.Channels[0].Loopback = true;
                session.StartChannel(0);
                Assert.Null(session.LastError);
                Assert.True(session.Channels[0].IsRunning);

                var slot = session.TxSlots[0];
                slot.Channel = 0;
                slot.Id = 0x123;
                slot.PeriodMs = 0;
                slot.Data = new byte[] { 0x11, 0x22 };
                session.SendOnce(0);

                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < deadline)
                {
                    session.PumpUntilIdle();
                    if (session.Trace.Any(row => row.Kind == "Echo" && row.Id == 0x123u) &&
                        session.Trace.Any(row => row.Kind == "Rx" && row.Id == 0x123u))
                    {
                        break;
                    }

                    Thread.Sleep(20);
                }

                Assert.Contains(session.Trace, row => row.Kind == "Echo" && row.Id == 0x123u);
                Assert.Contains(session.Trace, row => row.Kind == "Rx" && row.Id == 0x123u);
                Assert.All(session.Trace, row =>
                {
                    Assert.Contains(row.Kind, new[] { "Rx", "Echo", "Error" });
                    Assert.DoesNotContain("成功", row.Kind);
                    Assert.DoesNotContain("总线", row.Kind);
                });
            }
            finally
            {
                session.Close();
            }
        }
    }
}
