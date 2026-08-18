using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace FrameViewAnalyzer.App;

public partial class MainWindow
{
    private bool _multiKpiTemplateInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InstallMultiAwareKpiTemplate();
    }

    /// <summary>
    /// Replaces the legacy inline KPI DataTemplate with the reusable
    /// KpiTileView. This keeps Pair rendering unchanged while allowing Multi
    /// tiles to render N color-coded benchmark rows without a broad dashboard
    /// XAML rewrite on the feature branch.
    /// </summary>
    private void InstallMultiAwareKpiTemplate()
    {
        if (_multiKpiTemplateInstalled)
        {
            return;
        }

        var control = FindVisualChild<ItemsControl>(this, itemsControl =>
        {
            var binding = BindingOperations.GetBinding(
                itemsControl,
                ItemsControl.ItemsSourceProperty);
            return string.Equals(
                binding?.Path?.Path,
                "Chart.KpiTiles",
                StringComparison.Ordinal);
        });

        if (control is null)
        {
            return;
        }

        const string templateXaml = """
            <DataTemplate
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:views="clr-namespace:FrameViewAnalyzer.App.Views;assembly=FrameViewAnalyzer.App">
                <views:KpiTileView />
            </DataTemplate>
            """;

        control.ItemTemplate = (DataTemplate)XamlReader.Parse(templateXaml);
        _multiKpiTemplateInstalled = true;
    }

    private static T? FindVisualChild<T>(
        DependencyObject root,
        Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T candidate && predicate(candidate))
        {
            return candidate;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var result = FindVisualChild(child, predicate);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
