using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using GsCan;

namespace CanBench
{
    /// <summary>一条从总线收到 / 发出的帧。</summary>
    public sealed class BusFrame
    {
        public DateTime At;
        public int Channel;
        public bool Error;
        public uint Id;
        public bool Extended;
        public bool Echo;
        public byte[] Data = Array.Empty<byte>();
        public uint TimestampUs;

        /// <summary>错误帧里的位图。</summary>
        public uint ErrorBits;

        public string Kind => Error ? "Error" : (Echo ? "Echo" : "Rx");

        public string IdHex => "0x" + Id.ToString(Extended ? "X8" : "X3");

        public string DataHex => BitConverter.ToString(Data).Replace("-", " ");

        public string ErrorText => Error ? CanTransport.DescribeError(ErrorBits) : "";
    }

    /// <summary>
    /// CanBench 与 gs_usb 之间的传输层，内部全部走 GsCan 的 Device / Channel。
    /// 这里只做三件事：枚举设备、每路一个读线程、把 CanFrame 转成界面用的 BusFrame。
    /// candle_api 的 P/Invoke、超时切片和 Stop 解阻塞都在库里。
    /// </summary>
    public sealed class CanTransport : IDisposable
    {
        public sealed class DeviceEntry
        {
            public int Index;
            public string Path = "";
            public override string ToString() => Path;
        }

        private Device? _device;
        private bool[] _started = Array.Empty<bool>();
        private Thread?[] _readers = Array.Empty<Thread?>();
        private volatile bool _running;
        private readonly ConcurrentQueue<BusFrame> _queue = new ConcurrentQueue<BusFrame>();

        public int ChannelCount { get; private set; }

        public bool IsOpen => _device != null;

        /// <summary>当前插着的 gs_usb 设备，顺序与 Device.List() 一致。</summary>
        public static List<DeviceEntry> Scan()
        {
            var result = new List<DeviceEntry>();
            var devices = Device.List();
            for (int i = 0; i < devices.Count; i++)
            {
                result.Add(new DeviceEntry { Index = i, Path = devices[i].Path });
            }
            return result;
        }

        public string Open(int deviceIndex)
        {
            Close();

            var devices = Device.List();
            if (deviceIndex < 0 || deviceIndex >= devices.Count)
            {
                throw new InvalidOperationException("找不到该设备（可能已拔出，请点「刷新」）。");
            }

            Device device;
            try
            {
                device = Device.Open(devices[deviceIndex]);
            }
            catch (GsCanException ex)
            {
                throw new InvalidOperationException("打开设备失败：" + Explain(ex) + "。", ex);
            }

            _device = device;
            ChannelCount = device.Channels.Count;
            _started = new bool[ChannelCount];
            _readers = new Thread?[ChannelCount];
            _running = true;
            return $"已打开，{ChannelCount} 路通道";
        }

        public void StartChannel(int ch, uint bitrate, bool listenOnly)
        {
            var device = _device;
            if (device == null) throw new InvalidOperationException("请先打开设备。");
            if (ch < 0 || ch >= ChannelCount) throw new ArgumentOutOfRangeException(nameof(ch));
            if (_started[ch]) return;

            try
            {
                device.Channels[ch].Start(new ChannelOptions
                {
                    Bitrate = checked((int)bitrate),
                    ListenOnly = listenOnly,
                });
            }
            catch (GsCanException ex)
            {
                throw new InvalidOperationException($"启动 CH{ch} 失败：" + Explain(ex) + "。", ex);
            }

            _started[ch] = true;
            EnsureReader(ch);
        }

        public void StopChannel(int ch)
        {
            var device = _device;
            if (device == null) return;
            if (ch < 0 || ch >= _started.Length || !_started[ch]) return;

            _started[ch] = false;
            try { device.Channels[ch].Stop(); } catch { }
            JoinReader(ch);
        }

        public void StopAll()
        {
            var device = _device;
            if (device == null) return;

            int count = Math.Min(_started.Length, device.Channels.Count);
            for (int i = 0; i < count; i++)
            {
                if (_started[i]) StopChannel(i);
            }
        }

        private void EnsureReader(int ch)
        {
            if (_readers[ch] != null) return;

            var thread = new Thread(() => ReadLoop(ch))
            {
                IsBackground = true,
                Name = "can-reader-" + ch,
            };
            _readers[ch] = thread;
            thread.Start();
        }

        /// <summary>
        /// 每路一个读线程。GsCan 的 TryRead 只取本路队列；设备的 USB IN 分路由库负责，
        /// 两路同时读不会互相丢帧。Stop 会让阻塞中的 TryRead 返回 false。
        /// </summary>
        private void ReadLoop(int ch)
        {
            var device = _device;
            if (device == null) return;

            var channel = device.Channels[ch];
            while (_running && _started[ch])
            {
                CanFrame frame;
                try
                {
                    if (!channel.TryRead(out frame, 100))
                    {
                        continue;
                    }
                }
                catch (GsCanException)
                {
                    break;      // 设备被拔出或已关闭
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                bool error = frame.Kind == CanFrameKind.Error;
                _queue.Enqueue(new BusFrame
                {
                    At = DateTime.Now,
                    Channel = ch,
                    TimestampUs = frame.TimestampMicroseconds,
                    Extended = frame.Extended,
                    Echo = frame.Kind == CanFrameKind.Echo,
                    Error = error,
                    Id = frame.Id,
                    ErrorBits = error ? frame.Id : 0,
                    Data = frame.Data,
                });
            }

            _readers[ch] = null;
        }

        private void JoinReader(int ch)
        {
            var thread = _readers[ch];
            if (thread != null)
            {
                try { thread.Join(1000); } catch { }
            }
            _readers[ch] = null;
        }

        public List<BusFrame> Drain()
        {
            var list = new List<BusFrame>();
            while (_queue.TryDequeue(out var f)) list.Add(f);
            return list;
        }

        public bool Send(CanItem item)
        {
            var device = _device;
            if (device == null) throw new InvalidOperationException("请先打开设备。");
            if (item.Channel < 0 || item.Channel >= ChannelCount)
                throw new InvalidOperationException($"CH{item.Channel} 不存在。");
            if (!_started[item.Channel])
                throw new InvalidOperationException($"CH{item.Channel} 尚未启动。");
            if (!item.Fd && item.Data.Length > 8)
                throw new InvalidOperationException("经典 CAN 数据不能超过 8 字节。");

            var frame = new CanFrame(
                item.Id,
                item.Data,
                CanFrameKind.Rx,
                extended: item.Extended,
                remote: item.Remote,
                fd: item.Fd,
                bitRateSwitch: item.BitRateSwitch);

            try
            {
                device.Channels[item.Channel].Send(frame);
            }
            catch (GsCanException ex)
            {
                throw new InvalidOperationException("发送失败：" + Explain(ex) + "。", ex);
            }
            return true;
        }

        public static string DescribeError(uint bits)
        {
            var s = new List<string>();
            if ((bits & CAN_ERR_ACK) != 0) s.Add("无节点应答");
            if ((bits & CAN_ERR_BUS_OFF) != 0) s.Add("BUS-OFF");
            if ((bits & CAN_ERR_BUS_ERROR) != 0) s.Add("总线错误");
            if ((bits & CAN_ERR_LOST_ARBITRATION) != 0) s.Add("仲裁丢失");
            if ((bits & CAN_ERR_TX_TIMEOUT) != 0) s.Add("发送超时");
            if ((bits & CAN_ERR_CRTL) != 0) s.Add("控制器状态");
            return s.Count == 0 ? $"0x{bits:X8}" : string.Join("+", s);
        }

        private const uint CAN_ERR_TX_TIMEOUT = 0x00000001;
        private const uint CAN_ERR_LOST_ARBITRATION = 0x00000002;
        private const uint CAN_ERR_BUS_ERROR = 0x00000004;
        private const uint CAN_ERR_ACK = 0x00000020;
        private const uint CAN_ERR_BUS_OFF = 0x00000040;
        private const uint CAN_ERR_CRTL = 0x00000080;

        /// <summary>把 GsCanException 里的 native error 号翻成可操作的提示。</summary>
        private static string Explain(GsCanException ex)
        {
            int code = NativeErrorCode(ex.Message);
            if (code < 0) return ex.Message.TrimEnd('.', '。');

            string hint = code switch
            {
                9 => "（准备读取失败，可尝试重新插拔设备）",
                10 => "（设备正被其它程序占用，多半是 GsCan View；先关掉它）",
                11 or 12 or 13 => "（该波特率与设备时钟不匹配）",
                14 => "（发送失败，检查 USB 连接或设备是否被拔出）",
                _ => "",
            };
            return NativeErrorName(code) + " / " + code + hint;
        }

        private static int NativeErrorCode(string message)
        {
            const string marker = "native error ";
            int at = message.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return -1;

            at += marker.Length;
            int end = at;
            while (end < message.Length && message[end] >= '0' && message[end] <= '9') end++;
            return end > at && int.TryParse(message.Substring(at, end - at), out var code) ? code : -1;
        }

        private static string NativeErrorName(int code) => code switch
        {
            0 => "OK",
            1 => "CREATE_FILE",
            2 => "WINUSB_INITIALIZE",
            3 => "QUERY_INTERFACE",
            4 => "QUERY_PIPE",
            5 => "PARSE_IF_DESCR",
            6 => "SET_HOST_FORMAT",
            7 => "GET_DEVICE_INFO",
            8 => "GET_BITTIMING_CONST",
            9 => "PREPARE_READ",
            10 => "SET_DEVICE_MODE",
            11 => "SET_BITTIMING",
            12 => "BITRATE_FCLK",
            13 => "BITRATE_UNSUPPORTED",
            14 => "SEND_FRAME",
            15 => "READ_TIMEOUT",
            16 => "READ_WAIT",
            17 => "READ_RESULT",
            18 => "READ_SIZE",
            _ => "ERR",
        };

        public void Close()
        {
            _running = false;

            var device = _device;
            if (device != null)
            {
                int count = Math.Min(_started.Length, device.Channels.Count);
                for (int i = 0; i < count; i++)
                {
                    if (!_started[i]) continue;
                    _started[i] = false;
                    try { device.Channels[i].Stop(); } catch { }
                }

                for (int i = 0; i < _readers.Length; i++) JoinReader(i);

                device.Dispose();
                _device = null;
            }

            ChannelCount = 0;
            while (_queue.TryDequeue(out _)) { }
        }

        public void Dispose() => Close();
    }
}
