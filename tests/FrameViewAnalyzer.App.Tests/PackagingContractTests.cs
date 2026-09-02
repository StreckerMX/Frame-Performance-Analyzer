using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace FrameViewAnalyzer.App.Tests;

public class PackagingContractTests
{
    private const string PublicName = "Frame Performance Analyzer";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Store_identity_matches_the_partner_center_product()
    {
        using var document = JsonDocument.Parse(Read("packaging/store/StoreIdentity.json"));
        var identity = document.RootElement;

        Assert.Equal(5, identity.EnumerateObject().Count());
        Assert.Equal("Strecker.FrameViewAnalyzer", identity.GetProperty("PackageIdentityName").GetString());
        Assert.Equal(
            "CN=A37E4A45-43E1-42F1-866D-B4B9249062DE",
            identity.GetProperty("Publisher").GetString());
        Assert.Equal("Strecker", identity.GetProperty("PublisherDisplayName").GetString());
        Assert.Equal(
            "Strecker.FrameViewAnalyzer_9aqbg1gb4p26y",
            identity.GetProperty("PackageFamilyName").GetString());
        Assert.Equal("9P49TT4BJ798", identity.GetProperty("StoreId").GetString());
    }

    [Fact]
    public void Product_and_store_versions_are_derived_from_the_release_source()
    {
        var props = XDocument.Parse(Read("Directory.Build.props"));
        var version = props.Descendants("VersionPrefix").Single().Value;

        Assert.Equal("3.2.3", version);
        Assert.Equal("3.2.3.0", $"{version}.0");
        Assert.Equal(PublicName, props.Descendants("Product").Single().Value);
        Assert.Equal(PublicName, props.Descendants("AssemblyTitle").Single().Value);
        Assert.Equal("3.2.3.0", props.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal("3.2.3.0", props.Descendants("FileVersion").Single().Value);
    }

    [Fact]
    public void Store_manifest_preserves_identity_placeholders_and_public_brand()
    {
        var manifest = Read("packaging/store/AppxManifest.xml.template");

        Assert.Contains("Name=\"__PACKAGE_IDENTITY_NAME__\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Publisher=\"__PACKAGE_PUBLISHER__\"", manifest, StringComparison.Ordinal);
        Assert.Contains("Version=\"__PACKAGE_VERSION__\"", manifest, StringComparison.Ordinal);
        Assert.Contains($"<DisplayName>{PublicName}</DisplayName>", manifest, StringComparison.Ordinal);
        Assert.Contains($"DisplayName=\"{PublicName}\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Github_release_contract_uses_public_artifact_names()
    {
        var packageScript = Read("scripts/package-release.ps1");
        var releaseWorkflow = Read(".github/workflows/release.yml");
        var ciWorkflow = Read(".github/workflows/ci.yml");

        Assert.Contains("FramePerformanceAnalyzer-v$version-win-x64.zip", packageScript, StringComparison.Ordinal);
        Assert.Contains("FramePerformanceAnalyzer.exe", packageScript, StringComparison.Ordinal);
        Assert.Contains("FramePerformanceAnalyzer-v$env:VERSION-win-x64.zip", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("FramePerformanceAnalyzer-v$version-win-x64.zip", ciWorkflow, StringComparison.Ordinal);
        Assert.Contains("FramePerformanceAnalyzer.exe", ciWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Store_build_contract_uses_public_artifact_name_and_version()
    {
        var storeScript = Read("scripts/build-store-msix.ps1");
        var storeWorkflow = Read(".github/workflows/store-msix-ci.yml");

        Assert.Contains("FramePerformanceAnalyzer-Store-$PackageVersion-x64.msix", storeScript, StringComparison.Ordinal);
        Assert.Contains("FramePerformanceAnalyzer-Store-$packageVersion-x64.msix", storeWorkflow, StringComparison.Ordinal);
        Assert.Contains("${version}.0", storeWorkflow, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        foreach (var startingPath in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
