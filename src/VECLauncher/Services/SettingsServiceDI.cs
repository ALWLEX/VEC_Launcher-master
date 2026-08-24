using System.ComponentModel;

namespace VECLauncher.Services;

/// <summary>
/// DI-friendly settings service. Loads settings once, auto-saves when any property changes.
/// Implements <see cref="ISettingsService"/> for dependency injection.
/// </summary>
public sealed class SettingsServiceDI : ISettingsService
{
    private readonly object _lock = new();
    private DateTime _lastSave = DateTime.MinValue;

    /// <inheritdoc/>
    public LauncherSettings Settings { get; private set; }

    /// <inheritdoc/>
    public event Action<string>? SettingsChanged;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => ((INotifyPropertyChanged)Settings).PropertyChanged += value;
        remove => ((INotifyPropertyChanged)Settings).PropertyChanged -= value;
    }

    public SettingsServiceDI()
    {
        Settings = SettingsService.Load();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SettingsChanged?.Invoke(e.PropertyName ?? "");

        // Throttle saves — max once per 500ms to avoid disk thrashing
        lock (_lock)
        {
            if (DateTime.UtcNow - _lastSave < TimeSpan.FromMilliseconds(500)) return;
            _lastSave = DateTime.UtcNow;
        }

        // Save on background thread to avoid blocking UI
        var snapshot = CloneSettings(Settings);
        _ = Task.Run(() => SettingsService.Save(snapshot));
    }

    /// <inheritdoc/>
    public void Save()
    {
        lock (_lock) _lastSave = DateTime.UtcNow;
        SettingsService.Save(Settings);
    }

    /// <inheritdoc/>
    public void Reload()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        Settings = SettingsService.Load();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    /// <summary>Creates a shallow copy of settings for thread-safe serialization.</summary>
    private static LauncherSettings CloneSettings(LauncherSettings src)
    {
        return new LauncherSettings
        {
            LastVersion = src.LastVersion,
            LastInstanceId = src.LastInstanceId,
            MinMemoryMb = src.MinMemoryMb,
            MaxMemoryMb = src.MaxMemoryMb,
            WindowWidth = src.WindowWidth,
            WindowHeight = src.WindowHeight,
            Fullscreen = src.Fullscreen,
            ShowSnapshots = src.ShowSnapshots,
            CloseLauncherOnStart = src.CloseLauncherOnStart,
            ShowConsole = src.ShowConsole,
            ServerAddress = src.ServerAddress,
            CurseForgeApiKey = src.CurseForgeApiKey,
            ExtraJvmArgs = src.ExtraJvmArgs,
            GameDir = src.GameDir,
            CustomJavaPath = src.CustomJavaPath,
            AllowMultipleInstances = src.AllowMultipleInstances,
            MinimizeOnLaunch = src.MinimizeOnLaunch,
            ConfirmGameStop = src.ConfirmGameStop,
            AccentColor = src.AccentColor,
            BackgroundStyle = src.BackgroundStyle,
            CustomBannerPath = src.CustomBannerPath,
            CornerRadius = src.CornerRadius,
            Animations = src.Animations,
            CompactMode = src.CompactMode,
            DefaultIsolated = src.DefaultIsolated,
            Theme = src.Theme,
            WindowBackgroundPath = src.WindowBackgroundPath,
            WindowBackgroundOpacity = src.WindowBackgroundOpacity,
            GameLanguage = src.GameLanguage,
            AutoSetGameLanguage = src.AutoSetGameLanguage,
            CustomThemeJson = src.CustomThemeJson
        };
    }
}
