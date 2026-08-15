using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FrameViewAnalyzer.App.Converters;

/// <summary>Visibility for inverted booleans (missing-capture badge).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
