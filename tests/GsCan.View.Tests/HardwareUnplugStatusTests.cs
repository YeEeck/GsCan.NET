using System;
using System.Threading;
using GsCan;
using GsCan.View.Session;
using Xunit;

namespace GsCan.View.Tests
{
    public class HardwareUnplugStatusTests
    {
        [Fact]
        public void Hardware_loopback_counts_Rx_and_Echo_in_status()
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                Console.WriteLine("SKIP hardware status counts: no gs_usb device present.");
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
                    if (session.Status.TxCount >= 1 && session.Status.RxCount >= 1)
                    {
                        break;
                    }

                    Thread.Sleep(20);
                }

                Assert.True(session.Status.TxCount >= 1);
                Assert.True(session.Status.RxCount >= 1);
                Assert.Contains(session.Trace, row => row.Kind == "Echo" && row.Id == 0x123u);
                Assert.Contains(session.Trace, row => row.Kind == "Rx" && row.Id == 0x123u);
            }
            finally
            {
                session.Close();
            }
        }
    }
}
