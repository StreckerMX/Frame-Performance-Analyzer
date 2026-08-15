using System.Windows;
using FrameViewAnalyzer.Infrastructure.Stores;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Persists size, position, and maximized state for the main window.
/// Restored coordinates are validated against the current virtual screen so
/// a configuration saved on a removed monitor falls back to a centered
/// 88%-of-work-area layout.
/// </summary>
public sealed class WindowPlacementService : IWindowPlacementService
{
    private readonly ISettingsStore _settings;

    public WindowPlacementService(ISettingsStore settings) => _settings = settings;

    public void Restore(Window window)
    {
        var saved = _settings.Load().Window;
        var usable = saved is not null
            && WindowBoundsValidator.IsUsable(
                new Rect(saved.Left, saved.Top, saved.Width, saved.Height),
                WindowBoundsValidator.VirtualScreen());

        if (usable && saved is not null)
        {
            window.Left = saved.Left;
            window.Top = saved.Top;
            window.Width = saved.Width;
            window.Height = saved.Height;
            if (saved.Maximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }
        else
        {
            CenterOnWorkArea(window);
        }
    }

    public void Save(Window window)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        // When maximized, RestoreBounds holds the normal (restored) rect.
        var bounds = maximized && window.RestoreBounds is { Width: > 0, Height: > 0 } restore
            ? restore
            : new Rect(window.Left, window.Top, window.Width, window.Height);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var settings = _settings.Load();
        _settings.Save(settings with
        {
            Window = new WindowStateDocument(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                maximized),
        });
    }

    private static void CenterOnWorkArea(Window window)
    {
        var work = SystemParameters.WorkArea;
        window.Width = Math.Min(1760, work.Width * 0.88);
        window.Height = Math.Min(1040, work.Height * 0.88);
        window.Left = work.Left + (work.Width - window.Width) / 2;
        window.Top = work.Top + (work.Height - window.Height) / 2;
    }
}
