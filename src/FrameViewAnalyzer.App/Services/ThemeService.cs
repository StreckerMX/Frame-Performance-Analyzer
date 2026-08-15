using System.Windows;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Dark values are the base (Colors.xaml); light mode merges
/// LightTheme.xaml on top so every DynamicResource reference updates
/// immediately.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly Uri LightThemeUri =
        new("/FrameViewAnalyzer.App;component/Themes/LightTheme.xaml", UriKind.Relative);

    public string Current { get; private set; } = "dark";

    public void Apply(string mode)
    {
        var normalized = Normalize(mode);
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Remove any existing light overlay.
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            if (dictionaries[i].Source == LightThemeUri)
            {
                dictionaries.RemoveAt(i);
            }
        }

        if (normalized == "light")
        {
            dictionaries.Add(new ResourceDictionary { Source = LightThemeUri });
        }

        Current = normalized;
    }

    private static string Normalize(string mode) =>
        string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
}
