using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FrameViewAnalyzer.App.Services;

namespace FrameViewAnalyzer.App.Views;

public enum ThemedDialogKind
{
    Info,
    Warning,
    Error,
    Success,
    Confirmation,
}

/// <summary>
/// Reusable modal dialog that uses the application's WPF resources and native
/// title-bar theme instead of the system MessageBox chrome.
/// </summary>
public partial class ThemedDialogWindow : Window
{
    public ThemedDialogWindow(
        ThemedDialogKind kind,
        string title,
        string message,
        string primaryText = "OK",
        string? cancelText = null,
        bool destructive = false)
    {
        InitializeComponent();
        Kind = kind;
        Title = title;
        DialogHeading.Text = title;
        DialogMessage.Text = message;
        PrimaryButton.Content = primaryText;
        ConfigureKind(kind, destructive);

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            CancelButton.Content = cancelText;
            CancelButton.Visibility = Visibility.Visible;
            // Confirmations are intentionally safe by default: pressing Enter
            // does not perform the destructive/affirmative action accidentally.
            PrimaryButton.IsDefault = false;
            CancelButton.IsDefault = true;
        }

        PrimaryButton.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        CancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape)
            {
                return;
            }

            DialogResult = false;
            Close();
            args.Handled = true;
        };
    }

    public ThemedDialogKind Kind { get; }

    internal string IconGlyphText => IconGlyph.Text;

    internal string IconBrushResource { get; private set; } = "SeriesBBrush";

    internal bool HasCancelAction => CancelButton.Visibility == Visibility.Visible;

    private void ConfigureKind(ThemedDialogKind kind, bool destructive)
    {
        var (glyph, brushKey) = kind switch
        {
            ThemedDialogKind.Success => ("✓", "SuccessBrush"),
            ThemedDialogKind.Warning => ("!", "WarningBrush"),
            ThemedDialogKind.Error => ("×", "DangerBrush"),
            ThemedDialogKind.Confirmation => ("?", "WarningBrush"),
            _ => ("i", "SeriesBBrush"),
        };

        IconGlyph.Text = glyph;
        IconBrushResource = brushKey;
        IconBadge.SetResourceReference(Border.BorderBrushProperty, brushKey);
        IconGlyph.SetResourceReference(TextBlock.ForegroundProperty, brushKey);

        if (destructive)
        {
            PrimaryButton.SetResourceReference(
                FrameworkElement.StyleProperty,
                "DangerGhostButtonStyle");
        }
    }
}
