using System.Text.Json;
using System.Text.Json.Serialization;

namespace VECLauncher.Services;

public sealed class LauncherSettings : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    [JsonPropertyName("lastVersion")] public string? LastVersion { get; set; }
    [JsonPropertyName("lastInstanceId")] public string? LastInstanceId { get; set; }
    [JsonPropertyName("minMemoryMb")] public int MinMemoryMb { get; set; } = 1024;
    [JsonPropertyName("maxMemoryMb")] public int MaxMemoryMb { get; set; } = 4096;
    [JsonPropertyName("windowWidth")] public int WindowWidth { get; set; } = 1280;
    [JsonPropertyName("windowHeight")] public int WindowHeight { get; set; } = 720;
    [JsonPropertyName("fullscreen")] public bool Fullscreen { get; set; }
    [JsonPropertyName("showSnapshots")] public bool ShowSnapshots { get; set; }
    [JsonPropertyName("closeOnLaunch")] public bool CloseLauncherOnStart { get; set; }
    [JsonPropertyName("showConsole")] public bool ShowConsole { get; set; }
    [JsonPropertyName("serverAddress")] public string ServerAddress { get; set; } = "95.59.233.227:25565";
    [JsonPropertyName("curseForgeApiKey")] public string CurseForgeApiKey { get; set; } = "$2a$10$OWlzDC41GSVY/PTAJJw01uClvqVZn6t12H.s9gkaRihtOCu0fd8TW";
    [JsonPropertyName("extraJvmArgs")] public string ExtraJvmArgs { get; set; } = "";
    [JsonPropertyName("gameDir")] public string GameDir { get; set; } = "";
    [JsonPropertyName("customJavaPath")] public string CustomJavaPath { get; set; } = "";

    [JsonPropertyName("allowMultipleInstances")] public bool AllowMultipleInstances { get; set; }
    [JsonPropertyName("minimizeOnLaunch")] public bool MinimizeOnLaunch { get; set; } = true;
    [JsonPropertyName("confirmGameStop")] public bool ConfirmGameStop { get; set; } = true;

    [JsonPropertyName("accentColor")] public string AccentColor { get; set; } = "#FACC15";
    [JsonPropertyName("backgroundStyle")] public string BackgroundStyle { get; set; } = "Тёмный";
    [JsonPropertyName("customBannerPath")] public string CustomBannerPath { get; set; } = "";
    [JsonPropertyName("cornerRadius")] public int CornerRadius { get; set; } = 12;
    [JsonPropertyName("animations")] public bool Animations { get; set; } = true;
    [JsonPropertyName("compactMode")] public bool CompactMode { get; set; }
    [JsonPropertyName("defaultIsolated")] public bool DefaultIsolated { get; set; }
    [JsonPropertyName("theme")] public string Theme { get; set; } = "Тёмная";
    [JsonPropertyName("windowBackground")] public string WindowBackgroundPath { get; set; } = "";
    [JsonPropertyName("windowBackgroundOpacity")] public double WindowBackgroundOpacity { get; set; } = 0.35;
    [JsonPropertyName("gameLanguage")] public string GameLanguage { get; set; } = "ru";
    [JsonPropertyName("autoLanguage")] public bool AutoSetGameLanguage { get; set; } = true;
    [JsonPropertyName("customTheme")] public string CustomThemeJson { get; set; } = "";

    public static int RecommendedMaxMemory()
    {
        try
        {
            var totalMb = (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
            if (totalMb <= 0) return 4096;
            var half = (int)(totalMb / 2);
            return Math.Clamp(half - half % 512, 2048, 8192);
        }
        catch { return 4096; }
    }

    public static readonly (string Name, string Hex)[] AccentPresets =
    {
        ("Золото VEC", "#FACC15"),
        ("Океан", "#38BDF8"),
        ("Аметист", "#A78BFA"),
        ("Закат", "#FB923C"),
        ("Роза", "#FB7185"),
        ("Бирюза", "#2DD4BF"),
        ("Индиго", "#818CF8")
    };
}

public static class SettingsService
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(LauncherPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<LauncherSettings>(
                    File.ReadAllText(LauncherPaths.SettingsFile));
                if (s is not null)
                {
                    if (string.IsNullOrWhiteSpace(s.GameDir)) s.GameDir = LauncherPaths.Root;
                    if (string.IsNullOrWhiteSpace(s.AccentColor) ||
                        s.AccentColor.Equals("#22C55E", StringComparison.OrdinalIgnoreCase) ||
                        s.AccentColor.Equals("#10B981", StringComparison.OrdinalIgnoreCase) ||
                        s.AccentColor.Equals("#4ADE80", StringComparison.OrdinalIgnoreCase) ||
                        s.AccentColor.Equals("#14301F", StringComparison.OrdinalIgnoreCase))
                    {
                        s.AccentColor = "#FACC15";
                    }
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SettingsService: failed to load settings: {ex.Message}");
        }

        return new LauncherSettings
        {
            MaxMemoryMb = LauncherSettings.RecommendedMaxMemory(),
            GameDir = LauncherPaths.Root
        };
    }

    public static void Save(LauncherSettings settings)
    {
        try
        {
            LauncherPaths.EnsureAll();
            File.WriteAllText(LauncherPaths.SettingsFile, JsonSerializer.Serialize(settings, Opts));
        }
        catch (Exception ex)
        {
            Log.Warn($"SettingsService: failed to save settings: {ex.Message}");
        }
    }
}