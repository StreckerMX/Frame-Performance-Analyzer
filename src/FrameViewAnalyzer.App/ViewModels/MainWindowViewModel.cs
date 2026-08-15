using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Main-window state: theme mode, status line, and the application version.
/// Analytics state arrives in later phases.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IThemeService _themes;

    [ObservableProperty]
    private string _appearanceMode = "dark";

    [ObservableProperty]
    private bool _isDark = true;

    [ObservableProperty]
    private bool _isLight;

    [ObservableProperty]
    private string _statusText = "READY  ·  Ctrl+O to open a capture";

    public string VersionText => "FrameView Analyzer v2";

    public MainWindowViewModel(ISettingsStore settings, IThemeService themes)
    {
        _settings = settings;
        _themes = themes;

        var mode = Normalize(settings.Load().AppearanceMode);
        _appearanceMode = mode;
        _isDark = mode == "dark";
        _isLight = mode == "light";
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
