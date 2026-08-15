using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Main-window state: theme mode, capture loading, status line, and the
/// application version. Analytics state lives in the ChartViewModel.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themes;
    private readonly IFrameViewCsvReader _reader;
    private readonly ICaptureAnalysisService _analysis;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private string _appearanceMode = "dark";

    [ObservableProperty]
    private bool _isDark = true;

    [ObservableProperty]
    private bool _isLight;

    [ObservableProperty]
    private string _statusText = "READY  ·  Ctrl+O to open a capture";

    public ChartViewModel Chart { get; }

    public string VersionText => "FrameView Analyzer v2";

    public MainWindowViewModel(
        ISettingsStore settings,
        IThemeService themes,
        ChartViewModel chart,
        IFrameViewCsvReader reader,
        ICaptureAnalysisService analysis,
        IDialogService dialogs)
    {
        _settings = settings;
        _themes = themes;
        _reader = reader;
        _analysis = analysis;
        _dialogs = dialogs;
        Chart = chart;

        var mode = Normalize(settings.Load().AppearanceMode);
        _appearanceMode = mode;
        _isDark = mode == "dark";
        _isLight = mode == "light";
    }

    [RelayCommand]
    private async Task LoadCaptureAsync()
    {
        var path = _dialogs.PickCsvFile(null);
        if (path is null)
        {
            return;
        }

        try
        {
            var capture = await _reader.LoadCaptureAsync(path);
            var session = _analysis.Analyze(capture);
            Chart.Load(session);
            StatusText = $"ANALYZED {Chart.SeriesPointCount:N0} valid seconds  •  {Chart.SampleCount:N0} samples";
        }
        catch (Exception error)
        {
            _dialogs.ShowError("CSV loading error", error.Message);
        }
    }

    [RelayCommand]
    private void ChangeAppearance(string mode)
    {
        var normalized = Normalize(mode);
        if (normalized == AppearanceMode)
        {
            return;
        }

        AppearanceMode = normalized;
        IsDark = normalized == "dark";
        IsLight = normalized == "light";
        _themes.Apply(normalized);

        var settings = _settings.Load();
        _settings.Save(settings with { AppearanceMode = normalized });
    }

    partial void OnIsDarkChanged(bool value)
    {
        if (value && AppearanceMode != "dark")
        {
            ChangeAppearance("dark");
        }
    }

    partial void OnIsLightChanged(bool value)
    {
        if (value && AppearanceMode != "light")
        {
            ChangeAppearance("light");
        }
    }

    private static string Normalize(string? mode) =>
        string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
}
