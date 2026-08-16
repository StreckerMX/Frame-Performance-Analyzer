using System.Windows;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Applies the native DWM caption theme to a window when its HWND appears and
/// on every theme change, so secondary windows/dialogs match the main
/// window's dark/light title bar without duplicating the P/Invoke wiring.
/// The subscription is released when the window closes (no lifetime leak).
/// </summary>
public static class WindowThemeBootstrap
{
    public static void Attach(Window window, IThemeService themes)
    {
        window.SourceInitialized += (_, _) => Apply(window, themes);

        EventHandler onChanged = (_, _) => Apply(window, themes);
        themes.Changed += onChanged;
        window.Closed += (_, _) => themes.Changed -= onChanged;
    }

    private static void Apply(Window window, IThemeService themes)
    {
        var isDark = !string.Equals(themes.Current, "light", StringComparison.OrdinalIgnoreCase);
        WindowTitleBarTheme.Apply(window, isDark);
    }
}
