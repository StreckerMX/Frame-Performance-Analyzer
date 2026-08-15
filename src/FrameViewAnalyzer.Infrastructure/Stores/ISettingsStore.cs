namespace FrameViewAnalyzer.Infrastructure.Stores;

/// <summary>Versioned JSON preferences store (v2 data location).</summary>
public interface ISettingsStore
{
    SettingsDocument Load();

    void Save(SettingsDocument settings);
}
