using System.Reflection;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Reads the product version for the UI and diagnostics. The authoritative
/// declaration lives in <c>Directory.Build.props</c> (VersionPrefix →
/// informational version <c>3.1.4</c>); nothing else in the application
/// declares a version string.
/// </summary>
public static class ReleaseInfo
{
    public static string InformationalVersion { get; } =
        typeof(ReleaseInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "3.1.4";
}
