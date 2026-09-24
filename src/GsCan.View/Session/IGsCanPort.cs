using System;
using System.Collections.Generic;
using GsCan;

namespace GsCan.View.Session
{
    public interface IGsCanPort
    {
        IReadOnlyList<DeviceInfo> List();
        IOpenedDevice Open(DeviceInfo info);
    }

    public interface IOpenedDevice : IDisposable
    {
        int ChannelCount { get; }
        string Path { get; }
        void Start(int channelIndex, ChannelOptions options);
        void Stop(int channelIndex);
        void Send(int channelIndex, CanFrame frame);
        bool TryRead(int channelIndex, int timeoutMilliseconds, out CanFrame frame);
    }
}
