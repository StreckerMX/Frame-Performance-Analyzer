using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FrameViewAnalyzer.App;

public partial class MainWindow
{
    private static readonly RoutedCommand VisiblePngExportCommand = new();
    private static readonly RoutedCommand PortableCsvExportCommand = new();
    private bool _portableUiConfigured;

    /// <summary>
    /// Keeps the experiment isolated from the existing XAML while adding the
    /// requested Import control next to Export and routing the export menu /
    /// keyboard shortcuts through the range-aware handlers.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ConfigurePortableImportExportUi();
    }

    private void ConfigurePortableImportExportUi()
    {
        if (_portableUiConfigured)
        {
            return;
        }

        var exportButton = FindButtonByContent(this, "Export");
        if (exportButton is null || VisualTreeHelper.GetParent(exportButton) is not Grid toolbar)
        {
            return;
        }

        _portableUiConfigured = true;

        // Put Import immediately beside Export in the top toolbar. Keeping the
        // two actions together makes the new round-trip workflow discoverable
        // without taking space away from the capture selector.
        var originalColumn = Grid.GetColumn(exportButton);
        var originalMargin = exportButton.Margin;
        toolbar.Children.Remove(exportButton);

        exportButton.Margin = new Thickness(8, 0, 0, 0);
        var importButton = new Button
        {
            Content = "Import",
            ToolTip = "Import a Frame Performance Analyzer analyzed-data CSV or JSON export",
        };
        importButton.SetResourceReference(FrameworkElement.StyleProperty, "GhostButtonStyle");
        importButton.Click += ImportPortable_Click;

        var importExportPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = originalMargin,
        };
        importExportPanel.Children.Add(importButton);
        importExportPanel.Children.Add(exportButton);
        Grid.SetColumn(importExportPanel, originalColumn);
        toolbar.Children.Add(importExportPanel);

        // Replace the old statistics-only entries with the round-trippable,
        // range-aware export actions. The existing ContextMenu keeps its theme
        // resources and dropdown behavior.
        if (exportButton.ContextMenu is { } menu)
        {
            menu.Items.Clear();
            menu.Items.Add(MenuItem("PNG report", ExportVisiblePng_Click));
            menu.Items.Add(MenuItem("Analyzed data CSV", ExportPortableCsv_Click));
            menu.Items.Add(MenuItem("Analyzed data JSON", ExportPortableJson_Click));
        }

        // Ctrl+E and Ctrl+Shift+E previously reached the legacy handlers
        // directly through the ViewModel events. Replace only those two input
        // bindings so keyboard export follows the exact same visible-range
        // behavior as the toolbar.
        foreach (var binding in InputBindings.OfType<KeyBinding>().ToList())
        {
            if (binding.Key == Key.E
                && (binding.Modifiers == ModifierKeys.Control
                    || binding.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                InputBindings.Remove(binding);
            }
        }

        CommandBindings.Add(new CommandBinding(
            VisiblePngExportCommand,
            (_, _) => ExportVisiblePng_Click(this, new RoutedEventArgs())));
        CommandBindings.Add(new CommandBinding(
            PortableCsvExportCommand,
            (_, _) => ExportPortableCsv_Click(this, new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(
            VisiblePngExportCommand,
            Key.E,
            ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(
            PortableCsvExportCommand,
            Key.E,
            ModifierKeys.Control | ModifierKeys.Shift));
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button
                && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            {
                return button;
            }

            if (FindButtonByContent(child, content) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
