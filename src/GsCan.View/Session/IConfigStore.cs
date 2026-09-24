namespace GsCan.View.Session
{
    /// <summary>
    /// Remembers Device Path, ChannelOptions, Display Filter, and TxSlot.
    /// Load/Save must not Open or Start a Device.
    /// </summary>
    public interface IConfigStore
    {
        ViewConfig? Load();

        void Save(ViewConfig config);
    }
}
