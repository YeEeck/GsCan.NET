using System;
using System.Collections.Generic;
using System.Threading;
using GsCan.Native;

namespace GsCan
{
    public sealed class Device : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;
        private readonly ReaderWriterLockSlim _lifecycleLock = new ReaderWriterLockSlim();
        // candle.dll reuses one overlapped txEvent per device. Concurrent
        // WinUsb_WritePipe on that event fails with CANDLE_ERR_SEND_FRAME (14).
        private readonly object _sendLock = new object();
        private readonly DeviceReadMux _mux;
        private readonly bool[] _channelStarted;
        private readonly int[] _readCancelEpoch;
        private int _consecutiveRetryableReadErrors;
        private static readonly object ChannelCountCacheLock = new object();
        private static readonly Dictionary<string, int> ChannelCountByPath =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Device(IntPtr handle, Channel[] channels)
        {
            _handle = handle;
            Channels = channels;
            _mux = new DeviceReadMux(channels.Length);
            _channelStarted = new bool[channels.Length];
            _readCancelEpoch = new int[channels.Length];
        }

        public IReadOnlyList<Channel> Channels { get; }

        internal IntPtr Handle
        {
            get
            {
                ThrowIfDisposed();
                return _handle;
            }
        }

        internal bool IsDisposed => _disposed;

        internal void ThrowIfDisposed()
        {
            if (_disposed || _handle == IntPtr.Zero)
            {
                throw new GsCanException("Device is closed.");
            }
        }

        internal void EnterOperation()
        {
            _lifecycleLock.EnterReadLock();
            if (_disposed || _handle == IntPtr.Zero)
            {
                _lifecycleLock.ExitReadLock();
                throw new GsCanException("Device is closed.");
            }
        }

        internal void ExitOperation()
        {
            _lifecycleLock.ExitReadLock();
        }

        internal void EnterSend()
        {
            Monitor.Enter(_sendLock);
        }

        internal void ExitSend()
        {
            Monitor.Exit(_sendLock);
        }

        internal void EnterLifecycle()
        {
            _lifecycleLock.EnterWriteLock();
        }

        internal void ExitLifecycle()
        {
            _lifecycleLock.ExitWriteLock();
        }

        internal void MarkChannelStarted(int channelIndex)
        {
            _channelStarted[channelIndex] = true;
        }

        internal void MarkChannelStopped(int channelIndex)
        {
            _channelStarted[channelIndex] = false;
            Interlocked.Increment(ref _readCancelEpoch[channelIndex]);
            _mux.Clear(channelIndex);
        }

        internal bool IsChannelStarted(int channelIndex)
        {
            return _channelStarted[channelIndex];
        }

        private const int MaxConsecutiveRetryableReadErrors = 3;

        /// <summary>
        /// candle.dll keeps one last_error per device. A concurrent Send can
        /// overwrite READ_TIMEOUT (15) with OK (0) before TryRead reads it.
        /// </summary>
        private static bool NativeReadErrorIsTimeout(int err)
        {
            return err == NativeMethods.CANDLE_ERR_READ_TIMEOUT
                || err == NativeMethods.CANDLE_ERR_OK;
        }

        /// <summary>
        /// READ_RESULT (17) / READ_SIZE (18) after a resubmitted URB. One
        /// channel Stop can glitch the shared USB IN this way; a burst is unplug.
        /// </summary>
        private static bool NativeReadErrorIsRetryable(int err)
        {
            return err == NativeMethods.CANDLE_ERR_READ_RESULT
                || err == NativeMethods.CANDLE_ERR_READ_SIZE;
        }

        private static bool NativeFailedReadIsRecoverable(int err, ref int consecutiveRetryable)
        {
            if (NativeReadErrorIsTimeout(err))
            {
                consecutiveRetryable = 0;
                return true;
            }

            if (NativeReadErrorIsRetryable(err))
            {
                consecutiveRetryable++;
                if (consecutiveRetryable < MaxConsecutiveRetryableReadErrors)
                {
                    return true;
                }
            }

            consecutiveRetryable = 0;
            return false;
        }

        /// <summary>
        /// Reads the next frame for <paramref name="channelIndex"/>, demuxing
        /// device-wide USB IN into per-channel FIFO queues.
        /// Queue dequeue does not wait on the USB lock, so one channel blocked in a
        /// native timeout cannot starve already-queued frames on another channel.
        /// Native waits are sliced so Stop/Dispose can take the lifecycle write lock
        /// without deadlocking against a blocked TryRead.
        /// </summary>
        internal bool TryReadForChannel(int channelIndex, int timeoutMilliseconds, out CanFrame frame)
        {
            if (channelIndex < 0 || channelIndex >= _mux.ChannelCount)
            {
                throw new GsCanException("Channel index out of range.");
            }

            int epoch = Volatile.Read(ref _readCancelEpoch[channelIndex]);
            bool got = _mux.TryRead(
                channelIndex,
                timeoutMilliseconds,
                ReadNativeFrame,
                IsChannelStarted,
                () => !IsChannelStarted(channelIndex)
                    || Volatile.Read(ref _readCancelEpoch[channelIndex]) != epoch
                    || _disposed,
                EnterOperation,
                ExitOperation,
                out frame);

            if (got)
            {
                return true;
            }

            ThrowIfDisposed();
            return false;
        }

        private bool ReadNativeFrame(int timeoutMilliseconds, out CanFrame frame, out int channel)
        {
            CandleFdFrame native;
            if (!NativeMethods.candle_fd_frame_read(_handle, out native, (uint)timeoutMilliseconds))
            {
                int err = NativeMethods.candle_dev_last_error(_handle);
                if (NativeFailedReadIsRecoverable(err, ref _consecutiveRetryableReadErrors))
                {
                    frame = default;
                    channel = -1;
                    return false;
                }

                throw new GsCanException("Failed to read CAN frame (native error " + err + ").");
            }

            _consecutiveRetryableReadErrors = 0;
            frame = ConvertNativeFdFrame(ref native);
            channel = native.channel;
            return true;
        }

        internal static CanFrame ConvertNativeFdFrame(ref CandleFdFrame native)
        {
            CanFrameKind kind;
            if (native.echo_id != NativeMethods.EchoIdReceive)
            {
                kind = CanFrameKind.Echo;
            }
            else if ((native.can_id & NativeMethods.CANDLE_ID_ERR) != 0)
            {
                kind = CanFrameKind.Error;
            }
            else
            {
                kind = CanFrameKind.Rx;
            }

            uint id = native.can_id & 0x1FFFFFFFu;
            bool extended = (native.can_id & NativeMethods.CANDLE_ID_EXTENDED) != 0;
            bool remote = (native.can_id & NativeMethods.CANDLE_ID_RTR) != 0;
            bool fd = (native.flags & NativeMethods.CANDLE_FRAME_FLAG_FD) != 0;
            bool brs = (native.flags & NativeMethods.CANDLE_FRAME_FLAG_BRS) != 0;
            bool esi = (native.flags & NativeMethods.CANDLE_FRAME_FLAG_ESI) != 0;
            bool overflow = (native.flags & NativeMethods.CANDLE_FRAME_FLAG_OVERFLOW) != 0;

            int len = NativeMethods.DlcToLen(native.can_dlc);
            if (!fd && len > 8)
            {
                len = 8;
            }

            byte[] data = native.GetDataBytes(len);
            return new CanFrame(
                id,
                data,
                kind,
                extended: extended,
                remote: remote,
                fd: fd,
                bitRateSwitch: brs,
                errorStateIndicator: esi,
                overflow: overflow,
                timestampMicroseconds: native.timestamp_us);
        }

        public static IReadOnlyList<DeviceInfo> List()
        {
            var results = new List<DeviceInfo>();
            if (!NativeMethods.candle_list_scan(out var list) || list == IntPtr.Zero)
            {
                return results;
            }

            try
            {
                if (!NativeMethods.candle_list_length(list, out var len))
                {
                    return results;
                }

                for (byte i = 0; i < len; i++)
                {
                    if (TryProbeDevice(list, i, out var info))
                    {
                        results.Add(info);
                    }
                }

                return results;
            }
            finally
            {
                NativeMethods.candle_list_free(list);
            }
        }

        public static Device Open(DeviceInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            if (string.IsNullOrWhiteSpace(info.Path))
            {
                throw new GsCanException("DeviceInfo.Path is empty.");
            }

            if (!NativeMethods.candle_list_scan(out var list) || list == IntPtr.Zero)
            {
                throw new GsCanException("Failed to scan for gs_usb devices.");
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                if (!NativeMethods.candle_list_length(list, out var len))
                {
                    throw new GsCanException("Failed to read device list length.");
                }

                for (byte i = 0; i < len; i++)
                {
                    if (!NativeMethods.candle_dev_get(list, i, out var hdev) || hdev == IntPtr.Zero)
                    {
                        continue;
                    }

                    string path = NativeMethods.GetPath(hdev);
                    if (!string.Equals(path, info.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        NativeMethods.candle_dev_free(hdev);
                        continue;
                    }

                    if (!NativeMethods.candle_dev_open(hdev))
                    {
                        int err = NativeMethods.candle_dev_last_error(hdev);
                        NativeMethods.candle_dev_free(hdev);
                        throw new GsCanException("Failed to open device (native error " + err + ").");
                    }

                    handle = hdev;
                    break;
                }

                if (handle == IntPtr.Zero)
                {
                    throw new GsCanException("Device not found: " + info.Path);
                }

                if (!NativeMethods.candle_channel_count(handle, out var channelCount) || channelCount == 0)
                {
                    throw new GsCanException("Failed to read channel count.");
                }

                RememberChannelCount(info.Path, channelCount);

                var channels = new Channel[channelCount];
                var device = new Device(handle, channels);
                handle = IntPtr.Zero;
                for (int c = 0; c < channelCount; c++)
                {
                    channels[c] = new Channel(device, c);
                }

                return device;
            }
            catch
            {
                if (handle != IntPtr.Zero)
                {
                    NativeMethods.candle_dev_close(handle);
                    NativeMethods.candle_dev_free(handle);
                }

                throw;
            }
            finally
            {
                NativeMethods.candle_list_free(list);
            }
        }

        private static bool TryProbeDevice(IntPtr list, byte index, out DeviceInfo info)
        {
            info = null;
            if (!NativeMethods.candle_dev_get(list, index, out var hdev) || hdev == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                string path = NativeMethods.GetPath(hdev);
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                info = new DeviceInfo(path, CachedChannelCount(path));
                return true;
            }
            finally
            {
                NativeMethods.candle_dev_free(hdev);
            }
        }

        private static void RememberChannelCount(string path, int channelCount)
        {
            if (string.IsNullOrEmpty(path) || channelCount <= 0)
            {
                return;
            }

            lock (ChannelCountCacheLock)
            {
                ChannelCountByPath[path] = channelCount;
            }
        }

        private static int CachedChannelCount(string path)
        {
            lock (ChannelCountCacheLock)
            {
                int count;
                if (ChannelCountByPath.TryGetValue(path, out count))
                {
                    return count;
                }
            }

            return 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnterLifecycle();
            try
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                for (int i = 0; i < _channelStarted.Length; i++)
                {
                    _channelStarted[i] = false;
                    Interlocked.Increment(ref _readCancelEpoch[i]);
                }

                _mux.ClearAll();

                if (_handle != IntPtr.Zero)
                {
                    NativeMethods.candle_dev_close(_handle);
                    NativeMethods.candle_dev_free(_handle);
                    _handle = IntPtr.Zero;
                }
            }
            finally
            {
                ExitLifecycle();
            }

            GC.SuppressFinalize(this);
        }
    }
}
