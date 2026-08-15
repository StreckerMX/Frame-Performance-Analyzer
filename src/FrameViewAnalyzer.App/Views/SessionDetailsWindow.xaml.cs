using System.Windows;
using FrameViewAnalyzer.App.ViewModels;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Read-only "Complete data" window for one analyzed session. It never
/// mutates session state; it only presents the snapshot built by
/// SessionDetailsViewModel.
/// </summary>
public partial class SessionDetailsWindow : Window
{
    public SessionDetailsWindow(SessionDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
    }
}
