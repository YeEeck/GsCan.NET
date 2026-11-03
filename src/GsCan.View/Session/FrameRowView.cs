using System.ComponentModel;

namespace GsCan.View.Session
{
    /// <summary>Trace/Latest 里一行的高亮来源。</summary>
    public enum RowHighlight
    {
        /// <summary>普通行。</summary>
        None,

        /// <summary>本机刚发出的那一帧（Echo）。</summary>
        Sent,

        /// <summary>和刚才那次发送配对的应答（Rx）。</summary>
        Paired,
    }

    public sealed class FrameRowView : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs AllArgs = new PropertyChangedEventArgs(string.Empty);
        private static readonly PropertyChangedEventArgs HighlightArgs = new PropertyChangedEventArgs(nameof(Highlight));
        private static readonly PropertyChangedEventArgs IsHighlightedArgs = new PropertyChangedEventArgs(nameof(IsHighlighted));
        private static readonly PropertyChangedEventArgs IsPairedArgs = new PropertyChangedEventArgs(nameof(IsPaired));
        private static readonly PropertyChangedEventArgs PairTagArgs = new PropertyChangedEventArgs(nameof(PairTag));

        private FrameRow? _row;
        private RowHighlight _highlight;
        private string _pairTag = string.Empty;

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

        /// <summary>当前行的性质：发出的 / 配对的应答 / 普通。</summary>
        public RowHighlight Highlight
        {
            get => _highlight;
            private set
            {
                if (_highlight == value)
                {
                    return;
                }

                _highlight = value;
                PropertyChanged?.Invoke(this, HighlightArgs);
                PropertyChanged?.Invoke(this, IsHighlightedArgs);
                PropertyChanged?.Invoke(this, IsPairedArgs);
            }
        }

        /// <summary>是否处于高亮状态（发送的或配对的都算）。</summary>
        public bool IsHighlighted => _highlight != RowHighlight.None;

        /// <summary>是否是「配对的应答」。</summary>
        public bool IsPaired => _highlight == RowHighlight.Paired;

        /// <summary>高亮角标文字，例如「发送」「应答 1.2ms」。</summary>
        public string PairTag
        {
            get => _pairTag;
            private set
            {
                if (_pairTag == value)
                {
                    return;
                }

                _pairTag = value ?? string.Empty;
                PropertyChanged?.Invoke(this, PairTagArgs);
            }
        }

        internal void CopyFrom(FrameRow row)
        {
            _row = row;
            PropertyChanged?.Invoke(this, AllArgs);
        }

        /// <summary>设置高亮。传 None 清除。</summary>
        internal void SetHighlight(RowHighlight highlight, string pairTag = "")
        {
            Highlight = highlight;
            PairTag = highlight == RowHighlight.None ? string.Empty : pairTag;
        }
    }
}
