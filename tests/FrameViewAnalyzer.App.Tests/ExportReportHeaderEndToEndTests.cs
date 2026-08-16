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
/// End-to-end single-session header coverage through the REAL export path:
/// the actual ExportReportWindow is constructed, the user's radio + dropdown
/// selection is applied, the real Export button click fires the real
/// ExportRequested event, and the option that emerges from that pipeline
/// drives the same RoleLines helper MainWindow uses to build the final header
/// model passed to DrawHeader. The rendered PNG is then checked pixel-wise
/// for three visible logical rows (title, hardware, role line).
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
                new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", baseSession),
                new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", comparisonSession),
            };

            var window = new ExportReportWindow(options);
            try
            {
                window.Show();

                ExportScope? requestedScope = null;
                ExportSessionOption? requestedOption = null;
                window.ExportRequested += (scope, option) =>
                {
                    requestedScope = scope;
                    requestedOption = option;
                };

                ((RadioButton)window.FindName("SingleRadio")).IsChecked = true;
                ((ComboBox)window.FindName("SessionOptions")).SelectedIndex = 0;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                // The real click fired the real event; the option that
                // emerges carries the authoritative role for the header.
                Assert.Equal(ExportScope.Single, requestedScope);
                Assert.NotNull(requestedOption);
                Assert.Equal(SessionRole.Base, requestedOption!.Role);
                Assert.Same(baseSession, requestedOption.Session);

                var lines = ExportReport.RoleLines(
                    requestedScope!.Value,
                    baseSession,
                    comparisonSession,
                    requestedOption);
                var line = Assert.Single(lines);
                Assert.StartsWith("Base:", line, StringComparison.Ordinal);
                Assert.Contains(ExportReport.SessionExportLabel(baseSession), line);
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
                new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", baseSession),
                new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", comparisonSession),
            };

            var window = new ExportReportWindow(options);
            try
            {
                window.Show();

                ExportScope? requestedScope = null;
                ExportSessionOption? requestedOption = null;
                window.ExportRequested += (scope, option) =>
                {
                    requestedScope = scope;
                    requestedOption = option;
                };

                ((RadioButton)window.FindName("SingleRadio")).IsChecked = true;
                ((ComboBox)window.FindName("SessionOptions")).SelectedIndex = 1;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal(ExportScope.Single, requestedScope);
                Assert.NotNull(requestedOption);
                Assert.Equal(SessionRole.Comparison, requestedOption!.Role);
                Assert.Same(comparisonSession, requestedOption.Session);

                var lines = ExportReport.RoleLines(
                    requestedScope!.Value,
                    baseSession,
                    comparisonSession,
                    requestedOption);
                var line = Assert.Single(lines);
                Assert.StartsWith("Comparison:", line, StringComparison.Ordinal);
                Assert.Contains(ExportReport.SessionExportLabel(comparisonSession), line);
                Assert.DoesNotContain("Base:", line, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void All_sessions_export_keeps_both_role_lines() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var baseSession = Session("2560x1440");
            var comparisonSession = Session("3840x2160");
            var window = new ExportReportWindow(
            [
                new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", baseSession),
                new ExportSessionOption(SessionRole.Comparison, "GTA5 Enhanced", comparisonSession),
            ]);
            try
            {
                window.Show();

                ExportScope? requestedScope = null;
                window.ExportRequested += (scope, _) => requestedScope = scope;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal(ExportScope.All, requestedScope);
                var lines = ExportReport.RoleLines(
                    ExportScope.All,
                    baseSession,
                    comparisonSession,
                    selected: null);
                Assert.Equal(2, lines.Count);
                Assert.StartsWith("Base:", lines[0], StringComparison.Ordinal);
                Assert.StartsWith("Comparison:", lines[1], StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void All_sessions_export_with_only_a_base_loaded_identifies_the_base() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var baseSession = Session("2560x1440");
            var window = new ExportReportWindow(
                [new ExportSessionOption(SessionRole.Base, "GTA5 Enhanced", baseSession)]);
            try
            {
                window.Show();

                ExportScope? requestedScope = null;
                window.ExportRequested += (scope, _) => requestedScope = scope;
                ((Button)window.FindName("ExportButton"))
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                Assert.Equal(ExportScope.All, requestedScope);
                var lines = ExportReport.RoleLines(
                    ExportScope.All,
                    baseSession,
                    comparisonSession: null,
                    selected: null);
                var line = Assert.Single(lines);
                Assert.StartsWith("Base:", line, StringComparison.Ordinal);
                Assert.DoesNotContain("Comparison:", line, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// Renders the final header model through the real report renderer and
    /// verifies three logical rows are actually visible: title, metadata
    /// line, and role line.
    /// </summary>
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

            // Same font-metric math as DrawHeader, so each expected text row
            // maps to a concrete band in the rendered artifact.
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
