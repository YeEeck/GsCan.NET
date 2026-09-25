using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using GsCan;

namespace GsCan.View.Session
{
    public sealed class ViewSession : INotifyPropertyChanged
    {
        private const string DeviceMustCloseReason = "请先关闭当前 Device";

        // Held for later List/Open/Start. Construct must not call the port.
        private readonly IGsCanPort _port;
        private readonly IConfigStore? _configStore;
        private readonly bool _runBackgroundPumps;
        private readonly IClock _clock;
        private readonly FrameIngest _ingest;
        private readonly SynchronizationContext? _ui;
        private readonly TxScheduler _scheduler;
        private readonly List<ChannelConfig> _heldChannels = new List<ChannelConfig>();
        private readonly List<bool> _heldEnabled = new List<bool>();
        private IOpenedDevice? _opened;
        private bool _suppressPersist;
        private IReadOnlyList<DeviceInfo> _deviceList;
        private IReadOnlyList<ChannelState> _channels;
        private ReadPump?[] _pumps = Array.Empty<ReadPump?>();
        private string? _openedPath;
        private string? _lastError;
        private DeviceInfo? _selectedDevice;
        private IReadOnlyList<int> _channelChoices = TxSlot.DefaultChannelChoices;
        private ReceiveMode _receiveMode;
        private volatile bool _paused;

        public ViewSession(IGsCanPort port)
            : this(port, runBackgroundPumps: true)
        {
        }

        public ViewSession(IGsCanPort port, IClock clock)
            : this(port, runBackgroundPumps: true, clock: clock)
        {
        }

        public ViewSession(IGsCanPort port, IConfigStore? configStore)
            : this(port, runBackgroundPumps: true, clock: null, configStore: configStore)
        {
        }

        public ViewSession(IGsCanPort port, IClock clock, IConfigStore? configStore)
            : this(port, runBackgroundPumps: true, clock: clock, configStore: configStore)
        {
        }

        internal ViewSession(IGsCanPort port, bool runBackgroundPumps, IClock? clock = null, IConfigStore? configStore = null)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            _configStore = configStore;
            _runBackgroundPumps = runBackgroundPumps;
            _ui = runBackgroundPumps ? SynchronizationContext.Current : null;
            _clock = clock ?? new RealtimeClock();
            _deviceList = Array.Empty<DeviceInfo>();
            _channels = Array.Empty<ChannelState>();
            DisplayFilter = new DisplayFilter();
            _ingest = new FrameIngest(_clock, DisplayFilter, _ui);
            _ingest.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FrameIngest.PauseDroppedCount))
                {
                    Raise(nameof(PauseDroppedCount));
                }
            };
            _scheduler = new TxScheduler(_clock, CyclicSend);
            TxSlots = new ObservableCollection<TxSlot>();
            AppendSlot();
            Trace.CollectionChanged += (_, _) =>
            {
                Raise(nameof(TraceIsEmpty));
                Raise(nameof(CanSaveLog));
                Raise(nameof(SaveLogUnavailableReason));
            };
            Latest.CollectionChanged += (_, _) => Raise(nameof(LatestIsEmpty));
            RestoreFromStore();
            DisplayFilter.PropertyChanged += OnDisplayFilterPropertyChanged;
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
                Raise(nameof(OpenedLabel));
                Raise(nameof(IsDeviceOpen));
                RaiseDeviceCommandAvailability();
            }
        }

        public bool IsDeviceOpen => OpenedPath != null;

        public string? OpenedLabel =>
            string.IsNullOrEmpty(_openedPath)
                ? null
                : GsCan.View.DeviceLabel.Summary(_openedPath, Channels.Count);

        public IReadOnlyList<DeviceInfo> DeviceList
        {
            get => _deviceList;
            private set
            {
                _deviceList = value;
                Raise(nameof(DeviceList));
                Raise(nameof(DeviceListIsEmpty));
            }
        }

        public bool DeviceListIsEmpty => DeviceList.Count == 0;

        public IReadOnlyList<ChannelState> Channels
        {
            get => _channels;
            private set
            {
                foreach (var channel in _channels)
                {
                    channel.PropertyChanged -= OnChannelPropertyChanged;
                }

                _channels = value;
                foreach (var channel in _channels)
                {
                    channel.PropertyChanged += OnChannelPropertyChanged;
                }

                Raise(nameof(Channels));
                Raise(nameof(OpenedLabel));
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

                if (_opened != null
                    && (value == null
                        || !string.Equals(value.Path, _opened.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                _selectedDevice = value;
                Raise(nameof(SelectedDevice));
                RaiseDeviceCommandAvailability();
                Persist();
            }
        }

        public bool CanSelectDevice => !IsDeviceOpen;

        public string? DeviceSelectUnavailableReason =>
            IsDeviceOpen ? DeviceMustCloseReason : null;

        public string? DeviceComboTip =>
            IsDeviceOpen ? DeviceSelectUnavailableReason : SelectedDevice?.Path;

        public bool CanRefreshDevices => !IsDeviceOpen;

        public string? RefreshUnavailableReason =>
            IsDeviceOpen ? DeviceMustCloseReason : null;

        public bool CanOpenSelected => !IsDeviceOpen && SelectedDevice != null;

        public string? OpenUnavailableReason =>
            IsDeviceOpen
                ? DeviceMustCloseReason
                : SelectedDevice == null
                    ? "未选择设备"
                    : null;

        public bool CanClose => IsDeviceOpen;

        public string? CloseUnavailableReason =>
            IsDeviceOpen ? null : "未打开 Device";

        public IReadOnlyList<int> ChannelChoices => _channelChoices;

        public bool ShowFilterChannel1 => _channelChoices.Count > 1;

        public TraceRows Trace => _ingest.Trace;

        public ObservableCollection<FrameRowView> TraceDisplay => _ingest.TraceDisplay;

        public ObservableCollection<LatestRow> Latest => _ingest.Latest;

        public bool TraceIsEmpty => Trace.Count == 0;

        public bool LatestIsEmpty => Latest.Count == 0;

        public bool CanSaveLog => Trace.Count > 0;

        public string? SaveLogUnavailableReason => CanSaveLog ? null : "Trace 为空";

        public ReceiveMode ReceiveMode
        {
            get => _receiveMode;
            set
            {
                if (_receiveMode == value)
                {
                    return;
                }

                _receiveMode = value;
                Raise(nameof(ReceiveMode));
                Raise(nameof(IsTraceMode));
                Raise(nameof(IsLatestMode));
            }
        }

        public bool IsTraceMode
        {
            get => ReceiveMode == ReceiveMode.Trace;
            set
            {
                if (value)
                {
                    ReceiveMode = ReceiveMode.Trace;
                }
            }
        }

        public bool IsLatestMode
        {
            get => ReceiveMode == ReceiveMode.Latest;
            set
            {
                if (value)
                {
                    ReceiveMode = ReceiveMode.Latest;
                }
            }
        }

        public DisplayFilter DisplayFilter { get; }

        public ObservableCollection<TxSlot> TxSlots { get; }

        public bool CanAddTxSlot => TxSlots.Count < TxSlot.MaxSlotCount;

        public bool CanRemoveTxSlot => TxSlots.Count > 1;

        public SessionStatus Status => _ingest.Status;

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

        public int PauseDroppedCount => _ingest.PauseDroppedCount;

        public void RefreshDevices()
        {
            if (_opened != null)
            {
                return;
            }

            var listed = _port.List() ?? Array.Empty<DeviceInfo>();
            var snapshot = new List<DeviceInfo>(listed).ToArray();
            if (!SameDeviceList(_deviceList, snapshot))
            {
                DeviceList = snapshot;
            }

            var path = SelectedDevice?.Path;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            foreach (var device in DeviceList)
            {
                if (device.Path == path)
                {
                    SelectedDevice = device;
                    return;
                }
            }
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
                if (i < _heldChannels.Count)
                {
                    ApplyChannelConfig(channels[i], _heldChannels[i]);
                }
            }

            Channels = channels;
            _pumps = new ReadPump?[channels.Length];
            SetChannelChoices(channels.Length);
            ClampTxSlotChannels();
            RefreshSendAvailability();
            WriteOpenedChannelCountToList(_opened.Path, _opened.ChannelCount);
            Persist();
        }

        private void WriteOpenedChannelCountToList(string path, int channelCount)
        {
            if (channelCount <= 0)
            {
                return;
            }

            var current = _deviceList;
            DeviceInfo? listed = null;
            bool listChanged = false;
            var updated = new DeviceInfo[current.Count];
            for (int i = 0; i < current.Count; i++)
            {
                var item = current[i];
                if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)
                    && item.ChannelCount != channelCount)
                {
                    item = new DeviceInfo(item.Path, channelCount);
                    listChanged = true;
                }

                updated[i] = item;
                if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    listed = item;
                }
            }

            if (listChanged)
            {
                DeviceList = updated;
            }

            if (listed != null)
            {
                SelectedDevice = listed;
            }
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
            _ingest.ResetTimeOrigin(index);

            StartPump(index);
            if (channel.ListenOnly)
            {
                DisableCyclicOnChannel(index);
            }
            else
            {
                SyncCyclicOnChannel(index);
            }
        }

        public void StopChannel(int index)
        {
            if (_opened == null || index < 0 || index >= Channels.Count)
            {
                return;
            }

            DisableCyclicOnChannel(index);
            StopPump(index);
            if (index < Channels.Count)
            {
                Channels[index].IsRunning = false;
            }
        }

        public void Close()
        {
            _ingest.CancelFlush();
            DisableAllCyclic();
            Persist();
            if (_opened == null)
            {
                return;
            }

            _suppressPersist = true;
            try
            {
                DisableAllCyclic();
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
                SetChannelChoices(0);
                RefreshSendAvailability();
                LastError = null;
            }
            finally
            {
                _suppressPersist = false;
            }
        }

        public void Clear()
        {
            _ingest.Clear();
        }

        public void SaveLog(string path)
        {
            new TraceLogWriter().Write(path, Trace);
        }

        public void AddTxSlot()
        {
            if (!CanAddTxSlot)
            {
                return;
            }

            AppendSlot();
            Persist();
        }

        public void RemoveTxSlot(TxSlot slot)
        {
            if (slot == null || TxSlots.Count <= 1)
            {
                return;
            }

            var index = TxSlots.IndexOf(slot);
            if (index < 0)
            {
                return;
            }

            slot.PropertyChanged -= OnTxSlotPropertyChanged;
            _scheduler.Stop(slot.Index);
            TxSlots.RemoveAt(index);
            if (index < _heldEnabled.Count)
            {
                _heldEnabled.RemoveAt(index);
            }

            ReindexSlots();
            RefreshSlotListFlags();
            Persist();
        }

        public void SendOnce(int slotIndex)
        {
            if (!TrySendSlot(slotIndex, out var error, out var stopCyclic))
            {
                if (error != null)
                {
                    LastError = error;
                    if (stopCyclic)
                    {
                        DisableSlot(slotIndex);
                    }
                }

                return;
            }

            LastError = null;
        }

        private void CyclicSend(int slotIndex)
        {
            if (TrySendSlot(slotIndex, out var error, out var stopCyclic) || error == null)
            {
                return;
            }

            void Fail()
            {
                LastError = error;
                if (stopCyclic)
                {
                    DisableSlot(slotIndex);
                }
            }

            if (ShouldMarshalToUi)
            {
                _ui!.Post(_ => Fail(), null);
            }
            else
            {
                Fail();
            }
        }

        private bool TrySendSlot(int slotIndex, out string? error, out bool stopCyclic)
        {
            error = null;
            stopCyclic = false;
            if (slotIndex < 0 || slotIndex >= TxSlots.Count)
            {
                return false;
            }

            var slot = TxSlots[slotIndex];
            var opened = _opened;
            if (opened == null || !TryDescribeSend(slot.Channel, out _))
            {
                return false;
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
                opened.Send(slot.Channel, frame);
                return true;
            }
            catch (GsCanException ex)
            {
                error = ex.Message;
                stopCyclic = true;
                return false;
            }
        }

        private void DisableSlot(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < TxSlots.Count)
            {
                var slot = TxSlots[slotIndex];
                if (slot.Enabled)
                {
                    slot.Enabled = false;
                }
            }
        }

        private void OnTxSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var slot = sender as TxSlot;
            if (slot == null)
            {
                return;
            }

            if (e.PropertyName == nameof(TxSlot.Enabled)
                && !_suppressPersist
                && slot.Index >= 0
                && slot.Index < _heldEnabled.Count)
            {
                _heldEnabled[slot.Index] = slot.Enabled;
            }

            Persist();
            if (e.PropertyName == nameof(TxSlot.Channel))
            {
                RefreshSendAvailability();
            }

            if (e.PropertyName != nameof(TxSlot.Enabled)
                && e.PropertyName != nameof(TxSlot.PeriodMs)
                && e.PropertyName != nameof(TxSlot.Channel))
            {
                return;
            }

            SyncCyclic(slot);
        }

        private void OnDisplayFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Persist();
        }

        private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var channel = sender as ChannelState;
            if (channel == null)
            {
                return;
            }

            if (e.PropertyName != nameof(ChannelState.IsRunning))
            {
                Persist();
            }

            if (e.PropertyName == nameof(ChannelState.IsRunning)
                || e.PropertyName == nameof(ChannelState.ListenOnly))
            {
                RefreshSendAvailability();
            }

            if (e.PropertyName != nameof(ChannelState.ListenOnly))
            {
                return;
            }

            if (channel.ListenOnly)
            {
                DisableCyclicOnChannel(channel.Index);
            }
        }

        private void SyncCyclic(TxSlot slot)
        {
            if (slot.Enabled && slot.PeriodMs > 0 && CanSendOn(slot.Channel))
            {
                _scheduler.Start(slot.Index, slot.PeriodMs);
                return;
            }

            _scheduler.Stop(slot.Index);
            if (slot.Enabled && ChannelIsListenOnly(slot.Channel))
            {
                slot.Enabled = false;
            }
        }

        private bool ChannelIsListenOnly(int channelIndex)
        {
            return channelIndex >= 0
                && channelIndex < Channels.Count
                && Channels[channelIndex].ListenOnly;
        }

        private void SyncCyclicOnChannel(int channelIndex)
        {
            foreach (var slot in TxSlots)
            {
                if (slot.Channel == channelIndex)
                {
                    SyncCyclic(slot);
                }
            }
        }

        private void ObserveError(int channelIndex, ErrorClass.Classification error)
        {
            bool halt = error.HaltsCyclic && ChannelHasEnabledCyclic(channelIndex);

            void Apply()
            {
                if (halt)
                {
                    DisableCyclicOnChannel(channelIndex);
                }

                string message = "CH" + channelIndex + " " + error.Name + "：" + error.Hint;
                if (halt)
                {
                    message += " 已停止该路周期发送。";
                }

                LastError = message;
            }

            if (ShouldMarshalToUi)
            {
                _ui!.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        private bool ChannelHasEnabledCyclic(int channelIndex)
        {
            foreach (var slot in TxSlots)
            {
                if (slot.Channel == channelIndex && slot.Enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private void DisableCyclicOnChannel(int channelIndex)
        {
            foreach (var slot in TxSlots)
            {
                if (slot.Channel == channelIndex && slot.Enabled)
                {
                    slot.Enabled = false;
                }
            }
        }

        private void DisableAllCyclic()
        {
            foreach (var slot in TxSlots)
            {
                if (slot.Enabled)
                {
                    slot.Enabled = false;
                }
            }

            _scheduler.StopAll();
        }

        private bool CanSendOn(int channelIndex)
        {
            return TryDescribeSend(channelIndex, out _);
        }

        public void PumpUntilIdle()
        {
            DrainRunningChannels();
            _ingest.FlushUntilIdle();
        }

        private void DrainRunningChannels()
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
            void Fail()
            {
                Close();
                LastError = message;
            }

            if (ShouldMarshalToUi)
            {
                _ui!.Post(_ => Fail(), null);
            }
            else
            {
                Fail();
            }
        }

        private void DrainChannel(int channelIndex)
        {
            if (_opened == null)
            {
                return;
            }

            try
            {
                while (_opened.TryRead(channelIndex, 0, out var frame))
                {
                    Accept(channelIndex, frame);
                }
            }
            catch (GsCanException ex)
            {
                OnPumpError(ex.Message);
            }
        }

        private void Accept(int channelIndex, CanFrame frame)
        {
            if (frame.Kind == CanFrameKind.Error)
            {
                ObserveError(channelIndex, ErrorClass.Describe(frame));
            }

            _ingest.Accept(channelIndex, frame, _paused);
        }

        private bool ShouldMarshalToUi => _ui != null;

        private void Raise(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetChannelChoices(int channelCount)
        {
            IReadOnlyList<int> choices;
            if (channelCount <= 0)
            {
                choices = TxSlot.DefaultChannelChoices;
            }
            else if (channelCount == 1)
            {
                choices = new[] { 0 };
            }
            else if (channelCount == 2)
            {
                choices = TxSlot.DefaultChannelChoices;
            }
            else
            {
                var list = new int[channelCount];
                for (int i = 0; i < channelCount; i++)
                {
                    list[i] = i;
                }

                choices = list;
            }

            _channelChoices = choices;
            foreach (var slot in TxSlots)
            {
                slot.ChannelChoices = choices;
            }

            Raise(nameof(ChannelChoices));
            Raise(nameof(ShowFilterChannel1));
        }

        private void ClampTxSlotChannels()
        {
            int max = Channels.Count;
            if (max <= 0)
            {
                return;
            }

            foreach (var slot in TxSlots)
            {
                if (slot.Channel < 0 || slot.Channel >= max)
                {
                    slot.Channel = 0;
                }
            }
        }

        private void RaiseDeviceCommandAvailability()
        {
            Raise(nameof(CanSelectDevice));
            Raise(nameof(DeviceSelectUnavailableReason));
            Raise(nameof(DeviceComboTip));
            Raise(nameof(CanRefreshDevices));
            Raise(nameof(RefreshUnavailableReason));
            Raise(nameof(CanOpenSelected));
            Raise(nameof(OpenUnavailableReason));
            Raise(nameof(CanClose));
            Raise(nameof(CloseUnavailableReason));
        }

        private void RestoreFromStore()
        {
            if (_configStore == null)
            {
                return;
            }

            ViewConfig? config;
            try
            {
                config = _configStore.Load();
            }
            catch
            {
                return;
            }

            if (config == null)
            {
                return;
            }

            _suppressPersist = true;
            try
            {
                Restore(config);
            }
            finally
            {
                _suppressPersist = false;
            }
        }

        private void Restore(ViewConfig config)
        {
            if (!string.IsNullOrEmpty(config.DevicePath))
            {
                SelectedDevice = new DeviceInfo(config.DevicePath, config.DeviceChannelCount);
            }

            _heldChannels.Clear();
            if (config.Channels != null)
            {
                foreach (var channel in config.Channels)
                {
                    _heldChannels.Add(CloneChannel(channel));
                }
            }

            RestoreDisplayFilter(config.DisplayFilter);
            RestoreTxSlots(config.TxSlots);
        }

        private void RestoreDisplayFilter(DisplayFilterConfig? source)
        {
            if (source == null)
            {
                return;
            }

            DisplayFilter.ShowChannel0 = source.ShowChannel0;
            DisplayFilter.ShowChannel1 = source.ShowChannel1;
            DisplayFilter.ShowRx = source.ShowRx;
            DisplayFilter.ShowEcho = source.ShowEcho;
            DisplayFilter.ShowError = source.ShowError;
            DisplayFilter.ShowStandard = source.ShowStandard;
            DisplayFilter.ShowExtended = source.ShowExtended;
            DisplayFilter.ShowClassic = source.ShowClassic;
            DisplayFilter.ShowFd = source.ShowFd;
            DisplayFilter.ShowData = source.ShowData;
            DisplayFilter.ShowRemote = source.ShowRemote;
            DisplayFilter.IdText = source.IdText ?? string.Empty;
        }

        private void RestoreTxSlots(List<TxSlotConfig>? source)
        {
            if (source == null)
            {
                return;
            }

            var rows = NormalizeTxSlotConfigs(source);
            _scheduler.StopAll();
            foreach (var slot in TxSlots)
            {
                slot.PropertyChanged -= OnTxSlotPropertyChanged;
            }

            TxSlots.Clear();
            _heldEnabled.Clear();
            foreach (var saved in rows)
            {
                var slot = AppendSlot();
                ApplyTxSlotConfig(slot, saved);
                _heldEnabled[slot.Index] = false;
            }

            RefreshSlotListFlags();
        }

        private TxSlot AppendSlot()
        {
            var slot = new TxSlot(TxSlots.Count, SendOnce, RemoveTxSlot);
            slot.ChannelChoices = _channelChoices;
            slot.PropertyChanged += OnTxSlotPropertyChanged;
            TxSlots.Add(slot);
            _heldEnabled.Add(false);
            RefreshSlotListFlags();
            return slot;
        }

        private void ReindexSlots()
        {
            for (int i = 0; i < TxSlots.Count; i++)
            {
                var slot = TxSlots[i];
                if (slot.Index == i)
                {
                    continue;
                }

                _scheduler.Stop(slot.Index);
                slot.Index = i;
                SyncCyclic(slot);
            }
        }

        private void RefreshSlotListFlags()
        {
            var canRemove = TxSlots.Count > 1;
            foreach (var slot in TxSlots)
            {
                slot.CanRemove = canRemove;
            }

            Raise(nameof(CanAddTxSlot));
            Raise(nameof(CanRemoveTxSlot));
            RefreshSendAvailability();
        }

        private void RefreshSendAvailability()
        {
            foreach (var slot in TxSlots)
            {
                slot.SetSendAvailability(CanSendSlot(slot, out var reason), reason);
            }
        }

        private bool CanSendSlot(TxSlot slot, out string? reason)
        {
            return TryDescribeSend(slot.Channel, out reason);
        }

        private bool TryDescribeSend(int channelIndex, out string? reason)
        {
            if (_opened == null)
            {
                reason = "未打开 Device";
                return false;
            }

            if (channelIndex < 0 || channelIndex >= Channels.Count || !Channels[channelIndex].IsRunning)
            {
                reason = "通道未启动";
                return false;
            }

            if (Channels[channelIndex].ListenOnly)
            {
                reason = "只听通道不能发送";
                return false;
            }

            reason = null;
            return true;
        }

        private void ApplyTxSlotConfig(TxSlot slot, TxSlotConfig saved)
        {
            slot.Channel = saved.Channel;
            slot.Id = saved.Id;
            slot.Extended = saved.Extended;
            slot.Remote = saved.Remote;
            slot.Fd = saved.Fd;
            slot.BitRateSwitch = saved.BitRateSwitch;
            slot.DataHex = saved.DataHex ?? string.Empty;
            slot.Length = saved.Length > 0
                ? TxSlot.SnapLength(saved.Length, saved.Fd)
                : TxSlot.InferLength(saved.DataHex, saved.Fd);
            slot.PeriodMs = saved.PeriodMs;
            slot.Enabled = false;
        }

        private static List<TxSlotConfig> NormalizeTxSlotConfigs(List<TxSlotConfig> source)
        {
            var rows = new List<TxSlotConfig>();
            var take = source.Count < TxSlot.MaxSlotCount ? source.Count : TxSlot.MaxSlotCount;
            for (int i = 0; i < take; i++)
            {
                rows.Add(source[i]);
            }

            while (rows.Count > 1 && IsDefaultTxSlot(rows[rows.Count - 1]))
            {
                rows.RemoveAt(rows.Count - 1);
            }

            if (rows.Count == 0)
            {
                rows.Add(new TxSlotConfig());
            }

            return rows;
        }

        private static bool IsDefaultTxSlot(TxSlotConfig saved)
        {
            return saved.Channel == 0
                && saved.Id == 0
                && !saved.Extended
                && !saved.Remote
                && !saved.Fd
                && !saved.BitRateSwitch
                && (saved.Length == 0 || saved.Length == TxSlot.DefaultLength)
                && TxSlot.IsBlankData(saved.DataHex)
                && saved.PeriodMs == 0
                && !saved.Enabled;
        }

        private void Persist()
        {
            if (_configStore == null || _suppressPersist)
            {
                return;
            }

            try
            {
                _configStore.Save(Capture());
            }
            catch
            {
                // Remembering the form is best-effort; do not crash the session.
            }
        }

        private ViewConfig Capture()
        {
            return new ViewConfig
            {
                DevicePath = OpenedPath ?? SelectedDevice?.Path,
                DeviceChannelCount = _opened?.ChannelCount ?? SelectedDevice?.ChannelCount ?? 0,
                Channels = CaptureChannels(),
                DisplayFilter = CaptureDisplayFilter(),
                TxSlots = CaptureTxSlots()
            };
        }

        private List<ChannelConfig> CaptureChannels()
        {
            var list = new List<ChannelConfig>();
            if (Channels.Count > 0)
            {
                foreach (var channel in Channels)
                {
                    list.Add(ToChannelConfig(channel));
                }

                _heldChannels.Clear();
                foreach (var item in list)
                {
                    _heldChannels.Add(CloneChannel(item));
                }

                return list;
            }

            foreach (var held in _heldChannels)
            {
                list.Add(CloneChannel(held));
            }

            return list;
        }

        private DisplayFilterConfig CaptureDisplayFilter()
        {
            return new DisplayFilterConfig
            {
                ShowChannel0 = DisplayFilter.ShowChannel0,
                ShowChannel1 = DisplayFilter.ShowChannel1,
                ShowRx = DisplayFilter.ShowRx,
                ShowEcho = DisplayFilter.ShowEcho,
                ShowError = DisplayFilter.ShowError,
                ShowStandard = DisplayFilter.ShowStandard,
                ShowExtended = DisplayFilter.ShowExtended,
                ShowClassic = DisplayFilter.ShowClassic,
                ShowFd = DisplayFilter.ShowFd,
                ShowData = DisplayFilter.ShowData,
                ShowRemote = DisplayFilter.ShowRemote,
                IdText = DisplayFilter.IdText ?? string.Empty
            };
        }

        private List<TxSlotConfig> CaptureTxSlots()
        {
            var list = new List<TxSlotConfig>(TxSlots.Count);
            foreach (var slot in TxSlots)
            {
                if (_opened != null && slot.Index >= 0 && slot.Index < _heldEnabled.Count)
                {
                    _heldEnabled[slot.Index] = slot.Enabled;
                }

                list.Add(new TxSlotConfig
                {
                    Channel = slot.Channel,
                    Id = slot.Id,
                    Extended = slot.Extended,
                    Remote = slot.Remote,
                    Fd = slot.Fd,
                    BitRateSwitch = slot.BitRateSwitch,
                    Length = slot.Length,
                    DataHex = slot.DataHex ?? string.Empty,
                    PeriodMs = slot.PeriodMs,
                    Enabled = false
                });
            }

            return list;
        }

        private static void ApplyChannelConfig(ChannelState channel, ChannelConfig config)
        {
            channel.Bitrate = config.Bitrate;
            channel.FdEnabled = config.FdEnabled;
            channel.DataBitrate = config.DataBitrate;
            channel.ListenOnly = config.ListenOnly;
            channel.Loopback = config.Loopback;
            channel.OneShot = config.OneShot;
        }

        private static ChannelConfig ToChannelConfig(ChannelState channel)
        {
            return new ChannelConfig
            {
                Bitrate = channel.Bitrate,
                FdEnabled = channel.FdEnabled,
                DataBitrate = channel.DataBitrate,
                ListenOnly = channel.ListenOnly,
                Loopback = channel.Loopback,
                OneShot = channel.OneShot
            };
        }

        private static ChannelConfig CloneChannel(ChannelConfig source)
        {
            return new ChannelConfig
            {
                Bitrate = source.Bitrate,
                FdEnabled = source.FdEnabled,
                DataBitrate = source.DataBitrate,
                ListenOnly = source.ListenOnly,
                Loopback = source.Loopback,
                OneShot = source.OneShot
            };
        }

        private static bool SameDeviceList(IReadOnlyList<DeviceInfo> left, IReadOnlyList<DeviceInfo> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Path != right[i].Path || left[i].ChannelCount != right[i].ChannelCount)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
