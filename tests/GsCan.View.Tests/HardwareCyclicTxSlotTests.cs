using System;
using GsCan;
using GsCan.View.Session;
using Xunit;

namespace GsCan.View.Tests
{
    public class HardwareCyclicTxSlotTests
    {
        [Fact]
        public void Hardware_StopChannel_clears_cyclic_TxSlot_Enabled()
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                Console.WriteLine("SKIP hardware cyclic TxSlot StopChannel: no gs_usb device present.");
                return;
            }

            var session = new ViewSession(new GsCanPort());
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
                slot.PeriodMs = 50;
                slot.Enabled = true;
                Assert.True(slot.Enabled);

                session.StopChannel(0);
                Assert.False(session.Channels[0].IsRunning);
                Assert.False(slot.Enabled);
                Assert.Null(session.LastError);
            }
            finally
            {
                session.Close();
            }
        }
    }
}
