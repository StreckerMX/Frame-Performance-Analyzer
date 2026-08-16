using System.Windows;
using FrameViewAnalyzer.App.Services;
using Xunit;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// The DWM caption theming helper must never throw when invoked without a
/// native window handle (e.g., before the window is shown, or in headless
/// contexts) — the real HWND path is exercised by the application itself.
/// </summary>
public class WindowTitleBarThemeTests
{
    [Fact]
    public void Apply_is_a_no_op_before_the_native_handle_exists()
    {
        WpfStaTestHost.Run(() =>
        {
            var window = new Window();
            try
            {
                // The window was never shown, so WindowInteropHelper.Handle
                // is still zero; both themes must pass through safely.
                WindowTitleBarTheme.Apply(window, isDark: true);
                WindowTitleBarTheme.Apply(window, isDark: false);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
