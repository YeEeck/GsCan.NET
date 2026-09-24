using System;
using System.Collections.Generic;
using GsCan;

namespace GsCan.View.Session
{
    /// <summary>
    /// USB-free stub so the window can start. Real Device List/Open arrives later.
    /// </summary>
    internal sealed class NullGsCanPort : IGsCanPort
    {
        public IReadOnlyList<DeviceInfo> List()
        {
            return Array.Empty<DeviceInfo>();
        }

        public IOpenedDevice Open(DeviceInfo info)
        {
            throw new GsCanException("No Device is open.");
        }
    }
}
