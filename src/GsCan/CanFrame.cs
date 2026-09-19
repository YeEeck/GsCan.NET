using System;

namespace GsCan
{
    public enum CanFrameKind
    {
        Rx = 0,
        Echo = 1,
        Error = 2
    }

    public readonly struct CanFrame
    {
        public CanFrame(
            uint id,
            byte[] data,
            CanFrameKind kind,
            bool extended = false,
            bool remote = false,
            bool fd = false,
            bool bitRateSwitch = false,
            bool errorStateIndicator = false,
            bool overflow = false,
            uint timestampMicroseconds = 0)
        {
            Id = id;
            Data = data ?? Array.Empty<byte>();
            Kind = kind;
            Extended = extended;
            Remote = remote;
            IsFd = fd;
            BitRateSwitch = bitRateSwitch;
            ErrorStateIndicator = errorStateIndicator;
            Overflow = overflow;
            TimestampMicroseconds = timestampMicroseconds;
        }

        public uint Id { get; }
        public byte[] Data { get; }
        public CanFrameKind Kind { get; }
        public bool Extended { get; }
        public bool Remote { get; }
        public bool IsFd { get; }
        public bool BitRateSwitch { get; }
        public bool ErrorStateIndicator { get; }
        public bool Overflow { get; }
        public uint TimestampMicroseconds { get; }

        public static CanFrame Classic(uint id, byte[] data, bool extended = false)
        {
            return new CanFrame(id, data, CanFrameKind.Rx, extended: extended);
        }

        public static CanFrame Fd(uint id, byte[] data, bool extended = false, bool bitRateSwitch = true)
        {
            return new CanFrame(id, data, CanFrameKind.Rx, extended: extended, fd: true, bitRateSwitch: bitRateSwitch);
        }
    }
}
