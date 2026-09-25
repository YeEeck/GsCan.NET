using System;
using System.Runtime.InteropServices;

namespace GsCan.Native
{
    internal static class NativeMethods
    {
        private const string DllName = "candle_api";

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_list_scan(out IntPtr list);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_list_free(IntPtr list);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_list_length(IntPtr list, out byte len);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_dev_get(IntPtr list, byte devNum, out IntPtr hdev);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr candle_dev_get_path(IntPtr hdev);

        public static string GetPath(IntPtr hdev)
        {
            IntPtr p = candle_dev_get_path(hdev);
            return p == IntPtr.Zero ? null : Marshal.PtrToStringUni(p);
        }

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_dev_open(IntPtr hdev);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_dev_close(IntPtr hdev);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_dev_free(IntPtr hdev);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_channel_count(IntPtr hdev, out byte numChannels);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int candle_dev_last_error(IntPtr hdev);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_channel_get_capabilities(IntPtr hdev, byte ch, out CandleCapability cap);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_channel_set_bitrate(IntPtr hdev, byte ch, uint bitrate);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_channel_set_data_timing(IntPtr hdev, byte ch, ref CandleBittiming data);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_channel_start(IntPtr hdev, byte ch, uint flags);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_channel_stop(IntPtr hdev, byte ch);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_frame_send(IntPtr hdev, byte ch, ref CandleFrame frame);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_frame_read(IntPtr hdev, out CandleFrame frame, uint timeoutMs);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_fd_frame_send(IntPtr hdev, byte ch, ref CandleFdFrame frame);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool candle_fd_frame_read(IntPtr hdev, out CandleFdFrame frame, uint timeoutMs);

        public const uint CANDLE_MODE_LISTEN_ONLY = 0x0001;
        public const uint CANDLE_MODE_LOOP_BACK = 0x0002;
        public const uint CANDLE_MODE_ONE_SHOT = 0x0008;
        public const uint CANDLE_MODE_HW_TIMESTAMP = 0x0010;
        public const uint CANDLE_MODE_FD = 0x0100;

        public const uint CANDLE_FEATURE_HW_TIMESTAMP = 0x0010;

        public const uint CANDLE_ID_EXTENDED = 0x80000000;
        public const uint CANDLE_ID_RTR = 0x40000000;
        public const uint CANDLE_ID_ERR = 0x20000000;

        public const byte CANDLE_FRAME_FLAG_OVERFLOW = 0x01;
        public const byte CANDLE_FRAME_FLAG_FD = 0x02;
        public const byte CANDLE_FRAME_FLAG_BRS = 0x04;
        public const byte CANDLE_FRAME_FLAG_ESI = 0x08;

        public const int CANDLE_ERR_OK = 0;
        public const int CANDLE_ERR_READ_TIMEOUT = 15;

        public const uint EchoIdReceive = 0xFFFFFFFFu;

        /// <summary>
        /// Maps payload length to CAN FD DLC encoding (candle_len_to_dlc).
        /// </summary>
        public static byte LenToDlc(int len)
        {
            if (len < 0)
            {
                len = 0;
            }

            if (len <= 8) return (byte)len;
            if (len <= 12) return 9;
            if (len <= 16) return 10;
            if (len <= 20) return 11;
            if (len <= 24) return 12;
            if (len <= 32) return 13;
            if (len <= 48) return 14;
            return 15;
        }

        /// <summary>
        /// Maps CAN FD DLC encoding to payload length (candle_dlc_to_len).
        /// </summary>
        public static int DlcToLen(byte dlc)
        {
            switch (dlc)
            {
                case 0: return 0;
                case 1: return 1;
                case 2: return 2;
                case 3: return 3;
                case 4: return 4;
                case 5: return 5;
                case 6: return 6;
                case 7: return 7;
                case 8: return 8;
                case 9: return 12;
                case 10: return 16;
                case 11: return 20;
                case 12: return 24;
                case 13: return 32;
                case 14: return 48;
                case 15: return 64;
                default: return 0;
            }
        }

        /// <summary>
        /// 48 MHz / same style as candle_channel_set_bitrate (sync_seg implicit).
        /// Supports 1M, 2M, and 4M data bitrates. 5M does not divide 48 MHz.
        /// </summary>
        public static bool TryGetDataBittiming(int dataBitrate, out CandleBittiming timing)
        {
            // Default 16-tq: 1 sync + prop 1 + phase1 12 + phase2 2
            timing = new CandleBittiming
            {
                prop_seg = 1,
                phase_seg1 = 12,
                phase_seg2 = 2,
                sjw = 1,
                brp = 0
            };

            switch (dataBitrate)
            {
                case 1000000:
                    // 48e6 / (3 * 16) = 1M
                    timing.brp = 3;
                    return true;

                case 2000000:
                    // 16-tq cannot divide 48e6/2e6=24; use 12-tq: 1+1+8+2
                    // 48e6 / (2 * 12) = 2M
                    timing.phase_seg1 = 8;
                    timing.brp = 2;
                    return true;

                case 4000000:
                    // same 12-tq as 2M; 48e6 / (1 * 12) = 4M
                    timing.phase_seg1 = 8;
                    timing.brp = 1;
                    return true;

                default:
                    timing = default;
                    return false;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct CandleCapability
    {
        public uint feature;
        public uint fclk_can;
        public uint tseg1_min;
        public uint tseg1_max;
        public uint tseg2_min;
        public uint tseg2_max;
        public uint sjw_max;
        public uint brp_min;
        public uint brp_max;
        public uint brp_inc;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct CandleBittiming
    {
        public uint prop_seg;
        public uint phase_seg1;
        public uint phase_seg2;
        public uint sjw;
        public uint brp;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct CandleFrame
    {
        public uint echo_id;
        public uint can_id;
        public byte can_dlc;
        public byte channel;
        public byte flags;
        public byte reserved;
        public byte d0;
        public byte d1;
        public byte d2;
        public byte d3;
        public byte d4;
        public byte d5;
        public byte d6;
        public byte d7;
        public uint timestamp_us;

        public byte[] GetDataBytes(int length)
        {
            int n = length < 0 ? 0 : (length > 8 ? 8 : length);
            var data = new byte[n];
            if (n > 0) data[0] = d0;
            if (n > 1) data[1] = d1;
            if (n > 2) data[2] = d2;
            if (n > 3) data[3] = d3;
            if (n > 4) data[4] = d4;
            if (n > 5) data[5] = d5;
            if (n > 6) data[6] = d6;
            if (n > 7) data[7] = d7;
            return data;
        }

        public void SetDataBytes(byte[] data, int length)
        {
            d0 = length > 0 ? data[0] : (byte)0;
            d1 = length > 1 ? data[1] : (byte)0;
            d2 = length > 2 ? data[2] : (byte)0;
            d3 = length > 3 ? data[3] : (byte)0;
            d4 = length > 4 ? data[4] : (byte)0;
            d5 = length > 5 ? data[5] : (byte)0;
            d6 = length > 6 ? data[6] : (byte)0;
            d7 = length > 7 ? data[7] : (byte)0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct CandleFdFrame
    {
        public uint echo_id;
        public uint can_id;
        public byte can_dlc;
        public byte channel;
        public byte flags;
        public byte reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] data;
        public uint timestamp_us;

        public byte[] GetDataBytes(int length)
        {
            int n = length < 0 ? 0 : (length > 64 ? 64 : length);
            var result = new byte[n];
            if (data != null && n > 0)
            {
                Buffer.BlockCopy(data, 0, result, 0, n);
            }

            return result;
        }

        public void SetDataBytes(byte[] source, int length)
        {
            if (data == null || data.Length != 64)
            {
                data = new byte[64];
            }
            else
            {
                Array.Clear(data, 0, 64);
            }

            int n = length < 0 ? 0 : (length > 64 ? 64 : length);
            if (source != null && n > 0)
            {
                Buffer.BlockCopy(source, 0, data, 0, n);
            }
        }
    }
}
