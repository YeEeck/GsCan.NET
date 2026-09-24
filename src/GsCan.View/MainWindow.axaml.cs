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
        private bool _stickToBottom = true;

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
            _session.Trace.CollectionChanged += OnTraceChanged;
        }

        private async void OnSaveLogClick(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存 Log",
                SuggestedFileName = "trace.csv",
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
            var scroll = e.Source as ScrollViewer ?? TraceList.Scroll;
            if (scroll == null)
            {
                return;
            }

            const double slop = 8;
            _stickToBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - slop;
        }

        private void OnTraceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_stickToBottom || _session.Trace.Count == 0)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!_stickToBottom || _session.Trace.Count == 0)
                {
                    return;
                }

                TraceList.ScrollIntoView(_session.Trace[_session.Trace.Count - 1]);
            }, DispatcherPriority.Background);
        }
    }
}
