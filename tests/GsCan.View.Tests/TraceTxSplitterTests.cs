using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GsCan.View;
using GsCan.View.Session;
using GsCan.View.Tests.Fakes;
using Xunit;

namespace GsCan.View.Tests
{
    [Collection("Avalonia")]
    public class TraceTxSplitterTests
    {
        private readonly AvaloniaUiFixture _avalonia;

        public TraceTxSplitterTests(AvaloniaUiFixture avalonia)
        {
            _avalonia = avalonia;
        }

        [Fact]
        public void Splitter_can_drag_when_Trace_is_empty()
        {
            _avalonia.Run(() =>
            {
                WithWindow(session =>
                {
                    var window = session.Window;
                    var splitter = FindSplitter(window);
                    AssertOwnRow(window, splitter);
                    AssertHitIsSplitter(window, splitter);
                    AssertCanDrag(window, splitter);
                });
            });
        }

        [Fact]
        public void Splitter_can_drag_when_Trace_is_filling()
        {
            _avalonia.Run(() =>
            {
                WithWindow(session =>
                {
                    for (int i = 0; i < 128; i++)
                    {
                        session.Session.TraceDisplay.Add(new FrameRowView());
                    }

                    Dispatcher.UIThread.RunJobs();
                    session.Window.UpdateLayout();

                    var splitter = FindSplitter(session.Window);
                    AssertOwnRow(session.Window, splitter);
                    var origin = SplitterCenter(session.Window, splitter);
                    session.Window.MouseMove(origin);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(splitter.IsPointerOver, "Splitter should be pointer-over while Trace is filled.");
                    AssertCanDrag(session.Window, splitter);
                });
            });
        }

        private static void WithWindow(Action<OpenWindow> body)
        {
            var view = new ViewSession(new FakeGsCanPort(), runBackgroundPumps: false);
            var window = new MainWindow(view);
            try
            {
                window.Width = 1280;
                window.Height = 800;
                window.Show();
                Dispatcher.UIThread.RunJobs();
                body(new OpenWindow(view, window));
            }
            finally
            {
                window.Close();
            }
        }

        private static void AssertCanDrag(Window window, Control splitter)
        {
            var grid = Assert.IsType<Grid>(splitter.Parent);
            var txRow = grid.RowDefinitions[2];
            var before = txRow.ActualHeight;
            Assert.True(before > 0, "TxSlot row should have a height after layout.");

            var origin = SplitterCenter(window, splitter);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin + new Vector(0, 48), RawInputModifiers.LeftMouseButton);
            window.MouseUp(origin + new Vector(0, 48), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var afterDown = txRow.ActualHeight;

            origin = SplitterCenter(window, splitter);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin + new Vector(0, -48), RawInputModifiers.LeftMouseButton);
            window.MouseUp(origin + new Vector(0, -48), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var afterUp = txRow.ActualHeight;

            Assert.True(
                before != afterDown || before != afterUp,
                "TxSlot row should change when dragging the splitter. Before="
                    + before
                    + " Down="
                    + afterDown
                    + " Up="
                    + afterUp);
        }

        private static void AssertOwnRow(Window window, Control splitter)
        {
            var grid = Assert.IsType<Grid>(splitter.Parent);
            Assert.Equal(1, Grid.GetRow(splitter));
            Assert.Equal(3, grid.RowDefinitions.Count);
            Assert.Equal(GridUnitType.Pixel, grid.RowDefinitions[1].Height.GridUnitType);
            Assert.True(grid.RowDefinitions[1].Height.Value >= 8, "Splitter row should be thick enough to click.");

            var list = window.FindControl<ListBox>("TraceList");
            Assert.NotNull(list);
            var splitOrigin = splitter.TranslatePoint(new Point(0, 0), window);
            var listOrigin = list.TranslatePoint(new Point(0, 0), window);
            Assert.True(splitOrigin.HasValue && listOrigin.HasValue);
            var splitRect = new Rect(splitOrigin.Value, splitter.Bounds.Size);
            var listRect = new Rect(listOrigin.Value, list.Bounds.Size);
            Assert.False(
                splitRect.Intersects(listRect),
                "Splitter must not overlap Trace. Split=" + splitRect + " Trace=" + listRect);
        }

        private static Control FindSplitter(Window window)
        {
            var named = window.FindControl<Control>("TraceTxSplitter");
            Assert.NotNull(named);
            return named;
        }

        private static Point SplitterCenter(Window window, Control splitter)
        {
            var local = new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2);
            var mapped = splitter.TranslatePoint(local, window);
            Assert.True(mapped.HasValue, "Splitter should map into the window.");
            return mapped.Value;
        }

        private static void AssertHitIsSplitter(Window window, Control splitter)
        {
            var hit = window.InputHitTest(SplitterCenter(window, splitter));
            Assert.True(
                IsUnderSplitter(hit, splitter),
                "Hit at the Trace/TxSlot splitter should be the splitter, not Trace. Hit: "
                    + (hit == null ? "(null)" : hit.GetType().Name));
        }

        private static bool IsUnderSplitter(IInputElement? hit, Control splitter)
        {
            for (var visual = hit as Visual; visual != null; visual = visual.GetVisualParent())
            {
                if (ReferenceEquals(visual, splitter))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class OpenWindow
        {
            public OpenWindow(ViewSession session, MainWindow window)
            {
                Session = session;
                Window = window;
            }

            public ViewSession Session { get; }
            public MainWindow Window { get; }
        }
    }
}
