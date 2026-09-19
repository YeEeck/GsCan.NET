using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using GsCan.Native;

namespace GsCan
{
    public sealed class Device : IDisposable
    {
        private const int NativeWaitSliceMs = 50;

        private IntPtr _handle;
        private bool _disposed;
        private readonly ReaderWriterLockSlim _lifecycleLock = new ReaderWriterLockSlim();
        private readonly object _readLock = new object();
        private readonly Queue<CanFrame>[] _channelQueues;
        private readonly bool[] _channelStarted;
        private int _readCancelEpoch;

        private Device(IntPtr handle, Channel[] channels)
        {
            _handle = handle;
            Channels = channels;
            _channelQueues = new Queue<CanFrame>[channels.Length];
            _channelStarted = new bool[channels.Length];
            for (int i = 0; i < channels.Length; i++)
            {
                _channelQueues[i] = new Queue<CanFrame>();
            }
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
            Interlocked.Increment(ref _readCancelEpoch);
            lock (_readLock)
            {
                _channelQueues[channelIndex].Clear();
            }
        }

        internal bool IsChannelStarted(int channelIndex)
        {
            return _channelStarted[channelIndex];
        }

        /// <summary>
        /// Reads the next frame for <paramref name="channelIndex"/>, demuxing
        /// device-wide USB IN into per-channel FIFO queues.
        /// Native waits are sliced so Stop/Dispose can take the lifecycle write lock
        /// without deadlocking against a blocked TryRead.
        /// </summary>
        internal bool TryReadForChannel(int channelIndex, int timeoutMilliseconds, out CanFrame frame)
        {
            if (channelIndex < 0 || channelIndex >= _channelQueues.Length)
            {
                throw new GsCanException("Channel index out of range.");
            }

            if (timeoutMilliseconds < 0)
            {
                timeoutMilliseconds = 0;
            }

            int epoch = Volatile.Read(ref _readCancelEpoch);
            var sw = Stopwatch.StartNew();

            while (true)
            {
                ThrowIfDisposed();
                if (!IsChannelStarted(channelIndex)
                    || Volatile.Read(ref _readCancelEpoch) != epoch)
                {
                    throw new GsCanException("Channel is not started.");
                }

                EnterOperation();
                try
                {
                    ThrowIfDisposed();
                    if (!IsChannelStarted(channelIndex))
                    {
                        throw new GsCanException("Channel is not started.");
                    }

                    lock (_readLock)
                    {
                        if (_channelQueues[channelIndex].Count > 0)
                        {
                            frame = _channelQueues[channelIndex].Dequeue();
                            return true;
                        }

                        int remaining;
                        if (timeoutMilliseconds == 0 && sw.ElapsedMilliseconds == 0)
                        {
                            remaining = 0;
                        }
                        else
                        {
                            long left = timeoutMilliseconds - sw.ElapsedMilliseconds;
                            if (left <= 0)
                            {
                                frame = default;
                                return false;
                            }

                            remaining = left > int.MaxValue ? int.MaxValue : (int)left;
                        }

                        int slice = remaining;
                        if (slice > NativeWaitSliceMs)
                        {
                            slice = NativeWaitSliceMs;
                        }

                        CandleFdFrame native;
                        if (!NativeMethods.candle_fd_frame_read(_handle, out native, (uint)slice))
                        {
                            int err = NativeMethods.candle_dev_last_error(_handle);
                            if (err == NativeMethods.CANDLE_ERR_READ_TIMEOUT)
                            {
                                // Fall through to cancel/timeout checks outside the locks.
                            }
                            else
                            {
                                throw new GsCanException("Failed to read CAN frame (native error " + err + ").");
                            }
                        }
                        else
                        {
                            CanFrame converted = ConvertNativeFdFrame(ref native);
                            int frameChannel = native.channel;
                            if (frameChannel == channelIndex)
                            {
                                frame = converted;
                                return true;
                            }

                            if (frameChannel >= 0
                                && frameChannel < _channelQueues.Length
                                && _channelStarted[frameChannel])
                            {
                                _channelQueues[frameChannel].Enqueue(converted);
                            }
                        }
                    }
                }
                finally
                {
                    ExitOperation();
                }

                if (!IsChannelStarted(channelIndex)
                    || Volatile.Read(ref _readCancelEpoch) != epoch
                    || _disposed)
                {
                    throw new GsCanException("Channel is not started.");
                }

                if (timeoutMilliseconds == 0 || sw.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    frame = default;
                    return false;
                }
            }
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

                // dconf (channel count) is filled only after open.
                if (!NativeMethods.candle_dev_open(hdev))
                {
                    return false;
                }

                try
                {
                    if (!NativeMethods.candle_channel_count(hdev, out var count) || count == 0)
                    {
                        return false;
                    }

                    info = new DeviceInfo(path, count);
                    return true;
                }
                finally
                {
                    NativeMethods.candle_dev_close(hdev);
                }
            }
            finally
            {
                NativeMethods.candle_dev_free(hdev);
            }
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
                Interlocked.Increment(ref _readCancelEpoch);
                for (int i = 0; i < _channelStarted.Length; i++)
                {
                    _channelStarted[i] = false;
                }

                lock (_readLock)
                {
                    for (int i = 0; i < _channelQueues.Length; i++)
                    {
                        _channelQueues[i].Clear();
                    }
                }

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
