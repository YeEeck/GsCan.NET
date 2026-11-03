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
        private readonly SendPairTracker _pairTracker = new SendPairTracker();
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

        /// <summary>发送/应答配对器。点发送时登记，收帧时配对。</summary>
        public SendPairTracker PairTracker => _pairTracker;

        /// <summary>配对成功（或发送帧出现）时触发，参数是要高亮的帧时间戳与性质。</summary>
        public event Action<uint, int, RowHighlight, string>? HighlightRequested;

        public int PauseDroppedCount => _pauseDroppedCount;

        private long _lastFrameTimestampUs;

        /// <summary>
        /// 最近收到的一帧的硬件时间戳（微秒）。发送时用它作为配对窗口的起点，
        /// 保证发送侧和收帧侧用的是同一个时钟尺度。
        /// </summary>
        public long LastFrameTimestampUs => System.Threading.Interlocked.Read(ref _lastFrameTimestampUs);

        public void Accept(int channelIndex, CanFrame frame, bool paused)
        {
            lock (_timeOriginByChannel)
            {
                if (!_timeOriginByChannel.ContainsKey(channelIndex))
                {
                    _timeOriginByChannel[channelIndex] = frame.TimestampMicroseconds;
                }
            }

            System.Threading.Interlocked.Exchange(ref _lastFrameTimestampUs, frame.TimestampMicroseconds);

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

            var row = ToRow(channelIndex, frame, error);

            // 注意顺序：先算出高亮，再入队。这样 UI 线程刷行时标记一定已经就绪，
            // 不需要「先发事件、再等行建出来」那种时序假设。
            var highlight = ResolveHighlight(row);
            if (highlight.Kind != RowHighlight.None)
            {
                RememberHighlight(row.TimestampMicroseconds, highlight.Kind, highlight.Tag);
            }

            PublishRow(row);
        }

        /// <summary>
        /// 发送/应答配对。Echo 帧代表「我刚发出去的」，据此找到对应的待配对请求；
        /// Rx 帧在等待窗口内且 (通道, ID) 一致就认定是应答。
        /// 心跳帧没有对应的待配对请求，所以不会被误高亮。
        ///
        /// 该方法在后台读线程执行，不能碰界面集合，只返回要打的标记。
        /// </summary>
        private (RowHighlight Kind, string Tag) ResolveHighlight(FrameRow row)
        {
            bool isEcho = string.Equals(row.Kind, "Echo", StringComparison.Ordinal);
            bool isError = string.Equals(row.Kind, "Error", StringComparison.Ordinal);

            var pair = _pairTracker.Observe(
                row.Channel, row.Id, row.Extended, isEcho, isError, row.TimestampMicroseconds);

            if (pair != null)
            {
                return (RowHighlight.Paired, "应答 " + pair.LatencyText);
            }

            // Echo 就是「我发出去的那一帧」
            if (isEcho)
            {
                return (RowHighlight.Sent, "发送");
            }

            if (!isError)
            {
                // 同一 ID 的分段应答（例如 7 帧分帧回包）沿用同一次配对
                if (_pairTracker.ObserveRepeat(row.Channel, row.Id, row.Extended, row.TimestampMicroseconds) != null)
                {
                    return (RowHighlight.Paired, "应答");
                }
            }

            return (RowHighlight.None, string.Empty);
        }

        // 只保留「最近若干次」高亮，用队列淘汰最旧的，不再整表清空。
        private const int HighlightCapacity = 256;

        private readonly Dictionary<uint, (RowHighlight Kind, string Tag)> _highlightByTs =
            new Dictionary<uint, (RowHighlight Kind, string Tag)>();

        private readonly Queue<uint> _highlightOrder = new Queue<uint>();

        /// <summary>
        /// 每次点发送就自增。界面行带着代次号，代次号落后的行一律不高亮，
        /// 这样「上一轮的发送+应答」会自然失效，不需要去遍历改行。
        /// </summary>
        private int _highlightGeneration;

        public int HighlightGeneration
        {
            get
            {
                lock (_highlightByTs)
                {
                    return _highlightGeneration;
                }
            }
        }

        /// <summary>
        /// 开始新一轮发送：丢掉上一轮的高亮记录。
        /// 可以在后台线程调用——这里只改字典，不碰界面集合；
        /// 界面行通过代次号判断是否还该亮。
        /// </summary>
        public void ResetHighlightsForNewSend()
        {
            lock (_highlightByTs)
            {
                _highlightByTs.Clear();
                _highlightOrder.Clear();
                _highlightGeneration++;
            }
        }

        /// <summary>把某一帧的高亮标记为「本轮」。返回 false 表示这一帧不属于当前轮。</summary>
        public bool IsCurrentGeneration(uint timestampUs)
        {
            lock (_highlightByTs)
            {
                return _highlightByTs.ContainsKey(timestampUs);
            }
        }

        private void RememberHighlight(uint timestampUs, RowHighlight kind, string tag)
        {
            lock (_highlightByTs)
            {
                if (_highlightByTs.ContainsKey(timestampUs))
                {
                    _highlightByTs[timestampUs] = (kind, tag);
                    return;
                }

                _highlightByTs[timestampUs] = (kind, tag);
                _highlightOrder.Enqueue(timestampUs);

                // 逐条淘汰最旧的，不能清空——否则心跳一冲就把刚标好的帧也抹掉了
                while (_highlightOrder.Count > HighlightCapacity)
                {
                    _highlightByTs.Remove(_highlightOrder.Dequeue());
                }
            }
        }

        /// <summary>
        /// 把高亮同步到 Latest 行。必须在 UI 线程、行已建好之后调用。
        ///
        /// Latest 是「按 ID 聚合成一行」，同一个 ID 反复发会落在同一行上，
        /// 所以这里先清掉所有旧标记，只让当前轮的那一行亮——否则会同时亮好几行。
        /// </summary>
        public void ApplyHighlightToLatest(uint timestampUs, RowHighlight kind, string tag)
        {
            _latest.ClearHighlights();
            var row = _latest.FindByTimestamp(timestampUs);
            row?.SetHighlight(kind, tag);
        }

        /// <summary>
        /// 熄灭所有「不属于当前轮」的 Latest 高亮。
        /// 在 UI 线程、每批帧处理完之后调用一次。
        /// </summary>
        public void SweepStaleLatestHighlights()
        {
            for (int i = 0; i < Latest.Count; i++)
            {
                var row = Latest[i];
                if (!row.IsHighlighted)
                {
                    continue;
                }

                if (!IsCurrentGeneration(row.LastTimestampMicroseconds))
                {
                    row.SetHighlight(RowHighlight.None, string.Empty);
                }
            }
        }

        /// <summary>清除所有高亮。</summary>
        public void ClearHighlights()
        {
            lock (_highlightByTs)
            {
                _highlightByTs.Clear();
                _highlightOrder.Clear();
            }

            _latest.ClearHighlights();
            for (int i = 0; i < TraceDisplay.Count; i++)
            {
                TraceDisplay[i].SetHighlight(RowHighlight.None);
            }
        }

        public bool TryGetHighlight(uint timestampUs, out RowHighlight kind, out string tag)
        {
            lock (_highlightByTs)
            {
                if (_highlightByTs.TryGetValue(timestampUs, out var v))
                {
                    kind = v.Kind;
                    tag = v.Tag;
                    return true;
                }
            }

            kind = RowHighlight.None;
            tag = string.Empty;
            return false;
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
                if (TryGetHighlight(row.TimestampMicroseconds, out var kind, out var tag))
                {
                    ApplyHighlightToLatest(row.TimestampMicroseconds, kind, tag);
                }

                SweepStaleLatestHighlights();
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

            // 行已经建好，此时把高亮打上去（UI 线程）
            for (int i = 0; i < rows.Length; i++)
            {
                if (TryGetHighlight(rows[i].TimestampMicroseconds, out var kind, out var tag))
                {
                    ApplyHighlightToLatest(rows[i].TimestampMicroseconds, kind, tag);
                }
            }

            SweepStaleLatestHighlights();

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
                var row = Trace[start + i];
                var view = TraceDisplay[i];
                view.CopyFrom(row);

                if (TryGetHighlight(row.TimestampMicroseconds, out var kind, out var tag))
                {
                    view.SetHighlight(kind, tag);
                }
                else
                {
                    view.SetHighlight(RowHighlight.None);
                }
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
