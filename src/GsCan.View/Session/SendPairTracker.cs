using System;
using System.Collections.Generic;

namespace GsCan.View.Session
{
    /// <summary>一次发送请求，用来等待配对。</summary>
    public sealed class SendPairRequest
    {
        public SendPairRequest(int slotIndex, int channel, uint id, bool extended, string name)
        {
            SlotIndex = slotIndex;
            Channel = channel;
            Id = id;
            Extended = extended;
            Name = name;
        }

        public int SlotIndex { get; }
        public int Channel { get; }
        public uint Id { get; }
        public bool Extended { get; }
        public string Name { get; }
    }

    /// <summary>
    /// 把「点了一次发送」和它对应的应答帧配成一对。
    ///
    /// 规则（针对心跳帧刷屏的场景）：
    /// 1. 只有 Echo 帧才算「这一帧是我发出去的」；用 (通道, ID, 标志) 找最近的待配对请求。
    /// 2. 请求发出后进入等待窗口（默认 1000ms）。窗口内的 **Rx** 帧，只要
    ///    (通道, ID) 与请求一致，就认定为应答——不要求是「紧接着的下一帧」，
    ///    所以在几 KB/s 的心跳流里也能正确配上。
    /// 3. 同一 ID 正在等待时又发了一次，以最新那次为准（旧请求作废），
    ///    避免周期发送时请求越堆越多。
    /// 4. 超过窗口没有任何应答就过期。过期后不再配对，避免错把后来的帧当应答。
    /// 5. 错误帧不参与配对，但会让当前请求立刻失败。
    ///
    /// 线程安全：收帧在后台线程、发送在 UI 线程，内部加锁。
    /// </summary>
    public sealed class SendPairTracker
    {
        /// <summary>等待应答的默认窗口。太小会漏配，太大容易把无关帧当应答。</summary>
        /// <summary>
        /// 等待应答的默认窗口。总线拥塞时（例如同时有 3000 帧/秒的心跳），
        /// 应用层读到应答的延迟会到几百毫秒，所以给到 2000ms。
        /// 窗口从 Echo（帧确实发出）起算，不会把无关帧当应答。
        /// </summary>
        public const int DefaultWindowMs = 2000;

        /// <summary>最多同时挂多少个待配对请求（周期发送时防止无限增长）。</summary>
        public const int MaxPending = 64;

        private readonly object _gate = new object();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Dictionary<string, Pending> _byKey = new Dictionary<string, Pending>();
        private int _windowMs = DefaultWindowMs;

        /// <summary>最近一次配对结果，供界面高亮。null 表示没有。</summary>
        private Pair? _lastPair;

        /// <summary>最近一次点发送的时间戳。界面据此清除上一轮高亮。</summary>
        public long LastBeginAtUs { get; private set; }

        public int WindowMs
        {
            get
            {
                lock (_gate)
                {
                    return _windowMs;
                }
            }
            set
            {
                lock (_gate)
                {
                    _windowMs = Math.Max(10, Math.Min(60_000, value));
                }
            }
        }

        /// <summary>当前正在等待应答的请求数（状态栏可以显示）。</summary>
        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _pending.Clear();
                _byKey.Clear();
                _lastPair = null;
            }
        }

        /// <summary>点发送时调用：登记一个待配对请求。</summary>
        public void Begin(SendPairRequest request, long nowUs)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            lock (_gate)
            {
                PruneExpired(nowUs);

                LastBeginAtUs = nowUs;

                var key = KeyOf(request.Channel, request.Id, request.Extended);

                // 同 ID 又发了一次：旧请求作废，以最新的为准
                if (_byKey.TryGetValue(key, out var existing))
                {
                    _pending.Remove(existing);
                    _byKey.Remove(key);
                }

                // nowUs <= 0 表示「还不知道总线时间」（例如刚启动、还没收到过帧）。
                // 此时先挂起，等 Echo 帧带来真实时间戳后再计算窗口。
                long deadline = nowUs > 0 ? nowUs + _windowMs * 1000L : long.MaxValue;
                var pending = new Pending(request, nowUs, deadline);
                _pending.Add(pending);
                _byKey[key] = pending;

                while (_pending.Count > MaxPending)
                {
                    var oldest = _pending[0];
                    _pending.RemoveAt(0);
                    _byKey.Remove(KeyOf(oldest.Request.Channel, oldest.Request.Id, oldest.Request.Extended));
                }
            }
        }

        /// <summary>
        /// 收到一帧时调用。配对成功返回那次 Pair，否则 null。
        /// Echo 只用来「确认发送出去了」，不返回配对；Rx 才可能配对成功。
        /// </summary>
        public Pair? Observe(int channel, uint id, bool extended, bool isEcho, bool isError, long nowUs)
        {
            lock (_gate)
            {
                PruneExpired(nowUs);

                var key = KeyOf(channel, id, extended);

                if (isError)
                {
                    // 错误帧让同通道所有等待中的请求立即失败
                    for (int i = _pending.Count - 1; i >= 0; i--)
                    {
                        if (_pending[i].Request.Channel == channel)
                        {
                            _byKey.Remove(KeyOf(_pending[i].Request.Channel, _pending[i].Request.Id, _pending[i].Request.Extended));
                            _pending.RemoveAt(i);
                        }
                    }

                    return null;
                }

                if (isEcho)
                {
                    // Echo 才是「帧确实发出去了」的证据：用它的时间戳校准窗口起点。
                    if (_byKey.TryGetValue(key, out var echoPending) && echoPending.SentAtUs <= 0)
                    {
                        echoPending.Restart(nowUs, _windowMs * 1000L);
                    }

                    return null;
                }

                if (!_byKey.TryGetValue(key, out var pending))
                {
                    return null;
                }

                _pending.Remove(pending);
                _byKey.Remove(key);

                var pair = new Pair(pending.Request, channel, id, extended, nowUs - pending.SentAtUs)
                {
                    MatchedAtUs = nowUs,
                };
                _lastPair = pair;
                return pair;
            }
        }

        /// <summary>同一 ID 连续多次应答时（例如分段应答），同一次发送可配对多帧。</summary>
        public Pair? ObserveRepeat(int channel, uint id, bool extended, long nowUs)
        {
            lock (_gate)
            {
                if (_lastPair == null)
                {
                    return null;
                }

                if (_lastPair.Channel != channel || _lastPair.Id != id || _lastPair.Extended != extended)
                {
                    return null;
                }

                if (nowUs - _lastPair.MatchedAtUs > _windowMs * 1000L)
                {
                    return null;
                }

                return _lastPair;
            }
        }

        private void PruneExpired(long nowUs)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                // DeadlineUs == long.MaxValue 表示还在等 Echo 校准，不能被清掉
                if (_pending[i].DeadlineUs != long.MaxValue && _pending[i].DeadlineUs <= nowUs)
                {
                    var p = _pending[i];
                    _byKey.Remove(KeyOf(p.Request.Channel, p.Request.Id, p.Request.Extended));
                    _pending.RemoveAt(i);
                }
            }
        }

        private static string KeyOf(int channel, uint id, bool extended)
        {
            return string.Concat(
                channel.ToString(System.Globalization.CultureInfo.InvariantCulture), ":",
                id.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), ":",
                extended ? "E" : "S");
        }

        private sealed class Pending
        {
            public Pending(SendPairRequest request, long sentAtUs, long deadlineUs)
            {
                Request = request;
                SentAtUs = sentAtUs;
                DeadlineUs = deadlineUs;
            }

            public SendPairRequest Request { get; }
            public long SentAtUs { get; private set; }
            public long DeadlineUs { get; private set; }

            /// <summary>收到 Echo 后，用真实时间戳重设窗口起点与截止。</summary>
            public void Restart(long sentAtUs, long windowUs)
            {
                SentAtUs = sentAtUs;
                DeadlineUs = sentAtUs + windowUs;
            }
        }

        /// <summary>一次成功的配对。</summary>
        public sealed class Pair
        {
            public Pair(SendPairRequest request, int channel, uint id, bool extended, long latencyUs)
            {
                Request = request;
                Channel = channel;
                Id = id;
                Extended = extended;
                LatencyUs = latencyUs;
            }

            public SendPairRequest Request { get; }
            public int Channel { get; }
            public uint Id { get; }
            public bool Extended { get; }

            /// <summary>从点发送到收到应答的耗时（微秒）。</summary>
            public long LatencyUs { get; }

            internal long MatchedAtUs { get; set; }

            public string LatencyText
            {
                get
                {
                    var ms = LatencyUs / 1000.0;
                    if (ms < 1)
                    {
                        return $"{LatencyUs}µs";
                    }

                    return ms < 1000 ? $"{ms:0.0}ms" : $"{ms / 1000:0.00}s";
                }
            }
        }
    }
}
