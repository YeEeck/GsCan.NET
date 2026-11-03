using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace CanBench
{
    /// <summary>
    /// 按「通道 + 方向 + ID + 标志」聚合的最新一帧，带次数与周期。
    /// 心跳帧每秒几千条时，用它看每个 ID 的概况，不会被刷掉。
    ///
    /// 注意：全部用属性——WinForms 数据绑定只认属性，不认字段。
    /// 每帧只改内部状态，攒到界面节流点再一次性 NotifyChanged()，
    /// 这样只刷新变化的那一行，不做整表重绑。
    /// </summary>
    public sealed class LatestEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Channel { get; init; }
        public string Kind { get; init; } = "Rx";        // Rx / Echo / Error
        public uint Id { get; init; }
        public bool Extended { get; init; }
        public uint ErrorBits { get; init; }

        public byte[] Data { get; private set; } = Array.Empty<byte>();
        public int Count { get; private set; }
        public DateTime FirstAt { get; private set; }
        public DateTime LastAt { get; private set; }

        /// <summary>
        /// 用设备时间戳算周期，而不是 DateTime.Now。
        /// 一次 Drain 批量取回的帧共用同一个收包时刻，用本地时间会算出荒谬的周期。
        /// </summary>
        private uint _tsFirst;
        private uint _tsLast;
        private bool _hasTs;
        private long _tsTotalUs;
        private int _tsSamples;

        // ---- 只读显示属性 ----

        /// <summary>相对本机而言的方向：发送 / 接收 / 错误。</summary>
        public string Direction =>
            Kind == "Echo" ? "发送 →" :
            Kind == "Error" ? "错误 !" : "← 接收";

        public string IdHex => "0x" + Id.ToString(Extended ? "X8" : "X3");

        public string Flags => Extended ? "EXT" : "STD";

        public int Length => Data.Length;

        public string DataHex => BitConverter.ToString(Data).Replace("-", " ");

        public string ErrorText => Kind == "Error" ? CanTransport.DescribeError(ErrorBits) : "";

        /// <summary>
        /// 周期。优先用设备时间戳（candle 的 timestamp_us，硬件计数，最准）。
        /// 没有时间戳时退回本地时间；两者都可能缺失，此时显示「—」。
        /// </summary>
        public double PeriodMs
        {
            get
            {
                // 硬件时间戳最准；但「有样本」和「总时长 > 0」都要满足，
                // 否则会走进分支却除出 0，界面上显示成天文数字。
                if (_tsSamples > 0 && _tsTotalUs > 0)
                    return _tsTotalUs / (double)_tsSamples / 1000.0;

                if (Count < 2) return 0;
                var span = (LastAt - FirstAt).TotalMilliseconds;
                return span <= 0 ? 0 : span / (Count - 1);
            }
        }

        /// <summary>给 Latest 表显示用，自动选单位。</summary>
        public string PeriodText
        {
            get
            {
                if (Count < 2) return "—";
                var ms = PeriodMs;
                if (ms <= 0) return "—";
                if (ms < 1) return $"{ms * 1000:0}µs";
                if (ms < 1000) return $"{ms:0.0}ms";
                return $"{ms / 1000:0.00}s";
            }
        }

        public string Key => $"{Channel}|{Kind}|{Id:X8}|{(Extended ? 1 : 0)}|{ErrorBits:X8}";

        /// <summary>累加一帧（不触发界面通知，交给上层节流）。</summary>
        internal void Touch(BusFrame f)
        {
            if (Count == 0) FirstAt = f.At;
            Count++;
            LastAt = f.At;
            if (!f.Error) Data = f.Data;   // 错误帧没有数据，不要覆盖上一次的数据

            // 周期统计：设备时间戳是 32 位微秒计数，会回绕，用无符号差值
            uint ts = f.TimestampUs;
            if (ts != 0)
            {
                if (!_hasTs)
                {
                    _hasTs = true;
                    _tsFirst = ts;
                    _tsLast = ts;
                }
                else
                {
                    uint delta = ts - _tsLast;      // 无符号减法自动处理回绕
                    if (delta > 0 && delta < 10_000_000)   // 丢掉明显不合理的间隔（>10s）
                    {
                        _tsTotalUs += delta;
                        _tsSamples++;
                    }
                    _tsLast = ts;
                }
            }
        }

        /// <summary>一次性通知所有会变的显示字段——只重画这一行。</summary>
        internal void NotifyChanged()
        {
            var h = PropertyChanged;
            if (h == null) return;
            h(this, new PropertyChangedEventArgs(nameof(Count)));
            h(this, new PropertyChangedEventArgs(nameof(Data)));
            h(this, new PropertyChangedEventArgs(nameof(DataHex)));
            h(this, new PropertyChangedEventArgs(nameof(Length)));
            h(this, new PropertyChangedEventArgs(nameof(PeriodMs)));
            h(this, new PropertyChangedEventArgs(nameof(PeriodText)));
            h(this, new PropertyChangedEventArgs(nameof(LastAt)));
        }

        public override string ToString() =>
            $"CH{Channel} {Direction} {IdHex} {Flags} DLC={Length} [{DataHex}]";
    }

    /// <summary>Latest 视图的数据模型（与界面无关，可单独测试）。</summary>
    public sealed class LatestAggregator
    {
        private readonly Dictionary<string, LatestEntry> _map = new Dictionary<string, LatestEntry>();

        public int Count => _map.Count;

        public IEnumerable<LatestEntry> Entries => _map.Values;

        public void Clear() => _map.Clear();

        public LatestEntry Add(BusFrame f) => Add(f, out _);

        /// <summary>返回该帧归属的条目；isNew 表示这是新出现的 ID（需要往表里插一行）。</summary>
        public LatestEntry Add(BusFrame f, out bool isNew)
        {
            var kind = f.Error ? "Error" : (f.Echo ? "Echo" : "Rx");
            var key = $"{f.Channel}|{kind}|{f.Id:X8}|{(f.Extended ? 1 : 0)}|{(f.Error ? f.ErrorBits : 0u):X8}";

            if (!_map.TryGetValue(key, out var e))
            {
                e = new LatestEntry
                {
                    Channel = f.Channel,
                    Kind = kind,
                    Id = f.Id,
                    Extended = f.Extended,
                    ErrorBits = f.Error ? f.ErrorBits : 0,
                };
                _map[key] = e;
                isNew = true;
            }
            else
            {
                isNew = false;
            }

            e.Touch(f);
            return e;
        }

        public List<LatestEntry> Snapshot() => _map.Values.ToList();

        /// <summary>表格里的稳定排序：通道 → 方向 → ID。</summary>
        public static int Compare(LatestEntry a, LatestEntry b)
        {
            int c = a.Channel.CompareTo(b.Channel);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Kind, b.Kind);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }
    }

    /// <summary>
    /// ID 过滤。空 = 全放行。
    /// 语法（多个条件用空格、逗号或分号分隔）：
    ///   0x100          只显示这个 ID
    ///   0x100-0x1FF    只显示这个范围（也接受 0x100..0x1FF）
    ///   !0x01100001    排除这个 ID
    ///   !0x100-0x1FF   排除这个范围
    /// 只要有「只显示」的条件，就取交集；「排除」始终生效。
    /// </summary>
    public sealed class IdFilter
    {
        private readonly List<(uint Lo, uint Hi)> _include = new List<(uint, uint)>();
        private readonly List<(uint Lo, uint Hi)> _exclude = new List<(uint, uint)>();

        public bool IsEmpty => _include.Count == 0 && _exclude.Count == 0;

        public static IdFilter Parse(string? text)
        {
            var f = new IdFilter();
            if (string.IsNullOrWhiteSpace(text)) return f;

            var tokens = text.Split(new[] { ' ', ',', ';', '\t', '，', '；' },
                                    StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in tokens)
            {
                var tok = raw.Trim();
                bool exclude = false;
                if (tok.StartsWith("!") || tok.StartsWith("！"))
                {
                    exclude = true;
                    tok = tok.Substring(1).Trim();
                }
                if (tok.Length == 0) continue;

                var range = ParseRange(tok);
                if (exclude) f._exclude.Add(range);
                else f._include.Add(range);
            }
            return f;
        }

        private static (uint, uint) ParseRange(string tok)
        {
            var s = tok.Replace("..", "-");
            var dash = s.IndexOf('-');
            if (dash > 0)
            {
                var lo = ParseId(s.Substring(0, dash));
                var hi = ParseId(s.Substring(dash + 1));
                if (hi < lo) (lo, hi) = (hi, lo);
                return (lo, hi);
            }
            var single = ParseId(s);
            return (single, single);
        }

        private static uint ParseId(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length == 0) throw new FormatException("ID 不能为空");
            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"\"{s}\" 不是合法的十六进制 ID（ID 一律按十六进制解析）");
            if (v > 0x1FFFFFFF) throw new FormatException($"ID 0x{v:X} 超过 29 位");
            return v;
        }

        public bool Match(uint id)
        {
            foreach (var (lo, hi) in _exclude)
                if (id >= lo && id <= hi) return false;

            if (_include.Count == 0) return true;

            foreach (var (lo, hi) in _include)
                if (id >= lo && id <= hi) return true;

            return false;
        }
    }
}
