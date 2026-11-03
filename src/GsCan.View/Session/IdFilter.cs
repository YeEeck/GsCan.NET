using System;
using System.Collections.Generic;
using System.Globalization;

namespace GsCan.View.Session
{
    /// <summary>
    /// ID 过滤器。空 = 全放行。
    ///
    /// 语法（多个条件用空格、逗号或分号分隔）：
    ///   0x100          只显示这个 ID
    ///   0x100-0x1FF    只显示这个范围（也接受 0x100..0x1FF）
    ///   !0x01100001    排除这个 ID（心跳帧最常用）
    ///   !0x100-0x1FF   排除这个范围
    ///
    /// 只要有「只显示」条件就取交集；「排除」始终生效。ID 一律按十六进制解析。
    /// </summary>
    public sealed class IdFilter
    {
        private readonly List<(uint Lo, uint Hi)> _include = new List<(uint, uint)>();
        private readonly List<(uint Lo, uint Hi)> _exclude = new List<(uint, uint)>();

        public static readonly IdFilter Empty = new IdFilter();

        public bool IsEmpty => _include.Count == 0 && _exclude.Count == 0;

        public static IdFilter Parse(string? text)
        {
            var filter = new IdFilter();
            if (string.IsNullOrWhiteSpace(text))
            {
                return filter;
            }

            var tokens = text.Split(
                new[] { ' ', ',', ';', '\t', '，', '；' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in tokens)
            {
                var token = raw.Trim();
                bool exclude = false;
                if (token.StartsWith("!", StringComparison.Ordinal) || token.StartsWith("！", StringComparison.Ordinal))
                {
                    exclude = true;
                    token = token.Substring(1).Trim();
                }

                if (token.Length == 0)
                {
                    continue;
                }

                var range = ParseRange(token);
                if (exclude)
                {
                    filter._exclude.Add(range);
                }
                else
                {
                    filter._include.Add(range);
                }
            }

            return filter;
        }

        private static (uint Lo, uint Hi) ParseRange(string token)
        {
            var s = token.Replace("..", "-");
            var dash = s.IndexOf('-');
            if (dash > 0)
            {
                var lo = ParseId(s.Substring(0, dash));
                var hi = ParseId(s.Substring(dash + 1));
                if (hi < lo)
                {
                    (lo, hi) = (hi, lo);
                }

                return (lo, hi);
            }

            var single = ParseId(s);
            return (single, single);
        }

        private static uint ParseId(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(2);
            }

            if (s.Length == 0)
            {
                throw new FormatException("ID 不能为空。");
            }

            if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"\"{s}\" 不是合法的十六进制 ID（ID 一律按十六进制解析）。");
            }

            if (value > 0x1FFFFFFFu)
            {
                throw new FormatException($"ID 0x{value:X} 超过 29 位。");
            }

            return value;
        }

        public bool Match(uint id)
        {
            for (int i = 0; i < _exclude.Count; i++)
            {
                var (lo, hi) = _exclude[i];
                if (id >= lo && id <= hi)
                {
                    return false;
                }
            }

            if (_include.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < _include.Count; i++)
            {
                var (lo, hi) = _include[i];
                if (id >= lo && id <= hi)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
