using System.ComponentModel;
using System.Globalization;
using GsCan;

namespace GsCan.View.Session
{
    public sealed class DisplayFilter : INotifyPropertyChanged
    {
        private bool _showChannel0 = true;
        private bool _showChannel1 = true;
        private bool _showRx = true;
        private bool _showEcho = true;
        private bool _showError = true;
        private bool _showStandard = true;
        private bool _showExtended = true;
        private bool _showClassic = true;
        private bool _showFd = true;
        private bool _showData = true;
        private bool _showRemote = true;
        private string _idText = string.Empty;
        private string? _filterError;
        private uint? _idFrom;
        private uint? _idTo;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool ShowChannel0
        {
            get => _showChannel0;
            set => Set(ref _showChannel0, value, nameof(ShowChannel0));
        }

        public bool ShowChannel1
        {
            get => _showChannel1;
            set => Set(ref _showChannel1, value, nameof(ShowChannel1));
        }

        public bool ShowRx
        {
            get => _showRx;
            set => Set(ref _showRx, value, nameof(ShowRx));
        }

        public bool ShowEcho
        {
            get => _showEcho;
            set => Set(ref _showEcho, value, nameof(ShowEcho));
        }

        public bool ShowError
        {
            get => _showError;
            set => Set(ref _showError, value, nameof(ShowError));
        }

        public bool ShowStandard
        {
            get => _showStandard;
            set => Set(ref _showStandard, value, nameof(ShowStandard));
        }

        public bool ShowExtended
        {
            get => _showExtended;
            set => Set(ref _showExtended, value, nameof(ShowExtended));
        }

        public bool ShowClassic
        {
            get => _showClassic;
            set => Set(ref _showClassic, value, nameof(ShowClassic));
        }

        public bool ShowFd
        {
            get => _showFd;
            set => Set(ref _showFd, value, nameof(ShowFd));
        }

        public bool ShowData
        {
            get => _showData;
            set => Set(ref _showData, value, nameof(ShowData));
        }

        public bool ShowRemote
        {
            get => _showRemote;
            set => Set(ref _showRemote, value, nameof(ShowRemote));
        }

        public string IdText
        {
            get => _idText;
            set
            {
                var text = value ?? string.Empty;
                if (_idText == text)
                {
                    return;
                }

                _idText = text;
                ParseIdText(text);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IdText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilterError)));
            }
        }

        public string? FilterError => _filterError;

        public bool Matches(int channelIndex, CanFrame frame)
        {
            if (channelIndex == 0 && !ShowChannel0)
            {
                return false;
            }

            if (channelIndex == 1 && !ShowChannel1)
            {
                return false;
            }

            switch (frame.Kind)
            {
                case CanFrameKind.Echo:
                    if (!ShowEcho)
                    {
                        return false;
                    }

                    break;
                case CanFrameKind.Error:
                    if (!ShowError)
                    {
                        return false;
                    }

                    return true;
                default:
                    if (!ShowRx)
                    {
                        return false;
                    }

                    break;
            }

            if (frame.Extended)
            {
                if (!ShowExtended)
                {
                    return false;
                }
            }
            else if (!ShowStandard)
            {
                return false;
            }

            if (frame.IsFd)
            {
                if (!ShowFd)
                {
                    return false;
                }
            }
            else if (!ShowClassic)
            {
                return false;
            }

            if (frame.Remote)
            {
                if (!ShowRemote)
                {
                    return false;
                }
            }
            else if (!ShowData)
            {
                return false;
            }

            if (_idFrom.HasValue && _idTo.HasValue && (frame.Id < _idFrom.Value || frame.Id > _idTo.Value))
            {
                return false;
            }

            return true;
        }

        private void ParseIdText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _idFrom = null;
                _idTo = null;
                _filterError = null;
                return;
            }

            var trimmed = text.Trim();
            var dash = trimmed.IndexOf('-');
            if (dash < 0)
            {
                if (uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                {
                    _idFrom = id;
                    _idTo = id;
                    _filterError = null;
                    return;
                }
            }
            else
            {
                var left = trimmed.Substring(0, dash).Trim();
                var right = trimmed.Substring(dash + 1).Trim();
                if (left.Length > 0
                    && right.Length > 0
                    && right.IndexOf('-') < 0
                    && uint.TryParse(left, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var from)
                    && uint.TryParse(right, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var to))
                {
                    if (from > to)
                    {
                        var swap = from;
                        from = to;
                        to = swap;
                    }

                    _idFrom = from;
                    _idTo = to;
                    _filterError = null;
                    return;
                }
            }

            _filterError = "ID 须为单个十六进制或闭区间，例如 100 或 100-1FF。";
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
