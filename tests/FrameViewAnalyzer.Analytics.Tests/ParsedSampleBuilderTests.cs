using FrameViewAnalyzer.Analytics.Samples;

namespace FrameViewAnalyzer.Analytics.Tests;

public class ParsedSampleBuilderTests
{
    [Fact]
    public void Ascending_times_are_kept_in_row_order()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [
                ["0.0", "10.0", "80.0"],
                ["0.5", "12.0", "81.0"],
                ["1.0", "10.0", "80.0"],
                ["1.5", "11.0", "82.0"],
            ]);

        var samples = ParsedSampleBuilder.Build(capture);

        Assert.Equal([0.0, 0.5, 1.0, 1.5], samples.TimeSeconds);
        Assert.Equal([0, 1, 2, 3], samples.RowIndex);
        Assert.Equal([100.0, 1000.0 / 12.0, 100.0, 1000.0 / 11.0], samples.Fps);
    }

    [Fact]
    public void Unordered_times_are_stably_sorted()
    {
        // Two rows share time 1.0: the stable sort must keep their original
        // relative order exactly like the Python reference.
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [
                ["2.0", "10.0", "80.0"],
                ["1.0", "10.0", "80.0"],
                ["1.0", "20.0", "80.0"],
                ["0.0", "10.0", "80.0"],
            ]);

        var samples = ParsedSampleBuilder.Build(capture);

        Assert.Equal([0.0, 1.0, 1.0, 2.0], samples.TimeSeconds);
        Assert.Equal([3, 1, 2, 0], samples.RowIndex);
        Assert.Equal(10.0, samples.FrametimeMs[1]);
        Assert.Equal(20.0, samples.FrametimeMs[2]);
    }

    [Fact]
    public void Rows_without_a_valid_time_are_skipped()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            [
                ["0.0", "10.0", "80.0"],
                ["", "12.0", "81.0"],
                ["1.0", "10.0", "80.0"],
            ]);

        var samples = ParsedSampleBuilder.Build(capture);

        Assert.Equal([0.0, 1.0], samples.TimeSeconds);
        Assert.Equal([0, 2], samples.RowIndex);
    }

    [Fact]
    public void Empty_capture_produces_empty_samples()
    {
        var capture = TestCapture.CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)"],
            []);

        var samples = ParsedSampleBuilder.Build(capture);

        Assert.Equal(0, samples.Count);
    }
}
