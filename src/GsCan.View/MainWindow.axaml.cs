using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
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
        private Point? _splitStart;
        private double _splitTrace0;
        private double _splitTx0;

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

            // 发送/应答配对：收到配对结果后同步到 Latest，并驱动 Trace 行高亮
            _session.HighlightRequested += (ts, ch, kind, tag) =>
            {
                _session.ApplyHighlightToLatest(ts, kind, tag);
            };
            _session.Latest.CollectionChanged += OnLatestChanged;
            TraceTxSplitter.PointerPressed += OnTraceTxSplitterPointerPressed;
            TraceTxGrid.PointerPressed += OnTraceTxGridPointerPressed;
            TraceTxGrid.PointerMoved += OnTraceTxSplitterPointerMoved;
            TraceTxGrid.PointerReleased += OnTraceTxSplitterPointerReleased;
            TraceTxGrid.PointerCaptureLost += OnTraceTxSplitterPointerCaptureLost;
            _session.RefreshDevices();
        }

        private void OnDeviceDropDownOpened(object? sender, EventArgs e)
        {
            if (!_session.CanRefreshDevices)
            {
                return;
            }

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
            if (!_session.CanSaveLog)
            {
                return;
            }

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

        /// <summary>在底部状态栏显示一条临时提示（复用 LastError 那一行）。</summary>
        private void ShowNotice(string message)
        {
            _session.ShowNotice(message);
        }

private async void OnClearTxSlotsClick(object? sender, RoutedEventArgs e)
        {
            // 有内容才要确认，避免误点清掉一整张表
            if (_session.TxSlots.Count > 1 || !string.IsNullOrEmpty(_session.TxSlots[0].Name)
                || _session.TxSlots[0].Id != 0)
            {
                var ok = await ShowConfirmAsync("清空命令表", "将删除全部命令（不可撤销），继续？");
                if (!ok)
                {
                    return;
                }
            }

            _session.ClearTxSlots();
            ShowNotice("命令表已清空。");
        }

        private async Task<bool> ShowConfirmAsync(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
            };

            var result = false;
            var yes = new Button { Content = "确定清空", Width = 96 };
            var no = new Button { Content = "取消", Width = 80 };
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            no.Click += (_, _) => { result = false; dialog.Close(); };

            dialog.Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { no, yes },
                    },
                },
            };

            await dialog.ShowDialog(this);
            return result;
        }

        private async void OnImportTxSlotsClick(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入命令表",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("命令表")
                    {
                        Patterns = new[] { "*.list", "*.csv" }
                    },
                    new FilePickerFileType("命令帧表 (*.list)")
                    {
                        Patterns = new[] { "*.list" }
                    },
                    new FilePickerFileType("CSV (*.csv)")
                    {
                        Patterns = new[] { "*.csv" }
                    }
                }
            });

            if (files == null || files.Count == 0)
            {
                return;
            }

            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                var entries = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? CommandFile.LoadCsv(path)
                    : CommandFile.LoadList(path);

                if (entries.Count == 0)
                {
                    ShowNotice("文件里没有可导入的命令。");
                    return;
                }

                var count = _session.ImportCommands(entries);
                ShowNotice($"已导入 {count} 条：{System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ShowNotice("导入失败：" + ex.Message);
            }
        }

        private async void OnExportTxSlotsClick(object? sender, RoutedEventArgs e)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出命令表",
                SuggestedFileName = "commands.list",
                DefaultExtension = "list",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("命令帧表 (*.list)")
                    {
                        Patterns = new[] { "*.list" }
                    },
                    new FilePickerFileType("CSV (*.csv)")
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

            try
            {
                var entries = _session.ExportCommands();
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    CommandFile.SaveCsv(path, entries);
                }
                else
                {
                    CommandFile.SaveList(path, entries);
                }

                ShowNotice($"已导出 {entries.Count} 条：{System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ShowNotice("导出失败：" + ex.Message);
            }
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

        private void OnTraceTxGridPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, TraceTxGrid))
            {
                return;
            }

            var y = e.GetPosition(TraceTxGrid).Y;
            var top = TraceTxGrid.RowDefinitions[0].ActualHeight;
            var bottom = top + TraceTxGrid.RowDefinitions[1].ActualHeight;
            if (y < top - 3 || y > bottom + 3)
            {
                return;
            }

            OnTraceTxSplitterPointerPressed(sender, e);
        }

        private void OnTraceTxSplitterPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var properties = e.GetCurrentPoint(TraceTxSplitter).Properties;
            if (properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
                && !properties.IsLeftButtonPressed)
            {
                return;
            }

            _splitStart = e.GetPosition(TraceTxGrid);
            _splitTrace0 = TraceTxGrid.RowDefinitions[0].ActualHeight;
            _splitTx0 = TraceTxGrid.RowDefinitions[2].ActualHeight;
            e.Pointer.Capture(TraceTxGrid);
            e.PreventGestureRecognition();
            e.Handled = true;
        }

        private void OnTraceTxSplitterPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_splitStart == null || e.Pointer.Captured != TraceTxGrid)
            {
                return;
            }

            ApplyTraceTxSplit(e.GetPosition(TraceTxGrid).Y - _splitStart.Value.Y);
        }

        private void OnTraceTxSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer.Captured == TraceTxGrid)
            {
                e.Pointer.Capture(null);
            }

            _splitStart = null;
        }

        private void OnTraceTxSplitterPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _splitStart = null;
        }

        private void ApplyTraceTxSplit(double delta)
        {
            var trace = TraceTxGrid.RowDefinitions[0];
            var tx = TraceTxGrid.RowDefinitions[2];
            var total = _splitTrace0 + _splitTx0;
            var minTrace = trace.MinHeight;
            var minTx = tx.MinHeight;
            var newTrace = _splitTrace0 + delta;
            if (newTrace < minTrace)
            {
                newTrace = minTrace;
            }

            if (total - newTrace < minTx)
            {
                newTrace = total - minTx;
            }

            trace.Height = new GridLength(newTrace, GridUnitType.Star);
            tx.Height = new GridLength(total - newTrace, GridUnitType.Star);
        }
    }
}
