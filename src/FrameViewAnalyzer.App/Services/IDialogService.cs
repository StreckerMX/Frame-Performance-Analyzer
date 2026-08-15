namespace FrameViewAnalyzer.App.Services;

/// <summary>UI dialogs behind an interface so ViewModels stay testable.</summary>
public interface IDialogService
{
    string? PickCsvFile(string? initialDirectory);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);
}
