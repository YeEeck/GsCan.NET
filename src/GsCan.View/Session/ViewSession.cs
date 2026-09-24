using System;
using System.Collections.Generic;
using System.ComponentModel;
using GsCan;

namespace GsCan.View.Session
{
    public sealed class ViewSession : INotifyPropertyChanged
    {
        // Held for later List/Open/Start. Construct must not call the port.
        private readonly IGsCanPort _port;
        private IOpenedDevice? _opened;
        private IReadOnlyList<DeviceInfo> _deviceList;
        private IReadOnlyList<ChannelState> _channels;
        private string? _openedPath;
        private string? _lastError;
        private DeviceInfo? _selectedDevice;

        public ViewSession(IGsCanPort port)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _deviceList = Array.Empty<DeviceInfo>();
            _channels = Array.Empty<ChannelState>();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string WindowTitle => "GsCan View";

        public string? OpenedPath
        {
            get => _openedPath;
            private set
            {
                if (_openedPath == value)
                {
                    return;
                }

                _openedPath = value;
                Raise(nameof(OpenedPath));
            }
        }

        public IReadOnlyList<DeviceInfo> DeviceList
        {
            get => _deviceList;
            private set
            {
                _deviceList = value;
                Raise(nameof(DeviceList));
            }
        }

        public IReadOnlyList<ChannelState> Channels
        {
            get => _channels;
            private set
            {
                _channels = value;
                Raise(nameof(Channels));
            }
        }

        public string? LastError
        {
            get => _lastError;
            private set
            {
                if (_lastError == value)
                {
                    return;
                }

                _lastError = value;
                Raise(nameof(LastError));
            }
        }

        public DeviceInfo? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (ReferenceEquals(_selectedDevice, value))
                {
                    return;
                }

                _selectedDevice = value;
                Raise(nameof(SelectedDevice));
            }
        }

        public void RefreshDevices()
        {
            var listed = _port.List() ?? Array.Empty<DeviceInfo>();
            DeviceList = new List<DeviceInfo>(listed).ToArray();
        }

        public void OpenSelected()
        {
            if (SelectedDevice == null)
            {
                return;
            }

            Open(SelectedDevice);
        }

        public void Open(DeviceInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (_opened != null)
            {
                LastError = "已经打开一块 Device，请先关闭。";
                return;
            }

            try
            {
                _opened = _port.Open(info);
            }
            catch (GsCanException ex)
            {
                LastError = ex.Message;
                return;
            }

            LastError = null;
            OpenedPath = _opened.Path;
            var channels = new ChannelState[_opened.ChannelCount];
            for (int i = 0; i < channels.Length; i++)
            {
                channels[i] = new ChannelState(i, isRunning: false);
            }

            Channels = channels;
        }

        public void Close()
        {
            if (_opened == null)
            {
                return;
            }

            _opened.Dispose();
            _opened = null;
            OpenedPath = null;
            Channels = Array.Empty<ChannelState>();
            LastError = null;
        }

        private void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
