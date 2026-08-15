using System.Text;
using FrameViewAnalyzer.Core.Models;
using FrameViewAnalyzer.Infrastructure.Csv;

namespace FrameViewAnalyzer.Infrastructure.Tests;

public class DetectKindTests
{
    private readonly FrameViewCsvReader _reader = new();

    [Fact]
    public void Detects_log_via_time_column() =>
        Assert.Equal(CsvKind.Log, _reader.DetectKind(["TimeInSeconds", "FPS"], string.Empty));

    [Fact]
    public void Detects_log_via_presents_column() =>
        Assert.Equal(CsvKind.Log, _reader.DetectKind(["MsBetweenPresents"], string.Empty));

    [Fact]
    public void Detects_summary_via_headers() =>
        Assert.Equal(CsvKind.Summary, _reader.DetectKind(["Log Name", "Avg FPS"], string.Empty));

    [Fact]
    public void Detects_summary_via_filename() =>
        Assert.Equal(CsvKind.Summary, _reader.DetectKind(["Application"], "FrameView_Summary.csv"));

    [Fact]
    public void Unknown_otherwise() =>
        Assert.Equal(CsvKind.Unknown, _reader.DetectKind(["Application"], "capture.csv"));
}

public class LoadCaptureTests
{
    private readonly FrameViewCsvReader _reader = new();

    [Fact]
    public async Task Load_strips_bom_and_trims_cells()
    {
        using var temp = new TempDirectory();
        // The content starts with a BOM character; the writer must not add
        // a second one or the first header would keep the leftover BOM.
        var path = temp.WriteUtf8(
            "FrameView_Test_Log.csv",
            "\uFEFFTimeInSeconds,MsBetweenPresents,GPU0Util(%)\n 0.0 , 10.0 , 80 \n");

        var capture = await _reader.LoadCaptureAsync(path);

        Assert.Equal(CsvKind.Log, capture.Kind);
        Assert.Equal("0.0", capture.Cell(capture.IndexOfHeader("TimeInSeconds"), 0));
        Assert.Equal("80", capture.Cell(capture.IndexOfHeader("GPU0Util(%)"), 0));
        Assert.Equal("Test", capture.DisplayName);
    }

    [Fact]
    public async Task Load_falls_back_to_windows_1252()
    {
        using var temp = new TempDirectory();
        // 0xB5 is valid cp1252 (micro sign) but invalid strict UTF-8.
        var content = Encoding.ASCII.GetBytes("TimeInSeconds,MsBetweenPresents,GPU0Util(%)\n0.0,10.0,80 ")
            .Concat(new byte[] { 0xB5 })
            .Concat(Encoding.ASCII.GetBytes("\n"))
            .ToArray();
        var path = temp.WriteBytes("FrameView_Log.csv", content);

        var capture = await _reader.LoadCaptureAsync(path);

        Assert.Equal(CsvKind.Log, capture.Kind);
        Assert.Equal("0.0", capture.Cell(capture.IndexOfHeader("TimeInSeconds"), 0));
        Assert.Equal("80 µ", capture.Cell(capture.IndexOfHeader("GPU0Util(%)"), 0));
    }

    [Fact]
    public async Task Load_is_lenient_with_exotic_bytes()
    {
        using var temp = new TempDirectory();
        // 0x81 is undefined in cp1252 (must fail) but valid latin-1.
        var content = Encoding.ASCII.GetBytes("TimeInSeconds,MsBetweenPresents\n0.0,10.0\n1.0,")
            .Concat(new byte[] { 0x81 })
            .Concat(Encoding.ASCII.GetBytes("\n"))
            .ToArray();
        var path = temp.WriteBytes("weird.csv", content);

        var capture = await _reader.LoadCaptureAsync(path);

        Assert.Equal(CsvKind.Log, capture.Kind);
        Assert.Equal("\u0081", capture.Cell(capture.IndexOfHeader("MsBetweenPresents"), 1));
    }

    [Fact]
    public async Task Load_accepts_empty_files()
    {
        using var temp = new TempDirectory();
        var path = temp.WriteUtf8("empty.csv", string.Empty);

        var capture = await _reader.LoadCaptureAsync(path);

        Assert.Equal(CsvKind.Unknown, capture.Kind);
        Assert.Empty(capture.Headers);
        Assert.Equal(0, capture.RowCount);
    }

    [Fact]
    public async Task Load_reports_missing_files()
    {
        using var temp = new TempDirectory();
        await Assert.ThrowsAnyAsync<IOException>(
            () => _reader.LoadCaptureAsync(System.IO.Path.Combine(temp.Path, "missing.csv")));
    }

    [Fact]
    public async Task Load_keeps_cells_as_strings_for_later_numeric_conversion()
    {
        using var temp = new TempDirectory();
        // Decimal commas arrive quoted inside the CSV; the loader must
        // preserve the raw cell text and never convert it.
        var path = temp.WriteUtf8(
            "FrameView_Test_Log.csv",
            "TimeInSeconds,Value\n0.0,\"12,5\"\n");

        var capture = await _reader.LoadCaptureAsync(path);

        Assert.Equal("12,5", capture.Cell(capture.IndexOfHeader("Value"), 0));
    }
}
