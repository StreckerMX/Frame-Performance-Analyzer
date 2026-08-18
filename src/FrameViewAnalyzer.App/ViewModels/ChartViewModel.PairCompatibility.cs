using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.Core;

namespace FrameViewAnalyzer.App.ViewModels;

public partial class ChartViewModel
{
    /// <summary>
    /// The N-way series builder keeps the first available series as a general
    /// adapter. In Pair mode, if a metric exists only in Comparison, preserve
    /// the original contract: Series is null and ComparisonSeries owns it.
    /// </summary>
    partial void OnComparisonSeriesChanged(MetricSeries? value)
    {
        if (!_isMultiWorkspace
            && value is not null
            && value.Role == SessionRole.Comparison
            && ReferenceEquals(Series, value))
        {
            Series = null;
        }
    }
}
