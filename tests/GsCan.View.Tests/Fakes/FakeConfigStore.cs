using GsCan.View.Session;

namespace GsCan.View.Tests.Fakes
{
    internal sealed class FakeConfigStore : IConfigStore
    {
        public ViewConfig? Snapshot { get; set; }

        public int LoadCallCount { get; private set; }

        public int SaveCallCount { get; private set; }

        public ViewConfig? Load()
        {
            LoadCallCount++;
            return Snapshot;
        }

        public void Save(ViewConfig config)
        {
            SaveCallCount++;
            Snapshot = config;
        }
    }
}
