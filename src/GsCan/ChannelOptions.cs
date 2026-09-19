namespace GsCan
{
    public sealed class ChannelOptions
    {
        public int Bitrate { get; set; }
        public int? DataBitrate { get; set; }
        public bool ListenOnly { get; set; }
        public bool Loopback { get; set; }
        public bool OneShot { get; set; }
    }
}
