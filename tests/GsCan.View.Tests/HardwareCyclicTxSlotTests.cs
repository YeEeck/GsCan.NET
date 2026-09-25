using System;
using System.Threading;
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
                if (session.Channels.Count == 0)
                {
                    Console.WriteLine("SKIP hardware cyclic TxSlot StopChannel: Open failed: " + session.LastError);
                    return;
                }

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

        [Fact]
        public void Hardware_SendOnce_during_cyclic_does_not_stop_cyclic()
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                Console.WriteLine("SKIP hardware SendOnce during cyclic: no gs_usb device present.");
                return;
            }

            var session = new ViewSession(new GsCanPort());
            try
            {
                session.Open(list[0]);
                if (session.Channels.Count == 0)
                {
                    Console.WriteLine("SKIP hardware SendOnce during cyclic: Open failed: " + session.LastError);
                    return;
                }

                session.Channels[0].Loopback = true;
                session.StartChannel(0);
                Assert.Null(session.LastError);
                Assert.True(session.Channels[0].IsRunning);

                var slot = session.TxSlots[0];
                slot.Channel = 0;
                slot.Id = 0x123;
                slot.PeriodMs = 10;
                slot.Enabled = true;
                Assert.True(slot.Enabled);

                for (int i = 0; i < 20; i++)
                {
                    session.SendOnce(0);
                    Thread.Sleep(5);
                }

                Assert.Null(session.LastError);
                Assert.True(slot.Enabled);

                session.AddTxSlot();
                var extra = session.TxSlots[1];
                extra.Channel = 0;
                extra.Id = 0x124;
                extra.PeriodMs = 0;
                for (int i = 0; i < 20; i++)
                {
                    session.SendOnce(1);
                    Thread.Sleep(5);
                }

                Assert.Null(session.LastError);
                Assert.True(session.TxSlots[0].Enabled);
            }
            finally
            {
                session.Close();
            }
        }

        [Fact]
        public void Hardware_SendOnce_during_cyclic_Fd_does_not_stop_cyclic()
        {
            var list = Device.List();
            if (list.Count == 0)
            {
                Console.WriteLine("SKIP hardware FD SendOnce during cyclic: no gs_usb device present.");
                return;
            }

            var session = new ViewSession(new GsCanPort());
            try
            {
                session.Open(list[0]);
                if (session.Channels.Count == 0)
                {
                    Console.WriteLine("SKIP hardware FD SendOnce during cyclic: Open failed: " + session.LastError);
                    return;
                }

                session.Channels[0].Loopback = true;
                session.Channels[0].FdEnabled = true;
                session.StartChannel(0);
                if (session.LastError != null || !session.Channels[0].IsRunning)
                {
                    Console.WriteLine("SKIP hardware FD SendOnce during cyclic: FD Start failed: " + session.LastError);
                    return;
                }

                var slot = session.TxSlots[0];
                slot.Channel = 0;
                slot.Id = 0x123;
                slot.Fd = true;
                slot.BitRateSwitch = true;
                slot.PeriodMs = 10;
                slot.Enabled = true;

                for (int i = 0; i < 20; i++)
                {
                    session.SendOnce(0);
                    Thread.Sleep(5);
                }

                Assert.Null(session.LastError);
                Assert.True(slot.Enabled);
            }
            finally
            {
                session.Close();
            }
        }
    }
}
