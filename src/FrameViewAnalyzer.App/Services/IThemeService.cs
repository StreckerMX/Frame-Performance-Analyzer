namespace FrameViewAnalyzer.App.Services;

/// <summary>Applies the dark/light theme by managing the overlay dictionary.</summary>
public interface IThemeService
{
    string Current { get; }

    void Apply(string mode);
}
