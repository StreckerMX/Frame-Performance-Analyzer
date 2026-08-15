using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Tests;

public class MainWindowViewModelTests
{
    private sealed class FakeSettingsStore : ISettingsStore
    {
        public SettingsDocument Current { get; set; } = new();

        public int SaveCount { get; private set; }

        public SettingsDocument Load() => Current;

        public void Save(SettingsDocument settings)
        {
            Current = settings;
            SaveCount++;
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public string Current { get; private set; } = "dark";

        public int ApplyCount { get; private set; }

        public void Apply(string mode)
        {
            Current = mode;
            ApplyCount++;
        }
    }

    [Fact]
    public void Constructor_adopts_the_saved_theme()
    {
        var settings = new FakeSettingsStore { Current = new SettingsDocument(AppearanceMode: "light") };

        var viewModel = new MainWindowViewModel(settings, new FakeThemeService());

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.False(viewModel.IsDark);
        Assert.True(viewModel.IsLight);
    }

    [Fact]
    public void ChangeAppearance_applies_theme_and_persists()
    {
        var settings = new FakeSettingsStore();
        var themes = new FakeThemeService();
        var viewModel = new MainWindowViewModel(settings, themes);

        viewModel.ChangeAppearanceCommand.Execute("light");

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.True(viewModel.IsLight);
        Assert.False(viewModel.IsDark);
        Assert.Equal("light", themes.Current);
        Assert.Equal("light", settings.Current.AppearanceMode);
    }

    [Fact]
    public void Reapplying_the_current_theme_is_a_no_op()
    {
        var settings = new FakeSettingsStore();
        var themes = new FakeThemeService();
        var viewModel = new MainWindowViewModel(settings, themes);

        viewModel.ChangeAppearanceCommand.Execute("dark");

        Assert.Equal(0, settings.SaveCount);
        Assert.Equal(0, themes.ApplyCount);
    }

    [Fact]
    public void Invalid_mode_normalizes_to_dark()
    {
        var settings = new FakeSettingsStore { Current = new SettingsDocument(AppearanceMode: "light") };
        var viewModel = new MainWindowViewModel(settings, new FakeThemeService());

        viewModel.ChangeAppearanceCommand.Execute("sepia");

        Assert.Equal("dark", viewModel.AppearanceMode);
        Assert.Equal("dark", settings.Current.AppearanceMode);
    }

    [Fact]
    public void Theme_radio_state_flows_through_bindings()
    {
        var settings = new FakeSettingsStore();
        var themes = new FakeThemeService();
        var viewModel = new MainWindowViewModel(settings, themes);

        viewModel.IsLight = true;

        Assert.Equal("light", viewModel.AppearanceMode);
        Assert.Equal("light", themes.Current);
        Assert.Equal("light", settings.Current.AppearanceMode);
    }

    [Fact]
    public void Status_text_is_ready_by_default()
    {
        var viewModel = new MainWindowViewModel(new FakeSettingsStore(), new FakeThemeService());

        Assert.Contains("READY", viewModel.StatusText);
        Assert.Equal("FrameView Analyzer v2", viewModel.VersionText);
    }
}
