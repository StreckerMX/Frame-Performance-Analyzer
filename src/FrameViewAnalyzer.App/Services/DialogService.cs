using System.IO;
using System.Windows;
using FrameViewAnalyzer.App.Views;
using Microsoft.Win32;

namespace FrameViewAnalyzer.App.Services;

/// <summary>
/// Central dialog gateway. File/folder pickers intentionally remain native
/// Windows dialogs; application messages and confirmations use the reusable
/// themed WPF dialog so Dark/Light presentation stays consistent everywhere.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IThemeService _themes;

    /// <summary>Convenience constructor retained for lightweight window tests.</summary>
    public DialogService()
        : this(new ThemeService())
    {
    }

    public DialogService(IThemeService themes)
    {
        _themes = themes;
    }

    public string? PickCsvFile(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a performance CSV",
            Filter = "Performance CSV (*.csv)|*.csv|All files (*.*)|*.*",
        };
        if (!string.IsNullOrEmpty(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowNative(dialog) == true ? dialog.FileName : null;
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

        return ShowNative(dialog) == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a file",
            Filter = filter,
        };
        return ShowNative(dialog) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a capture folder",
        };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return ShowNative(dialog) == true ? dialog.FolderName : null;
    }

    public void ShowError(string title, string message) =>
        ShowThemed(ThemedDialogKind.Error, title, message);

    public void ShowInfo(string title, string message) =>
        ShowThemed(ThemedDialogKind.Info, title, message);

    public void ShowWarning(string title, string message) =>
        ShowThemed(ThemedDialogKind.Warning, title, message);

    public void ShowSuccess(string title, string message) =>
        ShowThemed(ThemedDialogKind.Success, title, message);

    public bool Confirm(
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "No",
        bool destructive = false) =>
        ShowThemed(
            ThemedDialogKind.Confirmation,
            title,
            message,
            confirmText,
            cancelText,
            destructive);

    private bool ShowThemed(
        ThemedDialogKind kind,
        string title,
        string message,
        string primaryText = "OK",
        string? cancelText = null,
        bool destructive = false)
    {
        var dialog = new ThemedDialogWindow(
            kind,
            title,
            message,
            primaryText,
            cancelText,
            destructive);
        if (ResolveOwner() is { } owner)
        {
            dialog.Owner = owner;
        }

        WindowThemeBootstrap.Attach(dialog, _themes);
        return dialog.ShowDialog() == true;
    }

    private static Window? ResolveOwner()
    {
        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsVisible && window.IsActive)
            ?? (application.MainWindow is { IsVisible: true } main ? main : null);
    }

    private static bool? ShowNative(OpenFileDialog dialog)
    {
        var owner = ResolveOwner();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static bool? ShowNative(SaveFileDialog dialog)
    {
        var owner = ResolveOwner();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static bool? ShowNative(OpenFolderDialog dialog)
    {
        var owner = ResolveOwner();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
