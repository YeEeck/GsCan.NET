using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GsCan.View.Session;

namespace GsCan.View
{
    public partial class MainWindow : Window
    {
        private const string DeviceDropDownHostName = "DeviceDropDownHost";
        private const string DeviceEmptyHintText = "未发现 gs_usb 设备。插入设备后点刷新。";

        private readonly ViewSession _session;
        private bool _latestStickToBottom = true;
        private bool _tracePinPosted;

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
            LatestList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLatestScrollChanged);
            _session.TraceDisplay.CollectionChanged += OnTraceChanged;
            _session.Latest.CollectionChanged += OnLatestChanged;
            _session.RefreshDevices();
        }

        private void OnDeviceDropDownOpened(object? sender, EventArgs e)
        {
            _session.RefreshDevices();
            if (sender is not ComboBox combo)
            {
                return;
            }

            if (!TryInstallDeviceEmptyHint(combo))
            {
                Dispatcher.UIThread.Post(() => TryInstallDeviceEmptyHint(combo), DispatcherPriority.Loaded);
            }
        }

        private bool TryInstallDeviceEmptyHint(ComboBox combo)
        {
            combo.ApplyTemplate();
            var popup = FindTemplatePart<Popup>(combo, "PART_Popup");
            if (popup?.Child is not Border border)
            {
                return false;
            }

            border.MinWidth = combo.Bounds.Width;
            if (border.Child is Grid existing && existing.Name == DeviceDropDownHostName)
            {
                return true;
            }

            var original = border.Child;
            var host = new Grid { Name = DeviceDropDownHostName };
            if (original != null)
            {
                border.Child = null;
                host.Children.Add(original);
            }

            var hint = new TextBlock
            {
                Text = DeviceEmptyHintText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                IsVisible = _session.DeviceListIsEmpty
            };
            hint.Classes.Add("hint");
            hint.Bind(Visual.IsVisibleProperty, new Binding(nameof(ViewSession.DeviceListIsEmpty))
            {
                Source = _session
            });
            host.Children.Add(hint);
            border.Child = host;
            return true;
        }

        private static T? FindTemplatePart<T>(TemplatedControl control, string name)
            where T : Control
        {
            var named = control.FindControl<T>(name);
            if (named != null)
            {
                return named;
            }

            foreach (var child in control.GetTemplateDescendants())
            {
                if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
                {
                    return match;
                }
            }

            return null;
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

        private void OnLatestScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (e.OffsetDelta.Y == 0)
            {
                return;
            }

            var scroll = e.Source as ScrollViewer ?? LatestList.Scroll;
            if (scroll == null)
            {
                return;
            }

            const double slop = 8;
            _latestStickToBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - slop;
        }

        private void OnTraceChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_tracePinPosted)
            {
                return;
            }

            _tracePinPosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                _tracePinPosted = false;
                PinToBottomNow(TraceList);
            }, DispatcherPriority.Background);
        }

        private void OnLatestChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            StickToLast(LatestList, () => _latestStickToBottom, () => _session.Latest.Count == 0 ? null : _session.Latest[_session.Latest.Count - 1]);
        }

        private static void PinToBottomNow(ListBox list)
        {
            var scroll = list.Scroll;
            if (scroll == null)
            {
                return;
            }

            var y = scroll.Extent.Height - scroll.Viewport.Height;
            if (y < 0)
            {
                y = 0;
            }

            scroll.Offset = new Vector(scroll.Offset.X, y);
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
