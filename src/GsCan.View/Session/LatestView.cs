using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GsCan.View.Session
{
    public enum ReceiveMode
    {
        Trace,
        Latest
    }

    public sealed class LatestRow : INotifyPropertyChanged
    {
        private FrameRow _frame;
        private int _count;

        public LatestRow(FrameRow frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _count = 1;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Count => _count;
        public double RelativeMilliseconds => _frame.RelativeMilliseconds;
        public int Channel => _frame.Channel;
        public string Kind => _frame.Kind;
        public uint Id => _frame.Id;
        public string IdHex => _frame.IdHex;
        public bool Extended => _frame.Extended;
        public bool Remote => _frame.Remote;
        public bool IsFd => _frame.IsFd;
        public bool BitRateSwitch => _frame.BitRateSwitch;
        public bool ErrorStateIndicator => _frame.ErrorStateIndicator;
        public bool Overflow => _frame.Overflow;
        public int Length => _frame.Length;
        public string DataHex => _frame.DataHex;
        public uint TimestampMicroseconds => _frame.TimestampMicroseconds;

        internal void Observe(FrameRow frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _count++;
            Raise(nameof(Count));
            Raise(nameof(RelativeMilliseconds));
            Raise(nameof(BitRateSwitch));
            Raise(nameof(ErrorStateIndicator));
            Raise(nameof(Overflow));
            Raise(nameof(Length));
            Raise(nameof(DataHex));
            Raise(nameof(TimestampMicroseconds));
        }

        private void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class LatestView
    {
        private readonly ObservableCollection<LatestRow> _rows;
        private readonly Dictionary<Key, LatestRow> _byKey = new Dictionary<Key, LatestRow>();

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
                return;
            }

            var created = new LatestRow(row);
            _byKey.Add(key, created);
            _rows.Add(created);
        }

        public void Clear()
        {
            _rows.Clear();
            _byKey.Clear();
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int _channel;
            private readonly string _kind;
            private readonly uint _id;
            private readonly bool _extended;
            private readonly bool _remote;
            private readonly bool _isFd;

            private Key(int channel, string kind, uint id, bool extended, bool remote, bool isFd)
            {
                _channel = channel;
                _kind = kind;
                _id = id;
                _extended = extended;
                _remote = remote;
                _isFd = isFd;
            }

            public static Key From(FrameRow row)
            {
                return new Key(row.Channel, row.Kind, row.Id, row.Extended, row.Remote, row.IsFd);
            }

            public bool Equals(Key other)
            {
                return _channel == other._channel
                    && _id == other._id
                    && _extended == other._extended
                    && _remote == other._remote
                    && _isFd == other._isFd
                    && string.Equals(_kind, other._kind, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_channel, _kind, _id, _extended, _remote, _isFd);
            }
        }
    }
}
