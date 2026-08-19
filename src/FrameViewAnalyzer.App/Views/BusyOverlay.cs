using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FrameViewAnalyzer.App.Busy;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Dim layer shown over a Window's content while it is visibly busy. A 50%
/// black translucent surface (theme-independent, matches both dark and light
/// modes) fades in/out over <see cref="FadeDuration"/> and intercepts mouse
/// input while visible, so controls underneath can neither be clicked nor
/// receive hover feedback. <see cref="WindowBusy"/> places it so it never
/// covers the status bar.
/// </summary>
public sealed class BusyOverlay : Border
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(BusyState),
        typeof(BusyOverlay),
        new PropertyMetadata(null, OnStateChanged));

    /// <summary>Fade in/out duration for the dim layer.</summary>
    public static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(120));

    private bool _shown;

    public BusyOverlay()
    {
        Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        Opacity = 0;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
    }

    /// <summary>The busy state that drives this overlay; one instance belongs to one Window.</summary>
    public BusyState? State
    {
        get => (BusyState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (BusyOverlay)d;
        if (e.OldValue is BusyState previous)
        {
            previous.BusyVisibleChanged -= overlay.OnBusyVisibleChanged;
        }

        var shown = false;
        if (e.NewValue is BusyState next)
        {
            next.BusyVisibleChanged += overlay.OnBusyVisibleChanged;
            shown = next.IsBusyVisible;
        }

        overlay.Marshal(() => overlay.SetShown(shown));
    }

    private void OnBusyVisibleChanged(object? sender, EventArgs e) =>
        Marshal(() => SetShown(State is { IsBusyVisible: true }));

    private void SetShown(bool shown)
    {
        if (_shown == shown)
        {
            return;
        }

        _shown = shown;
        if (shown)
        {
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, FadeDuration));
        }
        else
        {
            var hide = new DoubleAnimation(0, FadeDuration);
            hide.Completed += (_, _) =>
            {
                // A new operation may have begun during the fade; only
                // collapse when the state is still idle.
                if (!_shown)
                {
                    Visibility = Visibility.Collapsed;
                    IsHitTestVisible = false;
                }
            };
            BeginAnimation(OpacityProperty, hide);
        }
    }

    /// <summary>
    /// Busy events can arrive from the state's timer threads; all UI mutation
    /// happens on this control's dispatcher. Silently drops when the window's
    /// dispatcher is shutting down.
    /// </summary>
    private void Marshal(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
        catch (InvalidOperationException)
        {
            // Dispatcher is shutting down (window closed); nothing to render.
        }
    }
}
