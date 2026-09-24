using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GsCan.View.Session;

namespace GsCan.View
{
    public partial class MainWindow : Window
    {
        private readonly ViewSession _session;
        private bool _traceStickToBottom = true;
        private bool _latestStickToBottom = true;

        public MainWindow()
            : this(new ViewSession(new GsCanPort(), FileConfigStore.InLocalAppData()))
        {
        }

        public MainWindow(ViewSession session)
        {
            InitializeComponent();
            _session = session ?? throw new ArgumentNullException(nameof(session));
            DataContext = _session;
            Closed += (_, _) => _session.Close();
            TraceList.AddHandler(ScrollViewer.ScrollChangedEvent, OnTraceScrollChanged);
            LatestList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLatestScrollChanged);
            _session.Trace.CollectionChanged += OnTraceChanged;
            _session.Latest.CollectionChanged += OnLatestChanged;
        }

        private async void OnSaveLogClick(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存 Log",
                SuggestedFileName = "log.csv",
                DefaultExtension = "csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV")
                    {
                        Patterns = new[] { "*.csv" }
                    }
                }
            });
            if (file == null)
            {
                return;
            }

            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            _session.SaveLog(path);
        }

        private void OnTraceScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            UpdateStick(TraceList, e, ref _traceStickToBottom);
        }

        private void OnLatestScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            UpdateStick(LatestList, e, ref _latestStickToBottom);
        }

        private static void UpdateStick(ListBox list, ScrollChangedEventArgs e, ref bool stickToBottom)
        {
            var scroll = e.Source as ScrollViewer ?? list.Scroll;
            if (scroll == null)
            {
                return;
            }

            const double slop = 8;
            stickToBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - slop;
        }

        private void OnTraceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            StickToLast(TraceList, () => _traceStickToBottom, () => _session.Trace.Count == 0 ? null : _session.Trace[_session.Trace.Count - 1]);
        }

        private void OnLatestChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            StickToLast(LatestList, () => _latestStickToBottom, () => _session.Latest.Count == 0 ? null : _session.Latest[_session.Latest.Count - 1]);
        }

        private static void StickToLast(ListBox list, Func<bool> stickToBottom, Func<object?> lastItem)
        {
            if (!stickToBottom())
            {
                return;
            }

            var item = lastItem();
            if (item == null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!stickToBottom())
                {
                    return;
                }

                var current = lastItem();
                if (current == null)
                {
                    return;
                }

                list.ScrollIntoView(current);
            }, DispatcherPriority.Background);
        }
    }
}
