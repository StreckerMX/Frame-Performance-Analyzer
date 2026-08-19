using System.Windows;
using System.Windows.Controls;
using FrameViewAnalyzer.App.Busy;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Attaches the shared busy presentation to one Window:
/// <list type="bullet">
/// <item>a <see cref="BusyStatusBar"/> — an existing one placed in the XAML
/// is reused (MainWindow), otherwise a new bottom row is appended
/// (Benchmark Library);</item>
/// <item>a <see cref="BusyOverlay"/> spanning every row ABOVE the status bar,
/// so the status bar always stays bright and outside the dim;</item>
/// <item>disposal of the <see cref="BusyState"/> when the Window closes, so
/// timers and event subscriptions never outlive the window.</item>
/// </list>
/// Adopting the system in a new Window is one line in its constructor:
/// <c>WindowBusy.Attach(this, _busy)</c> (plus a busy scope per operation).
/// </summary>
public static class WindowBusy
{
    /// <summary>Spacing between the dimmed content and the status bar.</summary>
    private static readonly Thickness StatusBarMargin = new(0, 8, 0, 0);

    /// <summary>
    /// Attaches the status bar and overlay to the Window's root grid.
    /// No-op when the window content is not a Grid (all application windows
    /// use one).
    /// </summary>
    public static void Attach(Window window, BusyState state)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(state);

        if (window.Content is not Grid root)
        {
            return;
        }

        var statusBar = FindDirectChild<BusyStatusBar>(root);
        int statusRow;
        if (statusBar is null)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            statusRow = root.RowDefinitions.Count - 1;
            statusBar = new BusyStatusBar { Margin = StatusBarMargin };
            Grid.SetRow(statusBar, statusRow);
            root.Children.Add(statusBar);
        }
        else
        {
            statusRow = Grid.GetRow(statusBar);
        }

        statusBar.State = state;

        // Covers every content row but never the status bar row.
        var overlay = new BusyOverlay { State = state };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, Math.Max(1, statusRow));
        Panel.SetZIndex(overlay, 1000);
        root.Children.Add(overlay);

        window.Closed += (_, _) => state.Dispose();
    }

    private static T? FindDirectChild<T>(Grid root)
        where T : DependencyObject
    {
        foreach (var child in root.Children)
        {
            if (child is T match)
            {
                return match;
            }
        }

        return null;
    }
}
