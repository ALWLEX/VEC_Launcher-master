namespace VECLauncher.Services;

/// <summary>
/// Abstraction for toast notifications. Code-behind calls this instead of
/// the static ToastNotification class, making toasts testable and DI-friendly.
/// </summary>
public interface IToastService
{
    /// <summary>Shows a toast notification with auto-dismiss after 4 seconds.</summary>
    void Show(string title, string message, NotificationType type = NotificationType.Info);
}
