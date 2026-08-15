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

    public string? PickSaveFile(string? initialFile, string filter, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save export",
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
        };
        if (!string.IsNullOrEmpty(initialFile))
        {
            dialog.FileName = initialFile;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a file",
            Filter = filter,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
