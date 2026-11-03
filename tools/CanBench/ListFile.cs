using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace CanBench
{
    /// <summary>一条 CAN 发送命令。全部用属性——WinForms 数据绑定只认属性，不认字段。</summary>
    public sealed class CanItem : INotifyPropertyChanged
    {
        private string _name = "";
        private uint _id;
        private bool _extended = true;
        private bool _remote;
        private bool _fd;
        private bool _bitRateSwitch;
        private byte[] _data = Array.Empty<byte>();
        private int _periodMs;
        private bool _enabled;
        private int _channel;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, string name)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name == nameof(Data))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Length)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DataHex)));
            }
            if (name == nameof(Id) || name == nameof(Extended))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IdHex)));
        }

        /// <summary>备注 / 作用。原 .list 的 szname。</summary>
        public string Name { get => _name; set => Set(ref _name, value ?? "", nameof(Name)); }

        public uint Id { get => _id; set => Set(ref _id, value, nameof(Id)); }

        public bool Extended { get => _extended; set => Set(ref _extended, value, nameof(Extended)); }

        public bool Remote { get => _remote; set => Set(ref _remote, value, nameof(Remote)); }

        public bool Fd { get => _fd; set => Set(ref _fd, value, nameof(Fd)); }

        public bool BitRateSwitch { get => _bitRateSwitch; set => Set(ref _bitRateSwitch, value, nameof(BitRateSwitch)); }

        public byte[] Data
        {
            get => _data;
            set => Set(ref _data, value ?? Array.Empty<byte>(), nameof(Data));
        }

        public int PeriodMs { get => _periodMs; set => Set(ref _periodMs, value, nameof(PeriodMs)); }

        public bool Enabled { get => _enabled; set => Set(ref _enabled, value, nameof(Enabled)); }

        public int Channel { get => _channel; set => Set(ref _channel, value, nameof(Channel)); }

        // ---- 只读派生属性，供表格显示 ----

        public int Length => _data.Length;

        public string DataHex => BitConverter.ToString(_data).Replace("-", " ");

        /// <summary>ID 的十六进制文本；可写，便于直接在表格里编辑。</summary>
        public string IdHex
        {
            get => "0x" + _id.ToString(_extended ? "X8" : "X3");
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                Id = MainForm.ParseId(value);
            }
        }

        public CanItem Clone()
        {
            return new CanItem
            {
                Name = Name, Id = Id, Extended = Extended, Remote = Remote, Fd = Fd,
                BitRateSwitch = BitRateSwitch, Data = (byte[])Data.Clone(),
                PeriodMs = PeriodMs, Enabled = Enabled, Channel = Channel,
            };
        }
    }

    /// <summary>扩展 ID 位域拆解，便于在界面上直接看出命令含义。</summary>
    public static class IdCodec
    {
        public static uint Make(byte cmd, byte aux, byte addr, byte reserved = 0)
            => ((uint)(cmd & 0x1F) << 24) | ((uint)aux << 16) | ((uint)reserved << 8) | addr;
    }

    public static class DataCodec
    {
        public static byte[] ParseHex(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
            var sb = new StringBuilder();
            foreach (var ch in text)
            {
                if (ch == ' ' || ch == '-' || ch == '\t' || ch == '\r' || ch == '\n' || ch == ',') continue;
                sb.Append(ch);
            }
            var s = sb.ToString();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length % 2 != 0)
                throw new FormatException("十六进制字节数必须是偶数（每个字节两位）。");

            var result = new byte[s.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                var pair = s.Substring(i * 2, 2);
                if (!byte.TryParse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
                    throw new FormatException($"无法解析字节 \"{pair}\"。");
            }
            return result;
        }

        public static byte[] FloatBigEndian(float v)
        {
            var b = BitConverter.GetBytes(v);
            Array.Reverse(b);
            return b;
        }

        public static float ReadFloatBigEndian(byte[] d, int offset)
        {
            if (d.Length < offset + 4) return 0f;
            var b = new[] { d[offset + 3], d[offset + 2], d[offset + 1], d[offset] };
            return BitConverter.ToSingle(b, 0);
        }
    }

    public static class ListFile
    {
        // 逆向自 Doc/测试命令帧_261020.list：
        //   [0..3]  ID，uint32 小端，bit31=1 表示扩展帧
        //   [4..7]  DLC，uint32 小端
        //   [8..71] 数据区，固定 64 字节
        //   [72..75] 保留，恒 0
        private const int ObjSize = 76;

        public static List<CanItem> Load(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            return Parse(text);
        }

        public static List<CanItem> Parse(string text)
        {
            var result = new List<CanItem>();
            XDocument doc;
            try { doc = XDocument.Parse(text); }
            catch (Exception ex) { throw new FormatException("不是合法的 .list XML：" + ex.Message); }

            var root = doc.Root;
            if (root == null) throw new FormatException(".list 文件缺少根节点。");
            if (!string.Equals(root.Name.LocalName, "SendList", StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"根节点应为 <SendList>，实际是 <{root.Name.LocalName}>。");

            foreach (var el in root.Elements())
            {
                if (!string.Equals(el.Name.LocalName, "tagSendUint", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = (string?)el.Attribute("szname") ?? "";
                var objHex = ((string?)el.Attribute("obj") ?? "").Trim();
                var item = new CanItem { Name = name };

                if (!string.IsNullOrEmpty(objHex))
                {
                    var bytes = Convert.FromHexString(objHex);
                    if (bytes.Length < 8)
                        throw new FormatException($"\"{name}\" 的 obj 长度 {bytes.Length} 字节，至少需要 8 字节。");

                    uint raw = BitConverter.ToUInt32(bytes, 0);
                    item.Extended = (raw & 0x80000000u) != 0;
                    item.Id = raw & 0x1FFFFFFFu;

                    uint dlc = BitConverter.ToUInt32(bytes, 4);
                    if (dlc > 64) dlc = 64;
                    if (bytes.Length < 8 + dlc)
                        throw new FormatException($"\"{name}\" 声明 DLC={dlc}，但数据区不够。");

                    var data = new byte[dlc];
                    Array.Copy(bytes, 8, data, 0, (int)dlc);
                    item.Data = data;
                }

                if (int.TryParse((string?)el.Attribute("iInterval"), out var interval) && interval > 0)
                    item.PeriodMs = interval;

                result.Add(item);
            }

            return result;
        }

        public static void Save(string path, IEnumerable<CanItem> items)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>\r\n<SendList m_dwCycles=\"1\">\r\n");
            foreach (var it in items)
            {
                var blob = new byte[ObjSize];
                uint raw = it.Id & 0x1FFFFFFFu;
                if (it.Extended) raw |= 0x80000000u;
                BitConverter.GetBytes(raw).CopyTo(blob, 0);
                BitConverter.GetBytes((uint)it.Length).CopyTo(blob, 4);
                Array.Copy(it.Data, 0, blob, 8, it.Length);

                sb.Append("\t<tagSendUint bIncreaseID=\"0\" bIncreaseData=\"0\" start_index=\"0\" byte_length=\"0\" ")
                  .Append("iInterval=\"").Append(it.PeriodMs).Append("\" iTimes=\"1\" len=\"1\" szname=\"")
                  .Append(Escape(it.Name)).Append("\" select=\"0\" transmitas=\"0\" ")
                  .Append("e2e=\"0\" e2eDataId=\"0\" obj=\"").Append(Convert.ToHexString(blob))
                  .Append("\" is_canfd=\"").Append(it.Fd ? "1" : "0").Append("\"></tagSendUint>\r\n");
            }
            sb.Append("</SendList>\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
