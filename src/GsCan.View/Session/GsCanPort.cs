using System.Collections.Generic;
using GsCan;

namespace GsCan.View.Session
{
    /// <summary>
    /// Adapts <see cref="Device"/> / <see cref="Channel"/> to the session port seam.
    /// Does not change the GsCan public API.
    /// </summary>
    internal sealed class GsCanPort : IGsCanPort
    {
        public IReadOnlyList<DeviceInfo> List()
        {
            return Device.List();
        }

        public IOpenedDevice Open(DeviceInfo info)
        {
            var device = Device.Open(info);
            return new OpenedGsCanDevice(device, info.Path);
        }
    }

    internal sealed class OpenedGsCanDevice : IOpenedDevice
    {
        private readonly Device _device;

        public OpenedGsCanDevice(Device device, string path)
        {
            _device = device;
            Path = path;
        }

        public int ChannelCount => _device.Channels.Count;

        public string Path { get; }

        public void Start(int channelIndex, ChannelOptions options)
        {
            _device.Channels[channelIndex].Start(options);
        }

        public void Stop(int channelIndex)
        {
            _device.Channels[channelIndex].Stop();
        }

        public void Send(int channelIndex, CanFrame frame)
        {
            _device.Channels[channelIndex].Send(frame);
        }

        public bool TryRead(int channelIndex, int timeoutMilliseconds, out CanFrame frame)
        {
            return _device.Channels[channelIndex].TryRead(out frame, timeoutMilliseconds);
        }

        public void Dispose()
        {
            _device.Dispose();
        }
    }
}
