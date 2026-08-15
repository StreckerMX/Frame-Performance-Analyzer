using System.IO;
using Serilog;
using Serilog.Core;
namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Minimal release-grade file logging (Serilog) to the approved destination
/// %LOCALAPPDATA%\FrameViewAnalyzer\logs\. Rolling daily, 10 MB size
/// rollover, 7-day retention, local-only (no remote/network sink, no
/// telemetry).
///
/// Privacy policy implemented here:
/// - The application does NOT intentionally log CSV contents, benchmark
///   metadata, or user capture paths. Expected/controlled failures (store
///   persistence, package import/export, legacy import) are logged via
///   <see cref="ErrorOperation"/> with only an operation name and the
///   exception TYPE — never the exception object, message, or stack.
/// - Unexpected/unhandled failures (dispatcher, AppDomain, unobserved task,
///   startup) keep the full exception for local diagnosis. Those diagnostics
///   MAY contain OS/.NET-provided details such as file paths.
/// - Logs remain local and are never transmitted by the application.
///
/// Logging must never prevent startup: when the log directory cannot be
/// created, initialization falls back to a no-op logger and the application
/// continues without logging.
/// </summary>
public static class AppLog
{
    /// <summary>%LOCALAPPDATA%\FrameViewAnalyzer\logs</summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrameViewAnalyzer",
        "logs");

    /// <summary>Builds the standard file-logging configuration (testable).</summary>
    public static LoggerConfiguration CreateConfiguration(string directory) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(directory, "frameview-analyzer-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

    /// <summary>
    /// Installs the process-wide logger. Never throws: when the log
    /// directory cannot be created, a no-op logger is returned and the
    /// failure is NOT logged (no recursion into the broken logger).
    /// </summary>
    public static ILogger Initialize() => Initialize(LogDirectory);

    /// <summary>Testable variant of <see cref="Initialize"/>.</summary>
    public static ILogger Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return CreateConfiguration(directory).CreateLogger();
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return Logger.None;
        }
    }

    /// <summary>
    /// Logs a controlled/expected application failure. Only the operation
    /// name and the exception TYPE are recorded; the exception object is
    /// never attached, so capture paths, CSV contents, and benchmark
    /// metadata can never reach the log through this path.
    /// </summary>
    public static void ErrorOperation(string operation, Exception error) =>
        Log.Error(
            "{Operation} failed ({ErrorType})",
            operation,
            error.GetType().Name);

    /// <summary>Flushes and releases the process-wide logger; safe to call repeatedly.</summary>
    public static void Close() => Log.CloseAndFlush();
}
