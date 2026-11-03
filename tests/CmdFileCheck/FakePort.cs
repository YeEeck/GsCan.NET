using System;
using System.Collections.Generic;
using GsCan;
using GsCan.View.Session;

namespace CmdFileCheck
{
    /// <summary>不碰硬件的假端口，仅用于测试命令表操作。</summary>
    internal sealed class FakePort : IGsCanPort
    {
        public IReadOnlyList<DeviceInfo> List() => new List<DeviceInfo>();

        public IOpenedDevice Open(DeviceInfo info) => throw new NotSupportedException();
    }
}
