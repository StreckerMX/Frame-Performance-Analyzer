using System.Globalization;
using System.Windows.Data;

namespace FrameViewAnalyzer.App.Converters;

/// <summary>Inverts a boolean (used for the library sort-name radio).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean ? !boolean : value ?? false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool boolean ? !boolean : value ?? false;
}
