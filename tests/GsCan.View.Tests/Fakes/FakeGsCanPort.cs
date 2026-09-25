using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using GsCan;
using GsCan.View.Session;

namespace GsCan.View.Tests.Fakes
{
    internal sealed class FakeGsCanPort : IGsCanPort
    {
        public List<DeviceInfo> Devices { get; } = new List<DeviceInfo>();
        public Exception? OpenException { get; set; }

        /// <summary>
        /// When set, Open reports this ChannelCount instead of DeviceInfo.ChannelCount.
        /// Mirrors real Device.Open discovering the count after List returned 0.
        /// </summary>
        public int? OpenChannelCount { get; set; }

        public int ListCallCount { get; private set; }
        public int OpenCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public FakeOpenedDevice? LastOpenedDevice { get; private set; }

        public IReadOnlyList<DeviceInfo> List()
        {
            ListCallCount++;
            return Devices.ToArray();
        }

        public IOpenedDevice Open(DeviceInfo info)
        {
            OpenCallCount++;
            if (OpenException != null)
            {
                throw OpenException;
            }

            var device = new FakeOpenedDevice(info.Path, OpenChannelCount ?? info.ChannelCount, this);
            LastOpenedDevice = device;
            return device;
        }

        internal void RecordDispose()
        {
            DisposeCallCount++;
        }
    }

    internal sealed class FakeOpenedDevice : IOpenedDevice
    {
        private readonly FakeGsCanPort _port;
        private readonly ConcurrentQueue<CanFrame>[] _rx;
        private readonly AutoResetEvent[] _rxSignals;
        private readonly bool[] _started;
        private readonly bool[] _listenOnly;
        private readonly List<(int Channel, CanFrame Frame)> _sent = new List<(int, CanFrame)>();

        public FakeOpenedDevice(string path, int channelCount, FakeGsCanPort port)
        {
            Path = path;
            ChannelCount = channelCount;
            _port = port;
            _rx = new ConcurrentQueue<CanFrame>[channelCount];
            _rxSignals = new AutoResetEvent[channelCount];
            _started = new bool[channelCount];
            _listenOnly = new bool[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                _rx[i] = new ConcurrentQueue<CanFrame>();
                _rxSignals[i] = new AutoResetEvent(false);
            }
        }

        public int ChannelCount { get; }
        public string Path { get; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public List<int> StartedIndexes { get; } = new List<int>();
        public List<int> StoppedIndexes { get; } = new List<int>();
        public Dictionary<int, ChannelOptions> LastStartOptions { get; } = new Dictionary<int, ChannelOptions>();
        public Exception? StartException { get; set; }
        public Exception? TryReadException { get; set; }
        public Exception? SendException { get; set; }

        public IReadOnlyList<(int Channel, CanFrame Frame)> Sent
        {
            get
            {
                lock (_sent)
                {
                    return _sent.ToArray();
                }
            }
        }

        public void Enqueue(int channelIndex, CanFrame frame)
        {
            _rx[channelIndex].Enqueue(frame);
            _rxSignals[channelIndex].Set();
        }

        public void Start(int channelIndex, ChannelOptions options)
        {
            StartCallCount++;
            StartedIndexes.Add(channelIndex);
            LastStartOptions[channelIndex] = options;
            if (StartException != null)
            {
                throw StartException;
            }

            _started[channelIndex] = true;
            _listenOnly[channelIndex] = options.ListenOnly;
        }

        public void Stop(int channelIndex)
        {
            StopCallCount++;
            StoppedIndexes.Add(channelIndex);
            _started[channelIndex] = false;
            while (_rx[channelIndex].TryDequeue(out _))
            {
            }

            _rxSignals[channelIndex].Set();
        }

        public void Send(int channelIndex, CanFrame frame)
        {
            if (!_started[channelIndex])
            {
                throw new GsCanException("Channel is not started.");
            }

            if (_listenOnly[channelIndex])
            {
                throw new GsCanException("Cannot send on a listen-only channel.");
            }

            if (SendException != null)
            {
                throw SendException;
            }

            lock (_sent)
            {
                _sent.Add((channelIndex, frame));
            }
        }

        public bool TryRead(int channelIndex, int timeoutMilliseconds, out CanFrame frame)
        {
            if (TryReadException != null)
            {
                throw TryReadException;
            }

            if (!_started[channelIndex])
            {
                throw new GsCanException("Channel is not started.");
            }

            if (_rx[channelIndex].TryDequeue(out frame))
            {
                return true;
            }

            if (!_started[channelIndex])
            {
                frame = default;
                return false;
            }

            if (timeoutMilliseconds <= 0)
            {
                frame = default;
                return false;
            }

            var remaining = timeoutMilliseconds;
            var clock = Stopwatch.StartNew();
            while (remaining > 0)
            {
                _rxSignals[channelIndex].WaitOne(remaining);
                if (_rx[channelIndex].TryDequeue(out frame))
                {
                    return true;
                }

                if (!_started[channelIndex])
                {
                    frame = default;
                    return false;
                }

                remaining = timeoutMilliseconds - (int)clock.ElapsedMilliseconds;
            }

            frame = default;
            return false;
        }

        public void Dispose()
        {
            for (int i = 0; i < _rxSignals.Length; i++)
            {
                _rxSignals[i].Set();
            }

            _port.RecordDispose();
        }
    }
}
