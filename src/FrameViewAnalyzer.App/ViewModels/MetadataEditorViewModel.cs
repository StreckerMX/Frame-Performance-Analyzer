using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameViewAnalyzer.Analytics;
using FrameViewAnalyzer.Core.Models;

namespace FrameViewAnalyzer.App.ViewModels;

/// <summary>
/// Editor state for the manual benchmark metadata of one capture. Manual
/// values take precedence; detected values prefill the empty fields. Save
/// builds the metadata and raises <see cref="SaveRequested"/>.
/// </summary>
public partial class MetadataEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _benchmarkName = string.Empty;

    [ObservableProperty]
    private string _game = string.Empty;

    [ObservableProperty]
    private string _resolution = string.Empty;

    [ObservableProperty]
    private string _graphicsPreset = string.Empty;

    [ObservableProperty]
    private string _upscaler = string.Empty;

    [ObservableProperty]
    private string _upscalerQuality = string.Empty;

    [ObservableProperty]
    private string _frameGeneration = string.Empty;

    [ObservableProperty]
    private string _rayTracing = string.Empty;

    [ObservableProperty]
    private string _driverVersion = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    public string Title { get; }

    public event EventHandler<ManualMetadata>? SaveRequested;

    public event EventHandler? CancelRequested;

    public MetadataEditorViewModel(string title)
    {
        Title = title;
    }

    /// <summary>
    /// Prefills the editor from stored manual metadata and detected capture
    /// values (manual wins per field, like the Python reference).
    /// </summary>
    public static MetadataEditorViewModel From(SessionAnalysis session, ManualMetadata? current)
    {
        var editor = new MetadataEditorViewModel(
            $"Benchmark metadata · {session.Capture.DisplayName}");
        var detected = DetectedMetadata.DetectFieldValues(session);
        var manual = current ?? new ManualMetadata();

        editor.BenchmarkName = manual.BenchmarkName;
        editor.Game = FirstOf(manual.Game, detected, "game");
        editor.Resolution = FirstOf(manual.Resolution, detected, "resolution");
        editor.GraphicsPreset = manual.GraphicsPreset;
        editor.Upscaler = FirstOf(manual.Upscaler, detected, "upscaler");
        editor.UpscalerQuality = FirstOf(manual.UpscalerQuality, detected, "upscaler_quality");
        editor.FrameGeneration = FirstOf(manual.FrameGeneration, detected, "frame_generation");
        editor.RayTracing = FirstOf(manual.RayTracing, detected, "ray_tracing");
        editor.DriverVersion = FirstOf(manual.DriverVersion, detected, "driver_version");
        editor.Notes = manual.Notes;
        editor.TagsText = string.Join(", ", manual.Tags);
        return editor;
    }

    public ManualMetadata BuildMetadata() => new(
        BenchmarkName: BenchmarkName.Trim(),
        Game: Game.Trim(),
        Resolution: Resolution.Trim(),
        GraphicsPreset: GraphicsPreset.Trim(),
        Upscaler: Upscaler.Trim(),
        UpscalerQuality: UpscalerQuality.Trim(),
        FrameGeneration: FrameGeneration.Trim(),
        RayTracing: RayTracing.Trim(),
        DriverVersion: DriverVersion.Trim(),
        Notes: Notes.Trim(),
        Tags: TagsText
            .Split(',')
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .ToArray());

    [RelayCommand]
    private void Save() => SaveRequested?.Invoke(this, BuildMetadata());

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke(this, EventArgs.Empty);

    private static string FirstOf(
        string manual,
        IReadOnlyDictionary<string, string> detected,
        string key) =>
        manual.Length > 0
            ? manual
            : (detected.TryGetValue(key, out var value) ? value : string.Empty);
}
