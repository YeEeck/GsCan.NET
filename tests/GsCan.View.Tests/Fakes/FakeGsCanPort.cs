using System;
using System.Collections.Generic;
using GsCan;
using GsCan.View.Session;

namespace GsCan.View.Tests.Fakes
{
    internal sealed class FakeGsCanPort : IGsCanPort
    {
        public int ListCallCount { get; private set; }
        public int OpenCallCount { get; private set; }

        public IReadOnlyList<DeviceInfo> List()
        {
            ListCallCount++;
            return Array.Empty<DeviceInfo>();
        }

        public IOpenedDevice Open(DeviceInfo info)
        {
            OpenCallCount++;
            throw new InvalidOperationException("Fake port must not open a Device in this ticket.");
        }
    }
}
