using System.Reflection;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.App.Services;

namespace FrameViewAnalyzer.App.Tests;

public class ReleaseInfoTests
{
    [Fact]
    public void Informational_version_is_the_stable_semver()
    {
        // The single authoritative version source is Directory.Build.props.
        Assert.Equal("3.2.3", ReleaseInfo.InformationalVersion);
    }

    [Fact]
    public void Assembly_informational_version_matches_the_release_info_source()
    {
        var assembly = typeof(ReleaseInfo).Assembly;
        var attribute = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(ReleaseInfo.InformationalVersion, attribute!.InformationalVersion);
    }

    [Fact]
    public void Public_assembly_metadata_uses_the_product_brand()
    {
        var assembly = typeof(ReleaseInfo).Assembly;

        Assert.Equal(
            "Frame Performance Analyzer",
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
        Assert.Equal(
            "Frame Performance Analyzer",
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
    }

    [Fact]
    public void App_user_model_id_is_stable_and_machine_independent()
    {
        // Deterministic, no personal information, no paths.
        Assert.Equal("StreckerMX.FrameViewAnalyzer", AppUserModelId.Value);
        Assert.DoesNotContain('\\', AppUserModelId.Value);
        Assert.DoesNotContain(':', AppUserModelId.Value);
    }

    [Fact]
    public void Portable_analysis_format_version_remains_backward_compatible()
    {
        Assert.Equal(2, PortableAnalysisExport.FormatVersion);
    }

    [Fact]
    public void Applying_the_app_user_model_id_does_not_throw()
    {
        var exception = Record.Exception(AppUserModelId.ApplyToCurrentProcess);

        Assert.Null(exception);
    }
}
