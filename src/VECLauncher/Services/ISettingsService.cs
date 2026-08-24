using System.ComponentModel;

namespace VECLauncher.Services;

/// <summary>
/// Abstraction for launcher settings. Supports auto-save via <see cref="INotifyPropertyChanged"/>.
/// Subscribe to <see cref="SettingsChanged"/> to react to any property change.
/// </summary>
public interface ISettingsService : INotifyPropertyChanged
{
    /// <summary>Current settings instance (mutable — changes trigger auto-save).</summary>
    LauncherSettings Settings { get; }

    /// <summary>Raised when any property changes. Subscribers can react (e.g., apply theme).</summary>
    event Action<string>? SettingsChanged;

    /// <summary>Force save current settings to disk.</summary>
    void Save();

    /// <summary>Reload settings from disk (discards unsaved changes).</summary>
    void Reload();
}
