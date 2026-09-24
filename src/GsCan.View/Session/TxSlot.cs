using System;
using System.ComponentModel;
using System.Globalization;

namespace GsCan.View.Session
{
    public sealed class TxSlot : INotifyPropertyChanged
    {
        public const int MaxSlotCount = 16;
        public const int DefaultLength = 8;

        public static readonly int[] ChannelChoices = { 0, 1 };

        public static readonly int[] ClassicLengths = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        public static readonly int[] FdLengths =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64
        };

        private readonly Action<int>? _sendOnce;
        private readonly Action<TxSlot>? _remove;
        private int _index;
        private int _channel;
        private uint _id;
        private bool _extended;
        private bool _remote;
        private bool _fd;
        private bool _bitRateSwitch;
        private byte[] _data = new byte[DefaultLength];
        private int _length = DefaultLength;
        private int _periodMs;
        private bool _enabled;
        private bool _canRemove;

        public TxSlot(int index, Action<int>? sendOnce = null, Action<TxSlot>? remove = null)
        {
            _index = index;
            _sendOnce = sendOnce;
            _remove = remove;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index
        {
            get => _index;
            internal set => Set(ref _index, value, nameof(Index));
        }

        public bool CanRemove
        {
            get => _canRemove;
            internal set => Set(ref _canRemove, value, nameof(CanRemove));
        }

        public int Channel
        {
            get => _channel;
            set => Set(ref _channel, value, nameof(Channel));
        }

        public uint Id
        {
            get => _id;
            set
            {
                if (_id == value)
                {
                    return;
                }

                _id = value;
                Raise(nameof(Id));
                Raise(nameof(IdHex));
            }
        }

        public string IdHex
        {
            get => _id.ToString("X", CultureInfo.InvariantCulture);
            set
            {
                if (uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                {
                    Id = id;
                }
            }
        }

        public bool Extended
        {
            get => _extended;
            set => Set(ref _extended, value, nameof(Extended));
        }

        public bool Remote
        {
            get => _remote;
            set => Set(ref _remote, value, nameof(Remote));
        }

        public bool Fd
        {
            get => _fd;
            set
            {
                if (_fd == value)
                {
                    return;
                }

                _fd = value;
                Raise(nameof(Fd));
                Raise(nameof(LengthChoices));
                if (!_fd && _bitRateSwitch)
                {
                    BitRateSwitch = false;
                }

                if (!_fd && _length > 8)
                {
                    Length = 8;
                }
            }
        }

        public bool BitRateSwitch
        {
            get => _bitRateSwitch;
            set => Set(ref _bitRateSwitch, _fd && value, nameof(BitRateSwitch));
        }

        public int[] LengthChoices => _fd ? FdLengths : ClassicLengths;

        public int Length
        {
            get => _length;
            set
            {
                var length = SnapLength(value, _fd);
                if (_length == length && _data.Length == length)
                {
                    return;
                }

                _length = length;
                _data = PadOrTruncate(_data, length);
                Raise(nameof(Length));
                Raise(nameof(Data));
                Raise(nameof(DataHex));
            }
        }

        public byte[] Data
        {
            get => _data;
            set
            {
                var incoming = value ?? Array.Empty<byte>();
                var length = SnapLength(incoming.Length, _fd);
                var data = PadOrTruncate(incoming, length);
                if (_length == length && BytesEqual(_data, data))
                {
                    return;
                }

                _length = length;
                _data = data;
                Raise(nameof(Length));
                Raise(nameof(Data));
                Raise(nameof(DataHex));
            }
        }

        public string DataHex
        {
            get => FormatHex(_data);
            set
            {
                if (!TryParseHex(value, out var parsed))
                {
                    return;
                }

                var length = parsed.Length > _length
                    ? SnapLength(parsed.Length, _fd)
                    : _length;
                var data = PadOrTruncate(parsed, length);
                if (_length == length && BytesEqual(_data, data))
                {
                    return;
                }

                var lengthChanged = _length != length;
                _length = length;
                _data = data;
                if (lengthChanged)
                {
                    Raise(nameof(Length));
                }

                Raise(nameof(Data));
                Raise(nameof(DataHex));
            }
        }

        public int PeriodMs
        {
            get => _periodMs;
            set => Set(ref _periodMs, value, nameof(PeriodMs));
        }

        public bool Enabled
        {
            get => _enabled;
            set => Set(ref _enabled, value, nameof(Enabled));
        }

        public void Send() => _sendOnce?.Invoke(Index);

        public void Remove() => _remove?.Invoke(this);

        public static int InferLength(string? dataHex, bool fd)
        {
            if (!TryParseHex(dataHex, out var data) || data.Length == 0)
            {
                return 0;
            }

            return SnapLength(data.Length, fd);
        }

        public static int SnapLength(int length, bool fd)
        {
            var choices = fd ? FdLengths : ClassicLengths;
            if (length <= 0)
            {
                return 0;
            }

            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] >= length)
                {
                    return choices[i];
                }
            }

            return choices[choices.Length - 1];
        }

        public static bool IsBlankData(string? dataHex)
        {
            if (string.IsNullOrWhiteSpace(dataHex))
            {
                return true;
            }

            if (!TryParseHex(dataHex, out var data))
            {
                return false;
            }

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool TryParseHex(string? text, out byte[] data)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                data = Array.Empty<byte>();
                return true;
            }

            var compact = text.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (compact.Length % 2 != 0)
            {
                data = Array.Empty<byte>();
                return false;
            }

            var parsed = new byte[compact.Length / 2];
            for (int i = 0; i < parsed.Length; i++)
            {
                if (!byte.TryParse(compact.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[i]))
                {
                    data = Array.Empty<byte>();
                    return false;
                }
            }

            data = parsed;
            return true;
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            Raise(propertyName);
        }

        private void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static byte[] PadOrTruncate(byte[] data, int length)
        {
            if (data.Length == length)
            {
                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                return copy;
            }

            var next = new byte[length];
            var take = data.Length < length ? data.Length : length;
            if (take > 0)
            {
                Buffer.BlockCopy(data, 0, next, 0, take);
            }

            return next;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string FormatHex(byte[] data)
        {
            if (data.Length == 0)
            {
                return string.Empty;
            }

            var chars = new char[data.Length * 3 - 1];
            int o = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    chars[o++] = ' ';
                }

                var b = data[i];
                chars[o++] = ToHexNibble(b >> 4);
                chars[o++] = ToHexNibble(b & 0xF);
            }

            return new string(chars);
        }

        private static char ToHexNibble(int nibble)
        {
            return (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
        }
    }
}
