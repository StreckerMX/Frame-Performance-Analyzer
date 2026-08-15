using System.Windows;
using System.Windows.Threading;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Comparison;
using FrameViewAnalyzer.Analytics.RangeAnalysis;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Infrastructure;
using FrameViewAnalyzer.Infrastructure.Csv;
using FrameViewAnalyzer.Infrastructure.Exports;
using FrameViewAnalyzer.Infrastructure.Legacy;
using FrameViewAnalyzer.Infrastructure.Stores;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FrameViewAnalyzer.App;

/// <summary>
/// Composition root: registers services, loads settings, applies the saved
/// theme, and shows the main window. File logging (%LOCALAPPDATA%\FrameView
/// Analyzer\logs) starts before anything else and flushes on exit; logging
/// complements — never replaces — the user-facing error dialogs.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Logger = AppLog.Initialize();
        DispatcherUnhandledException += (_, args) =>
            Log.Error(args.Exception, "Unhandled dispatcher exception");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error(args.ExceptionObject as Exception, "Unhandled AppDomain exception");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        try
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            services.AddSingleton<IFrameViewCsvReader, FrameViewCsvReader>();
            services.AddSingleton<ICaptureAnalysisService, CaptureAnalysisService>();
            services.AddSingleton<IComparisonService, ComparisonService>();
            services.AddSingleton<IRangeAnalysisService, RangeAnalysisService>();
            services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore());
            services.AddSingleton<IManualMetadataStore>(_ => new JsonManualMetadataStore());
            services.AddSingleton<ILibraryStore>(_ => new JsonLibraryStore());
            services.AddSingleton<CaptureFolderScanner>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<ILegacyDataImporter>(provider => new LegacyDataImporter(
                provider.GetRequiredService<ISettingsStore>(),
                provider.GetRequiredService<IManualMetadataStore>(),
                provider.GetRequiredService<ILibraryStore>()));
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
            Log.Information("FrameView Analyzer started");
        }
        catch (Exception error)
        {
            // Controlled startup failure: log it, tell the user, and stop.
            Log.Fatal(error, "FrameView Analyzer startup failed");
            MessageBox.Show(
                $"The application could not start.\n\n{error.Message}",
                "FrameView Analyzer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.Dispose();
        }
        finally
        {
            Log.Information("FrameView Analyzer exiting");
            AppLog.Close();
        }

        base.OnExit(e);
    }
}

