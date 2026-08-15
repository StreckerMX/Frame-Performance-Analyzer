using System.Windows;
using Microsoft.Win32;

namespace FrameViewAnalyzer.App.Services;

public sealed class DialogService : IDialogService
{
    public string? PickCsvFile(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a FrameView CSV",
            Filter = "FrameView CSV (*.csv)|*.csv|All files (*.*)|*.*",
        };
        if (!string.IsNullOrEmpty(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
