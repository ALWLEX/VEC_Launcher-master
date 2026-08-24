using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

/// <summary>
/// Handles settings persistence, JVM presets, language selection,
/// maintenance operations, and portable mode configuration.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly JavaService _java;
    private readonly EventAggregator _events;

    /// <summary>Raised when settings are applied (after event from code-behind).</summary>
    public event Action? SettingsApplied;

    public SettingsViewModel(JavaService java, EventAggregator events)
    {
        _java = java;
        _events = events;

        _events.Subscribe<SettingsSavedEvent>(_ =>
        {
            SettingsApplied?.Invoke();
        });
    }

    // ── Sections ──
    [ObservableProperty]
    private string _currentSection = "game";

    [ObservableProperty]
    private bool _isGameSection = true;

    [ObservableProperty]
    private bool _isJavaSection;

    [ObservableProperty]
    private bool _isStorageSection;

    [ObservableProperty]
    private bool _isVersionsSection;

    [ObservableProperty]
    private bool _isMaintSection;

    public void SetSection(string tag)
    {
        CurrentSection = tag;
        IsGameSection = tag == "game";
        IsJavaSection = tag == "java";
        IsStorageSection = tag == "storage";
        IsVersionsSection = tag == "versions";
        IsMaintSection = tag == "maint";
    }

    // ── Apply to UI ──
    public void ApplySettingsToUi(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GameDir))
            settings.GameDir = LauncherPaths.Root;
    }

    // ── Persist ──
    public void PersistSettings(LauncherSettings settings)
    {
        SettingsService.Save(settings);
    }

    // ── Reset Section ──
    public void ResetSection(string section, LauncherSettings settings)
    {
        var def = new LauncherSettings();

        switch (section)
        {
            case "game":
                settings.WindowWidth = def.WindowWidth;
                settings.WindowHeight = def.WindowHeight;
                settings.Fullscreen = def.Fullscreen;
                settings.AllowMultipleInstances = def.AllowMultipleInstances;
                settings.MinimizeOnLaunch = def.MinimizeOnLaunch;
                settings.ConfirmGameStop = def.ConfirmGameStop;
                settings.CloseLauncherOnStart = def.CloseLauncherOnStart;
                settings.ShowConsole = def.ShowConsole;
                settings.ShowSnapshots = def.ShowSnapshots;
                settings.DefaultIsolated = def.DefaultIsolated;
                settings.AutoSetGameLanguage = def.AutoSetGameLanguage;
                settings.GameLanguage = def.GameLanguage;
                break;
            case "java":
                settings.MaxMemoryMb = LauncherSettings.RecommendedMaxMemory();
                settings.CustomJavaPath = "";
                settings.ExtraJvmArgs = "";
                break;
            case "storage":
                settings.GameDir = LauncherPaths.Root;
                break;
        }

        SettingsService.Save(settings);
    }

    // ── Versions Management ──
    public List<InstalledVersion> ScanVersions(List<GameInstance> instances)
    {
        return VersionManagerService.Scan(instances);
    }

    public long DeleteVersion(InstalledVersion version)
    {
        return VersionManagerService.Delete(version);
    }

    // ── Storage ──
    public long CalculateStorageSize()
    {
        long Size(string dir)
        {
            try
            {
                return Directory.Exists(dir)
                    ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                    : 0;
            }
            catch { return 0; }
        }

        return Size(LauncherPaths.LibrariesDir) +
               Size(LauncherPaths.AssetsDir) +
               Size(LauncherPaths.VersionsDir) +
               Size(LauncherPaths.RuntimeDir) +
               Size(LauncherPaths.CacheDir);
    }

    public long CalculateInstanceSize(GameInstance inst)
    {
        try
        {
            var dir = InstanceService.InstanceDir(inst);
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch { return 0; }
    }

    public long ClearCache()
    {
        long freed = 0;
        if (Directory.Exists(LauncherPaths.CacheDir))
        {
            foreach (var f in Directory.GetFiles(LauncherPaths.CacheDir))
            {
                if (f.EndsWith("version_manifest_v2.json", StringComparison.OrdinalIgnoreCase)) continue;
                try { freed += new FileInfo(f).Length; File.Delete(f); } catch (Exception ex) { Log.Warn(ex.Message); }
            }
        }
        return freed;
    }

    // ── Maintenance ──
    public List<MaintenanceService.TargetInfo> EnumerateMaintenance()
    {
        return MaintenanceService.Enumerate();
    }

    public long TotalMaintenanceSize()
    {
        return MaintenanceService.TotalSize();
    }

    public long CleanMaintenance(List<MaintenanceService.TargetInfo> targets)
    {
        return MaintenanceService.Clean(targets);
    }

    // ── Java Detection ──
    public List<JavaInstallation> DetectJava()
    {
        var java = _java;
        return java.FindAll();
    }

    // ── Portable Mode ──
    public bool IsPortable => LauncherPaths.IsPortable;
    public bool CanUsePortable => LauncherPaths.CanUsePortable();
    public string ExeDir => LauncherPaths.ExeDir;
}
