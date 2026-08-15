namespace FrameViewAnalyzer.App.Services;

/// <summary>Applies the dark/light theme by managing the overlay dictionary.</summary>
public interface IThemeService
{
    string Current { get; }

    /// <summary>Raised after the theme dictionaries were swapped.</summary>
    event EventHandler? Changed;

    void Apply(string mode);
}
