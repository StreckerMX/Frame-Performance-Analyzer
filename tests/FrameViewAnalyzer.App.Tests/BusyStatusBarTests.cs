using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FrameViewAnalyzer.App.Busy;
using FrameViewAnalyzer.App.Views;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// WPF presentation of the busy system on the shared STA test host: the
/// status bar renders the shared READY / BUSY formats, the overlay dims and
/// blocks the content rows while leaving the status bar row uncovered, and
/// closing a Window disposes its busy state (no leaked timers or events).
/// </summary>
public class BusyStatusBarTests
{
    private static BusyState FastState() =>
        new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(60));

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var deadline = DateTime.UtcNow + timeout;
        var frame = new DispatcherFrame();
        void Tick()
        {
            if (condition() || DateTime.UtcNow > deadline)
            {
                frame.Continue = false;
                return;
            }

            dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Tick);
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)Tick);
        Dispatcher.PushFrame(frame);
    }

    private static string StatusTextOf(BusyStatusBar bar) =>
        string.Concat(bar.StatusTextBlock.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));

    [Fact]
    public void Status_bar_renders_ready_with_the_accent_dot() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var bar = new BusyStatusBar { ReadyText = "READY  ·  Ctrl+O to open a capture" };

            Assert.Equal("● READY  ·  Ctrl+O to open a capture", StatusTextOf(bar));
        });

    [Fact]
    public void Status_bar_renders_busy_operation_with_animated_dots() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var state = FastState();
            var bar = new BusyStatusBar
            {
                ReadyText = "READY",
                State = state,
            };
            var scope = state.Begin("Loading benchmark library");
            try
            {
                PumpUntil(() => state.IsBusyVisible, TimeSpan.FromSeconds(5));
                // The render is marshaled to the dispatcher; wait for it.
                PumpUntil(() => StatusTextOf(bar).StartsWith("● BUSY", StringComparison.Ordinal), TimeSpan.FromSeconds(5));

                var text = StatusTextOf(bar);
                Assert.StartsWith("● BUSY", text);
                Assert.Contains("Loading benchmark library", text);
                Assert.EndsWith(".", text);
            }
            finally
            {
                scope.Dispose();
            }

            PumpUntil(() => !state.IsBusyVisible, TimeSpan.FromSeconds(5));
            PumpUntil(() => StatusTextOf(bar) == "● READY", TimeSpan.FromSeconds(5));
            Assert.Equal("● READY", StatusTextOf(bar));
        });

    [Fact]
    public void Attach_appends_a_status_bar_and_overlay_below_the_content() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var state = FastState();
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(new Border { Background = System.Windows.Media.Brushes.Black });
            var window = new Window { Content = root };
            try
            {
                WindowBusy.Attach(window, state);

                var statusBar = Assert.Single(root.Children.OfType<BusyStatusBar>());
                var overlay = Assert.Single(root.Children.OfType<BusyOverlay>());
                Assert.Equal(2, Grid.GetRow(statusBar));
                Assert.Equal(0, Grid.GetRow(overlay));
                // The overlay covers the content rows only; the status bar row
                // (row 2) stays outside the dim.
                Assert.Equal(2, Grid.GetRowSpan(overlay));
                Assert.Same(state, statusBar.State);
                Assert.Same(state, overlay.State);
                Assert.False(overlay.IsHitTestVisible);
                Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Attach_reuses_a_status_bar_already_placed_in_xaml() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var state = FastState();
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var statusBar = new BusyStatusBar { ReadyText = "READY  ·  custom" };
            Grid.SetRow(statusBar, 2);
            root.Children.Add(statusBar);
            var window = new Window { Content = root };
            try
            {
                WindowBusy.Attach(window, state);

                // The XAML-placed bar is reused (no second bar), the overlay
                // spans the two content rows above it.
                Assert.Single(root.Children.OfType<BusyStatusBar>());
                var overlay = Assert.Single(root.Children.OfType<BusyOverlay>());
                Assert.Equal(2, Grid.GetRowSpan(overlay));
                Assert.Same(state, statusBar.State);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Overlay_blocks_input_while_visible_and_releases_afterwards() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var state = FastState();
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(new Border { Background = System.Windows.Media.Brushes.Black });
            var window = new Window { Content = root };
            try
            {
                WindowBusy.Attach(window, state);
                var overlay = Assert.Single(root.Children.OfType<BusyOverlay>());

                var scope = state.Begin("Loading benchmark library");
                try
                {
                    PumpUntil(() => state.IsBusyVisible, TimeSpan.FromSeconds(5));
                    // The overlay update is marshaled to the dispatcher.
                    PumpUntil(() => overlay.IsHitTestVisible, TimeSpan.FromSeconds(5));

                    Assert.True(overlay.IsHitTestVisible);
                    Assert.Equal(Visibility.Visible, overlay.Visibility);
                }
                finally
                {
                    scope.Dispose();
                }

                PumpUntil(() => overlay.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(5));
                Assert.False(overlay.IsHitTestVisible);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Closing_the_window_disposes_the_state_and_leaks_no_events() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var state = FastState();
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(new Border { Background = System.Windows.Media.Brushes.Black });
            var window = new Window { Content = root };
            window.Show();
            WindowBusy.Attach(window, state);
            state.Begin("Loading benchmark library");

            window.Close();

            Assert.True(state.IsDisposed);
            Assert.False(state.IsBusy);
            Assert.False(state.IsBusyVisible);

            // Subscribing only after the close proves that disposal stopped
            // every timer: no event may arrive afterwards.
            var eventsAfterClose = 0;
            state.BusyVisibleChanged += (_, _) => eventsAfterClose++;
            state.EllipsisChanged += (_, _) => eventsAfterClose++;
            Thread.Sleep(300);
            Assert.Equal(0, eventsAfterClose);
        });
}
