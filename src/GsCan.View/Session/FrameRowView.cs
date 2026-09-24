using System.ComponentModel;

namespace GsCan.View.Session
{
    public sealed class FrameRowView : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs AllArgs = new PropertyChangedEventArgs(string.Empty);

        private FrameRow? _row;

        public event PropertyChangedEventHandler? PropertyChanged;

        public double RelativeMilliseconds => _row?.RelativeMilliseconds ?? 0;
        public int Channel => _row?.Channel ?? 0;
        public string Kind => _row?.Kind ?? "Rx";
        public uint Id => _row?.Id ?? 0;
        public string IdHex => _row?.IdDisplay ?? string.Empty;
        public string ErrorHint => _row?.ErrorHint ?? string.Empty;
        public bool Extended => _row is { Extended: true };
        public bool Remote => _row is { Remote: true };
        public bool IsFd => _row is { IsFd: true };
        public bool BitRateSwitch => _row is { BitRateSwitch: true };
        public bool ErrorStateIndicator => _row is { ErrorStateIndicator: true };
        public bool Overflow => _row is { Overflow: true };
        public int Length => _row?.Length ?? 0;
        public string DataHex => _row?.DataHex ?? string.Empty;
        public uint TimestampMicroseconds => _row?.TimestampMicroseconds ?? 0;

        internal void CopyFrom(FrameRow row)
        {
            _row = row;
            PropertyChanged?.Invoke(this, AllArgs);
        }
    }
}
