using System.Windows;
using FrameViewAnalyzer.App.Services;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App;

public partial class MainWindow : Window
{
    private readonly IWindowPlacementService _placement;

    public MainWindow(MainWindowViewModel viewModel, IWindowPlacementService placement)
    {
        InitializeComponent();
        _placement = placement;
        DataContext = viewModel;

        // Restore once the native window exists; save on every close.
        SourceInitialized += (_, _) => _placement.Restore(this);
        Closing += (_, _) => _placement.Save(this);
    }
}