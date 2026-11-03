using System;
using System.Collections.Generic;
using System.IO;
using GsCan.View.Session;

namespace CmdFileCheck
{
    /// <summary>验证 .list / CSV 往返、IdFilter、TxSlot 可编辑性。</summary>
    internal static class Program
    {
        private static int _fail;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  OK   " : "  FAIL ") + what);
            if (!ok)
            {
                _fail++;
            }
        }

        private static void Main(string[] args)
        {
            string sampleList = args.Length > 0
                ? args[0]
                : @"D:\Project\resisterSim\Doc\测试命令帧_261020.list";

            ListRoundTrip(sampleList);
            CsvRoundTrip(sampleList);
            Filter();
            Editing();
            Period();
            Pairing();
            MoveAndClear();
            _fail += HighlightTest.Run();

            Console.WriteLine(_fail == 0 ? "\n全部通过" : $"\n失败 {_fail} 项");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }

        private static void ListRoundTrip(string sample)
        {
            Console.WriteLine("[.list 往返]");
            if (!File.Exists(sample))
            {
                Console.WriteLine("  （样例文件不存在，跳过）");
                return;
            }

            var items = CommandFile.LoadList(sample);
            Check(items.Count == 42, $"解析 {items.Count} 条（期望 42）");
            Check(items[0].Name == "重置", $"第 1 条备注「{items[0].Name}」");
            Check(items[0].Id == 0x0AF00011u, $"第 1 条 ID = 0x{items[0].Id:X8}（原始 0x8AF00011 去掉扩展位）");
            Check(items[0].Extended, "第 1 条为扩展帧");

            var tmp = Path.Combine(Path.GetTempPath(), "gsv_rt.list");
            CommandFile.SaveList(tmp, items);
            var back = CommandFile.LoadList(tmp);

            Check(back.Count == items.Count, "往返条数一致");
            bool same = true;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Id != back[i].Id || items[i].Extended != back[i].Extended ||
                    items[i].Name != back[i].Name || items[i].Length != back[i].Length)
                {
                    same = false;
                    break;
                }

                for (int b = 0; b < items[i].Length; b++)
                {
                    if (items[i].Data[b] != back[i].Data[b])
                    {
                        same = false;
                        break;
                    }
                }
            }

            Check(same, "往返后 ID/扩展/备注/数据 完全一致");

            // 与原始文件的 obj 十六进制逐字节比对
            var origObjs = ExtractObjs(File.ReadAllText(sample));
            var newObjs = ExtractObjs(File.ReadAllText(tmp));
            Check(origObjs.Count == newObjs.Count, $"obj 数量 {origObjs.Count} vs {newObjs.Count}");
            bool objsSame = origObjs.Count == newObjs.Count;
            for (int i = 0; objsSame && i < origObjs.Count; i++)
            {
                if (!string.Equals(origObjs[i], newObjs[i], StringComparison.OrdinalIgnoreCase))
                {
                    objsSame = false;
                }
            }

            Check(objsSame, "obj 十六进制与原始文件逐字节一致");
            File.Delete(tmp);
        }

        private static List<string> ExtractObjs(string text)
        {
            var list = new List<string>();
            int i = 0;
            while (true)
            {
                int at = text.IndexOf("obj=\"", i, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                int start = at + 5;
                int end = text.IndexOf('"', start);
                if (end < 0)
                {
                    break;
                }

                list.Add(text.Substring(start, end - start));
                i = end;
            }

            return list;
        }

        private static void CsvRoundTrip(string sample)
        {
            Console.WriteLine("\n[CSV 往返]");
            if (!File.Exists(sample))
            {
                return;
            }

            var items = CommandFile.LoadList(sample);
            var tmp = Path.Combine(Path.GetTempPath(), "gsv_rt.csv");
            CommandFile.SaveCsv(tmp, items);
            var back = CommandFile.LoadCsv(tmp);

            Check(back.Count == items.Count, $"往返条数 {back.Count}");
            bool same = true;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Id != back[i].Id || items[i].Extended != back[i].Extended || items[i].Name != back[i].Name)
                {
                    same = false;
                    break;
                }
            }

            Check(same, "往返后 ID/扩展/备注 一致");
            File.Delete(tmp);

            // 带逗号和引号的备注要能原样回来
            var weird = new List<CommandFile.Entry>
            {
                new CommandFile.Entry { Name = "a,b", Id = 0x123u, Extended = false, Data = new byte[] { 1, 2 } },
                new CommandFile.Entry { Name = "带\"引号\"", Id = 0x124u, Extended = false, Data = Array.Empty<byte>() },
                new CommandFile.Entry { Name = "", Id = 0x125u, Extended = false, Data = Array.Empty<byte>() },
            };
            var tmp2 = Path.Combine(Path.GetTempPath(), "gsv_weird.csv");
            CommandFile.SaveCsv(tmp2, weird);
            var back2 = CommandFile.LoadCsv(tmp2);
            Check(back2.Count == 3 && back2[0].Name == "a,b" && back2[1].Name == "带\"引号\"",
                  "含逗号/引号的备注往返正确");
            File.Delete(tmp2);

            // 读 View 风格（英文列名、无备注列）
            var tmp3 = Path.Combine(Path.GetTempPath(), "gsv_view.csv");
            File.WriteAllText(tmp3,
                "Channel,Id,Extended,Remote,Fd,BitRateSwitch,Length,DataHex,PeriodMs,Enabled\n" +
                "0,0x02000002,true,false,false,false,0,,600,false\n" +
                "0,0x01020002,true,false,false,false,5,00 44 7A 00 00,0,false\n");
            var v = CommandFile.LoadCsv(tmp3);
            Check(v.Count == 2, $"View 风格读到 {v.Count} 条");
            Check(v[0].Id == 0x02000002u && v[0].PeriodMs == 600, "View 风格 ID/周期正确");
            Check(v[1].Data.Length == 5 && v[1].Data[0] == 0x00, "View 风格数据正确");
            File.Delete(tmp3);
        }

        private static void Filter()
        {
            Console.WriteLine("\n[ID 过滤]");
            Check(IdFilter.Parse("").IsEmpty, "空条件");
            Check(IdFilter.Parse("0x01100001").Match(0x01100001u), "单个 ID");
            Check(!IdFilter.Parse("0x01100001").Match(0x02000002u), "单个 ID 不匹配其它");

            var r = IdFilter.Parse("0x100-0x1FF");
            Check(r.Match(0x100) && r.Match(0x1FF) && !r.Match(0xFF) && !r.Match(0x200), "范围边界");

            var ex = IdFilter.Parse("!0x01100001");
            Check(!ex.Match(0x01100001u) && ex.Match(0x02000002u), "排除心跳帧");

            var exr = IdFilter.Parse("!0x01100000-0x011000FF");
            Check(!exr.Match(0x01100001u) && exr.Match(0x02000002u), "排除范围");

            var mixed = IdFilter.Parse("0x02000002 0x01000002 !0x01000002");
            Check(mixed.Match(0x02000002u), "包含+排除混用");

            bool threw = false;
            try
            {
                IdFilter.Parse("ZZZZ");
            }
            catch (FormatException)
            {
                threw = true;
            }

            Check(threw, "非法 ID 抛 FormatException");
        }

        private static void Editing()
        {
            Console.WriteLine("\n[编辑能力]");
            var slot = new TxSlot(0);

            // DataHex 的既有语义：保留当前 DLC，写不满就补 0。
            slot.DataHex = "00 44 7A";
            Check(slot.Length == 8 && slot.Data[1] == 0x44 && slot.Data[2] == 0x7A,
                  $"写 DataHex 能改数据（DLC 保持 8，实际 Length={slot.Length}）");

            slot.Length = 3;
            slot.DataHex = "0x00447A";
            Check(slot.Length == 3 && slot.Data[0] == 0x00 && slot.Data[2] == 0x7A,
                  $"带 0x 前缀也能写（Length={slot.Length}）");

            slot.IdHex = "0x02000002";
            Check(slot.Id == 0x02000002u, "IdHex 接受 0x 前缀");
            slot.IdHex = "1020002";
            Check(slot.Id == 0x01020002u, "IdHex 无前缀按十六进制");

            slot.IdHex = "0x30000000";
            Check(slot.Id == 0x01020002u, "超 29 位被拒绝，保持原值");

            slot.Name = "查询版本";
            Check(slot.Name == "查询版本", "备注可写");

            slot.Length = 5;
            Check(slot.Data.Length == 5, "改 DLC 会补齐数据");

            Check(TxSlot.MaxSlotCount >= 64, $"行数上限 {TxSlot.MaxSlotCount}（够放整条序列）");
        }

        private static void MoveAndClear()
        {
            Console.WriteLine("\n[移动与清空]");

            // 无回调时调用是安全的
            var solo = new TxSlot(0);
            solo.MoveUp();
            solo.MoveDown();
            Check(solo.CanMoveUp == false && solo.CanMoveDown == false, "无回调时移动安全且状态为不可移动");

            // 真实的会话级移动：用假设备跑一遍
            var fake = new FakePort();
            var session = new ViewSession(fake, runBackgroundPumps: false);
            session.ImportCommands(new List<CommandFile.Entry>
            {
                new CommandFile.Entry { Name = "A", Id = 1, Extended = false },
                new CommandFile.Entry { Name = "B", Id = 2, Extended = false },
                new CommandFile.Entry { Name = "C", Id = 3, Extended = false },
            });

            Check(session.TxSlots.Count == 3, $"导入 3 条（{session.TxSlots.Count}）");
            Check(!session.TxSlots[0].CanMoveUp, "首行不能上移");
            Check(!session.TxSlots[2].CanMoveDown, "末行不能下移");

            // 把 C（第 3 条）上移两次 -> C A B
            session.MoveTxSlotUp(session.TxSlots[2]);
            session.MoveTxSlotUp(session.TxSlots[1]);
            Check(session.TxSlots[0].Name == "C" && session.TxSlots[1].Name == "A" && session.TxSlots[2].Name == "B",
                  $"上移顺序正确（{session.TxSlots[0].Name}{session.TxSlots[1].Name}{session.TxSlots[2].Name}）");

            // 再下移回去
            session.MoveTxSlotDown(session.TxSlots[0]);
            Check(session.TxSlots[0].Name == "A" && session.TxSlots[1].Name == "C",
                  $"下移顺序正确（{session.TxSlots[0].Name}{session.TxSlots[1].Name}）");

            // 索引已重排
            Check(session.TxSlots[0].Index == 0 && session.TxSlots[2].Index == 2, "移动后索引已重排");

            // 清空
            session.ClearTxSlots();
            Check(session.TxSlots.Count == 1, $"清空后剩 1 行（{session.TxSlots.Count}）");
            Check(string.IsNullOrEmpty(session.TxSlots[0].Name) && session.TxSlots[0].Id == 0,
                  "清空后是空白行");
        }

        private static void Pairing()
        {
            Console.WriteLine("\n[发送/应答配对]");

            var tr = new SendPairTracker();

            // 基本配对：登记 -> Echo -> 应答
            tr.Begin(new SendPairRequest(0, 0, 0x02000002u, true, "查询版本"), 1_000_000);
            Check(tr.PendingCount == 1, "登记后有 1 个待配对");
            Check(tr.Observe(0, 0x02000002u, true, true, false, 1_000_100) == null, "Echo 不产生配对");
            var p1 = tr.Observe(0, 0x02000002u, true, false, false, 1_002_000);
            Check(p1 != null, "应答配对成功");
            Check(p1 != null && p1.Request.SlotIndex == 0, "配到正确的发送行");
            Check(p1 != null && Math.Abs(p1.LatencyUs - 2000) < 1, $"延迟 2ms（{p1?.LatencyText}）");

            // 心跳帧不能抢走配对
            tr = new SendPairTracker();
            tr.Begin(new SendPairRequest(0, 0, 0x01020002u, true, "工作模式"), 10_000_000);
            // 模拟 3000 帧/秒的心跳，夹在中间
            bool heartbeatStole = false;
            for (int i = 0; i < 3000; i++)
            {
                if (tr.Observe(1, 0x01100001u, true, false, false, 10_000_000 + i * 300) != null)
                {
                    heartbeatStole = true;
                    break;
                }
            }

            Check(!heartbeatStole, "3000 帧心跳一帧都没有被误配成应答");

            Check(tr.PendingCount == 1, "3000 帧心跳过后请求仍在等待");
            var p2 = tr.Observe(0, 0x01020002u, true, false, false, 10_002_500);
            Check(p2 != null, "心跳干扰下仍能正确配对");

            // 超窗口不配对
            tr = new SendPairTracker { WindowMs = 100 };
            tr.Begin(new SendPairRequest(0, 0, 0x123u, false, "短窗"), 20_000_000);
            Check(tr.Observe(0, 0x123u, false, false, false, 20_500_000) == null, "超出窗口后不再配对");

            // 同 ID 重发：以最新那次为准，不堆积
            tr = new SendPairTracker();
            for (int i = 0; i < 10; i++)
            {
                tr.Begin(new SendPairRequest(0, 0, 0x777u, false, "周期"), 30_000_000 + i * 1000);
            }

            Check(tr.PendingCount == 1, $"同 ID 连续发 10 次只有 1 个待配对（实际 {tr.PendingCount}）");

            // 错误帧让请求立刻失效
            tr = new SendPairTracker();
            tr.Begin(new SendPairRequest(0, 0, 0x888u, false, "会失败"), 40_000_000);
            tr.Observe(0, 0x888u, false, false, true, 40_000_500);
            Check(tr.PendingCount == 0, "错误帧后请求被清掉");

            // 不同通道/不同 ID 不误配
            tr = new SendPairTracker();
            tr.Begin(new SendPairRequest(0, 0, 0x100u, false, "CH0"), 50_000_000);
            Check(tr.Observe(1, 0x100u, false, false, false, 50_000_100) == null, "不同通道不配对");
            Check(tr.Observe(0, 0x101u, false, false, false, 50_000_200) == null, "不同 ID 不配对");
            Check(tr.Observe(0, 0x100u, false, false, false, 50_000_300) != null, "同通道同 ID 才配对");

            // 标准帧与扩展帧不混淆
            tr = new SendPairTracker();
            tr.Begin(new SendPairRequest(0, 0, 0x200u, false, "标准帧"), 60_000_000);
            Check(tr.Observe(0, 0x200u, true, false, false, 60_000_100) == null, "标准/扩展不混淆");
        }

        private static void Period()
        {
            Console.WriteLine("\n[周期与方向]");
            var frame1 = new FrameRow(0, 1, "Rx", 0x01100001u, true, false, false, false, false, false, 8, "FF FF 03 BE 00 00 00 00", 1_000_000u);
            var row = new LatestRow(frame1);
            Check(row.Direction == "← 接收", $"Rx 方向 = {row.Direction}");
            Check(row.PeriodText == "—", "只有 1 帧时周期显示「—」");

            frame1 = new FrameRow(0, 1, "Rx", 0x01100001u, true, false, false, false, false, false, 8, "FF FF 03 BE 00 00 00 00", 1_000_300u);
            row.Apply(frame1, 1);
            Check(Math.Abs(row.PeriodMilliseconds - 0.3) < 0.01, $"周期 300µs（{row.PeriodText}）");

            var echoFrame = new FrameRow(0, 0, "Echo", 0x02000002u, true, false, false, false, false, false, 0, string.Empty, 5u);
            var echoRow = new LatestRow(echoFrame);
            Check(echoRow.Direction == "发送 →", $"Echo 方向 = {echoRow.Direction}");

            var errFrame = new FrameRow(0, 0, "Error", 0, false, false, false, false, false, false, 0, string.Empty, 6u, "ACK", "无节点应答");
            var errRow = new LatestRow(errFrame);
            Check(errRow.Direction == "← 错误!", $"Error 方向 = {errRow.Direction}");

            // 时间戳回绕
            var wrap1 = new FrameRow(0, 1, "Rx", 0x123u, false, false, false, false, false, false, 1, "00", 0xFFFFF000u);
            var wrapRow = new LatestRow(wrap1);
            var wrap2 = new FrameRow(0, 1, "Rx", 0x123u, false, false, false, false, false, false, 1, "00", unchecked(0xFFFFF000u + 300u));
            wrapRow.Apply(wrap2, 1);
            Check(Math.Abs(wrapRow.PeriodMilliseconds - 0.3) < 0.01, $"跨 uint32 回绕周期仍 300µs（{wrapRow.PeriodText}）");
        }
    }
}
