namespace GsCan.View.Session
{
    public sealed class ChannelState
    {
        public ChannelState(int index, bool isRunning)
        {
            Index = index;
            IsRunning = isRunning;
        }

        public int Index { get; }
        public bool IsRunning { get; }
    }
}
