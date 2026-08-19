using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using FrameViewAnalyzer.App.Busy;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Shared per-window status bar. Idle, it renders a green dot followed by the
/// window's normal status text (<see cref="ReadyText"/>); while the attached
/// <see cref="State"/> is visibly busy it renders
/// <c>● BUSY • &lt;operation&gt;&lt;animated dots&gt;</c>, emphasized with an
/// accent border, and never dims. The trailing dots are animated by
/// <see cref="BusyState.EllipsisChanged"/> — the operation message itself is
/// stored without dots. <see cref="RightContent"/> stays visible in both
/// states (the main window uses it for the version text).
/// </summary>
public partial class BusyStatusBar : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(BusyState),
        typeof(BusyStatusBar),
        new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty ReadyTextProperty = DependencyProperty.Register(
        nameof(ReadyText),
        typeof(string),
        typeof(BusyStatusBar),
        new PropertyMetadata("READY", OnReadyTextChanged));

    public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
        nameof(RightContent),
        typeof(object),
        typeof(BusyStatusBar),
        new PropertyMetadata(null, OnRightContentChanged));

    private const string AccentBrushKey = "AccentBrush";
    private const string MutedBrushKey = "MutedBrush";
    private const string PanelBrushKey = "PanelBrush";
    private const string TextSoftBrushKey = "TextSoftBrush";

    public BusyStatusBar()
    {
        InitializeComponent();
        Render();
    }

    /// <summary>The busy state this bar presents; one instance belongs to one Window.</summary>
    public BusyState? State
    {
        get => (BusyState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>Normal (idle) status text, e.g. "READY  ·  Ctrl+O to open a capture".</summary>
    public string ReadyText
    {
        get => (string)GetValue(ReadyTextProperty);
        set => SetValue(ReadyTextProperty, value);
    }

    /// <summary>Optional right-aligned content that stays visible while busy (version text).</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (BusyStatusBar)d;
        if (e.OldValue is BusyState previous)
        {
            previous.BusyVisibleChanged -= bar.OnBusyPresentationChanged;
            previous.EllipsisChanged -= bar.OnBusyPresentationChanged;
        }

        if (e.NewValue is BusyState next)
        {
            next.BusyVisibleChanged += bar.OnBusyPresentationChanged;
            next.EllipsisChanged += bar.OnBusyPresentationChanged;
        }

        bar.Render();
    }

    private static void OnReadyTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BusyStatusBar)d).Render();

    private static void OnRightContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BusyStatusBar)d).RightContentPresenter.Content = e.NewValue;

    private void OnBusyPresentationChanged(object? sender, EventArgs e) => Marshal(Render);

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

    private void Render()
    {
        var state = State;
        var busy = state is { IsBusyVisible: true };

        // Ready: transparent, like the original main-window status line.
        // Busy: a card surface with the green accent border — the status bar
        // is the primary loading indicator and must stand out from the dim.
        if (busy)
        {
            RootBorder.SetResourceReference(Border.BackgroundProperty, PanelBrushKey);
            RootBorder.SetResourceReference(Border.BorderBrushProperty, AccentBrushKey);
            RootBorder.BorderThickness = new Thickness(1);
        }
        else
        {
            RootBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            RootBorder.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            RootBorder.BorderThickness = new Thickness(0);
        }

        StatusTextBlock.Inlines.Clear();
        if (busy && state is not null)
        {
            StatusTextBlock.Inlines.Add(ResourceRun("● ", AccentBrushKey));
            StatusTextBlock.Inlines.Add(ResourceRun("BUSY", TextSoftBrushKey, bold: true));
            StatusTextBlock.Inlines.Add(ResourceRun("  •  ", MutedBrushKey));
            StatusTextBlock.Inlines.Add(ResourceRun(state.OperationText ?? string.Empty, TextSoftBrushKey));
            StatusTextBlock.Inlines.Add(ResourceRun(
                new string('.', Math.Max(1, state.EllipsisDots)),
                TextSoftBrushKey));
        }
        else
        {
            StatusTextBlock.Inlines.Add(ResourceRun("● ", AccentBrushKey));
            StatusTextBlock.Inlines.Add(ResourceRun(ReadyText, TextSoftBrushKey));
        }
    }

    private static Run ResourceRun(string text, string brushKey, bool bold = false)
    {
        var run = new Run(text);
        // Resource references (not static brushes) keep the theme-switchable.
        run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        if (bold)
        {
            run.FontWeight = FontWeights.Bold;
        }

        return run;
    }
}
