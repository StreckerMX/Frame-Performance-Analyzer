using System.Windows;
using FrameViewAnalyzer.Analytics;

namespace FrameViewAnalyzer.App.Views;

public enum ExportScope
{
    All,
    Single,
}

/// <summary>
/// Choose between exporting every loaded session or exactly one session for
/// the PNG report, like the Python ExportDialog.
/// </summary>
public partial class ExportReportWindow : Window
{
    private readonly IReadOnlyList<(SessionAnalysis Session, string Label)> _options;

    public ExportReportWindow(IReadOnlyList<(SessionAnalysis Session, string Label)> options)
    {
        InitializeComponent();
        _options = options;
        SessionOptions.ItemsSource = options;
        if (options.Count > 0)
        {
            SessionOptions.SelectedIndex = 0;
        }
    }

    public event Action<ExportScope, SessionAnalysis?>? ExportRequested;

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var scope = SingleRadio.IsChecked == true ? ExportScope.Single : ExportScope.All;
        var selected = scope == ExportScope.Single
            && SessionOptions.SelectedItem is ValueTuple<SessionAnalysis, string> option
                ? option.Item1
                : null;
        ExportRequested?.Invoke(scope, selected);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
