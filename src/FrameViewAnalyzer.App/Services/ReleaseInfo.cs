using System.Reflection;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Reads the product version for the UI and diagnostics. The authoritative
/// declaration lives in <c>Directory.Build.props</c> (VersionPrefix +
/// VersionSuffix → informational version <c>2.0.0-rc.1</c> for the release
/// candidate); nothing else in the application declares a version string.
/// </summary>
public static class ReleaseInfo
{
    public static string InformationalVersion { get; } =
        typeof(ReleaseInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "2.0.0-rc.1";
}
