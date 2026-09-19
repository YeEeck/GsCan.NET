namespace GsCan
{
    public sealed class DeviceInfo
    {
        public DeviceInfo(string path, int channelCount)
        {
            Path = path ?? throw new System.ArgumentNullException(nameof(path));
            ChannelCount = channelCount;
        }

        public string Path { get; }
        public int ChannelCount { get; }
    }
}
