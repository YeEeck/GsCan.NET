using System;
using System.Collections.Generic;
using GsCan;
using GsCan.View.Session;

namespace GsCan.View.Tests.Fakes
{
    internal sealed class FakeGsCanPort : IGsCanPort
    {
        public List<DeviceInfo> Devices { get; } = new List<DeviceInfo>();
        public Exception? OpenException { get; set; }

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

            var device = new FakeOpenedDevice(info.Path, info.ChannelCount, this);
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

        public FakeOpenedDevice(string path, int channelCount, FakeGsCanPort port)
        {
            Path = path;
            ChannelCount = channelCount;
            _port = port;
        }

        public int ChannelCount { get; }
        public string Path { get; }
        public int StartCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public List<int> StartedIndexes { get; } = new List<int>();
        public List<int> StoppedIndexes { get; } = new List<int>();
        public Dictionary<int, ChannelOptions> LastStartOptions { get; } = new Dictionary<int, ChannelOptions>();
        public Exception? StartException { get; set; }

        public void Start(int channelIndex, ChannelOptions options)
        {
            StartCallCount++;
            StartedIndexes.Add(channelIndex);
            LastStartOptions[channelIndex] = options;
            if (StartException != null)
            {
                throw StartException;
            }
        }

        public void Stop(int channelIndex)
        {
            StopCallCount++;
            StoppedIndexes.Add(channelIndex);
        }

        public void Send(int channelIndex, CanFrame frame)
        {
            throw new InvalidOperationException("Ticket 02 must not Send.");
        }

        public bool TryRead(int channelIndex, int timeoutMilliseconds, out CanFrame frame)
        {
            frame = default;
            throw new InvalidOperationException("Ticket 02 must not TryRead.");
        }

        public void Dispose()
        {
            _port.RecordDispose();
        }
    }
}
