using VECLauncher.Models;

namespace VECLauncher.Services;

/// <summary>
/// Abstraction for UI interactions that ViewModels need but cannot access directly.
/// Implementations live in the View layer and are injected into VMs at construction time.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a yes/no confirmation dialog. Returns true if user clicked Yes.</summary>
    Task<bool> ConfirmAsync(string title, string message, string yesText = "Да", string noText = "Отмена");

    /// <summary>Shows a yes/no/cancel confirmation dialog. Returns the user's choice.</summary>
    Task<ConfirmResult> ConfirmCancelAsync(string title, string message, string yesText = "Да", string noText = "Нет", string cancelText = "Отмена");

    /// <summary>Shows a message box (OK button). Use for errors and info.</summary>
    void ShowMessage(string message, string title = "VEC Launcher", MessageSeverity severity = MessageSeverity.Info);

    /// <summary>Shows an info/warning/error toast notification.</summary>
    void ShowToast(string title, string message, ToastType type = ToastType.Info);

    /// <summary>Shows a folder browser dialog. Returns selected path or null.</summary>
    string? BrowseFolder(string description = "");

    /// <summary>Shows a file open dialog. Returns selected file path or null.</summary>
    string? BrowseFile(string filter, string title = "Выберите файл");

    /// <summary>Shows a Microsoft OAuth login dialog. Returns authorization code or null.</summary>
    Task<(string? code, string? error)> ShowMicrosoftLoginAsync(string authUrl);

    /// <summary>Shows a VEC login dialog. Returns authenticated account or null if cancelled.</summary>
    Task<MinecraftAccount?> ShowVecLoginAsync();

    /// <summary>Minimizes the main window.</summary>
    void MinimizeWindow();
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public enum ConfirmResult
{
    Yes,
    No,
    Cancel
}

public enum MessageSeverity
{
    Info,
    Warning,
    Error
}
