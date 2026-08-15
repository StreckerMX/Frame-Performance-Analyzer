namespace FrameViewAnalyzer.Infrastructure;

/// <summary>
/// Windows known-folder paths used by the application. The default capture
/// folder mirrors the Python reference: Documents\FrameView.
/// </summary>
public static class PlatformFolders
{
    /// <summary>Default FrameView capture directory (Documents\FrameView).</summary>
    public static string FrameViewDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FrameView");

    /// <summary>Local app data root used by the stores and logs.</summary>
    public static string LocalAppData() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
