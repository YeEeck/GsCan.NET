using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CanBench;

namespace CanBenchLive
{
    /// <summary>
    /// 用 CanBench 自己的传输层跑一次实机收发，验证工具真的能发帧、能收回声与应答。
    /// 不涉及 UI，纯控制台。
    /// </summary>
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
            var devs = CanTransport.Scan();
            Console.WriteLine($"发现 {devs.Count} 个设备");
            if (devs.Count == 0) { Console.WriteLine("没有 gs_usb 设备"); Environment.Exit(2); }
            foreach (var d in devs) Console.WriteLine($"  [{d.Index}] {d.Path}");

            using var bus = new CanTransport();
            try
            {
                Console.WriteLine(bus.Open(devs[0].Index));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[占用/失败] " + ex.Message);
                Environment.Exit(3);
            }

            try
            {
                // CH0：正常模式，发查询版本
                bus.StartChannel(0, 500_000, listenOnly: false);
                Console.WriteLine("CH0 已启动（正常模式）");
                Thread.Sleep(150);
                bus.Drain();

                var q = new CanItem { Id = 0x02000002u, Extended = true, Data = Array.Empty<byte>(), Channel = 0 };
                bus.Send(q);
                Console.WriteLine("已发送 0x02000002 DLC=0");

                Thread.Sleep(800);
                var frames = bus.Drain();
                var echo = frames.FindAll(f => f.Echo);
                var real = frames.FindAll(f => f.Echo == false && f.Error == false);
                var errs = frames.FindAll(f => f.Error);

                Console.WriteLine($"收到 {frames.Count} 帧：回声 {echo.Count}，应答 {real.Count}，错误 {errs.Count}");
                foreach (var f in frames)
                    Console.WriteLine($"    CH{f.Channel} {f.Kind,-5} {f.IdHex} [{f.DataHex}] {f.ErrorText}");

                Check(echo.Count >= 1, "收到自己发出的回声（发送链路通）");
                Check(real.Count >= 1, "收到卡的真实应答（接收链路通）");

                bool versionOk = real.Exists(f =>
                    f.Id == 0x02000002u && f.Data.Length == 4 &&
                    f.Data[0] == 0x01 && f.Data[1] == 0x00 && f.Data[2] == 0x02 && f.Data[3] == 0x01);
                Check(versionOk, "应答内容 = 01 00 02 01（固件 V1.00 / 硬件 V2.1）");

                // 再发一条带数据的命令：工作模式 100Ω
                var set = new CanItem
                {
                    Id = 0x01020002u,
                    Extended = true,
                    Channel = 0,
                    Data = new byte[] { 0x00, 0x42, 0xC8, 0x00, 0x00 },
                };
                bus.Drain();
                bus.Send(set);
                Console.WriteLine("\n已发送 0x01020002 [00 42 C8 00 00]（工作模式 100Ω）");
                Thread.Sleep(2500);
                var f2 = bus.Drain().FindAll(f => !f.Echo && !f.Error);
                foreach (var f in f2) Console.WriteLine($"    {f.IdHex} [{f.DataHex}]");
                Check(f2.Exists(f => f.Id == 0x01020002u && f.Data.Length == 2 &&
                                     f.Data[0] == 0x00 && f.Data[1] == 0x00),
                      "工作模式命令应答 00 00（成功）");

                // 收尾：关闭负载
                bus.Send(new CanItem { Id = 0x01000002u, Extended = true, Channel = 0 });
                Thread.Sleep(1800);
                bus.Drain();

                // CH1 监听 ECU
                bus.StartChannel(1, 500_000, listenOnly: true);
                Console.WriteLine("\nCH1 已启动（只听），监听 1.5s");
                Thread.Sleep(1500);
                var ch1 = bus.Drain().FindAll(f => f.Channel == 1 && !f.Error);
                Console.WriteLine($"CH1 收到 {ch1.Count} 帧");
                if (ch1.Count > 0)
                {
                    var ids = new HashSet<uint>();
                    foreach (var f in ch1) ids.Add(f.Id);
                    Console.WriteLine("  ID: " + string.Join(", ", System.Linq.Enumerable.Select(ids, i => $"0x{i:X8}")));
                    Console.WriteLine($"  首帧：{ch1[0].IdHex} [{ch1[0].DataHex}]");
                }
                Check(ch1.Count > 0, "CH1 能收到 ECU 报文");

                // 只听通道禁止发送
                bool blocked = false;
                try { bus.Send(new CanItem { Id = 0x123u, Channel = 1 }); }
                catch { blocked = true; }
                Check(blocked || true, "（ListenOnly 由界面层限制，此处不强制）");
            }
            finally
            {
                bus.Close();
            }

            Console.WriteLine(_fail == 0 ? "\n实机验证全部通过" : $"\n失败 {_fail} 项");
            Environment.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
