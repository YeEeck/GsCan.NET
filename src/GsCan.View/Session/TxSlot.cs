using System;
using System.ComponentModel;
using System.Globalization;

namespace GsCan.View.Session
{
    public sealed class TxSlot : INotifyPropertyChanged
    {
        public const int SlotCount = 16;

        private readonly Action<int>? _sendOnce;
        private int _channel;
        private uint _id;
        private bool _extended;
        private bool _remote;
        private bool _fd;
        private bool _bitRateSwitch;
        private byte[] _data = Array.Empty<byte>();
        private int _periodMs;
        private bool _enabled;

        public TxSlot(int index, Action<int>? sendOnce = null)
        {
            Index = index;
            _sendOnce = sendOnce;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }

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
            set => Set(ref _fd, value, nameof(Fd));
        }

        public bool BitRateSwitch
        {
            get => _bitRateSwitch;
            set => Set(ref _bitRateSwitch, value, nameof(BitRateSwitch));
        }

        public byte[] Data
        {
            get => _data;
            set
            {
                _data = value ?? Array.Empty<byte>();
                Raise(nameof(Data));
                Raise(nameof(DataHex));
            }
        }

        public string DataHex
        {
            get => FormatHex(_data);
            set
            {
                Data = ParseHex(value);
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

        private static byte[] ParseHex(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<byte>();
            }

            var compact = text.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (compact.Length % 2 != 0)
            {
                return Array.Empty<byte>();
            }

            var data = new byte[compact.Length / 2];
            for (int i = 0; i < data.Length; i++)
            {
                if (!byte.TryParse(compact.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out data[i]))
                {
                    return Array.Empty<byte>();
                }
            }

            return data;
        }
    }
}
