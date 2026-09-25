using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Threading;
using GsCan;

namespace GsCan.View.Session
{
    /// <summary>
    /// In-process CanFrame ingest: Pause, Display Filter, Trace, Latest, Status,
    /// and the 128-row Trace display window. Relative time, Kind text, hex,
    /// ErrorClass, the ring, Latest keys, pending flush, and row recycling stay
    /// behind this interface.
    /// </summary>
    internal sealed class FrameIngest
    {
        private readonly DisplayFilter _filter;
        private readonly IClock _clock;
        private readonly LatestView _latest;
        private readonly Dictionary<int, uint> _timeOriginByChannel = new Dictionary<int, uint>();
        private readonly SynchronizationContext? _ui;
        private readonly object _pendingLock = new object();
        private readonly Queue<FrameRow> _pendingRows = new Queue<FrameRow>();
        private int _pauseDroppedCount;
        private int _pendingDrops;
        private bool _flushPosted;
        private IDisposable? _flushDelay;

        public FrameIngest(IClock clock, DisplayFilter filter, SynchronizationContext? ui)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _ui = ui;
            Trace = new TraceRows(TraceRows.DefaultCapacity);
            TraceDisplay = new ObservableCollection<FrameRowView>();
            Latest = new ObservableCollection<LatestRow>();
            _latest = new LatestView(Latest);
            Status = new SessionStatus();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public TraceRows Trace { get; }

        public ObservableCollection<FrameRowView> TraceDisplay { get; }

        public ObservableCollection<LatestRow> Latest { get; }

        public SessionStatus Status { get; }

        public int PauseDroppedCount => _pauseDroppedCount;

        public void Accept(int channelIndex, CanFrame frame, bool paused)
        {
            lock (_timeOriginByChannel)
            {
                if (!_timeOriginByChannel.ContainsKey(channelIndex))
                {
                    _timeOriginByChannel[channelIndex] = frame.TimestampMicroseconds;
                }
            }

            Status.Observe(frame);

            if (!ShouldMarshalToUi)
            {
                Status.Publish();
            }

            if (paused)
            {
                PublishDrop();
                return;
            }

            if (!_filter.Matches(channelIndex, frame))
            {
                return;
            }

            ErrorClass.Classification? error = null;
            if (frame.Kind == CanFrameKind.Error)
            {
                error = ErrorClass.Describe(frame);
            }

            PublishRow(ToRow(channelIndex, frame, error));
        }

        public void ResetTimeOrigin(int channelIndex)
        {
            lock (_timeOriginByChannel)
            {
                _timeOriginByChannel.Remove(channelIndex);
            }
        }

        public void Clear()
        {
            lock (_pendingLock)
            {
                _pendingRows.Clear();
                _pendingDrops = 0;
                _flushPosted = false;
            }

            CancelFlush();
            Trace.ClearRows();
            TraceDisplay.Clear();
            _latest.Clear();
        }

        public void CancelFlush()
        {
            _flushDelay?.Dispose();
            _flushDelay = null;
        }

        public void FlushUntilIdle()
        {
            if (!ShouldMarshalToUi)
            {
                SyncTraceDisplay();
                return;
            }

            while (true)
            {
                FlushPending(reschedule: false);
                lock (_pendingLock)
                {
                    if (_pendingRows.Count == 0 && _pendingDrops == 0)
                    {
                        _flushPosted = false;
                        break;
                    }
                }
            }
        }

        private bool ShouldMarshalToUi => _ui != null;

        private void PublishRow(FrameRow row)
        {
            if (!ShouldMarshalToUi)
            {
                Trace.Append(row);
                _latest.Observe(row);
                return;
            }

            bool post;
            lock (_pendingLock)
            {
                _pendingRows.Enqueue(row);
                while (_pendingRows.Count > TraceRows.DefaultCapacity)
                {
                    _pendingRows.Dequeue();
                }

                post = NeedFlush();
            }

            if (post)
            {
                _ui!.Post(_ => FlushPending(reschedule: true), null);
            }
        }

        private void PublishDrop()
        {
            if (!ShouldMarshalToUi)
            {
                SetPauseDroppedCount(_pauseDroppedCount + 1);
                return;
            }

            bool post;
            lock (_pendingLock)
            {
                _pendingDrops++;
                post = NeedFlush();
            }

            if (post)
            {
                _ui!.Post(_ => FlushPending(reschedule: true), null);
            }
        }

        private bool NeedFlush()
        {
            if (_flushPosted)
            {
                return false;
            }

            _flushPosted = true;
            return true;
        }

        private void FlushPending(bool reschedule)
        {
            FrameRow[] rows;
            int drops;
            bool more;
            lock (_pendingLock)
            {
                rows = _pendingRows.ToArray();
                _pendingRows.Clear();
                drops = _pendingDrops;
                _pendingDrops = 0;
                more = false;
                _flushPosted = false;
            }

            Trace.AppendMany(rows);
            SyncTraceDisplay();
            _latest.ObserveMany(rows);

            if (drops != 0)
            {
                SetPauseDroppedCount(_pauseDroppedCount + drops);
            }

            Status.Publish();

            lock (_pendingLock)
            {
                more = _pendingRows.Count > 0 || _pendingDrops != 0;
                if (more && reschedule)
                {
                    _flushPosted = true;
                }
                else if (!more)
                {
                    _flushPosted = false;
                }
            }

            if (more && reschedule)
            {
                ScheduleFlushDelay();
            }
        }

        private void ScheduleFlushDelay()
        {
            CancelFlush();
            _flushDelay = _clock.Schedule(
                TimeSpan.FromMilliseconds(33),
                () =>
                {
                    if (ShouldMarshalToUi)
                    {
                        _ui!.Post(_ => FlushPending(reschedule: true), null);
                    }
                    else
                    {
                        FlushPending(reschedule: true);
                    }
                });
        }

        private void SyncTraceDisplay()
        {
            int n = Trace.Count;
            if (n > TraceRows.DisplayCapacity)
            {
                n = TraceRows.DisplayCapacity;
            }

            int start = Trace.Count - n;
            while (TraceDisplay.Count < n)
            {
                TraceDisplay.Add(new FrameRowView());
            }

            while (TraceDisplay.Count > n)
            {
                TraceDisplay.RemoveAt(TraceDisplay.Count - 1);
            }

            for (int i = 0; i < n; i++)
            {
                TraceDisplay[i].CopyFrom(Trace[start + i]);
            }
        }

        private FrameRow ToRow(int channelIndex, CanFrame frame, ErrorClass.Classification? error)
        {
            uint origin;
            lock (_timeOriginByChannel)
            {
                origin = _timeOriginByChannel[channelIndex];
            }

            var data = frame.Data ?? Array.Empty<byte>();
            string errorClass = error?.Name ?? string.Empty;
            string errorHint = error?.Hint ?? string.Empty;

            return new FrameRow(
                relativeMilliseconds: (frame.TimestampMicroseconds - origin) / 1000.0,
                channel: channelIndex,
                kind: KindText(frame.Kind),
                frameKind: frame.Kind,
                id: frame.Id,
                extended: frame.Extended,
                remote: frame.Remote,
                isFd: frame.IsFd,
                bitRateSwitch: frame.BitRateSwitch,
                errorStateIndicator: frame.ErrorStateIndicator,
                overflow: frame.Overflow,
                length: data.Length,
                dataHex: ToHex(data),
                timestampMicroseconds: frame.TimestampMicroseconds,
                errorClass: errorClass,
                errorHint: errorHint);
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

        private void SetPauseDroppedCount(int value)
        {
            if (_pauseDroppedCount == value)
            {
                return;
            }

            _pauseDroppedCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PauseDroppedCount)));
        }
    }
}
