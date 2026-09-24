using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GsCan
{
    internal delegate bool NativeFrameRead(int timeoutMilliseconds, out CanFrame frame, out int channel);

    /// <summary>
    /// Demuxes a device-wide USB IN stream into per-channel queues.
    /// Queue dequeue must not wait on the USB lock: a channel blocked in a native
    /// timeout would otherwise starve already-queued frames on the other channel.
    /// </summary>
    internal sealed class DeviceReadMux
    {
        internal const int NativeWaitSliceMs = 50;
        internal const int MaxQueuedFramesPerChannel = 8192;

        private readonly Queue<CanFrame>[] _queues;
        private readonly object _queueLock = new object();
        private readonly object _usbLock = new object();

        public DeviceReadMux(int channelCount)
        {
            if (channelCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channelCount));
            }

            _queues = new Queue<CanFrame>[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                _queues[i] = new Queue<CanFrame>();
            }
        }

        public int ChannelCount
        {
            get { return _queues.Length; }
        }

        public void Clear(int channelIndex)
        {
            lock (_queueLock)
            {
                _queues[channelIndex].Clear();
                Monitor.PulseAll(_queueLock);
            }
        }

        public void ClearAll()
        {
            lock (_queueLock)
            {
                for (int i = 0; i < _queues.Length; i++)
                {
                    _queues[i].Clear();
                }

                Monitor.PulseAll(_queueLock);
            }
        }

        public bool TryRead(
            int channelIndex,
            int timeoutMilliseconds,
            NativeFrameRead readNative,
            Func<int, bool> isChannelStarted,
            Func<bool> isCancelled,
            Action enterUsbRegion,
            Action exitUsbRegion,
            out CanFrame frame)
        {
            if (readNative == null)
            {
                throw new ArgumentNullException(nameof(readNative));
            }

            if (isChannelStarted == null)
            {
                throw new ArgumentNullException(nameof(isChannelStarted));
            }

            if (isCancelled == null)
            {
                throw new ArgumentNullException(nameof(isCancelled));
            }

            if (enterUsbRegion == null)
            {
                throw new ArgumentNullException(nameof(enterUsbRegion));
            }

            if (exitUsbRegion == null)
            {
                throw new ArgumentNullException(nameof(exitUsbRegion));
            }

            if (channelIndex < 0 || channelIndex >= _queues.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            }

            if (timeoutMilliseconds < 0)
            {
                timeoutMilliseconds = 0;
            }

            var sw = Stopwatch.StartNew();

            while (true)
            {
                if (isCancelled())
                {
                    frame = default;
                    return false;
                }

                if (TryDequeue(channelIndex, out frame))
                {
                    return true;
                }

                int remaining;
                if (timeoutMilliseconds == 0)
                {
                    if (sw.ElapsedMilliseconds > 0)
                    {
                        frame = default;
                        return false;
                    }

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

                if (Monitor.TryEnter(_usbLock))
                {
                    try
                    {
                        enterUsbRegion();
                        try
                        {
                            if (isCancelled())
                            {
                                frame = default;
                                return false;
                            }

                            if (TryReadUsbHoldingLock(
                                channelIndex,
                                slice,
                                readNative,
                                isChannelStarted,
                                out frame))
                            {
                                return true;
                            }
                        }
                        finally
                        {
                            exitUsbRegion();
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_usbLock);
                    }
                }
                else if (WaitDequeue(channelIndex, slice, out frame))
                {
                    return true;
                }

                if (isCancelled())
                {
                    frame = default;
                    return false;
                }

                if (timeoutMilliseconds == 0 || sw.ElapsedMilliseconds >= timeoutMilliseconds)
                {
                    frame = default;
                    return false;
                }
            }
        }

        private bool TryDequeue(int channelIndex, out CanFrame frame)
        {
            lock (_queueLock)
            {
                if (_queues[channelIndex].Count > 0)
                {
                    frame = _queues[channelIndex].Dequeue();
                    return true;
                }
            }

            frame = default;
            return false;
        }

        private bool WaitDequeue(int channelIndex, int timeoutMilliseconds, out CanFrame frame)
        {
            lock (_queueLock)
            {
                if (_queues[channelIndex].Count > 0)
                {
                    frame = _queues[channelIndex].Dequeue();
                    return true;
                }

                if (timeoutMilliseconds > 0)
                {
                    Monitor.Wait(_queueLock, timeoutMilliseconds);
                }

                if (_queues[channelIndex].Count > 0)
                {
                    frame = _queues[channelIndex].Dequeue();
                    return true;
                }
            }

            frame = default;
            return false;
        }

        private bool TryReadUsbHoldingLock(
            int channelIndex,
            int timeoutMilliseconds,
            NativeFrameRead readNative,
            Func<int, bool> isChannelStarted,
            out CanFrame frame)
        {
            if (TryDequeue(channelIndex, out frame))
            {
                return true;
            }

            CanFrame nativeFrame;
            int frameChannel;
            if (!readNative(timeoutMilliseconds, out nativeFrame, out frameChannel))
            {
                frame = default;
                return false;
            }

            if (frameChannel == channelIndex)
            {
                frame = nativeFrame;
                return true;
            }

            if (frameChannel >= 0
                && frameChannel < _queues.Length
                && isChannelStarted(frameChannel))
            {
                lock (_queueLock)
                {
                    Queue<CanFrame> queued = _queues[frameChannel];
                    if (queued.Count >= MaxQueuedFramesPerChannel)
                    {
                        queued.Dequeue();
                    }

                    queued.Enqueue(nativeFrame);
                    Monitor.PulseAll(_queueLock);
                }
            }

            frame = default;
            return false;
        }
    }
}
