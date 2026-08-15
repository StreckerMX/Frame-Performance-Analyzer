using System.Windows;

namespace FrameViewAnalyzer.App.Services;

/// <summary>Restores and persists the main-window placement.</summary>
public interface IWindowPlacementService
{
    void Restore(Window window);

    void Save(Window window);
}
