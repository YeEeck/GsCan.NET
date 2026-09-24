using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace GsCan.View.Session
{
    public sealed class ChannelState : INotifyPropertyChanged
    {
        public static readonly IReadOnlyList<int> ArbitrationBitrates = new[]
        {
            10_000, 20_000, 50_000, 83_333, 100_000, 125_000, 250_000, 500_000, 800_000, 1_000_000
        };

        public static readonly IReadOnlyList<int> DataBitrates = new[]
        {
            1_000_000, 2_000_000, 4_000_000
        };

        private readonly Action<int>? _start;
        private readonly Action<int>? _stop;
        private int _bitrate = 500_000;
        private bool _fdEnabled;
        private int _dataBitrate = 2_000_000;
        private bool _listenOnly;
        private bool _loopback;
        private bool _oneShot;
        private bool _isRunning;

        public ChannelState(int index, bool isRunning, Action<int>? start = null, Action<int>? stop = null)
        {
            Index = index;
            _isRunning = isRunning;
            _start = start;
            _stop = stop;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }

        public int Bitrate
        {
            get => _bitrate;
            set => Set(ref _bitrate, value, nameof(Bitrate));
        }

        public bool FdEnabled
        {
            get => _fdEnabled;
            set => Set(ref _fdEnabled, value, nameof(FdEnabled));
        }

        public int DataBitrate
        {
            get => _dataBitrate;
            set => Set(ref _dataBitrate, value, nameof(DataBitrate));
        }

        public bool ListenOnly
        {
            get => _listenOnly;
            set => Set(ref _listenOnly, value, nameof(ListenOnly));
        }

        public bool Loopback
        {
            get => _loopback;
            set => Set(ref _loopback, value, nameof(Loopback));
        }

        public bool OneShot
        {
            get => _oneShot;
            set => Set(ref _oneShot, value, nameof(OneShot));
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => Set(ref _isRunning, value, nameof(IsRunning));
        }

        public void Start() => _start?.Invoke(Index);

        public void Stop() => _stop?.Invoke(Index);

        public static string FormatBitrate(int bitsPerSecond)
        {
            switch (bitsPerSecond)
            {
                case 10_000: return "10k";
                case 20_000: return "20k";
                case 50_000: return "50k";
                case 83_333: return "83.333k";
                case 100_000: return "100k";
                case 125_000: return "125k";
                case 250_000: return "250k";
                case 500_000: return "500k";
                case 800_000: return "800k";
                case 1_000_000: return "1M";
                case 2_000_000: return "2M";
                case 4_000_000: return "4M";
                default: return bitsPerSecond.ToString();
            }
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
