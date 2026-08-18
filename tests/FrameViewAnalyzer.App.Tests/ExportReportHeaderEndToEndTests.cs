using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Analytics.Exports;
using FrameViewAnalyzer.Analytics.Series;
using FrameViewAnalyzer.App.Charting;
using FrameViewAnalyzer.App.Views;
using FrameViewAnalyzer.Core;
using FrameViewAnalyzer.Core.Models;
using SkiaSharp;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// End-to-end header coverage through the real checklist export dialog. The
/// selected ExportSessionOption objects carry the authoritative roles and
/// names that MainWindow uses for the final report header.
/// </summary>
public class ExportReportHeaderEndToEndTests
{
    [Fact]
    public void Selected_base_export_produces_a_visible_base_role_line() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var baseSession = Session("2560x1440");
            var comparisonSession = Session("3840x2160");
            var options = new[]
            {
                new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced Base", baseSession),
                new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced Comparison", comparisonSession),
            };

            var window = new ExportReportWindow(options, baseSession.Catalog);
            try
            {
                window.Show();
                var checklist = SessionItems(window);
                checklist[1].IsSelected = false;

                ExportReportSelection? requested = null;
                window.ExportRequested += selection => requested = selection;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.NotNull(requested);
                var selected = Assert.Single(requested!.Sessions);
                Assert.Equal(SessionRole.Base, selected.Role);
                Assert.Same(baseSession, selected.Session);
                Assert.Equal(["fps"], requested.MetricIds);

                var line = ExportReport.SessionRoleLine(selected.Role, selected.DisplayName);
                Assert.StartsWith("Base:", line, StringComparison.Ordinal);
                Assert.DoesNotContain("Comparison:", line, StringComparison.Ordinal);

                AssertRenderedHeaderHasThreeVisibleRows(
                    headerLines: [HardwareLine(baseSession), line],
                    title: "GTA5 Enhanced",
                    baseSession);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Selected_comparison_export_produces_a_visible_comparison_role_line() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var baseSession = Session("2560x1440");
            var comparisonSession = Session("3840x2160");
            var options = new[]
            {
                new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced Base", baseSession),
                new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced Comparison", comparisonSession),
            };

            var window = new ExportReportWindow(options, baseSession.Catalog);
            try
            {
                window.Show();
                var checklist = SessionItems(window);
                checklist[0].IsSelected = false;

                ExportReportSelection? requested = null;
                window.ExportRequested += selection => requested = selection;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.NotNull(requested);
                var selected = Assert.Single(requested!.Sessions);
                Assert.Equal(SessionRole.Comparison, selected.Role);
                Assert.Same(comparisonSession, selected.Session);

                var line = ExportReport.SessionRoleLine(selected.Role, selected.DisplayName);
                Assert.StartsWith("Comparison:", line, StringComparison.Ordinal);
                Assert.DoesNotContain("Base:", line, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Default_export_keeps_both_selected_role_lines() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var baseSession = Session("2560x1440");
            var comparisonSession = Session("3840x2160");
            var window = new ExportReportWindow(
            [
                new ExportSessionOption(SessionRole.Base, "Base run", baseSession),
                new ExportSessionOption(SessionRole.Comparison, "Comparison run", comparisonSession),
            ],
            baseSession.Catalog);
            try
            {
                window.Show();
                ExportReportSelection? requested = null;
                window.ExportRequested += selection => requested = selection;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.NotNull(requested);
                Assert.Equal(2, requested!.Sessions.Count);
                var lines = requested.Sessions
                    .Select(option => ExportReport.SessionRoleLine(option.Role, option.DisplayName))
                    .ToList();
                Assert.StartsWith("Base:", lines[0], StringComparison.Ordinal);
                Assert.StartsWith("Comparison:", lines[1], StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void Base_only_default_export_identifies_the_base() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var baseSession = Session("2560x1440");
            var window = new ExportReportWindow(
                [new ExportSessionOption(SessionRole.Base, "Base run", baseSession)],
                baseSession.Catalog);
            try
            {
                window.Show();
                ExportReportSelection? requested = null;
                window.ExportRequested += selection => requested = selection;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                var selected = Assert.Single(requested!.Sessions);
                var line = ExportReport.SessionRoleLine(selected.Role, selected.DisplayName);
                Assert.StartsWith("Base:", line, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    private static IReadOnlyList<ExportSessionChecklistItem> SessionItems(ExportReportWindow window) =>
        Assert.IsAssignableFrom<IReadOnlyList<ExportSessionChecklistItem>>(
            ((ItemsControl)window.FindName("SessionChecklist")).ItemsSource);

    private static void AssertRenderedHeaderHasThreeVisibleRows(
        IReadOnlyList<string> headerLines,
        string title,
        SessionAnalysis baseSession)
    {
        Assert.Equal(2, headerLines.Count);

        var header = new ReportPlotBuilder.ReportHeader(title, headerLines);
        var baseSeries = SeriesBuilder.Build(baseSession, "fps") with { Role = SessionRole.Base };
        var group = new ReportPlotBuilder.ReportGroup(
            baseSession.Catalog.First(metric => metric.Id == "fps"),
            [baseSeries]);

        var style = ChartStyle.FromApplicationResources();
        var multiplot = ReportPlotBuilder.Build([group], style);
        var headerHeight = ReportPlotBuilder.MeasureHeaderHeight(header);

        var path = Path.Combine(Path.GetTempPath(), "fva-hdre2e-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            ReportPlotBuilder.SavePng(multiplot, style, header, path, 1600, headerHeight + 520);

            using var bitmap = SKBitmap.Decode(path);
            Assert.NotNull(bitmap);

            var titleHeight = LineHeight(22, bold: true);
            var lineHeight = LineHeight(14, bold: false);
            var linesTop = 16 + titleHeight + 12;

            Assert.True(
                CountBrightRows(bitmap, 16, 16 + titleHeight) > 0,
                "Title row is not visible in the rendered header.");
            Assert.True(
                CountBrightRows(bitmap, linesTop, linesTop + lineHeight) > 0,
                "Metadata row is not visible in the rendered header.");
            Assert.True(
                CountBrightRows(bitmap, linesTop + lineHeight, linesTop + 2 * lineHeight) > 0,
                "Role line is not visible in the rendered header.");
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static int LineHeight(float size, bool bold)
    {
        using var font = new SKFont
        {
            Size = size,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", bold ? SKFontStyle.Bold : SKFontStyle.Normal),
        };
        var metrics = font.Metrics;
        return (int)Math.Ceiling(metrics.Descent - metrics.Ascent) + 2;
    }

    private static int CountBrightRows(SKBitmap bitmap, int yStart, int yEnd)
    {
        var count = 0;
        for (var y = yStart; y < yEnd && y < bitmap.Height; y++)
        {
            for (var x = 10; x < bitmap.Width; x += 6)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red > 8 || pixel.Green > 8 || pixel.Blue > 8)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static string HardwareLine(SessionAnalysis session)
    {
        var metadata = session.Metadata!;
        return string.Join("  ·  ", new[] { metadata.Resolution, metadata.Gpu, metadata.Cpu });
    }

    private static SessionAnalysis Session(string resolution)
    {
        var rows = new List<string[]>();
        for (var second = 0; second < 120; second++)
        {
            var fps = 72.0 + (140.0 - 72.0) * second / 119.0;
            var frameMs = 1000.0 / fps;
            for (var frame = 0; frame < 4; frame++)
            {
                rows.Add(
                [
                    (second + frame * 0.25).ToString("F2"),
                    frameMs.ToString("F3"),
                    "80.0",
                    "GTA5 Enhanced",
                    resolution,
                    "RTX 4090",
                    "Ryzen 7",
                ]);
            }
        }

        var capture = CaptureWith(
            ["TimeInSeconds", "MsBetweenPresents", "GPU0Util(%)", "Application", "Resolution", "GPU", "CPU"],
            [.. rows]);

        return new CaptureAnalysisService().Analyze(
            capture,
            new AnalysisOptions(
                GpuThreshold: 25,
                TrimBufferSeconds: 1,
                AutoGpuThreshold: false,
                ExcludeTransitions: false));
    }

    private static CaptureData CaptureWith(string[] headers, string[][] rows)
    {
        var columns = new string[headers.Length][];
        for (var i = 0; i < headers.Length; i++)
        {
            columns[i] = new string[rows.Length];
            for (var r = 0; r < rows.Length; r++)
            {
                columns[i][r] = rows[r][i];
            }
        }

        return new CaptureData
        {
            Path = "capture.csv",
            DisplayName = "capture",
            Kind = CsvKind.Log,
            Headers = headers,
            Columns = columns,
        };
    }
}
