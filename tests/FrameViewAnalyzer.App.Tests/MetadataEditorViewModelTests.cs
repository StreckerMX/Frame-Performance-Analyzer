using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.App.ViewModels;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.Tests;

public class MetadataEditorViewModelTests
{
    private static SessionAnalysis SessionOf()
    {
        var capture = new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "Application", "Resolution"],
            Columns =
            [
                ["0.0", "0.25", "0.5", "0.75", "1.0"],
                ["10.0", "10.0", "10.0", "10.0", "10.0"],
                ["80.0", "80.0", "80.0", "80.0", "80.0"],
                ["Game.exe", "Game.exe", "Game.exe", "Game.exe", "Game.exe"],
                ["3840x2160", "3840x2160", "3840x2160", "3840x2160", "3840x2160"],
            ],
        };
        return new CaptureAnalysisService().Analyze(capture);
    }

    [Fact]
    public void Detected_values_prefill_the_empty_fields()
    {
        var editor = MetadataEditorViewModel.From(SessionOf(), null);

        Assert.Equal("Game", editor.Game);
        Assert.Equal("3840x2160", editor.Resolution);
        Assert.Equal(string.Empty, editor.BenchmarkName);
    }

    [Fact]
    public void Manual_values_override_the_detected_prefill()
    {
        var manual = new ManualMetadata(Game: "My Custom Run", Resolution: "1080p", Tags: ["cpu"]);

        var editor = MetadataEditorViewModel.From(SessionOf(), manual);

        Assert.Equal("My Custom Run", editor.Game);
        Assert.Equal("1080p", editor.Resolution);
        Assert.Equal("cpu", editor.TagsText);
    }

    [Fact]
    public void Build_metadata_trims_fields_and_splits_tags()
    {
        var editor = new MetadataEditorViewModel("title")
        {
            BenchmarkName = "  Run 1  ",
            Upscaler = " DLSS ",
            TagsText = " gpu ,  night,  ",
        };

        var metadata = editor.BuildMetadata();

        Assert.Equal("Run 1", metadata.BenchmarkName);
        Assert.Equal("DLSS", metadata.Upscaler);
        Assert.Equal(["gpu", "night"], metadata.Tags);
    }

    [Fact]
    public void Save_raises_the_built_metadata()
    {
        var editor = new MetadataEditorViewModel("title") { Game = "GTA V" };
        ManualMetadata? saved = null;
        editor.SaveRequested += (_, metadata) => saved = metadata;

        editor.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal("GTA V", saved!.Game);
    }

    [Fact]
    public void Cancel_raises_the_cancel_event()
    {
        var editor = new MetadataEditorViewModel("title");
        var cancelled = false;
        editor.CancelRequested += (_, _) => cancelled = true;

        editor.CancelCommand.Execute(null);

        Assert.True(cancelled);
    }
}
