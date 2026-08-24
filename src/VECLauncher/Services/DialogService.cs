using System.Windows;
using VECLauncher.Models;
using VECLauncher.Views;

namespace VECLauncher.Services;

/// <summary>
/// WPF implementation of IDialogService. Holds a reference to the main Window
/// for dialog ownership and window manipulation.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Window _owner;

    public DialogService(Window owner)
    {
        _owner = owner;
    }

    public Task<bool> ConfirmAsync(string title, string message, string yesText = "Да", string noText = "Отмена")
    {
        var result = ConfirmDialog.Show(_owner, title, message, yesText, noText);
        return Task.FromResult(result);
    }

    public void ShowToast(string title, string message, ToastType type = ToastType.Info)
    {
        var nt = type switch
        {
            ToastType.Success => NotificationType.Success,
            ToastType.Warning => NotificationType.Warning,
            ToastType.Error => NotificationType.Error,
            _ => NotificationType.Info
        };
        ToastNotification.Show(title, message, nt);
    }

    public string? BrowseFolder(string description = "")
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = string.IsNullOrEmpty(description) ? "Выберите папку" : description
        };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public string? BrowseFile(string filter, string title = "Выберите файл")
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public async Task<(string? code, string? error)> ShowMicrosoftLoginAsync(string authUrl)
    {
        var dialog = new MicrosoftLoginDialog(authUrl) { Owner = _owner };
        var result = dialog.ShowDialog();

        if (result != true || string.IsNullOrEmpty(dialog.AuthorizationCode))
        {
            if (!string.IsNullOrEmpty(dialog.ErrorDescription))
                return (null, dialog.ErrorDescription);
            return (null, null); // cancelled
        }

        return (dialog.AuthorizationCode, null);
    }

    public async Task<MinecraftAccount?> ShowVecLoginAsync()
    {
        var dialog = new VecLoginDialog { Owner = _owner };
        var result = dialog.ShowDialog();

        if (result != true || dialog.ResultAccount is null)
            return null;

        return dialog.ResultAccount;
    }

    public void MinimizeWindow()
    {
        _owner.WindowState = WindowState.Minimized;
    }

    public Task<ConfirmResult> ConfirmCancelAsync(string title, string message, string yesText = "Да", string noText = "Нет", string cancelText = "Отмена")
    {
        var r = (MessageBoxResult)_owner.Dispatcher.Invoke(() =>
            MessageBox.Show(_owner, message, title,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question));
        var result = r switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.No => ConfirmResult.No,
            _ => ConfirmResult.Cancel
        };
        return Task.FromResult(result);
    }

    public void ShowMessage(string message, string title = "VEC Launcher", MessageSeverity severity = MessageSeverity.Info)
    {
        var icon = severity switch
        {
            MessageSeverity.Warning => MessageBoxImage.Warning,
            MessageSeverity.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Information
        };
        _owner.Dispatcher.Invoke(() =>
            MessageBox.Show(_owner, message, title, MessageBoxButton.OK, icon));
    }
}
