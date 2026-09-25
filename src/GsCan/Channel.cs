using System;
using GsCan.Native;

namespace GsCan
{
    public sealed class Channel
    {
        private bool _started;
        private bool _listenOnly;

        internal Channel(Device device, int index)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            Index = index;
        }

        public Device Device { get; }
        public int Index { get; }

        /// <summary>
        /// Puts this channel on the bus with the given options.
        /// When <see cref="ChannelOptions.DataBitrate"/> is set, starts in CAN FD mode.
        /// </summary>
        public void Start(ChannelOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Bitrate <= 0)
            {
                throw new GsCanException("Bitrate must be a positive integer.");
            }

            CandleBittiming dataTiming = default;
            bool useFd = options.DataBitrate.HasValue;
            if (useFd)
            {
                int dataBitrate = options.DataBitrate.Value;
                if (dataBitrate <= 0
                    || !NativeMethods.TryGetDataBittiming(dataBitrate, out dataTiming))
                {
                    throw new GsCanException("Unsupported DataBitrate: " + dataBitrate + ".");
                }
            }

            Device.EnterLifecycle();
            try
            {
                Device.ThrowIfDisposed();
                IntPtr handle = Device.Handle;
                byte ch = checked((byte)Index);

                if (!NativeMethods.candle_channel_set_bitrate(handle, ch, (uint)options.Bitrate))
                {
                    int err = NativeMethods.candle_dev_last_error(handle);
                    throw new GsCanException("Failed to set bitrate (native error " + err + ").");
                }

                if (useFd)
                {
                    if (!NativeMethods.candle_channel_set_data_timing(handle, ch, ref dataTiming))
                    {
                        int err = NativeMethods.candle_dev_last_error(handle);
                        throw new GsCanException("Failed to set data bitrate timing (native error " + err + ").");
                    }
                }

                uint flags = 0;
                CandleCapability cap;
                if (NativeMethods.candle_channel_get_capabilities(handle, ch, out cap)
                    && (cap.feature & NativeMethods.CANDLE_FEATURE_HW_TIMESTAMP) != 0)
                {
                    flags |= NativeMethods.CANDLE_MODE_HW_TIMESTAMP;
                }

                if (useFd)
                {
                    flags |= NativeMethods.CANDLE_MODE_FD;
                }

                if (options.Loopback)
                {
                    flags |= NativeMethods.CANDLE_MODE_LOOP_BACK;
                }

                if (options.ListenOnly)
                {
                    flags |= NativeMethods.CANDLE_MODE_LISTEN_ONLY;
                }

                if (options.OneShot)
                {
                    flags |= NativeMethods.CANDLE_MODE_ONE_SHOT;
                }

                if (!NativeMethods.candle_channel_start(handle, ch, flags))
                {
                    int err = NativeMethods.candle_dev_last_error(handle);
                    throw new GsCanException("Failed to start channel (native error " + err + ").");
                }

                _listenOnly = options.ListenOnly;
                _started = true;
                Device.MarkChannelStarted(Index);
            }
            finally
            {
                Device.ExitLifecycle();
            }
        }

        /// <summary>
        /// Stops this channel. Does not throw.
        /// Unfinished Host TX are dropped by the device and will not appear as Echo.
        /// </summary>
        public void Stop()
        {
            try
            {
                if (Device.IsDisposed)
                {
                    _started = false;
                    _listenOnly = false;
                    return;
                }

                Device.EnterLifecycle();
                try
                {
                    if (Device.IsDisposed || !_started)
                    {
                        _started = false;
                        _listenOnly = false;
                        return;
                    }

                    Device.MarkChannelStopped(Index);
                    try
                    {
                        NativeMethods.candle_channel_stop(Device.Handle, checked((byte)Index));
                    }
                    catch (GsCanException)
                    {
                        // Stop must not throw.
                    }
                }
                finally
                {
                    _started = false;
                    _listenOnly = false;
                    Device.ExitLifecycle();
                }
            }
            catch
            {
                _started = false;
                _listenOnly = false;
            }
        }

        /// <summary>
        /// Queues a CAN frame for transmission. Returns immediately;
        /// completion on the device is observed later as an Echo via <see cref="TryRead"/>.
        /// Echo is not bus confirmation.
        /// </summary>
        public void Send(CanFrame frame)
        {
            Device.EnterOperation();
            try
            {
                Device.ThrowIfDisposed();
                EnsureStarted();
                if (_listenOnly)
                {
                    throw new GsCanException("Cannot send on a listen-only channel.");
                }

                if (frame.IsFd)
                {
                    SendFd(frame);
                    return;
                }

                byte[] data = frame.Data ?? Array.Empty<byte>();
                if (data.Length > 8)
                {
                    throw new GsCanException("Classic CAN data length must be 0..8.");
                }

                var native = new CandleFrame();
                native.echo_id = 0;
                native.can_id = frame.Id & 0x1FFFFFFFu;
                if (frame.Extended)
                {
                    native.can_id |= NativeMethods.CANDLE_ID_EXTENDED;
                }

                if (frame.Remote)
                {
                    native.can_id |= NativeMethods.CANDLE_ID_RTR;
                }

                native.can_dlc = (byte)data.Length;
                native.channel = checked((byte)Index);
                native.flags = 0;
                native.SetDataBytes(data, data.Length);

                IntPtr handle = Device.Handle;
                Device.EnterSend();
                try
                {
                    if (!NativeMethods.candle_frame_send(handle, checked((byte)Index), ref native))
                    {
                        int err = NativeMethods.candle_dev_last_error(handle);
                        throw new GsCanException("Failed to send CAN frame (native error " + err + ").");
                    }
                }
                finally
                {
                    Device.ExitSend();
                }
            }
            finally
            {
                Device.ExitOperation();
            }
        }

        private void SendFd(CanFrame frame)
        {
            byte[] data = frame.Data ?? Array.Empty<byte>();
            if (data.Length > 64)
            {
                throw new GsCanException("CAN FD data length must be 0..64.");
            }

            var native = new CandleFdFrame();
            native.echo_id = 0;
            native.can_id = frame.Id & 0x1FFFFFFFu;
            if (frame.Extended)
            {
                native.can_id |= NativeMethods.CANDLE_ID_EXTENDED;
            }

            if (frame.Remote)
            {
                native.can_id |= NativeMethods.CANDLE_ID_RTR;
            }

            native.can_dlc = NativeMethods.LenToDlc(data.Length);
            native.channel = checked((byte)Index);
            native.flags = NativeMethods.CANDLE_FRAME_FLAG_FD;
            if (frame.BitRateSwitch)
            {
                native.flags |= NativeMethods.CANDLE_FRAME_FLAG_BRS;
            }

            if (frame.ErrorStateIndicator)
            {
                native.flags |= NativeMethods.CANDLE_FRAME_FLAG_ESI;
            }

            native.SetDataBytes(data, data.Length);

            IntPtr handle = Device.Handle;
            Device.EnterSend();
            try
            {
                if (!NativeMethods.candle_fd_frame_send(handle, checked((byte)Index), ref native))
                {
                    int err = NativeMethods.candle_dev_last_error(handle);
                    throw new GsCanException("Failed to send CAN FD frame (native error " + err + ").");
                }
            }
            finally
            {
                Device.ExitSend();
            }
        }

        /// <summary>
        /// Tries to read the next frame for this channel (Rx, Echo, or Error).
        /// Returns false on timeout; does not throw for timeout.
        /// </summary>
        public bool TryRead(out CanFrame frame, int timeoutMilliseconds)
        {
            Device.ThrowIfDisposed();
            EnsureStarted();
            return Device.TryReadForChannel(Index, timeoutMilliseconds, out frame);
        }

        private void EnsureStarted()
        {
            if (!_started || !Device.IsChannelStarted(Index))
            {
                throw new GsCanException("Channel is not started.");
            }
        }
    }
}
