using System.Collections.Generic;

namespace GsCan.View.Session
{
    /// <summary>
    /// Serializable snapshot of the form: Device Path, per-channel options,
    /// Display Filter, and TxSlot rows. Restoring this must not Open or Start.
    /// </summary>
    public sealed class ViewConfig
    {
        public string? DevicePath { get; set; }

        public int DeviceChannelCount { get; set; }

        public List<ChannelConfig> Channels { get; set; } = new List<ChannelConfig>();

        public DisplayFilterConfig DisplayFilter { get; set; } = new DisplayFilterConfig();

        public List<TxSlotConfig> TxSlots { get; set; } = new List<TxSlotConfig>();
    }

    public sealed class ChannelConfig
    {
        public int Bitrate { get; set; } = 500_000;

        public bool FdEnabled { get; set; }

        public int DataBitrate { get; set; } = 2_000_000;

        public bool ListenOnly { get; set; }

        public bool Loopback { get; set; }

        public bool OneShot { get; set; }
    }

    public sealed class DisplayFilterConfig
    {
        public bool ShowChannel0 { get; set; } = true;

        public bool ShowChannel1 { get; set; } = true;

        public bool ShowRx { get; set; } = true;

        public bool ShowEcho { get; set; } = true;

        public bool ShowError { get; set; } = true;

        public bool ShowStandard { get; set; } = true;

        public bool ShowExtended { get; set; } = true;

        public bool ShowClassic { get; set; } = true;

        public bool ShowFd { get; set; } = true;

        public bool ShowData { get; set; } = true;

        public bool ShowRemote { get; set; } = true;

        public string IdText { get; set; } = string.Empty;
    }

    public sealed class TxSlotConfig
    {
        public int Channel { get; set; }

        public uint Id { get; set; }

        public bool Extended { get; set; }

        public bool Remote { get; set; }

        public bool Fd { get; set; }

        public bool BitRateSwitch { get; set; }

        public string DataHex { get; set; } = string.Empty;

        public int PeriodMs { get; set; }

        public bool Enabled { get; set; }
    }
}
