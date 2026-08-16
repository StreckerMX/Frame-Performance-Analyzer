using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Makes a Button open its ContextMenu on a normal LEFT click, anchored below
/// the button — standard dropdown UX. WPF does not open
/// <c>Button.ContextMenu</c> on left click by itself. The menu still closes
/// normally when the user clicks elsewhere, selects an item, or presses Esc;
/// menu commands and keyboard shortcuts are untouched.
/// </summary>
public static class DropdownMenu
{
    public static readonly DependencyProperty OpenOnLeftClickProperty =
        DependencyProperty.RegisterAttached(
            "OpenOnLeftClick",
            typeof(bool),
            typeof(DropdownMenu),
            new PropertyMetadata(false, OnOpenOnLeftClickChanged));

    public static bool GetOpenOnLeftClick(DependencyObject obj) =>
        (bool)obj.GetValue(OpenOnLeftClickProperty);

    public static void SetOpenOnLeftClick(DependencyObject obj, bool value) =>
        obj.SetValue(OpenOnLeftClickProperty, value);

    /// <summary>
    /// Resolves the button's ContextMenu and prepares it as a bottom-anchored
    /// dropdown. Pure and headless-testable.
    /// </summary>
    public static bool TryPrepareOpen(
        Button button,
        [NotNullWhen(true)] out ContextMenu? menu)
    {
        menu = button.ContextMenu;
        if (menu is null)
        {
            return false;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        return true;
    }

    private static void OnOpenOnLeftClickChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button)
        {
            return;
        }

        if (e.NewValue is true)
        {
            button.Click += OnButtonClick;
        }
        else
        {
            button.Click -= OnButtonClick;
        }
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button
            || !TryPrepareOpen(button, out var menu))
        {
            return;
        }

        menu.IsOpen = true;
        e.Handled = true;
    }
}
