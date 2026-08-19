using System.IO;
using System.Text.RegularExpressions;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression guard for the busy presentation contract: <c>BusyStatusBar</c>
/// appends the animated dots itself, so every real operation message passed
/// to BusyState.Begin / RunAsync / RunOnThreadPoolAsync must end dot-free.
/// The test scans the production sources of the App project instead of
/// repeating the message list, so a newly added dotted message fails
/// immediately.
/// </summary>
public class BusyMessageTests
{
    [Fact]
    public void Production_busy_operation_messages_never_end_in_a_dot()
    {
        var appSource = FindAppSourceDirectory();
        var offenders = new List<(string File, int Line, string Message)>();
        var totalCallSites = 0;

        foreach (var file in Directory.EnumerateFiles(appSource, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(@"\obj\", StringComparison.Ordinal)
                || file.Contains(@"\bin\", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in CallSite.Matches(text))
            {
                totalCallSites++;
                var message = match.Groups["message"].Value;
                if (message.EndsWith('.'))
                {
                    var line = text[..match.Index].Count(character => character == '\n') + 1;
                    offenders.Add((Path.GetFileName(file), line, message));
                }
            }
        }

        Assert.True(
            totalCallSites > 0,
            "No BusyState call sites were found; the production source scan is broken.");
        Assert.True(
            offenders.Count == 0,
            "Busy operation messages must not end in '.' because BusyStatusBar appends the animated dots:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                offenders.Select(offender => $"  {offender.File}:{offender.Line} — \"{offender.Message}\"")));
    }

    /// <summary>
    /// Walks up from the test output directory to the solution root and
    /// returns the App project's source directory.
    /// </summary>
    private static string FindAppSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FrameViewAnalyzer.sln")))
            {
                return Path.Combine(directory.FullName, "src", "FrameViewAnalyzer.App");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate FrameViewAnalyzer.sln above the test output directory.");
    }

    private static readonly Regex CallSite = new(
        @"\.(?:Begin|RunAsync|RunOnThreadPoolAsync)\s*(?:<[^>]*>)?\s*\(\s*""(?<message>[^""]*)""",
        RegexOptions.Compiled);
}
