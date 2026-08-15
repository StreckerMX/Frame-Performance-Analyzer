using FrameViewAnalyzer.Core;

namespace FrameViewAnalyzer.Infrastructure;

/// <summary>
/// Builds the stable capture identity for a file path from its name, size,
/// and modification time. Returns null when the file cannot be inspected.
/// </summary>
public static class CaptureIdentityResolver
{
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string? TryBuild(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            var lastWriteNanoseconds = (long)((info.LastWriteTimeUtc - UnixEpoch).Ticks * 100);
            return CaptureIdentity.Build(info.Name, info.Length, lastWriteNanoseconds);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
