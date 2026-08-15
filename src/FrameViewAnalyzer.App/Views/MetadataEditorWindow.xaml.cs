using System.Windows;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Views;

/// <summary>
/// Modal editor for the manual benchmark metadata of one capture. Save
/// raises <see cref="Saved"/> and closes; Cancel just closes.
/// </summary>
public partial class MetadataEditorWindow : Window
{
    private readonly MetadataEditorViewModel _viewModel;

    public MetadataEditorWindow(SessionAnalysis session, ManualMetadata current)
    {
        InitializeComponent();
        _viewModel = MetadataEditorViewModel.From(session, current);
        DataContext = _viewModel;
        _viewModel.SaveRequested += (_, metadata) =>
        {
            Saved?.Invoke(metadata);
            Close();
        };
        _viewModel.CancelRequested += (_, _) => Close();
    }

    public event Action<ManualMetadata>? Saved;
}
