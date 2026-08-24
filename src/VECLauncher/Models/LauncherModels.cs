using System.Text.Json.Serialization;

namespace VECLauncher.Models;

public enum LoaderKind
{
    Vanilla = 0,
    Fabric = 1,
    Forge = 2,
    NeoForge = 3
}

public static class LoaderKindExtensions
{
    public static string Display(this LoaderKind k) => k switch
    {
        LoaderKind.Vanilla => "Vanilla",
        LoaderKind.Fabric => "Fabric",
        LoaderKind.Forge => "Forge",
        LoaderKind.NeoForge => "NeoForge",
        _ => k.ToString()
    };
}

public sealed class LoaderVersion
{
    public required LoaderKind Kind { get; init; }
    public required string Version { get; init; }
    public string? McVersion { get; init; }
    public bool IsStable { get; init; } = true;
    public bool IsRecommended { get; init; }

    public override string ToString() =>
        Version + (IsRecommended ? "  (рекомендуется)" : IsStable ? "" : "  (beta)");
}

public sealed class GameInstance
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    [JsonPropertyName("name")] public string Name { get; set; } = "Новая сборка";
    [JsonPropertyName("mcVersion")] public string McVersion { get; set; } = "";
    [JsonPropertyName("loader")] public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
    [JsonPropertyName("loaderVersion")] public string? LoaderVersion { get; set; }
    [JsonPropertyName("launchVersionId")] public string? LaunchVersionId { get; set; }
    [JsonPropertyName("created")] public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("lastPlayed")] public DateTimeOffset? LastPlayed { get; set; }
    [JsonPropertyName("totalPlaySeconds")] public long TotalPlaySeconds { get; set; }
    [JsonPropertyName("maxMemoryMb")] public int MaxMemoryMb { get; set; }
    [JsonPropertyName("extraJvmArgs")] public string ExtraJvmArgs { get; set; } = "";
    [JsonPropertyName("iconColor")] public string IconColor { get; set; } = "#FACC15";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("isolated")] public bool Isolated { get; set; }
    [JsonPropertyName("javaPath")] public string JavaPath { get; set; } = "";
    [JsonPropertyName("windowWidth")] public int WindowWidth { get; set; }
    [JsonPropertyName("windowHeight")] public int WindowHeight { get; set; }
    [JsonPropertyName("serverAddress")] public string ServerAddress { get; set; } = "";
    [JsonPropertyName("iconPath")] public string IconPath { get; set; } = "";
    [JsonPropertyName("activeModProfile")] public string ActiveModProfile { get; set; } = "По умолчанию";
    [JsonPropertyName("jvmPreset")] public string JvmPreset { get; set; } = "Стандарт";
    [JsonPropertyName("sessions")] public List<PlaySession> Sessions { get; set; } = new();

    public void AddSession(long seconds)
    {
        if (seconds < 5) return;

        TotalPlaySeconds += seconds;
        LastPlayed = DateTimeOffset.Now;

        Sessions.Add(new PlaySession
        {
            Date = DateTimeOffset.Now,
            Seconds = seconds
        });

        var limit = DateTimeOffset.Now.AddDays(-180);
        Sessions.RemoveAll(s => s.Date < limit);
    }

    [JsonIgnore] public string EffectiveVersionId => LaunchVersionId ?? McVersion;

    [JsonIgnore]
    public object IconBrush
    {
        get
        {
            try
            {
                var color = (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString(IconColor);

                var brush = new System.Windows.Media.SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return System.Windows.Media.Brushes.Gray;
            }
        }
    }

    [JsonIgnore]
    public object? IconImage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(IconPath) || !File.Exists(IconPath)) return null;

            var stamp = File.GetLastWriteTimeUtc(IconPath);
            if (_iconCache is not null && _iconStamp == stamp) return _iconCache;

            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 256;
                bmp.UriSource = new Uri(IconPath);
                bmp.EndInit();
                bmp.Freeze();

                _iconCache = bmp;
                _iconStamp = stamp;
                return bmp;
            }
            catch { return null; }
        }
    }

    private object? _iconCache;
    private DateTime _iconStamp;

    [JsonIgnore]
    public object DisplayIcon
    {
        get
        {
            if (IconImage is not null) return IconImage;
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Assets/default_instance_icon.jpg", UriKind.Absolute));
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return IconBrush;
            }
        }
    }

    [JsonIgnore]
    public string LoaderDisplay => Loader == LoaderKind.Vanilla
        ? "Vanilla"
        : $"{Loader.Display()} {LoaderVersion}";

    [JsonIgnore]
    public string PlayTimeDisplay
    {
        get
        {
            if (TotalPlaySeconds < 60) return "менее минуты";
            var ts = TimeSpan.FromSeconds(TotalPlaySeconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} ч {ts.Minutes} мин";
            return $"{ts.Minutes} мин";
        }
    }

    public override string ToString() => Name;
}

public sealed class PlaySession
{
    [JsonPropertyName("date")] public DateTimeOffset Date { get; set; }
    [JsonPropertyName("seconds")] public long Seconds { get; set; }
}

public sealed class ServerEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("version")] public string RequiredVersion { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("site")] public string? Site { get; set; }
    [JsonPropertyName("featured")] public bool Featured { get; set; }
    [JsonPropertyName("loader")] public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
}

public sealed class ServerStatus
{
    public bool Online { get; init; }
    public int OnlinePlayers { get; init; }
    public int MaxPlayers { get; init; }
    public string VersionName { get; init; } = "";
    public int ProtocolVersion { get; init; }
    public string Motd { get; init; } = "";
    public long PingMs { get; init; }
    public byte[]? FaviconPng { get; init; }
    public string? Error { get; init; }

    public static ServerStatus Offline(string error) => new() { Online = false, Error = error };

    public string PlayersDisplay => Online ? $"{OnlinePlayers} / {MaxPlayers}" : "—";
}