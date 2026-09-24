using System;
using System.Collections.Generic;
using GsCan;

namespace GsCan.View.Session
{
    public sealed class ViewSession
    {
        // Held for later List/Open/Start. Construct must not call the port.
        private readonly IGsCanPort _port;

        public ViewSession(IGsCanPort port)
        {
            _port = port ?? throw new ArgumentNullException(nameof(port));
            DeviceList = Array.Empty<DeviceInfo>();
            Channels = Array.Empty<ChannelState>();
        }

        public string WindowTitle => "GsCan View";
        public string? OpenedPath { get; }
        public IReadOnlyList<DeviceInfo> DeviceList { get; }
        public IReadOnlyList<ChannelState> Channels { get; }
    }
}
