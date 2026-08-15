using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.Core.Metrics;

/// <summary>
/// FrameView metric catalog: core definitions, descriptions, per-metric
/// statistic fields, direction labels, and improvement classification.
/// Mirrors the Python reference catalog exactly.
/// </summary>
public static class CoreMetricCatalog
{
    public static readonly IReadOnlyList<string> TimeColumnKeys =
    [
        "TimeInSeconds",
        "Timestamp (Elapsed time in seconds)",
    ];

    public static readonly IReadOnlySet<string> SkipColumns = new HashSet<string>(StringComparer.Ordinal)
    {
        "Application",
        "GPU",
        "CPU",
        "Resolution",
        "Runtime",
        "AllowsTearing",
        "ProcessID",
        "SwapChainAddress",
        "SyncInterval",
        "PresentFlags",
        "PresentMode",
        "FlipToken",
        "TimeStamp",
        "Log Name",
        "OS",
        "GPU Base Driver",
        "GPU Driver Package",
        "System RAM",
        "Motherboard",
        "GPU0",
        "GPU1",
    };

    public static readonly IReadOnlyList<MetricDefinition> CoreMetrics =
    [
        new("fps", "FPS (Calculated)", "FPS", "Performance", [], MetricDirection.HigherIsBetter, Computed: true),
        new("frametime", "Frame time", "ms", "Performance", ["MsBetweenPresents", "MsBetweenDisplayChange"], MetricDirection.LowerIsBetter),
        new("latency", "PC Latency", "ms", "Latency", ["MsPCLatency", "Average PC Latency(MSec)", "AvgPCLatency (ms)"], MetricDirection.LowerIsBetter),
        new("fg_multiplier", "Frame Gen Multiplier", "x", "Performance", ["Frame Gen Multiplier"], MetricDirection.Undefined),
        new("render_present_latency", "Render Present Latency", "ms", "Latency", ["MsRenderPresentLatency", "RenderPresentLatency (ms)"], MetricDirection.LowerIsBetter),
        new("until_displayed", "Time Until Displayed", "ms", "Latency", ["MsUntilDisplayed"], MetricDirection.LowerIsBetter),
        new("in_present_api", "Time in Present API", "ms", "Latency", ["MsInPresentAPI"], MetricDirection.LowerIsBetter),
        new("flip_delay", "Flip Delay", "ms", "Latency", ["MsFlipDelay"], MetricDirection.LowerIsBetter),
        new("simulation_start", "Time Between Simulation Starts", "ms", "Performance", ["MsBetweenSimulationStart"], MetricDirection.LowerIsBetter),
        new("display_change", "Time Between Display Changes", "ms", "Performance", ["MsBetweenDisplayChange"], MetricDirection.LowerIsBetter),
        new("render_queue_depth", "Render Queue Depth", "", "Performance", ["Render Queue Depth"], MetricDirection.Undefined),
        new("dropped", "Frames Dropped", "", "Performance", ["Dropped"], MetricDirection.LowerIsBetter),
        new("gpu0_util", "GPU0 Utilization", "%", "GPU", ["GPU0Util(%)", "GPU0 Util%", "GPU Utilization(%)"], MetricDirection.Undefined),
        new("gpu0_clk", "GPU0 Clock", "MHz", "GPU", ["GPU0Clk(MHz)"], MetricDirection.Undefined),
        new("gpu0_mem_clk", "GPU0 Mem Clock", "MHz", "GPU", ["GPU0MemClk(MHz)"], MetricDirection.Undefined),
        new("gpu0_temp", "GPU0 Temperature", "°C", "GPU", ["GPU0Temp(C)", "GPU0 Temp (C)", "GPU Temperature(Degrees celsius)"], MetricDirection.LowerIsBetter),
        new("gpu1_util", "GPU1 Utilization", "%", "GPU", ["GPU1Util(%)", "GPU1 Util%", "GPU1 Utilization(%)"], MetricDirection.Undefined),
        new("gpu1_clk", "GPU1 Clock", "MHz", "GPU", ["GPU1Clk(MHz)"], MetricDirection.Undefined),
        new("gpu1_mem_clk", "GPU1 Mem Clock", "MHz", "GPU", ["GPU1MemClk(MHz)"], MetricDirection.Undefined),
        new("gpu1_temp", "GPU1 Temperature", "°C", "GPU", ["GPU1Temp(C)", "GPU1 Temp (C)", "GPU1 Temperature(Degrees celsius)"], MetricDirection.LowerIsBetter),
        new("nv_power", "NV GPU Power", "W", "Power", ["NV Pwr(W) (API)", "GPU NV Power (Watts) (API)"], MetricDirection.LowerIsBetter),
        new("gpu_only_power", "GPU Only Power", "W", "Power", ["GPUOnlyPwr(W) (API)"], MetricDirection.LowerIsBetter),
        new("pcat_power", "PCAT Total Power", "W", "Power", ["PCAT Power Total(W)", "PCAT Power (Watts)"], MetricDirection.LowerIsBetter),
        new("perf_w_api", "Performance per Watt (API)", "F/J", "Power", ["Perf/W Total(F/J) (API)", "Perf/W (F/J) (PCAT)"], MetricDirection.HigherIsBetter),
        new("cpu_util", "CPU Utilization", "%", "CPU", ["CPUUtil(%)", "CPU Util %", "CPU Utilization(%)"], MetricDirection.Undefined),
        new("cpu_clk", "CPU Clock", "MHz", "CPU", ["CPUClk(MHz)"], MetricDirection.Undefined),
        new("cpu_temp", "CPU Temperature", "°C", "CPU", ["CPU Package Temp(C)", "CPU Temp (C)"], MetricDirection.LowerIsBetter),
        new("cpu_power", "CPU Package Power", "W", "CPU", ["CPU Package Power(W)", "CPU Package Power(Watts)"], MetricDirection.LowerIsBetter),
        new("battery_drain", "Battery Drain Rate", "W", "Power", ["Battery Drain Rate(W)"], MetricDirection.LowerIsBetter),
    ];

    public static readonly IReadOnlyDictionary<string, MetricDefinition> CoreById =
        CoreMetrics.ToDictionary(metric => metric.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fps"] = "Estimated frames per second based on frame times.",
            ["frametime"] = "Time between rendered frames. Low, stable values produce smoother motion.",
            ["latency"] = "Internal PC latency from input processing until the frame is sent to the display; it does not include mouse or monitor latency.",
            ["fg_multiplier"] = "Frame Generation multiplier. A value of 1 is native rendering; 2, 3, or 4 indicates active frame generation.",
            ["render_present_latency"] = "Time from entering the Present queue until the GPU executes that presentation.",
            ["until_displayed"] = "Time from the Present call until the frame reaches the display.",
            ["in_present_api"] = "Time the application spends inside the graphics API Present call.",
            ["flip_delay"] = "Delay associated with the flip before the frame is presented.",
            ["simulation_start"] = "Time between consecutive game-simulation starts.",
            ["display_change"] = "Time between displayed image changes; this represents what the user ultimately sees.",
            ["render_queue_depth"] = "Maximum number of pre-rendered frames waiting in the queue.",
            ["dropped"] = "Dropped-frame indicator: 1 means dropped and 0 means displayed. The chart shows the average proportion per second.",
            ["gpu0_util"] = "Utilization of the primary GPU during the capture.",
            ["gpu0_clk"] = "Primary GPU core frequency while the benchmark was running.",
            ["gpu0_mem_clk"] = "Primary GPU memory frequency during the capture.",
            ["gpu0_temp"] = "Primary GPU temperature during the benchmark.",
            ["gpu1_util"] = "Utilization of the second GPU when the system reports one.",
            ["gpu1_clk"] = "Second GPU core frequency.",
            ["gpu1_mem_clk"] = "Second GPU memory frequency.",
            ["gpu1_temp"] = "Second GPU temperature.",
            ["nv_power"] = "Total NVIDIA graphics-card power reported by NVAPI.",
            ["gpu_only_power"] = "Power consumed by the GPU chip after the voltage regulator.",
            ["pcat_power"] = "Total graphics-card power measured by NVIDIA PCAT hardware.",
            ["perf_w_api"] = "GPU efficiency measured as frames produced per joule consumed.",
            ["cpu_util"] = "Total CPU utilization during the capture.",
            ["cpu_clk"] = "Average CPU frequency during the benchmark.",
            ["cpu_temp"] = "CPU package temperature during the capture.",
            ["cpu_power"] = "Total CPU package power consumption.",
            ["battery_drain"] = "Battery discharge rate; it can be negative while a laptop is discharging.",
        };

    private static readonly IReadOnlySet<string> HighTailMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        "frametime",
        "latency",
        "render_present_latency",
        "until_displayed",
        "in_present_api",
        "flip_delay",
    };

    private static readonly IReadOnlySet<string> AverageRangeMetrics = new HashSet<string>(StringComparer.Ordinal)
    {
        "gpu0_util",
        "gpu1_util",
        "cpu_util",
        "gpu0_temp",
        "gpu1_temp",
        "cpu_temp",
    };

    /// <summary>
    /// Statistic fields shown for one metric, in display order.
    /// Latency-style metrics use the high tail ("1% High"); utilizations and
    /// temperatures use a plain average/range; everything else uses the low
    /// tail ("1% Low").
    /// </summary>
    public static IReadOnlyList<(string Key, string Label)> StatFields(string metricId)
    {
        if (HighTailMetrics.Contains(metricId))
        {
            return
            [
                ("avg", "Average"),
                ("p1", "1% High"),
                ("p01", "0.1% High"),
                ("max", "Peak"),
                ("min", "Minimum"),
            ];
        }

        if (metricId == "fg_multiplier")
        {
            return
            [
                ("avg", "Average"),
                ("min", "Minimum"),
                ("max", "Maximum"),
            ];
        }

        if (AverageRangeMetrics.Contains(metricId))
        {
            return
            [
                ("avg", "Average"),
                ("max", "Peak"),
                ("min", "Minimum"),
            ];
        }

        return
        [
            ("avg", "Average"),
            ("p1", "1% Low"),
            ("p01", "0.1% Low"),
            ("max", "Maximum"),
            ("min", "Minimum"),
        ];
    }

    public static string DescriptionFor(MetricDefinition metric) =>
        Descriptions.TryGetValue(metric.Id, out var description)
            ? description
            : GeneratedDescription(metric.Label);

    public static string SourceFor(MetricDefinition metric, IReadOnlyList<string>? headers)
    {
        if (metric.Id == "fps")
        {
            var resolved = CoreById["frametime"].ResolveColumn(headers ?? []);
            var source = resolved ?? "MsBetweenPresents / MsBetweenDisplayChange";
            return $"{source} → 1000 × frames / frame-time sum";
        }

        if (headers is not null)
        {
            var column = metric.ResolveColumn(headers);
            if (column is not null)
            {
                return column;
            }
        }

        return metric.ColumnKeys.Count > 0
            ? string.Join(" / ", metric.ColumnKeys)
            : "Calculated value";
    }

    public static string DirectionLabelFor(MetricDefinition metric) => metric.Direction switch
    {
        MetricDirection.HigherIsBetter => "Higher is usually better",
        MetricDirection.LowerIsBetter => "Lower is usually better",
        _ => "Interpret according to context",
    };

    public static string ChartExplanationFor(MetricDefinition metric)
    {
        var baseText = metric.Id == "fps"
            ? "Each point uses every frame from a one-second interval; it is not a simple average of per-frame FPS values."
            : "Each point is the average of the frames recorded within a one-second interval.";
        return $"{baseText} Excluded loads appear as gaps, and statistics follow the visible zoom range.";
    }

    public static string StatisticsExplanationFor(string metricId)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Average"] = "mean of the active segment",
            ["1% Low"] = "lower tail that reveals stutter",
            ["0.1% Low"] = "extreme stutter tail",
            ["1% High"] = "upper latency or frame-time tail",
            ["0.1% High"] = "extreme peaks",
            ["Peak"] = "maximum value",
            ["Maximum"] = "maximum value",
            ["Minimum"] = "minimum value",
        };

        return string.Join(
            " · ",
            StatFields(metricId).Select(field =>
                $"{field.Label}: {(definitions.TryGetValue(field.Label, out var value) ? value : field.Label.ToLowerInvariant())}"));
    }

    /// <summary>
    /// Classifies a comparison as improvement, regression, or neutral from
    /// the metric's direction semantics — never from the delta sign alone.
    /// </summary>
    public static ImprovementKind ClassifyImprovement(
        MetricDirection direction,
        double? baseValue,
        double? comparisonValue)
    {
        if (direction == MetricDirection.Undefined
            || baseValue is null
            || comparisonValue is null)
        {
            return ImprovementKind.None;
        }

        var delta = comparisonValue.Value - baseValue.Value;
        if (delta == 0)
        {
            return ImprovementKind.None;
        }

        return (delta > 0) == (direction == MetricDirection.HigherIsBetter)
            ? ImprovementKind.Improvement
            : ImprovementKind.Regression;
    }

    private static string GeneratedDescription(string label)
    {
        var upper = label.ToUpperInvariant();
        if (upper.Contains("CPUCOREUTIL"))
        {
            return "Utilization of the logical CPU core identified by the column name.";
        }

        if (upper.Contains("UTIL"))
        {
            return "Utilization level of the identified component during the benchmark.";
        }

        if (upper.Contains("TEMP"))
        {
            return "Temperature recorded by the identified sensor during the capture.";
        }

        if (upper.Contains("CLK") || upper.Contains("CLOCK"))
        {
            return "Operating frequency recorded during the benchmark.";
        }

        if (upper.Contains("PWR") || upper.Contains("POWER"))
        {
            return "Power recorded by the source or sensor identified in the metric name.";
        }

        if (upper.Contains("LATENCY") || label.StartsWith("Ms", StringComparison.Ordinal))
        {
            return "Time recorded by FrameView for this pipeline stage; lower values generally indicate better responsiveness.";
        }

        if (upper.Contains("BATTERY"))
        {
            return "Battery data sampled by FrameView while the application was running.";
        }

        return "Numeric metric exported by FrameView and detected automatically in this CSV.";
    }
}
