namespace FrameViewAnalyzer.App;

public partial class MainWindow
{
    /// <summary>
    /// Reuses the same transactional Multi loader used by the folder-backed
    /// selector when Benchmark Library sends a checked set of capture paths.
    /// </summary>
    internal Task LoadMultiBenchmarksFromLibraryAsync(IReadOnlyList<string> paths) =>
        _viewModel.LoadMultiBenchmarksAsync(paths);
}
