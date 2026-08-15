namespace FrameViewAnalyzer.App.Services;

/// <summary>UI dialogs behind an interface so ViewModels stay testable.</summary>
public interface IDialogService
{
    string? PickCsvFile(string? initialDirectory);

    string? PickSaveFile(string? initialFile, string filter, string defaultExtension);

    string? PickOpenFile(string filter);

    /// <summary>Folder picker; returns the selected path or null when cancelled.</summary>
    string? PickFolder(string? initialDirectory);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);
}
