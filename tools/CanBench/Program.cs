using System;
using System.IO;
using System.Windows.Forms;

namespace CanBench
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // 无界面自检：连设备、收 N 秒、把 Latest 表打到 stdout 后退出。
            // 用来在 CI/命令行里验证心跳帧聚合是否生效。
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length > 0 && args[0] == "--dump")
            {
                DumpMode(args);
                return;
            }

            ApplicationConfiguration.Initialize();

            var form = new MainForm();

            string? openFile = null;
            bool auto = false;

            foreach (var a in args)
            {
                if (a == "--auto") auto = true;
                else if (!a.StartsWith("--") && File.Exists(a)) openFile = a;
            }

            form.Shown += (_, __) =>
            {
                if (openFile != null)
                {
                    try { form.LoadFile(openFile); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                if (auto) form.AutoStart();
            };

            Application.Run(form);
        }

        /// <summary>
        /// --dump [--ms=3000] [--ch1] [--filter=...]
        /// 不开界面，直接打开设备、启动 CH1（只听）收一段时间，打印 Latest 聚合结果。
        /// </summary>
        private static void DumpMode(string[] args)
        {
            int ms = 3000;
            string filter = "";
            bool sendQuery = false;
            foreach (var a in args)
            {
                if (a.StartsWith("--ms=") && int.TryParse(a.Substring(5), out var v)) ms = v;
                if (a.StartsWith("--filter=")) filter = a.Substring(9);
                if (a == "--send") sendQuery = true;
            }

            var devs = CanTransport.Scan();
            Console.WriteLine($"设备数 = {devs.Count}");
            if (devs.Count == 0) { Environment.Exit(2); }

            using var bus = new CanTransport();
            try { Console.WriteLine(bus.Open(devs[0].Index)); }
            catch (Exception ex) { Console.WriteLine("[占用] " + ex.Message); Environment.Exit(3); }

            var flt = IdFilter.Parse(filter);
            var latest = new LatestAggregator();

            bus.StartChannel(0, 500_000, listenOnly: false);
            bus.StartChannel(1, 500_000, listenOnly: true);
            Console.WriteLine($"已启动 CH0(正常) / CH1(只听)，收 {ms}ms，过滤=\"{filter}\"");

            if (sendQuery)
            {
                bus.Send(new CanItem { Id = 0x02000002u, Extended = true, Channel = 0 });
                Console.WriteLine("已发 0x02000002（查询版本）");
            }

            var deadline = Environment.TickCount64 + ms;
            long total = 0, kept = 0;
            while (Environment.TickCount64 < deadline)
            {
                foreach (var f in bus.Drain())
                {
                    total++;
                    if (!flt.Match(f.Id)) continue;
                    kept++;
                    latest.Add(f);
                }
                System.Threading.Thread.Sleep(10);
            }
            bus.Close();

            Console.WriteLine($"\n共收到 {total} 帧，过滤后参与聚合 {kept} 帧");
            Console.WriteLine($"Latest 表 {latest.Count} 行：");
            Console.WriteLine("  CH  方向     ID          标志 长度 数据                     计数    周期");
            var rows = latest.Snapshot();
            rows.Sort((a, b) =>
            {
                int c = a.Channel.CompareTo(b.Channel);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.Kind, b.Kind);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            foreach (var r in rows)
            {
                Console.WriteLine($"  {r.Channel}   {r.Direction,-6}  {r.IdHex}  {r.Flags}  {r.Length,4} {r.DataHex,-24} {r.Count,6}  {r.PeriodText}");
            }

            // 判定：Raw 流量远大于行数才算真的没堵满
            if (total > 1000 && latest.Count <= 20)
                Console.WriteLine($"\n√ {total} 帧只聚合出 {latest.Count} 行 —— 心跳帧没有堵满");
            else if (total > 0)
                Console.WriteLine($"\n? {total} 帧 -> {latest.Count} 行，请人工判断");
            else
                Console.WriteLine("\n! 没收到任何帧");
        }
    }
}
