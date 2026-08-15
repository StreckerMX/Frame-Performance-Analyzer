using System.Windows;

namespace FrameViewAnalyzer.App.Services;

/// <summary>Pure bounds validation against the virtual screen.</summary>
public static class WindowBoundsValidator
{
    public const double MinVisibleWidth = 120;
    public const double MinVisibleHeight = 48;

    /// <summary>The virtual screen covering all monitors.</summary>
    public static Rect VirtualScreen() => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// True when the window rectangle has a usable, interactable overlap
    /// with the virtual screen: positive size and at least a title-bar-sized
    /// visible area, so an old configuration can never reopen the app
    /// off-screen.
    /// </summary>
    public static bool IsUsable(Rect window, Rect virtualScreen)
    {
        if (window.Width <= 0 || window.Height <= 0)
        {
            return false;
        }

        window.Intersect(virtualScreen);
        return window.Width >= MinVisibleWidth && window.Height >= MinVisibleHeight;
    }
}
