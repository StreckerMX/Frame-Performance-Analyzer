using System.Windows;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace FrameViewAnalyzer.App;

/// <summary>
/// Composition root: registers services, loads settings, applies the saved
/// theme, and shows the main window.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IFrameViewCsvReader, FrameViewCsvReader>();
        services.AddSingleton<ICaptureAnalysisService, CaptureAnalysisService>();
        services.AddSingleton<IComparisonService, ComparisonService>();
        services.AddSingleton<IRangeAnalysisService, RangeAnalysisService>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore());
        services.AddSingleton<IManualMetadataStore>(_ => new JsonManualMetadataStore());
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IWindowPlacementService, WindowPlacementService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ChartViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var settings = _services.GetRequiredService<ISettingsStore>().Load();
        _services.GetRequiredService<IThemeService>().Apply(settings.AppearanceMode);

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}

