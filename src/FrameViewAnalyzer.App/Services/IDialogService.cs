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

    /// <summary>
    /// Warning presentation. The default keeps existing test doubles source-compatible;
    /// the production service provides the themed warning treatment.
    /// </summary>
    void ShowWarning(string title, string message) => ShowInfo(title, message);

    /// <summary>
    /// Positive completion presentation for exports/imports and other completed work.
    /// Existing lightweight test doubles may safely inherit the informational fallback.
    /// </summary>
    void ShowSuccess(string title, string message) => ShowInfo(title, message);

    /// <summary>
    /// Modal confirmation. The production service defaults to the safe/cancel result;
    /// callers may opt into destructive styling for irreversible record actions.
    /// </summary>
    bool Confirm(
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "No",
        bool destructive = false) => false;
}
