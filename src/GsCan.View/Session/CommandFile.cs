using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace GsCan.View.Session
{
    /// <summary>
    /// TxSlot 命令表的文件读写：.list（命令帧表）与 .csv。
    ///
    /// .list 单条目 76 字节，逆向自上位机导出文件：
    ///   [0..3]   ID，uint32 小端，bit31=1 表示扩展帧
    ///   [4..7]   DLC，uint32 小端
    ///   [8..71]  数据区，固定 64 字节
    ///   [72..75] 保留，恒 0
    ///
    /// CSV 表头：备注,CH,ID,扩展,RTR,DLC,数据,周期ms,启用
    /// 读取时对列名大小写、空格、别名都容错，也能读本工具旧版导出的
    /// Channel,Id,Extended,...,DataHex,PeriodMs,Enabled 风格。
    /// </summary>
    public static class CommandFile
    {
        private const int ObjSize = 76;

        // ---------------------------------------------------------------- .list

        public sealed class Entry
        {
            public string Name = string.Empty;
            public int Channel;
            public uint Id;
            public bool Extended;
            public bool Remote;
            public byte[] Data = Array.Empty<byte>();
            public double PeriodMs;
            public bool Enabled;

            public int Length => Data.Length;
        }

        public static List<Entry> LoadList(string path)
        {
            return ParseList(File.ReadAllText(path, Encoding.UTF8));
        }

        public static List<Entry> ParseList(string text)
        {
            var result = new List<Entry>();

            XDocument doc;
            try
            {
                doc = XDocument.Parse(text);
            }
            catch (Exception ex)
            {
                throw new FormatException("不是合法的 .list 文件：" + ex.Message, ex);
            }

            var root = doc.Root;
            if (root == null)
            {
                throw new FormatException(".list 文件缺少根节点。");
            }

            if (!string.Equals(root.Name.LocalName, "SendList", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"根节点应为 <SendList>，实际是 <{root.Name.LocalName}>。");
            }

            foreach (var el in root.Elements())
            {
                if (!string.Equals(el.Name.LocalName, "tagSendUint", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = (string?)el.Attribute("szname") ?? string.Empty;
                var objHex = ((string?)el.Attribute("obj") ?? string.Empty).Trim();
                var entry = new Entry { Name = name };

                if (objHex.Length > 0)
                {
                    var bytes = Convert.FromHexString(objHex);
                    if (bytes.Length < 8)
                    {
                        throw new FormatException($"\"{name}\" 的 obj 只有 {bytes.Length} 字节，至少需要 8 字节。");
                    }

                    uint raw = BitConverter.ToUInt32(bytes, 0);
                    entry.Extended = (raw & 0x80000000u) != 0;
                    entry.Id = raw & 0x1FFFFFFFu;

                    uint dlc = BitConverter.ToUInt32(bytes, 4);
                    if (dlc > 64)
                    {
                        dlc = 64;
                    }

                    if (bytes.Length < 8 + dlc)
                    {
                        throw new FormatException($"\"{name}\" 声明 DLC={dlc}，但数据区不够。");
                    }

                    var data = new byte[dlc];
                    Array.Copy(bytes, 8, data, 0, (int)dlc);
                    entry.Data = data;
                }

                if (double.TryParse((string?)el.Attribute("iInterval"), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var interval) && interval > 0)
                {
                    entry.PeriodMs = interval;
                }

                result.Add(entry);
            }

            return result;
        }

        public static void SaveList(string path, IEnumerable<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>\r\n<SendList m_dwCycles=\"1\">\r\n");

            foreach (var entry in entries)
            {
                var blob = new byte[ObjSize];
                uint raw = entry.Id & 0x1FFFFFFFu;
                if (entry.Extended)
                {
                    raw |= 0x80000000u;
                }

                BitConverter.GetBytes(raw).CopyTo(blob, 0);
                BitConverter.GetBytes((uint)entry.Length).CopyTo(blob, 4);
                Array.Copy(entry.Data, 0, blob, 8, entry.Length);

                sb.Append("\t<tagSendUint bIncreaseID=\"0\" bIncreaseData=\"0\" start_index=\"0\" byte_length=\"0\" ")
                  .Append("iInterval=\"").Append(entry.PeriodMs).Append("\" iTimes=\"1\" len=\"1\" szname=\"")
                  .Append(EscapeXml(entry.Name)).Append("\" select=\"0\" transmitas=\"0\" ")
                  .Append("e2e=\"0\" e2eDataId=\"0\" obj=\"").Append(Convert.ToHexString(blob))
                  .Append("\" is_canfd=\"0\"></tagSendUint>\r\n");
            }

            sb.Append("</SendList>\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        // ---------------------------------------------------------------- CSV

        public static List<Entry> LoadCsv(string path)
        {
            return ParseCsv(File.ReadAllLines(path, Encoding.UTF8));
        }

        public static List<Entry> ParseCsv(string[] lines)
        {
            if (lines.Length == 0)
            {
                return new List<Entry>();
            }

            var rows = new List<List<string>>(lines.Length);
            foreach (var line in lines)
            {
                rows.Add(SplitCsvLine(line));
            }

            int headerRow = FindHeaderRow(rows);
            if (headerRow < 0)
            {
                throw new FormatException(
                    "CSV 里找不到表头。至少要有一列 ID；" +
                    "本工具导出的表头是：备注,CH,ID,扩展,RTR,DLC,数据,周期ms,启用");
            }

            var header = new List<string>();
            foreach (var cell in rows[headerRow])
            {
                header.Add(Normalize(cell));
            }

            int Col(params string[] names)
            {
                foreach (var name in names)
                {
                    var idx = header.IndexOf(Normalize(name));
                    if (idx >= 0)
                    {
                        return idx;
                    }
                }

                return -1;
            }

            int cName = Col("备注", "作用", "说明", "name", "comment");
            int cCh = Col("CH", "channel", "通道");
            int cId = Col("ID", "id", "帧id", "canid");
            int cExt = Col("扩展", "extended", "ext");
            int cRtr = Col("RTR", "remote");
            int cDlc = Col("DLC", "length", "长度");
            int cData = Col("数据", "data", "datahex");
            int cPeriod = Col("周期ms", "periodms", "period", "周期", "interval");
            int cEnable = Col("启用", "enabled", "使能");

            if (cId < 0)
            {
                throw new FormatException("CSV 缺少 ID 列。");
            }

            var result = new List<Entry>();
            for (int r = headerRow + 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || IsBlank(row))
                {
                    continue;
                }

                string Get(int idx) => idx >= 0 && idx < row.Count ? row[idx].Trim() : string.Empty;

                var idText = Get(cId);
                if (idText.Length == 0)
                {
                    continue;
                }

                var entry = new Entry
                {
                    Name = Get(cName),
                    Channel = int.TryParse(Get(cCh), out var ch) && ch == 1 ? 1 : 0,
                    Extended = ParseFlag(Get(cExt), idText, true),
                    Remote = ParseFlag(Get(cRtr), string.Empty, false),
                    Enabled = ParseFlag(Get(cEnable), string.Empty, false),
                };

                if (!TxSlot.TryParseId(idText, out var id))
                {
                    throw new FormatException($"第 {r + 1} 行：ID \"{idText}\" 不合法。");
                }

                entry.Id = id;
                entry.Data = ParseHexBytes(Get(cData), r + 1);

                if (entry.Data.Length > 64)
                {
                    throw new FormatException($"第 {r + 1} 行：数据 {entry.Data.Length} 字节，最多 64 字节。");
                }

                if (!entry.Extended && entry.Id > 0x7FFu)
                {
                    throw new FormatException($"第 {r + 1} 行：标准帧 ID 不能超过 0x7FF。");
                }

                if (int.TryParse(Get(cDlc), out var dlc) && dlc >= 0 && dlc <= 64 && dlc != entry.Data.Length)
                {
                    var padded = new byte[dlc];
                    Array.Copy(entry.Data, padded, Math.Min(dlc, entry.Data.Length));
                    entry.Data = padded;
                }

                if (double.TryParse(Get(cPeriod), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var period) && period > 0)
                {
                    entry.PeriodMs = period;
                }

                result.Add(entry);
            }

            return result;
        }

        public static void SaveCsv(string path, IEnumerable<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("备注,CH,ID,扩展,RTR,DLC,数据,周期ms,启用");

            foreach (var entry in entries)
            {
                sb.AppendLine(string.Join(",",
                    Quote(entry.Name),
                    entry.Channel.ToString(CultureInfo.InvariantCulture),
                    Quote("0x" + entry.Id.ToString(entry.Extended ? "X8" : "X3", CultureInfo.InvariantCulture)),
                    entry.Extended ? "1" : "0",
                    entry.Remote ? "1" : "0",
                    entry.Length.ToString(CultureInfo.InvariantCulture),
                    Quote(FormatHex(entry.Data)),
                    entry.PeriodMs.ToString("0.###", CultureInfo.InvariantCulture),
                    entry.Enabled ? "1" : "0"));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static bool IsBlank(List<string> row)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(row[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindHeaderRow(List<List<string>> rows)
        {
            int limit = Math.Min(rows.Count, 10);

            for (int i = 0; i < limit; i++)
            {
                var norm = new List<string>();
                foreach (var cell in rows[i])
                {
                    norm.Add(Normalize(cell));
                }

                if (norm.Contains("id") && (norm.Contains("data") || norm.Contains("数据") || norm.Contains("dlc")))
                {
                    return i;
                }
            }

            for (int i = 0; i < limit; i++)
            {
                var norm = new List<string>();
                foreach (var cell in rows[i])
                {
                    norm.Add(Normalize(cell));
                }

                if (norm.Contains("id"))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string Normalize(string s)
        {
            return (s ?? string.Empty).Trim().Trim('\uFEFF').Replace(" ", string.Empty)
                .Replace("_", string.Empty).ToLowerInvariant();
        }

        private static bool ParseFlag(string text, string idText, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                // 没写扩展列时，按 ID 位数猜：超过 0x7FF 必然是扩展帧
                if (!string.IsNullOrEmpty(idText) && TxSlot.TryParseId(idText, out var id))
                {
                    return id > 0x7FFu;
                }

                return fallback;
            }

            text = text.Trim().ToLowerInvariant();
            return text is "1" or "true" or "ext" or "是" or "y" or "yes";
        }

        private static byte[] ParseHexBytes(string text, int lineNo)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<byte>();
            }

            if (!TxSlot.TryParseHex(text, out var data))
            {
                throw new FormatException($"第 {lineNo} 行：数据 \"{text}\" 不是合法的十六进制。");
            }

            return data;
        }

        private static string FormatHex(byte[] data)
        {
            if (data.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(data.Length * 3 - 1);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return s;
            }

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

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
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    cells.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            cells.Add(sb.ToString());
            return cells;
        }
    }
}
