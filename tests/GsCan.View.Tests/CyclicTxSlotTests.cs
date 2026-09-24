using System;
using GsCan;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    public class CyclicTxSlotTests
    {
        [Fact]
        public void Enabled_period_sends_on_each_fake_clock_period()
        {
            var (session, opened, clock) = OpenStarted();
            var slot = session.TxSlots[0];
            slot.Channel = 0;
            slot.Id = 0x100;
            slot.Data = new byte[] { 0x01 };
            slot.PeriodMs = 10;
            slot.Enabled = true;

            Assert.Empty(opened.Sent);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Single(opened.Sent);
            Assert.Equal(0, opened.Sent[0].Channel);
            Assert.Equal(0x100u, opened.Sent[0].Frame.Id);
            Assert.Equal(new byte[] { 0x01 }, opened.Sent[0].Frame.Data);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Equal(2, opened.Sent.Count);

            clock.Advance(TimeSpan.FromMilliseconds(9));
            Assert.Equal(2, opened.Sent.Count);

            clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(3, opened.Sent.Count);
        }

        [Fact]
        public void StopChannel_stops_cyclic_send_and_does_not_resume_on_Start()
        {
            var (session, opened, clock) = OpenStarted();
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x100);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Single(opened.Sent);
            Assert.True(session.TxSlots[0].Enabled);

            session.StopChannel(0);
            Assert.False(session.TxSlots[0].Enabled);

            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(opened.Sent);

            session.StartChannel(0);
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(opened.Sent);
            Assert.False(session.TxSlots[0].Enabled);
        }

        [Fact]
        public void ListenOnly_stops_running_cyclic_and_does_not_send()
        {
            var (session, opened, clock) = OpenStarted();
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x200);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Single(opened.Sent);

            session.Channels[0].ListenOnly = true;
            Assert.False(session.TxSlots[0].Enabled);

            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(opened.Sent);
        }

        [Fact]
        public void Start_with_ListenOnly_does_not_send_cyclic_TxSlot()
        {
            var (session, opened, clock) = OpenStarted(listenOnly: true);
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x300);

            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Empty(opened.Sent);
            Assert.False(session.TxSlots[0].Enabled);
        }

        [Fact]
        public void Clearing_Enabled_stops_cyclic_send()
        {
            var (session, opened, clock) = OpenStarted();
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x400);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Single(opened.Sent);

            session.TxSlots[0].Enabled = false;
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(opened.Sent);
        }

        [Fact]
        public void Period_zero_still_sends_only_on_SendOnce()
        {
            var (session, opened, clock) = OpenStarted();
            var slot = session.TxSlots[0];
            slot.Channel = 0;
            slot.Id = 0x123;
            slot.Data = new byte[] { 0xAA };
            slot.PeriodMs = 0;
            slot.Enabled = true;

            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Empty(opened.Sent);

            session.SendOnce(0);
            Assert.Single(opened.Sent);
            Assert.Equal(0x123u, opened.Sent[0].Frame.Id);
        }

        [Fact]
        public void Close_stops_cyclic_send()
        {
            var (session, opened, clock) = OpenStarted();
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x111);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Single(opened.Sent);

            session.Close();
            Assert.False(session.TxSlots[0].Enabled);

            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Single(opened.Sent);
        }

        [Fact]
        public void StopChannel_does_not_stop_cyclic_send_on_the_other_channel()
        {
            var (session, opened, clock) = OpenStarted();
            session.StartChannel(1);
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x10);
            EnableCyclic(session, slotIndex: 1, channel: 1, id: 0x11);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Equal(2, opened.Sent.Count);

            session.StopChannel(0);
            Assert.False(session.TxSlots[0].Enabled);
            Assert.True(session.TxSlots[1].Enabled);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Equal(3, opened.Sent.Count);
            Assert.Equal(1, opened.Sent[2].Channel);
            Assert.Equal(0x11u, opened.Sent[2].Frame.Id);
        }

        [Fact]
        public void Removing_a_cyclic_TxSlot_stops_it_and_keeps_the_other()
        {
            var (session, opened, clock) = OpenStarted();
            session.StartChannel(1);
            EnableCyclic(session, slotIndex: 0, channel: 0, id: 0x10);
            EnableCyclic(session, slotIndex: 1, channel: 1, id: 0x11);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Equal(2, opened.Sent.Count);

            session.RemoveTxSlot(session.TxSlots[0]);
            Assert.Single(session.TxSlots);
            Assert.Equal(0, session.TxSlots[0].Index);
            Assert.True(session.TxSlots[0].Enabled);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.Equal(3, opened.Sent.Count);
            Assert.Equal(1, opened.Sent[2].Channel);
            Assert.Equal(0x11u, opened.Sent[2].Frame.Id);
        }

        private static void EnableCyclic(ViewSession session, int slotIndex, int channel, uint id)
        {
            while (session.TxSlots.Count <= slotIndex)
            {
                session.AddTxSlot();
            }

            var slot = session.TxSlots[slotIndex];
            slot.Channel = channel;
            slot.Id = id;
            slot.Data = new byte[] { 0x01 };
            slot.PeriodMs = 10;
            slot.Enabled = true;
        }

        private static (ViewSession Session, FakeOpenedDevice Opened, FakeClock Clock) OpenStarted(
            bool listenOnly = false)
        {
            var clock = new FakeClock();
            var info = new DeviceInfo(@"\\?\usb#a", 2);
            var port = new FakeGsCanPort();
            var session = new ViewSession(port, runBackgroundPumps: false, clock: clock);
            session.Open(info);
            session.Channels[0].ListenOnly = listenOnly;
            session.StartChannel(0);
            return (session, port.LastOpenedDevice!, clock);
        }
    }
}
