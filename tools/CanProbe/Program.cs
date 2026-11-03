using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace CanProbe
{
    /// <summary>
    /// 直接调用 candle_api.dll 的 CAN 总线探针（FlintCAN-FD / gs_usb）。
    ///
    ///   CanProbe --ch=0                只看 CH0，不发帧
    ///   CanProbe --ch=0 --send         在 CH0 发一条查询帧，看有没有应答
    ///   CanProbe --ch=0 --selftest     按协议跑完整自检序列
    ///   CanProbe --ch=1 --ms=3000      看 CH1 上都有哪些 ID、周期多少
    ///
    /// 设备被 GsCan View 占用时会直接报 SET_DEVICE_MODE，先关 View。
    /// </summary>
    internal static class Program
    {
        private const string DllName = "candle_api";

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CandleCapability
        {
            public uint feature, fclk_can, tseg1_min, tseg1_max, tseg2_min, tseg2_max,
                        sjw_max, brp_min, brp_max, brp_inc;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CandleFrame
        {
            public uint echo_id, can_id;
            public byte can_dlc, channel, flags, reserved;
            public byte d0, d1, d2, d3, d4, d5, d6, d7;
            public uint timestamp_us;
        }

        // candle_api.dll 导出的是 C 的 bool（1 字节）。.NET 默认按 4 字节 Win32 BOOL 封送，
        // 不标 U1 会读到全错的返回值（表现为每次 read 都"成功"但字段全 0）。
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_list_scan(out IntPtr list);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_list_free(IntPtr list);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_list_length(IntPtr list, out byte len);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_dev_get(IntPtr list, byte n, out IntPtr hdev);
        [DllImport(DllName)] private static extern IntPtr candle_dev_get_path(IntPtr hdev);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_dev_open(IntPtr hdev);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_dev_close(IntPtr hdev);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_dev_free(IntPtr hdev);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_channel_count(IntPtr hdev, out byte n);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_channel_get_capabilities(IntPtr hdev, byte ch, out CandleCapability cap);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_channel_set_bitrate(IntPtr hdev, byte ch, uint bitrate);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_channel_start(IntPtr hdev, byte ch, uint flags);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_channel_stop(IntPtr hdev, byte ch);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_frame_send(IntPtr hdev, byte ch, ref CandleFrame f);
        [DllImport(DllName)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool candle_frame_read(IntPtr hdev, out CandleFrame f, uint timeoutMs);
        [DllImport(DllName)] private static extern int candle_dev_last_error(IntPtr hdev);

        private const uint ID_EXTENDED = 0x80000000;
        private const uint ID_ERR = 0x20000000;
        private const uint MODE_LISTEN_ONLY = 0x0001;

        private const uint CAN_ERR_TX_TIMEOUT = 0x00000001;
        private const uint CAN_ERR_LOST_ARBITRATION = 0x00000002;
        private const uint CAN_ERR_BUS_ERROR = 0x00000004;
        private const uint CAN_ERR_ACK = 0x00000020;
        private const uint CAN_ERR_BUS_OFF = 0x00000040;
        private const uint CAN_ERR_CRTL = 0x00000080;

        private const byte DEV_ADDR = 0x02;

        private static readonly Dictionary<int, string> ErrNames = new Dictionary<int, string>
        {
            {0, "OK"}, {1, "CREATE_FILE"}, {2, "WINUSB_INITIALIZE"}, {3, "QUERY_INTERFACE"},
            {4, "QUERY_PIPE"}, {5, "PARSE_IF_DESCR"}, {6, "SET_HOST_FORMAT"},
            {7, "GET_DEVICE_INFO"}, {8, "GET_BITTIMING_CONST"}, {9, "PREPARE_READ"},
            {10, "SET_DEVICE_MODE"}, {11, "SET_BITTIMING"}, {12, "BITRATE_FCLK"},
            {13, "BITRATE_UNSUPPORTED"}, {14, "SEND_FRAME"}, {15, "READ_TIMEOUT"},
        };

        private sealed class Case
        {
            public string Name;
            public uint Id;
            public byte[] Data = Array.Empty<byte>();
            public string Expect = "";
            public int WaitMs = 800;
        }

        private static int Main(string[] args)
        {
            uint bitrate = 500_000;
            bool sendTest = false, selfTest = false;
            int listenMs = 2000;
            byte targetCh = 0;

            foreach (var a in args)
            {
                if (a.StartsWith("--bitrate=")) uint.TryParse(a.Substring(10), out bitrate);
                if (a.StartsWith("--ch=")) byte.TryParse(a.Substring(5), out targetCh);
                if (a.StartsWith("--ms=")) int.TryParse(a.Substring(5), out listenMs);
                if (a == "--send") sendTest = true;
                if (a == "--selftest") { selfTest = true; sendTest = true; }
            }

            if (!candle_list_scan(out var list)) { Console.WriteLine("candle_list_scan 失败"); return 1; }
            candle_list_length(list, out byte count);
            Console.WriteLine($"发现 {count} 个 gs_usb 设备");
            if (count == 0) { candle_list_free(list); return 2; }

            candle_dev_get(list, 0, out var dev);
            var pathPtr = candle_dev_get_path(dev);
            Console.WriteLine($"Path = {(pathPtr == IntPtr.Zero ? "(空)" : Marshal.PtrToStringAnsi(pathPtr))}");

            if (!candle_dev_open(dev))
            {
                int e = candle_dev_last_error(dev);
                Console.WriteLine($"\n[占用] candle_dev_open 失败: {ErrName(e)} ({e})");
                Console.WriteLine("       设备正被其它程序使用 —— 先关掉 GsCan View 再跑。");
                candle_dev_free(dev);
                candle_list_free(list);
                return 3;
            }

            candle_channel_count(dev, out byte chans);
            Console.WriteLine($"通道数 = {chans}");
            if (chans == 0)
            {
                Console.WriteLine("[异常] 通道数为 0");
                candle_dev_close(dev); candle_dev_free(dev); candle_list_free(list);
                return 4;
            }

            for (byte c = 0; c < chans; c++)
            {
                if (!candle_channel_get_capabilities(dev, c, out var cap) || cap.fclk_can == 0)
                {
                    Console.WriteLine($"  CH{c}: 拿不到能力信息");
                    continue;
                }
                double brp = (1.0 / bitrate) / (16.0 / cap.fclk_can);
                Console.WriteLine($"  CH{c}: fclk_can={cap.fclk_can}Hz  @{bitrate / 1000}k -> brp={brp:0.####}" +
                                  $" {(Math.Abs(brp - Math.Round(brp)) < 1e-6 ? "(精确)" : "(不精确!)")}");
            }

            var started = new List<byte>();
            try
            {
                if (!candle_channel_set_bitrate(dev, targetCh, bitrate))
                    Console.WriteLine($"  CH{targetCh} set_bitrate 失败: {ErrName(candle_dev_last_error(dev))}");
                if (!candle_channel_start(dev, targetCh, MODE_LISTEN_ONLY))
                {
                    Console.WriteLine($"  CH{targetCh} start 失败: {ErrName(candle_dev_last_error(dev))}");
                    return 5;
                }
                started.Add(targetCh);
                Console.WriteLine($"  CH{targetCh} 已启动（只听）");

                Thread.Sleep(100);
                Collect(dev, targetCh, 100, out _, out _);

                if (!selfTest)
                {
                    Console.WriteLine($"\n--- CH{targetCh} 只听 {listenMs}ms ---");
                    var got = Collect(dev, targetCh, listenMs, out int errFrames, out var errBits);
                    Summary(got, errFrames, errBits);
                }

                if (sendTest)
                {
                    Console.WriteLine("\n--- 切到正常模式，主动发送 ---");
                    candle_channel_stop(dev, targetCh);
                    started.Clear();
                    if (!candle_channel_start(dev, targetCh, 0))
                    {
                        Console.WriteLine($"  start(正常) 失败: {ErrName(candle_dev_last_error(dev))}");
                        return 6;
                    }
                    started.Add(targetCh);
                    Thread.Sleep(100);
                    Collect(dev, targetCh, 150, out _, out _);

                    if (selfTest)
                    {
                        RunSelfTest(dev, targetCh);
                    }
                    else
                    {
                        SendRaw(dev, targetCh, MakeId(0x02, 0x00), Array.Empty<byte>());
                        Console.WriteLine(">>> 查询版本 0x02000002 dlc=0");
                        var ack = Collect(dev, targetCh, 1500, out int e2, out var b2);
                        var echo = ack.FindAll(f => f.echo_id != 0xFFFFFFFF);
                        var real = ack.FindAll(f => f.echo_id == 0xFFFFFFFF);
                        Console.WriteLine($"  回声 {echo.Count} 条，对方应答 {real.Count} 条，错误帧 {e2}");
                        foreach (var f in real) PrintFrame(f);
                        foreach (var kv in b2) Console.WriteLine($"  错误位 {DescribeErr(kv.Key)} x{kv.Value}");
                        if (real.Count > 0) Console.WriteLine("  → 对方有应答，总线通。");
                        else if (b2.ContainsKey(CAN_ERR_ACK)) Console.WriteLine("  → ACK 错误：总线上没有节点应答。");
                    }
                }
            }
            finally
            {
                foreach (var c in started) candle_channel_stop(dev, c);
                candle_dev_close(dev);
                candle_dev_free(dev);
                candle_list_free(list);
            }

            return 0;
        }

        // ---------------------------------------------------------------- 自检

        private static void RunSelfTest(IntPtr dev, byte ch)
        {
            Console.WriteLine("\n========== ResisterSimCard 自检序列（地址 0x02，500k）==========");
            var cases = new List<Case>();
            void Q(byte aux, string name, string expect) =>
                cases.Add(new Case { Name = name, Id = MakeId(0x02, aux), Expect = expect });
            void C(byte aux, string name, byte[] data, string expect, int wait = 1200) =>
                cases.Add(new Case { Name = name, Id = MakeId(0x01, aux), Data = data, Expect = expect, WaitMs = wait });

            Q(0x00, "查询版本", "01 00 02 01");
            Q(0x02, "查询初始化状态", "00 00");
            Q(0x01, "查询设备状态", "模式/高低边/错误码");

            Q(0x03, "查询输入电压电流〔固件空〕", "无应答");
            Q(0x04, "查询当前工作电阻〔固件空〕", "无应答");
            Q(0x05, "查询温度〔固件空〕", "无应答");
            Q(0x06, "查询PWM〔固件空〕", "无应答");
            Q(0x07, "查询当前通道〔固件空〕", "无应答");

            C(0x05, "解除保护〔固件无分支〕", Array.Empty<byte>(), "01 02", 800);

            C(0x00, "关闭负载（回空闲）", Array.Empty<byte>(), "00 00", 1500);
            C(0x03, "高低边=高边", new byte[] { 0x00 }, "00 00", 800);
            C(0x04, "输入继电器=吸合", new byte[] { 0x01 }, "00 00", 800);
            Q(0x01, "查询状态", "00 00 00");
            C(0x04, "输入继电器=断开", new byte[] { 0x00 }, "00 00", 800);
            C(0x03, "高低边=低边", new byte[] { 0x01 }, "00 00", 800);
            Q(0x01, "查询状态（第2字节=01）", "00 01 00");
            C(0x03, "高低边=高边", new byte[] { 0x00 }, "00 00", 800);
            C(0x04, "输入继电器=吸合", new byte[] { 0x01 }, "00 00", 800);

            C(0x01, "通道选择 load1/Res1/255", new byte[] { 0x00, 0x01, 0x01, 0xFF }, "00 00", 1500);
            Q(0x01, "查询状态（应 02 测试模式）", "02 00 00");
            C(0x00, "关闭负载（回空闲）", Array.Empty<byte>(), "00 00", 1500);

            var d = new List<byte> { 0x00 };
            d.AddRange(FBe(100.0f));
            C(0x02, "工作模式 100Ω", d.ToArray(), "00 00", 2000);
            Q(0x01, "查询状态（应 01 工作模式）", "01 00 00");
            C(0x00, "关闭负载（收尾）", Array.Empty<byte>(), "00 00", 1500);
            C(0x04, "输入继电器=断开", new byte[] { 0x00 }, "00 00", 800);
            Q(0x01, "最终状态", "00 00 00");

            int answered = 0;
            for (int i = 0; i < cases.Count; i++)
            {
                var c = cases[i];
                Collect(dev, ch, 60, out _, out _);
                SendRaw(dev, ch, c.Id, c.Data);
                var got = Collect(dev, ch, c.WaitMs, out int errs, out var bits);
                var real = got.FindAll(f => f.echo_id == 0xFFFFFFFF);

                Console.Write($"[{i + 1,2}] {c.Name,-32} ");
                if (real.Count == 0)
                {
                    Console.WriteLine($"—— 无应答   (期望 {c.Expect})");
                }
                else
                {
                    answered++;
                    foreach (var f in real)
                    {
                        var data = new[] { f.d0, f.d1, f.d2, f.d3, f.d4, f.d5, f.d6, f.d7 };
                        var hex = "";
                        for (int k = 0; k < Math.Min((int)f.can_dlc, 8); k++) hex += data[k].ToString("X2") + " ";
                        Console.WriteLine($"-> [{hex.TrimEnd(),-23}] (期望 {c.Expect})");
                    }
                }
                if (errs > 0) foreach (var kv in bits) Console.WriteLine($"      错误位 {DescribeErr(kv.Key)} x{kv.Value}");
            }
            Console.WriteLine($"\n共 {cases.Count} 条，有应答 {answered} 条，无应答 {cases.Count - answered} 条");
        }

        // ---------------------------------------------------------------- 工具

        private static uint MakeId(byte cmd, byte aux) =>
            ID_EXTENDED | ((uint)(cmd & 0x1F) << 24) | ((uint)aux << 16) | DEV_ADDR;

        private static byte[] FBe(float v)
        {
            var b = BitConverter.GetBytes(v);
            Array.Reverse(b);
            return b;
        }

        private static bool SendRaw(IntPtr dev, byte ch, uint idWithExtFlag, byte[] data)
        {
            var f = new CandleFrame
            {
                echo_id = 0xFFFFFFFF,
                can_id = idWithExtFlag,
                can_dlc = (byte)data.Length,
                channel = ch,
                flags = 0,
            };
            if (data.Length > 0) f.d0 = data[0];
            if (data.Length > 1) f.d1 = data[1];
            if (data.Length > 2) f.d2 = data[2];
            if (data.Length > 3) f.d3 = data[3];
            if (data.Length > 4) f.d4 = data[4];
            if (data.Length > 5) f.d5 = data[5];
            if (data.Length > 6) f.d6 = data[6];
            if (data.Length > 7) f.d7 = data[7];
            return candle_frame_send(dev, ch, ref f);
        }

        private static List<CandleFrame> Collect(IntPtr dev, byte ch, int ms, out int errFrames,
                                                out Dictionary<uint, int> errBits)
        {
            var list = new List<CandleFrame>();
            errFrames = 0;
            errBits = new Dictionary<uint, int>();
            var until = Environment.TickCount64 + ms;
            while (Environment.TickCount64 < until)
            {
                if (!candle_frame_read(dev, out var f, 50)) continue;
                if (f.channel != ch) continue;
                // candle_frame_type(): echo_id != 0xFFFFFFFF 是 ECHO，can_id 含 ID_ERR 是 ERROR
                if ((f.can_id & ID_ERR) != 0)
                {
                    errFrames++;
                    uint bits = f.can_id & 0x1FFFFFFF;
                    errBits[bits] = errBits.TryGetValue(bits, out var n) ? n + 1 : 1;
                    continue;
                }
                list.Add(f);
            }
            return list;
        }

        private static void Summary(List<CandleFrame> got, int errFrames, Dictionary<uint, int> errBits)
        {
            Console.WriteLine($"收到 {got.Count} 帧，错误帧 {errFrames}");
            if (got.Count > 0)
            {
                var byId = new Dictionary<uint, List<CandleFrame>>();
                foreach (var f in got)
                {
                    uint k = f.can_id & 0x1FFFFFFF;
                    if (!byId.TryGetValue(k, out var l)) { l = new List<CandleFrame>(); byId[k] = l; }
                    l.Add(f);
                }
                Console.WriteLine($"共 {byId.Count} 个不同 ID：");
                Console.WriteLine("    ID          数量   周期ms  扩展  DLC  数据");
                foreach (var kv in byId)
                {
                    var l = kv.Value;
                    double period = 0;
                    if (l.Count > 1)
                    {
                        uint minTs = uint.MaxValue, maxTs = 0;
                        foreach (var x in l)
                        {
                            if (x.timestamp_us < minTs) minTs = x.timestamp_us;
                            if (x.timestamp_us > maxTs) maxTs = x.timestamp_us;
                        }
                        period = (maxTs - minTs) / (double)(l.Count - 1) / 1000.0;
                    }
                    PrintRow(kv.Key, l, period);
                }
            }
            foreach (var kv in errBits) Console.WriteLine($"  错误位 {DescribeErr(kv.Key)} x{kv.Value}");
            if (got.Count == 0 && errFrames == 0)
                Console.WriteLine("→ 总线上完全没有波形/帧：没有其它节点在发，或物理层没接通。");
        }

        private static void PrintRow(uint id, List<CandleFrame> l, double period)
        {
            var f = l[0];
            var data = new[] { f.d0, f.d1, f.d2, f.d3, f.d4, f.d5, f.d6, f.d7 };
            var hex = "";
            for (int i = 0; i < Math.Min((int)f.can_dlc, 8); i++) hex += data[i].ToString("X2") + " ";
            bool ext = (f.can_id & ID_EXTENDED) != 0;
            Console.WriteLine($"    0x{id:X8}  {l.Count,5}  {period,7:0.0}  {(ext ? "EXT" : "STD")}   {f.can_dlc,3}   {hex.TrimEnd()}");
        }

        private static void PrintFrame(CandleFrame f)
        {
            uint id = f.can_id;
            bool ext = (id & ID_EXTENDED) != 0;
            uint real = id & 0x1FFFFFFF;
            var data = new[] { f.d0, f.d1, f.d2, f.d3, f.d4, f.d5, f.d6, f.d7 };
            var hex = "";
            for (int i = 0; i < Math.Min((int)f.can_dlc, 8); i++) hex += data[i].ToString("X2") + " ";
            string meaning = "";
            if (ext)
                meaning = $"  [cmd=0x{(real >> 24) & 0x1F:X2} aux=0x{(real >> 16) & 0xFF:X2} addr=0x{real & 0xFF:X2}]";
            Console.WriteLine($"    {(ext ? "EXT" : "STD")} 0x{real:X8} dlc={f.can_dlc} [{hex.TrimEnd()}]{meaning}");
        }

        private static string ErrName(int e) => ErrNames.TryGetValue(e, out var n) ? n : "未知";

        private static string DescribeErr(uint bits)
        {
            var s = new List<string>();
            if ((bits & CAN_ERR_ACK) != 0) s.Add("ACK(无节点应答)");
            if ((bits & CAN_ERR_BUS_OFF) != 0) s.Add("BUS-OFF");
            if ((bits & CAN_ERR_BUS_ERROR) != 0) s.Add("总线错误");
            if ((bits & CAN_ERR_LOST_ARBITRATION) != 0) s.Add("仲裁丢失");
            if ((bits & CAN_ERR_TX_TIMEOUT) != 0) s.Add("发送超时");
            if ((bits & CAN_ERR_CRTL) != 0) s.Add("控制器状态");
            if (s.Count == 0) s.Add($"0x{bits:X8}");
            return string.Join("+", s);
        }
    }
}
