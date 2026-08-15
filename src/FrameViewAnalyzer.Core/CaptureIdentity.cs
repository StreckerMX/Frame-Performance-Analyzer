namespace FrameViewAnalyzer.Core;

/// <summary>
/// Stable capture identity from cheap file attributes: name, size, and
/// modification time in nanoseconds. Moving a capture to another folder
/// keeps its identity; renaming or replacing the file creates a new one.
/// Mirrors the Python reference (no hashing of large files on every refresh).
/// </summary>
public static class CaptureIdentity
{
    public static string Build(string fileName, long sizeBytes, long lastWriteNanoseconds) =>
        $"{fileName}|{sizeBytes}|{lastWriteNanoseconds}";
}
