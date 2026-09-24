using System.Globalization;

namespace GsCan.View.Session
{
    public sealed class FrameRow
    {
        public FrameRow(
            double relativeMilliseconds,
            int channel,
            string kind,
            uint id,
            bool extended,
            bool remote,
            bool isFd,
            bool bitRateSwitch,
            bool errorStateIndicator,
            bool overflow,
            int length,
            string dataHex,
            uint timestampMicroseconds)
        {
            RelativeMilliseconds = relativeMilliseconds;
            Channel = channel;
            Kind = kind;
            Id = id;
            Extended = extended;
            Remote = remote;
            IsFd = isFd;
            BitRateSwitch = bitRateSwitch;
            ErrorStateIndicator = errorStateIndicator;
            Overflow = overflow;
            Length = length;
            DataHex = dataHex;
            TimestampMicroseconds = timestampMicroseconds;
        }

        public double RelativeMilliseconds { get; }
        public int Channel { get; }
        public string Kind { get; }
        public uint Id { get; }

        public string IdHex => Extended
            ? Id.ToString("X8", CultureInfo.InvariantCulture)
            : Id.ToString("X3", CultureInfo.InvariantCulture);
        public bool Extended { get; }
        public bool Remote { get; }
        public bool IsFd { get; }
        public bool BitRateSwitch { get; }
        public bool ErrorStateIndicator { get; }
        public bool Overflow { get; }
        public int Length { get; }
        public string DataHex { get; }
        public uint TimestampMicroseconds { get; }
    }
}
