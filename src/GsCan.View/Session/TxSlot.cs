using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace GsCan.View.Session
{
    public sealed class TxSlot : INotifyPropertyChanged
    {
        /// <summary>
        /// 命令表最多行数。原来 16 行放不下一整条测试序列（实测有的要 32~43 条），
        /// 提到 256。列表是虚拟化滚动，行数多也不卡。
        /// </summary>
        public const int MaxSlotCount = 256;
        public const int DefaultLength = 8;

        public static readonly int[] DefaultChannelChoices = { 0, 1 };

        public static readonly int[] ClassicLengths = { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

        public static readonly int[] FdLengths =
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64
        };

        private readonly Action<int>? _sendOnce;
        private readonly Action<TxSlot>? _remove;
        private readonly Action<TxSlot, int>? _move;
        private int _index;
        private int _channel;
        private uint _id;
        private bool _extended;
        private bool _remote;
        private bool _fd;
        private bool _bitRateSwitch;
        private byte[] _data = new byte[DefaultLength];
        private int _length = DefaultLength;
        private double _periodMs;
        private bool _enabled;
        private bool _canRemove;
        private bool _canMoveUp;
        private bool _canMoveDown;
        private bool _canSend;
        private string? _sendUnavailableReason = "未打开 Device";
        private IReadOnlyList<int> _channelChoices = DefaultChannelChoices;
        private string _name = string.Empty;

        public TxSlot(int index, Action<int>? sendOnce = null, Action<TxSlot>? remove = null,
                      Action<TxSlot, int>? move = null)
        {
            _move = move;
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

        public IReadOnlyList<int> ChannelChoices
        {
            get => _channelChoices;
            internal set => Set(ref _channelChoices, value, nameof(ChannelChoices));
        }

        public bool CanSend => _canSend;

        public bool CanEnable => _canSend;

        public string? SendUnavailableReason => _sendUnavailableReason;

        public string EnableTip =>
            _canSend ? "周期大于 0 时按周期发送" : (_sendUnavailableReason ?? string.Empty);

        internal void SetSendAvailability(bool canSend, string? reason)
        {
            if (_canSend == canSend && _sendUnavailableReason == reason)
            {
                return;
            }

            _canSend = canSend;
            _sendUnavailableReason = reason;
            Raise(nameof(CanSend));
            Raise(nameof(CanEnable));
            Raise(nameof(SendUnavailableReason));
            Raise(nameof(EnableTip));
            if (!canSend && _enabled)
            {
                Enabled = false;
            }
        }

        public int Channel
        {
            get => _channel;
            set => Set(ref _channel, value, nameof(Channel));
        }

        /// <summary>备注 / 作用。导入导出时对应 .list 的 szname、CSV 的「备注」列。</summary>
        public string Name
        {
            get => _name;
            set => Set(ref _name, value ?? string.Empty, nameof(Name));
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
                if (TryParseId(value, out var id))
                {
                    Id = id;
                }

                // 无论输入是否合法都回显规范化结果，避免输入框停在半截状态
                Raise(nameof(IdHex));
            }
        }

        /// <summary>接受 "0x123"、"123"、"0X123"，一律按十六进制；超过 29 位视为非法。</summary>
        internal static bool TryParseId(string? text, out uint id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var s = text.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(2);
            }

            if (s.Length == 0)
            {
                return false;
            }

            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            if (value > 0x1FFFFFFFu)
            {
                return false;
            }

            id = value;
            return true;
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

        /// <summary>
        /// 周期（毫秒）。用 double 是为了支持小数，例如 200Hz PWM 需要 2.5ms。
        /// 0 表示只发一次。
        /// </summary>
        public double PeriodMs
        {
            get => _periodMs;
            set => Set(ref _periodMs, Math.Max(0, value), nameof(PeriodMs));
        }

        /// <summary>
        /// 周期文本。整数不带小数点，小数最多三位。
        /// 可写：解析失败（例如空串、乱填）时保持原值，不抛异常打断绑定。
        /// 固定用不变文化，避免中文系统把小数点当成逗号。
        /// </summary>
        public string PeriodText
        {
            get => _periodMs <= 0
                ? "0"
                : _periodMs.ToString("0.###", CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                    && ms >= 0
                    && ms < 3_600_000)
                {
                    PeriodMs = ms;
                }

                // 无论输入是否合法都回显规范化结果
                Raise(nameof(PeriodText));
            }
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (value && !_canSend)
                {
                    return;
                }

                Set(ref _enabled, value, nameof(Enabled));
            }
        }

        public void Send() => _sendOnce?.Invoke(Index);

        public void Remove() => _remove?.Invoke(this);

        /// <summary>上移一行。</summary>
        public void MoveUp() => _move?.Invoke(this, -1);

        /// <summary>下移一行。</summary>
        public void MoveDown() => _move?.Invoke(this, +1);

        /// <summary>是否允许上移（首行为 false）。由会话按位置刷新。</summary>
        public bool CanMoveUp
        {
            get => _canMoveUp;
            internal set => Set(ref _canMoveUp, value, nameof(CanMoveUp));
        }

        /// <summary>是否允许下移（末行为 false）。</summary>
        public bool CanMoveDown
        {
            get => _canMoveDown;
            internal set => Set(ref _canMoveDown, value, nameof(CanMoveDown));
        }

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
