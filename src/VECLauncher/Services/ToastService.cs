namespace VECLauncher.Services;

/// <summary>
/// WPF implementation of <see cref="IToastService"/>.
/// Delegates to the existing static <see cref="ToastNotification"/> class.
/// Requires <see cref="ToastNotification.Initialize"/> to be called once at startup.
/// </summary>
public sealed class ToastService : IToastService
{
    /// <inheritdoc/>
    public void Show(string title, string message, NotificationType type = NotificationType.Info)
    {
        ToastNotification.Show(title, message, type);
    }
}
