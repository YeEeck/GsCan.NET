using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CanBench
{
    /// <summary>
    /// CSV 读写。列名与 GsCan View 的 TxSlotConfig 对齐，另外多一列「备注」。
    /// 读取时对列名大小写、空格、常见别名都做了容错。
    /// </summary>
    public static class CsvFile
    {
        private static readonly string[] Headers =
        {
            "备注", "CH", "ID", "扩展", "RTR", "DLC", "数据", "周期ms", "启用",
        };

        public static void Save(string path, IEnumerable<CanItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Headers));
            foreach (var it in items)
            {
                sb.AppendLine(string.Join(",",
                    Quote(it.Name),
                    it.Channel.ToString(CultureInfo.InvariantCulture),
                    Quote(it.IdHex),
                    it.Extended ? "1" : "0",
                    it.Remote ? "1" : "0",
                    it.Length.ToString(CultureInfo.InvariantCulture),
                    Quote(it.DataHex),
                    it.PeriodMs.ToString(CultureInfo.InvariantCulture),
                    it.Enabled ? "1" : "0"));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Quote(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static List<CanItem> Load(string path)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0) return new List<CanItem>();

            var rows = lines.Select(SplitCsvLine).ToList();
            int headerRow = FindHeaderRow(rows);
            if (headerRow < 0)
                throw new FormatException(
                    "CSV 里找不到表头。需要至少包含 ID 列；" +
                    "本工具导出的表头是：备注,CH,ID,扩展,RTR,DLC,数据,周期ms,启用");

            var header = rows[headerRow].Select(Normalize).ToList();
            int Col(params string[] names)
            {
                foreach (var n in names)
                {
                    var idx = header.FindIndex(h => h == Normalize(n));
                    if (idx >= 0) return idx;
                }
                return -1;
            }

            int cName = Col("备注", "作用", "说明", "name", "comment", "备注/作用");
            int cCh = Col("CH", "channel", "通道");
            int cId = Col("ID", "id", "帧id", "canid");
            int cExt = Col("扩展", "extended", "ext", "扩展帧");
            int cRtr = Col("RTR", "remote", "远程帧");
            int cDlc = Col("DLC", "length", "长度", "len");
            int cData = Col("数据", "data", "datahex", "数据(hex)", "十六进制数据");
            int cPeriod = Col("周期ms", "periodms", "period", "周期", "interval");
            int cEnable = Col("启用", "enabled", "enable", "使能");

            if (cId < 0) throw new FormatException("CSV 缺少 ID 列。");

            var result = new List<CanItem>();
            for (int r = headerRow + 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

                string Get(int idx) => idx >= 0 && idx < row.Count ? row[idx].Trim() : "";

                var idText = Get(cId);
                if (string.IsNullOrWhiteSpace(idText)) continue;

                var item = new CanItem
                {
                    Name = Get(cName),
                    Channel = int.TryParse(Get(cCh), out var ch) ? (ch == 1 ? 1 : 0) : 0,
                    Extended = ParseFlag(Get(cExt), idText),
                    Remote = ParseFlag(Get(cRtr), defaultValue: false),
                    Enabled = ParseFlag(Get(cEnable), defaultValue: false),
                };

                item.Id = MainForm.ParseId(idText);
                item.Data = DataCodec.ParseHex(Get(cData));

                if (item.Extended && item.Id > 0x1FFFFFFF)
                    throw new FormatException($"第 {r + 1} 行：扩展帧 ID 超出 29 位范围。");
                if (!item.Extended && item.Id > 0x7FF)
                    throw new FormatException($"第 {r + 1} 行：标准帧 ID 不能超过 0x7FF。");
                if (item.Data.Length > 8)
                    throw new FormatException($"第 {r + 1} 行：数据 {item.Data.Length} 字节，经典 CAN 最多 8 字节。");

                var dlcText = Get(cDlc);
                if (int.TryParse(dlcText, out var dlc) && dlc != item.Data.Length && dlc >= 0 && dlc <= 8)
                {
                    var padded = new byte[dlc];
                    Array.Copy(item.Data, padded, Math.Min(dlc, item.Data.Length));
                    item.Data = padded;
                }

                if (int.TryParse(Get(cPeriod), out var period) && period > 0)
                    item.PeriodMs = period;

                result.Add(item);
            }
            return result;
        }

        private static int FindHeaderRow(List<List<string>> rows)
        {
            for (int i = 0; i < Math.Min(rows.Count, 10); i++)
            {
                var norm = rows[i].Select(Normalize).ToList();
                if (norm.Contains("id") && (norm.Contains("data") || norm.Contains("数据") || norm.Contains("dlc")))
                    return i;
            }
            // 退一步：只要有 id 列就当表头
            for (int i = 0; i < Math.Min(rows.Count, 10); i++)
            {
                if (rows[i].Select(Normalize).Contains("id")) return i;
            }
            return -1;
        }

        private static string Normalize(string s)
            => (s ?? "").Trim().Trim('\uFEFF').Replace(" ", "").Replace("_", "").ToLowerInvariant();

        private static bool ParseFlag(string text, string idText = "", bool defaultValue = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                // 没写扩展列时，按 ID 的位数猜：超过 0x7FF 必然是扩展帧
                if (!string.IsNullOrEmpty(idText))
                {
                    try { return MainForm.ParseId(idText) > 0x7FF; } catch { }
                }
                return defaultValue;
            }
            text = text.Trim().ToLowerInvariant();
            return text is "1" or "true" or "ext" or "是" or "y" or "yes";
        }

        /// <summary>支持逗号分隔、双引号包裹（含转义 ""）。</summary>
        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            cells.Add(sb.ToString());
            return cells;
        }

        public static void SaveTrace(string path, IEnumerable<BusFrame> frames)
        {
            var sb = new StringBuilder();
            sb.AppendLine("时间,通道,类型,ID,格式,DLC,数据,说明");
            foreach (var f in frames)
            {
                sb.AppendLine(string.Join(",",
                    f.At.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    f.Channel.ToString(CultureInfo.InvariantCulture),
                    f.Kind,
                    Quote("0x" + f.Id.ToString(f.Extended ? "X8" : "X3")),
                    f.Extended ? "EXT" : "STD",
                    f.Data.Length.ToString(CultureInfo.InvariantCulture),
                    Quote(f.DataHex),
                    Quote(f.Error ? f.ErrorText : "")));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
