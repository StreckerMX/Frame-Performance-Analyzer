using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Paints the native Windows caption so the standard title bar follows the
/// application theme. Uses DWM attributes on the MainWindow HWND; the native
/// chrome (minimize/maximize/close buttons, resize behavior, icon, and the
/// AppUserModelID) is untouched.
/// </summary>
public static class WindowTitleBarTheme
{
    // Windows 10 1903+ / Windows 11: asks DWM to render the caption using the
    // dark palette (also fixes the caption buttons on Windows 11).
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // Windows 11 (build 22000+): explicit caption and caption-text colors.
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    // COLORREF (0x00BBGGRR) values matching Colors.xaml / LightTheme.xaml.
    private const int DarkCaptionColor = 0x00050505;   // RGB #050505 — WindowBrush (dark)
    private const int DarkTextColor = 0x00FFFFFF;      // RGB #FFFFFF — TextBrush (dark)
    private const int LightCaptionColor = 0x00F4F1EE;  // RGB #EEF1F4 — WindowBrush (light)
    private const int LightTextColor = 0x00221B14;     // RGB #141B22 — TextBrush (light)

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    /// <summary>
    /// Applies the requested title-bar theme to the window. Safe to call
    /// before the HWND exists (no-op) and on unsupported attribute versions
    /// (attribute writes are best-effort).
    /// </summary>
    public static void Apply(Window window, bool isDark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var immersiveDarkMode = isDark ? 1 : 0;
        Set(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref immersiveDarkMode);

        var captionColor = isDark ? DarkCaptionColor : LightCaptionColor;
        Set(hwnd, DWMWA_CAPTION_COLOR, ref captionColor);

        var textColor = isDark ? DarkTextColor : LightTextColor;
        Set(hwnd, DWMWA_TEXT_COLOR, ref textColor);
    }

    private static void Set(IntPtr hwnd, int attribute, ref int value)
    {
        // Older Windows builds reject the newer attributes; the OS-default
        // caption stays in that case, which is acceptable.
        _ = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }
}
