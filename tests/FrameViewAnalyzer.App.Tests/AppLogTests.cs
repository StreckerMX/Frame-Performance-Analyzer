using System.IO;
using FrameViewAnalyzer.App.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace FrameViewAnalyzer.App.Tests;

public class AppLogTests
{
    [Fact]
    public void Log_directory_is_under_local_app_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FrameViewAnalyzer",
            "logs");

        Assert.Equal(expected, AppLog.LogDirectory);
    }

    [Fact]
    public void Configuration_writes_to_the_configured_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = AppLog.CreateConfiguration(directory).CreateLogger();

            logger.Information("probe {Value}", 1);

            Assert.True(Directory.Exists(directory));
            Assert.NotEmpty(Directory.GetFiles(directory, "*.log"));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void No_remote_or_network_sink_is_referenced()
    {
        var referenced = typeof(AppLog).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        Assert.DoesNotContain("Serilog.Sinks.Http", referenced);
        Assert.DoesNotContain("Serilog.Sinks.Network", referenced);
        Assert.DoesNotContain("Serilog.Sinks.PeriodicBatching", referenced);
    }

    [Fact]
    public void Initialization_failure_falls_back_to_a_no_op_logger()
    {
        // A FILE blocks the directory path so Directory.CreateDirectory fails.
        var blocker = Path.Combine(Path.GetTempPath(), "fva-log-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "block");
        try
        {
            var invalid = Path.Combine(blocker, "logs");

            var logger = AppLog.Initialize(invalid);

            Assert.Same(Logger.None, logger);
        }
        finally
        {
            try
            {
                File.Delete(blocker);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void Controlled_failures_log_operation_and_type_without_paths()
    {
        var sink = new CollectingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            // The exception message contains a user capture path on purpose:
            // it must never reach the log through the controlled path.
            AppLog.ErrorOperation(
                "Benchmark package import",
                new IOException(@"D:\Users\me\captures\FrameView_Log.csv"));

            var evt = sink.Events.Single();
            Assert.Null(evt.Exception);
            Assert.Equal(
                "Benchmark package import",
                ((ScalarValue)evt.Properties["Operation"]).Value);
            Assert.Equal(
                nameof(IOException),
                ((ScalarValue)evt.Properties["ErrorType"]).Value);

            var rendered = evt.RenderMessage();
            Assert.DoesNotContain("captures", rendered);
            Assert.DoesNotContain(@"D:\Users", rendered);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }
    }

    [Fact]
    public void Shutdown_is_safe_and_idempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fva-log-close-" + Guid.NewGuid().ToString("N"));
        var previous = Log.Logger;
        try
        {
            Log.Logger = AppLog.Initialize(directory);
            Log.Information("probe");

            AppLog.Close();
            AppLog.Close();
        }
        finally
        {
            Log.Logger = previous;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
