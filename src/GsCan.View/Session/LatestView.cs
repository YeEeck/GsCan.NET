using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using GsCan;

namespace GsCan.View.Session
{
    public enum ReceiveMode
    {
        Trace,
        Latest
    }

    public sealed class LatestRow : INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs CountArgs = new PropertyChangedEventArgs(nameof(Count));
        private static readonly PropertyChangedEventArgs RelativeArgs = new PropertyChangedEventArgs(nameof(RelativeMilliseconds));
        private static readonly PropertyChangedEventArgs BrsArgs = new PropertyChangedEventArgs(nameof(BitRateSwitch));
        private static readonly PropertyChangedEventArgs EsiArgs = new PropertyChangedEventArgs(nameof(ErrorStateIndicator));
        private static readonly PropertyChangedEventArgs OverflowArgs = new PropertyChangedEventArgs(nameof(Overflow));
        private static readonly PropertyChangedEventArgs LengthArgs = new PropertyChangedEventArgs(nameof(Length));
        private static readonly PropertyChangedEventArgs DataArgs = new PropertyChangedEventArgs(nameof(DataHex));
        private static readonly PropertyChangedEventArgs TimestampArgs = new PropertyChangedEventArgs(nameof(TimestampMicroseconds));
        private static readonly PropertyChangedEventArgs IdHexArgs = new PropertyChangedEventArgs(nameof(IdHex));
        private static readonly PropertyChangedEventArgs ErrorClassArgs = new PropertyChangedEventArgs(nameof(ErrorClass));
        private static readonly PropertyChangedEventArgs ErrorHintArgs = new PropertyChangedEventArgs(nameof(ErrorHint));
        private static readonly PropertyChangedEventArgs DirectionArgs = new PropertyChangedEventArgs(nameof(Direction));
        private static readonly PropertyChangedEventArgs PeriodArgs = new PropertyChangedEventArgs(nameof(PeriodText));
        private static readonly PropertyChangedEventArgs HighlightStateArgs = new PropertyChangedEventArgs(nameof(Highlight));
        private static readonly PropertyChangedEventArgs IsHighlightedArgs = new PropertyChangedEventArgs(nameof(IsHighlighted));
        private static readonly PropertyChangedEventArgs IsPairedArgs = new PropertyChangedEventArgs(nameof(IsPaired));
        private static readonly PropertyChangedEventArgs PairTagArgs = new PropertyChangedEventArgs(nameof(PairTag));

        private FrameRow _frame;
        private int _count;

        // 周期统计用设备硬件时间戳（微秒）。批量取包时本地时钟会被压平，
        // 用 DateTime 算会得出荒谬结果。uint32 约 71 分钟回绕，靠无符号减法处理。
        private uint _lastTs;
        private bool _hasTs;
        private long _tsTotalUs;
        private int _tsSamples;

        public LatestRow(FrameRow frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _count = 1;
            _lastTs = frame.TimestampMicroseconds;
            _hasTs = _lastTs != 0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Count => _count;
        public double RelativeMilliseconds => _frame.RelativeMilliseconds;
        public int Channel => _frame.Channel;
        public string Kind => _frame.Kind;
        public uint Id => _frame.Id;
        public string IdHex => _frame.IdDisplay;
        public string ErrorClass => _frame.ErrorClass;
        public string ErrorHint => _frame.ErrorHint;
        public bool Extended => _frame.Extended;
        public bool Remote => _frame.Remote;
        public bool IsFd => _frame.IsFd;
        public bool BitRateSwitch => _frame.BitRateSwitch;
        public bool ErrorStateIndicator => _frame.ErrorStateIndicator;
        public bool Overflow => _frame.Overflow;
        public int Length => _frame.Length;
        public string DataHex => _frame.DataHex;
        public uint TimestampMicroseconds => _frame.TimestampMicroseconds;

        /// <summary>
        /// 相对本机而言的方向。Echo 是本机发出的，Rx 是总线上别的节点发来的。
        /// 心跳帧刷屏时靠这一列区分「我发的」和「他发的」。
        /// </summary>
        /// <summary>最近一次到达的帧时间戳。倒序排列用它。</summary>
        public uint LastTimestampMicroseconds => _frame.TimestampMicroseconds;

        /// <summary>这一行当前是否处于发送/应答高亮。</summary>
        public RowHighlight Highlight { get; private set; }

        public bool IsHighlighted => Highlight != RowHighlight.None;

        public bool IsPaired => Highlight == RowHighlight.Paired;

        private string _pairTag = string.Empty;

        public string PairTag => _pairTag;

        internal void SetHighlight(RowHighlight kind, string tag)
        {
            if (Highlight == kind && _pairTag == tag)
            {
                return;
            }

            Highlight = kind;
            _pairTag = tag ?? string.Empty;
            var h = PropertyChanged;
            if (h == null)
            {
                return;
            }

            h(this, HighlightStateArgs);
            h(this, IsHighlightedArgs);
            h(this, IsPairedArgs);
            h(this, PairTagArgs);
        }

        public string Direction => Kind switch
        {
            "Echo" => "发送 →",
            "Error" => "← 错误!",
            _ => "← 接收",
        };

        /// <summary>实测周期。用设备硬件时间戳算，避免批量取包时本地时间被压平。</summary>
        public string PeriodText
        {
            get
            {
                if (_count < 2)
                {
                    return "—";
                }

                var period = PeriodMilliseconds;
                if (period <= 0)
                {
                    return "—";
                }

                if (period < 1)
                {
                    return $"{period * 1000:0}µs";
                }

                if (period < 1000)
                {
                    return $"{period:0.0}ms";
                }

                return $"{period / 1000:0.00}s";
            }
        }

        /// <summary>实测周期（毫秒）。样本不足时返回 0。</summary>
        internal double PeriodMilliseconds
            => _tsSamples > 0 && _tsTotalUs > 0 ? _tsTotalUs / (double)_tsSamples / 1000.0 : 0;

        internal bool IsDirty { get; set; }

        internal void Observe(FrameRow frame)
        {
            Apply(frame, 1);
            Publish();
        }

        internal void Apply(FrameRow frame, int add)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _count += add;
            AccumulatePeriod(frame);
        }

        private void AccumulatePeriod(FrameRow frame)
        {
            uint ts = frame.TimestampMicroseconds;
            if (ts == 0)
            {
                return;
            }

            if (!_hasTs)
            {
                _hasTs = true;
                _lastTs = ts;
                return;
            }

            uint delta = ts - _lastTs;              // 无符号减法自动跨回绕
            if (delta > 0 && delta < 10_000_000)    // 丢掉明显不合理的间隔（>10s）
            {
                _tsTotalUs += delta;
                _tsSamples++;
            }

            _lastTs = ts;
        }

        internal void Publish()
        {
            var handler = PropertyChanged;
            if (handler == null)
            {
                return;
            }

            handler(this, CountArgs);
            handler(this, RelativeArgs);
            handler(this, BrsArgs);
            handler(this, EsiArgs);
            handler(this, OverflowArgs);
            handler(this, LengthArgs);
            handler(this, DataArgs);
            handler(this, TimestampArgs);
            handler(this, IdHexArgs);
            handler(this, ErrorClassArgs);
            handler(this, ErrorHintArgs);
            handler(this, DirectionArgs);
            handler(this, PeriodArgs);
        }
    }

    internal sealed class LatestView
    {
        private readonly ObservableCollection<LatestRow> _rows;
        private readonly Dictionary<Key, LatestRow> _byKey = new Dictionary<Key, LatestRow>();
        private readonly List<LatestRow> _dirty = new List<LatestRow>();

        public LatestView(ObservableCollection<LatestRow> rows)
        {
            _rows = rows;
        }

        public void Observe(FrameRow row)
        {
            var key = Key.From(row);
            if (_byKey.TryGetValue(key, out var existing))
            {
                existing.Observe(row);
                MoveToTop(existing);
                return;
            }

            var created = new LatestRow(row);
            _byKey.Add(key, created);
            _rows.Insert(0, created);   // 新 ID 放最前，配合「按到达时间倒序」
        }

        public void ObserveMany(IReadOnlyList<FrameRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            if (rows.Count == 1)
            {
                Observe(rows[0]);
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var key = Key.From(row);
                if (_byKey.TryGetValue(key, out var existing))
                {
                    existing.Apply(row, 1);
                    if (!existing.IsDirty)
                    {
                        existing.IsDirty = true;
                        _dirty.Add(existing);
                    }

                    // 按到达时间倒序：这一条刚有帧，挪到最前
                    MoveToTop(existing);
                    continue;
                }

                var created = new LatestRow(row);
                _byKey.Add(key, created);
                _rows.Insert(0, created);
            }

            for (int i = 0; i < _dirty.Count; i++)
            {
                _dirty[i].Publish();
                _dirty[i].IsDirty = false;
            }

            _dirty.Clear();
        }

        public void Clear()
        {
            _rows.Clear();
            _byKey.Clear();
            _dirty.Clear();
        }

        /// <summary>把某一行挪到最前（按到达时间倒序）。已经在最前就不动。</summary>
        private void MoveToTop(LatestRow row)
        {
            int index = _rows.IndexOf(row);
            if (index > 0)
            {
                _rows.Move(index, 0);
            }
        }

        /// <summary>找出「最近一次到达时间」等于该时间戳的行，用于高亮。</summary>
        public LatestRow? FindByTimestamp(uint timestampUs)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].LastTimestampMicroseconds == timestampUs)
                {
                    return _rows[i];
                }
            }

            return null;
        }

        /// <summary>清除所有行的高亮。</summary>
        public void ClearHighlights()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].SetHighlight(RowHighlight.None, string.Empty);
            }
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int _channel;
            private readonly CanFrameKind _kind;
            private readonly uint _id;
            private readonly bool _extended;
            private readonly bool _remote;
            private readonly bool _isFd;
            private readonly string _errorClass;

            private Key(int channel, CanFrameKind kind, uint id, bool extended, bool remote, bool isFd, string errorClass)
            {
                _channel = channel;
                _kind = kind;
                _id = id;
                _extended = extended;
                _remote = remote;
                _isFd = isFd;
                _errorClass = errorClass;
            }

            public static Key From(FrameRow row)
            {
                if (row.FrameKind == CanFrameKind.Error)
                {
                    return new Key(row.Channel, row.FrameKind, 0, false, false, false, row.ErrorClass ?? string.Empty);
                }

                return new Key(row.Channel, row.FrameKind, row.Id, row.Extended, row.Remote, row.IsFd, string.Empty);
            }

            public bool Equals(Key other)
            {
                return _channel == other._channel
                    && _id == other._id
                    && _extended == other._extended
                    && _remote == other._remote
                    && _isFd == other._isFd
                    && _kind == other._kind
                    && string.Equals(_errorClass, other._errorClass, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_channel, _kind, _id, _extended, _remote, _isFd, _errorClass);
            }
        }
    }
}
