using System.Windows;
using System.Windows.Controls;
using FrameViewAnalyzer.Analytics.Exports;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Choose between exporting every loaded session or exactly one session for
/// the PNG report, like the Python ExportDialog.
/// </summary>
public partial class ExportReportWindow : Window
{
    private readonly IReadOnlyList<ExportSessionOption> _options;

    public ExportReportWindow(IReadOnlyList<ExportSessionOption> options)
    {
        InitializeComponent();
        _options = options;

        // Subscribe AFTER InitializeComponent: the All radio's IsChecked=True
        // fires Checked during XAML load, when ExportButton does not exist
        // yet — wiring through XAML caused a startup NullReferenceException.
        AllRadio.Checked += Scope_Checked;
        SingleRadio.Checked += Scope_Checked;

        SessionOptions.ItemsSource = options;
        if (options.Count > 0)
        {
            // Prefer Base as the initial selection when both sessions exist.
            SessionOptions.SelectedIndex = 0;
        }

        UpdateExportEnabled();
    }

    public event Action<ExportScope, ExportSessionOption?>? ExportRequested;

    /// <summary>Selected-session export is only valid when a session is chosen.</summary>
    public static bool CanExport(ExportScope scope, ExportSessionOption? selected) =>
        scope == ExportScope.All || selected is not null;

    /// <summary>Resolves the exact export target for the current UI state.</summary>
    public static ExportSessionOption? SelectedSession(ExportScope scope, object? selectedItem) =>
        scope == ExportScope.Single && selectedItem is ExportSessionOption option
            ? option
            : null;

    private void UpdateExportEnabled()
    {
        var scope = SingleRadio.IsChecked == true ? ExportScope.Single : ExportScope.All;
        var selected = SelectedSession(scope, SessionOptions.SelectedItem);
        ExportButton.IsEnabled = CanExport(scope, selected);
    }

    private void Scope_Checked(object sender, RoutedEventArgs e) => UpdateExportEnabled();

    private void SessionOptions_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateExportEnabled();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var scope = SingleRadio.IsChecked == true ? ExportScope.Single : ExportScope.All;
        var option = SelectedSession(scope, SessionOptions.SelectedItem);
        if (!CanExport(scope, option))
        {
            return;
        }

        // The full option travels with the request: the role it carries is
        // authoritative for the header and the series styling, so the
        // selected-session export can never mislabel Base as Comparison or
        // vice versa.
        ExportRequested?.Invoke(scope, option);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
