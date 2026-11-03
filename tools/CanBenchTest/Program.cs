using System;
using System.IO;
using System.Linq;
using CanBench;

namespace CanBenchTest
{
    /// <summary>离屏测试：list/CSV 往返、位域、与既有文件逐字节对齐。</summary>
    internal static class Program
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok) _fail++;
        }

        private static void Main(string[] args)
        {
            string samples = args.Length > 0 ? args[0] : FindSamples();

            // 1) 读仓库里既有的 .list，验证解析
            var src = Path.Combine(samples, "commands.list");
            var items = ListFile.Load(src);
            Console.WriteLine($"[1] 解析 {Path.GetFileName(src)}：{items.Count} 条");
            Check(items.Count == 42, "应有 42 条");

            var first = items[0];
            Check(first.Name == "重置", $"第 1 条备注 = 「{first.Name}」");
            Check(first.Id == 0x0AF00011u, $"第 1 条 ID = 0x{first.Id:X8}（原始 0x8AF00011 去掉 bit31 扩展标志）");
            Check(first.Extended, "第 1 条是扩展帧");
            Check(first.Data.Length == 8, $"第 1 条 DLC = {first.Data.Length}");

            var ver = items[2];
            Check(ver.Id == 0x02000002u, $"查询版本 ID = 0x{ver.Id:X8}");
            Check(ver.Data.Length == 0, "查询版本 DLC=0");

            // 2) 往返：写出后再读回，obj 必须逐字节一致
            var tmp = Path.Combine(Path.GetTempPath(), "canbench_rt.list");
            ListFile.Save(tmp, items);
            var back = ListFile.Load(tmp);
            Check(back.Count == items.Count, "往返条数一致");
            bool allSame = items.Zip(back).All(p =>
                p.First.Id == p.Second.Id && p.First.Extended == p.Second.Extended &&
                p.First.Data.AsSpan().SequenceEqual(p.Second.Data) && p.First.Name == p.Second.Name);
            Check(allSame, "往返后 ID/扩展/数据/备注 完全一致");

            // 3) 关键：与既有文件的 obj 十六进制逐字节比对
            var origText = File.ReadAllText(src);
            var newText = File.ReadAllText(tmp);
            var origObjs = System.Text.RegularExpressions.Regex.Matches(origText, "obj=\"([0-9A-Fa-f]+)\"")
                .Select(m => m.Groups[1].Value.ToUpperInvariant()).ToList();
            var newObjs = System.Text.RegularExpressions.Regex.Matches(newText, "obj=\"([0-9A-Fa-f]+)\"")
                .Select(m => m.Groups[1].Value.ToUpperInvariant()).ToList();
            Check(origObjs.Count == newObjs.Count, $"obj 数量 {origObjs.Count} vs {newObjs.Count}");
            Check(origObjs.SequenceEqual(newObjs), "obj 十六进制与既有文件逐字节一致");

            // 4) samples 下的每个 .list 都要能读
            foreach (var f in Directory.GetFiles(samples, "*.list"))
            {
                var got = ListFile.Load(f);
                Check(got.Count > 0, $"{Path.GetFileName(f)} 可解析（{got.Count} 条）");
            }

            // 5) CSV 往返
            var csv = Path.Combine(Path.GetTempPath(), "canbench_rt.csv");
            CsvFile.Save(csv, items);
            var fromCsv = CsvFile.Load(csv);
            Check(fromCsv.Count == items.Count, $"CSV 往返条数 {fromCsv.Count}");
            bool csvSame = items.Zip(fromCsv).All(p =>
                p.First.Id == p.Second.Id && p.First.Extended == p.Second.Extended &&
                p.First.Data.AsSpan().SequenceEqual(p.Second.Data) && p.First.Name == p.Second.Name);
            Check(csvSame, "CSV 往返后 ID/扩展/数据/备注 一致");

            // 6) 读 View 风格的 CSV（英文列名，含缺失列）
            var viewCsv = Path.Combine(Path.GetTempPath(), "view_style.csv");
            File.WriteAllText(viewCsv,
                "Channel,Id,Extended,Remote,Fd,BitRateSwitch,Length,DataHex,PeriodMs,Enabled\n" +
                "0,0x02000002,true,false,false,false,0,,600,false\n" +
                "0,0x01020002,true,false,false,false,5,00 44 7A 00 00,0,false\n");
            var viewItems = CsvFile.Load(viewCsv);
            Check(viewItems.Count == 2, $"View 风格 CSV 读到 {viewItems.Count} 条");
            Check(viewItems[0].Id == 0x02000002u && viewItems[0].PeriodMs == 600, "View 风格：ID/周期正确");
            Check(viewItems[1].Data.Length == 5 && viewItems[1].Data[0] == 0x00, "View 风格：数据正确");

            // 7) 数据解析容错
            Check(DataCodec.ParseHex("00 44 7A 00 00").Length == 5, "带空格解析");
            Check(DataCodec.ParseHex("00447A0000").Length == 5, "无空格解析");
            Check(DataCodec.ParseHex("0x00447A0000").Length == 5, "0x 前缀解析");
            Check(DataCodec.ParseHex("").Length == 0, "空串 = 0 字节");
            try { DataCodec.ParseHex("0G"); Check(false, "非法字符应抛异常"); }
            catch (FormatException) { Check(true, "非法字符抛 FormatException"); }

            // 8) ID 解析
            Check(MainForm.ParseId("0x02000002") == 0x02000002u, "0x 十六进制 ID");
            Check(MainForm.ParseId("0X02000002") == 0x02000002u, "0X 大写前缀");
            try { MainForm.ParseId("0x20000000"); Check(false, "超 29 位应抛异常"); }
            catch (FormatException) { Check(true, "超 29 位抛异常"); }

            // 9) 位域
            Check(IdCodec.Make(0x02, 0x00, 0x02) == 0x02000002u, "IdCodec 查询版本");
            Check(IdCodec.Make(0x01, 0x02, 0x02) == 0x01020002u, "IdCodec 工作模式");

            // 10) float 大端
            var fe = DataCodec.FloatBigEndian(100.0f);
            Check(fe[0] == 0x42 && fe[1] == 0xC8 && fe[2] == 0x00 && fe[3] == 0x00, "100.0f 大端 = 42 C8 00 00");
            Check(Math.Abs(DataCodec.ReadFloatBigEndian(fe, 0) - 100.0f) < 0.001, "大端回读 = 100.0");

            // 11) 错误帧文案
            Check(CanTransport.DescribeError(0x04).Contains("总线错误"), "错误位 0x04 = 总线错误");
            Check(CanTransport.DescribeError(0x40).Contains("BUS-OFF"), "错误位 0x40 = BUS-OFF");

            File.Delete(tmp); File.Delete(csv); File.Delete(viewCsv);

            _fail += TraceTests.Run();
            Console.WriteLine(_fail == 0 ? "\n全部通过" : $"\n失败 {_fail} 项");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }

        /// <summary>默认从输出目录向上找到仓库根的 tools/samples；也可以用参数指定。</summary>
        private static string FindSamples()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "samples");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return Path.Combine(Environment.CurrentDirectory, "samples");
        }
    }
}
