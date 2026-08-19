using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FrameViewAnalyzer.App.Busy;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Dim layer shown over a Window's content while it is busy. Input blocking
/// and the dim are deliberately separated:
/// <list type="bullet">
/// <item>the moment the state becomes logically busy (<c>IsBusy</c>), the
/// overlay turns visible and hit-testable at opacity 0, so conflicting
/// pointer interaction is intercepted immediately — before any threshold;</item>
/// <item>only when the state becomes visually busy (<c>IsBusyVisible</c>,
/// after the presentation threshold) does it fade to the 50% black dim;</item>
/// <item>when the last operation ends, input is released right away; the
/// dim — if it was ever shown — fades out without blocking input, and an
/// operation that never dimmed collapses instantly.</item>
/// </list>
/// <see cref="WindowBusy"/> places the overlay so it never covers the status
/// bar.
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

    private bool _blocking;
    private bool _dimmed;

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
            previous.BusyChanged -= overlay.OnBusyChanged;
            previous.BusyVisibleChanged -= overlay.OnBusyVisibleChanged;
        }

        if (e.NewValue is BusyState next)
        {
            next.BusyChanged += overlay.OnBusyChanged;
            next.BusyVisibleChanged += overlay.OnBusyVisibleChanged;
        }

        overlay.Marshal(overlay.Sync);
    }

    private void OnBusyChanged(object? sender, EventArgs e) => Marshal(Sync);

    private void OnBusyVisibleChanged(object? sender, EventArgs e) => Marshal(Sync);

    /// <summary>Reconciles the overlay with the current state; dispatcher thread only.</summary>
    private void Sync()
    {
        var state = State;
        var busy = state is { IsBusy: true };
        var visible = state is { IsBusyVisible: true };
        _blocking = busy;

        if (busy)
        {
            // Intercept input immediately — before any dimming is visible.
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
            if (_dimmed == visible)
            {
                // Presentation already matches (transparent while below the
                // threshold, dimmed while above); avoid re-starting a fade.
                return;
            }

            _dimmed = visible;
            BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, FadeDuration));
        }
        else if (_dimmed)
        {
            // The dim was shown: release input now and let it fade out.
            _dimmed = false;
            IsHitTestVisible = false;
            var hide = new DoubleAnimation(0, FadeDuration);
            hide.Completed += (_, _) =>
            {
                // A new operation may have begun during the fade; only
                // collapse when the state is still idle.
                if (!_blocking)
                {
                    Visibility = Visibility.Collapsed;
                }
            };
            BeginAnimation(OpacityProperty, hide);
        }
        else
        {
            // Never dimmed: nothing to fade, collapse immediately.
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
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
