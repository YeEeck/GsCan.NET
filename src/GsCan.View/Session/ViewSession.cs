using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Threading;
using GsCan;

namespace GsCan.View.Session
{
    public sealed class ViewSession : INotifyPropertyChanged
    {
        // Held for later List/Open/Start. Construct must not call the port.
        private readonly IGsCanPort _port;
        private readonly bool _runBackgroundPumps;
        private readonly TraceBuffer _trace;
        private readonly Dictionary<int, uint> _timeOriginByChannel = new Dictionary<int, uint>();
        private readonly SynchronizationContext? _ui;
        private readonly object _pendingLock = new object();
        private readonly List<FrameRow> _pendingRows = new List<FrameRow>();
        private IOpenedDevice? _opened;
        private IReadOnlyList<DeviceInfo> _deviceList;
        private IReadOnlyList<ChannelState> _channels;
        private ReadPump?[] _pumps = Array.Empty<ReadPump?>();
        private string? _openedPath;
        private string? _lastError;
        private DeviceInfo? _selectedDevice;
        private volatile bool _paused;
        private int _pauseDroppedCount;
        private int _pendingDrops;
        private bool _flushPosted;

        public ViewSession(IGsCanPort port)
            : this(port, runBackgroundPumps: true)
        {
        }

        internal ViewSession(IGsCanPort port, bool runBackgroundPumps)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _runBackgroundPumps = runBackgroundPumps;
            _ui = SynchronizationContext.Current;
            _deviceList = Array.Empty<DeviceInfo>();
            _channels = Array.Empty<ChannelState>();
            Trace = new ObservableCollection<FrameRow>();
            _trace = new TraceBuffer(Trace);
            var slots = new TxSlot[TxSlot.SlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new TxSlot(i, SendOnce);
            }

            TxSlots = slots;
            DisplayFilter = new DisplayFilter();
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

        public ObservableCollection<FrameRow> Trace { get; }

        public DisplayFilter DisplayFilter { get; }

        public IReadOnlyList<TxSlot> TxSlots { get; }

        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value)
                {
                    return;
                }

                _paused = value;
                Raise(nameof(Paused));
            }
        }

        public int PauseDroppedCount
        {
            get => _pauseDroppedCount;
            private set
            {
                if (_pauseDroppedCount == value)
                {
                    return;
                }

                _pauseDroppedCount = value;
                Raise(nameof(PauseDroppedCount));
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
                channels[i] = new ChannelState(i, isRunning: false, StartChannel, StopChannel);
            }

            Channels = channels;
            _pumps = new ReadPump?[channels.Length];
        }

        public void StartChannel(int index)
        {
            if (_opened == null || index < 0 || index >= Channels.Count)
            {
                return;
            }

            var channel = Channels[index];
            var options = new ChannelOptions
            {
                Bitrate = channel.Bitrate,
                DataBitrate = channel.FdEnabled ? channel.DataBitrate : null,
                ListenOnly = channel.ListenOnly,
                Loopback = channel.Loopback,
                OneShot = channel.OneShot
            };

            try
            {
                _opened.Start(index, options);
            }
            catch (GsCanException ex)
            {
                LastError = ex.Message;
                channel.IsRunning = false;
                return;
            }

            LastError = null;
            channel.IsRunning = true;
            lock (_timeOriginByChannel)
            {
                _timeOriginByChannel.Remove(index);
            }

            StartPump(index);
        }

        public void StopChannel(int index)
        {
            if (_opened == null || index < 0 || index >= Channels.Count)
            {
                return;
            }

            StopPump(index);
            Channels[index].IsRunning = false;
        }

        public void Close()
        {
            if (_opened == null)
            {
                return;
            }

            for (int i = 0; i < Channels.Count; i++)
            {
                if ((i < _pumps.Length && _pumps[i] != null) || Channels[i].IsRunning)
                {
                    StopPump(i);
                    Channels[i].IsRunning = false;
                }
            }

            _opened.Dispose();
            _opened = null;
            _pumps = Array.Empty<ReadPump?>();
            OpenedPath = null;
            Channels = Array.Empty<ChannelState>();
            LastError = null;
        }

        public void Clear()
        {
            _trace.Clear();
        }

        public void SendOnce(int slotIndex)
        {
            if (_opened == null || slotIndex < 0 || slotIndex >= TxSlots.Count)
            {
                return;
            }

            var slot = TxSlots[slotIndex];
            if (slot.Channel < 0 || slot.Channel >= Channels.Count)
            {
                return;
            }

            var channel = Channels[slot.Channel];
            if (!channel.IsRunning)
            {
                return;
            }

            if (channel.ListenOnly)
            {
                LastError = "Cannot send on a listen-only channel.";
                return;
            }

            var frame = new CanFrame(
                slot.Id,
                slot.Data ?? Array.Empty<byte>(),
                CanFrameKind.Rx,
                extended: slot.Extended,
                remote: slot.Remote,
                fd: slot.Fd,
                bitRateSwitch: slot.BitRateSwitch);

            try
            {
                _opened.Send(slot.Channel, frame);
                LastError = null;
            }
            catch (GsCanException ex)
            {
                LastError = ex.Message;
            }
        }

        public void PumpUntilIdle()
        {
            if (_opened == null)
            {
                return;
            }

            for (int i = 0; i < Channels.Count; i++)
            {
                if (!Channels[i].IsRunning)
                {
                    continue;
                }

                DrainChannel(i);
            }

            if (ShouldMarshalToUi)
            {
                FlushPending();
            }
        }

        private void StartPump(int index)
        {
            if (!_runBackgroundPumps || _opened == null)
            {
                return;
            }

            if (index < _pumps.Length && _pumps[index] != null)
            {
                return;
            }

            var pump = new ReadPump(_opened, index, Accept, OnPumpError);
            if (index < _pumps.Length)
            {
                _pumps[index] = pump;
            }

            pump.Start();
        }

        private void StopPump(int index)
        {
            if (_opened == null)
            {
                return;
            }

            var pump = index < _pumps.Length ? _pumps[index] : null;
            if (index < _pumps.Length)
            {
                _pumps[index] = null;
            }

            var opened = _opened;
            if (pump != null)
            {
                pump.Stop(() => opened.Stop(index));
            }
            else
            {
                opened.Stop(index);
            }
        }

        private void OnPumpError(string message)
        {
            void SetError() => LastError = message;
            if (_ui != null)
            {
                _ui.Post(_ => SetError(), null);
            }
            else
            {
                SetError();
            }
        }

        private void DrainChannel(int channelIndex)
        {
            if (_opened == null)
            {
                return;
            }

            while (_opened.TryRead(channelIndex, 0, out var frame))
            {
                Accept(channelIndex, frame);
            }
        }

        private void Accept(int channelIndex, CanFrame frame)
        {
            lock (_timeOriginByChannel)
            {
                if (!_timeOriginByChannel.ContainsKey(channelIndex))
                {
                    _timeOriginByChannel[channelIndex] = frame.TimestampMicroseconds;
                }
            }

            if (_paused)
            {
                PublishDrop();
                return;
            }

            if (!DisplayFilter.Matches(channelIndex, frame))
            {
                return;
            }

            PublishRow(ToRow(channelIndex, frame));
        }

        private bool ShouldMarshalToUi => _runBackgroundPumps && _ui != null;

        private void PublishRow(FrameRow row)
        {
            if (!ShouldMarshalToUi)
            {
                _trace.Append(row);
                return;
            }

            lock (_pendingLock)
            {
                _pendingRows.Add(row);
                ScheduleFlush();
            }
        }

        private void PublishDrop()
        {
            if (!ShouldMarshalToUi)
            {
                PauseDroppedCount++;
                return;
            }

            lock (_pendingLock)
            {
                _pendingDrops++;
                ScheduleFlush();
            }
        }

        private void ScheduleFlush()
        {
            if (_flushPosted)
            {
                return;
            }

            _flushPosted = true;
            _ui!.Post(_ => FlushPending(), null);
        }

        private void FlushPending()
        {
            List<FrameRow> rows;
            int drops;
            lock (_pendingLock)
            {
                rows = new List<FrameRow>(_pendingRows);
                _pendingRows.Clear();
                drops = _pendingDrops;
                _pendingDrops = 0;
                _flushPosted = false;
            }

            foreach (var row in rows)
            {
                _trace.Append(row);
            }

            if (drops != 0)
            {
                PauseDroppedCount += drops;
            }
        }

        private FrameRow ToRow(int channelIndex, CanFrame frame)
        {
            uint origin;
            lock (_timeOriginByChannel)
            {
                origin = _timeOriginByChannel[channelIndex];
            }

            var data = frame.Data ?? Array.Empty<byte>();
            return new FrameRow(
                relativeMilliseconds: (frame.TimestampMicroseconds - origin) / 1000.0,
                channel: channelIndex,
                kind: KindText(frame.Kind),
                id: frame.Id,
                extended: frame.Extended,
                remote: frame.Remote,
                isFd: frame.IsFd,
                bitRateSwitch: frame.BitRateSwitch,
                errorStateIndicator: frame.ErrorStateIndicator,
                overflow: frame.Overflow,
                length: data.Length,
                dataHex: ToHex(data),
                timestampMicroseconds: frame.TimestampMicroseconds);
        }

        private static string KindText(CanFrameKind kind)
        {
            switch (kind)
            {
                case CanFrameKind.Echo:
                    return "Echo";
                case CanFrameKind.Error:
                    return "Error";
                default:
                    return "Rx";
            }
        }

        private static string ToHex(byte[] data)
        {
            if (data.Length == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(data[i].ToString("X2"));
            }

            return text.ToString();
        }

        private void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
