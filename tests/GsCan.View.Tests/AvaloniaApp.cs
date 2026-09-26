using System;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using GsCan.View;
using Xunit;

namespace GsCan.View.Tests
{
    public class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions());
        }
    }

    public sealed class AvaloniaUiFixture : IDisposable
    {
        public AvaloniaUiFixture()
        {
            Session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        }

        internal HeadlessUnitTestSession Session { get; }

        public void Run(Action action)
        {
            Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Session.Dispose();
        }
    }

    [CollectionDefinition("Avalonia")]
    public class AvaloniaCollection : ICollectionFixture<AvaloniaUiFixture>
    {
    }
}
