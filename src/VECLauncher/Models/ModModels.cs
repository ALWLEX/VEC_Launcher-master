using System.Text.Json.Serialization;

namespace VECLauncher.Models;

public enum ModProvider
{
    Modrinth = 0,
    CurseForge = 1
}

public enum ModContentType
{
    Mod = 0,
    ResourcePack = 1,
    ShaderPack = 2,
    ModPack = 3
}

public sealed class ModSearchResult
{
    public required ModProvider Provider { get; init; }
    public required string ProjectId { get; init; }
    public required string Title { get; init; }
    public string Slug { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Author { get; init; } = "";
    public string? IconUrl { get; init; }
    public long Downloads { get; init; }
    public DateTimeOffset? Updated { get; init; }
    public List<string> Categories { get; init; } = new();
    public List<string> Loaders { get; init; } = new();
    public ModContentType ContentType { get; init; } = ModContentType.Mod;
    public string? PageUrl { get; init; }

    public string DownloadsDisplay => Downloads switch
    {
        >= 1_000_000_000 => $"{Downloads / 1_000_000_000.0:0.#} млрд",
        >= 1_000_000 => $"{Downloads / 1_000_000.0:0.#} млн",
        >= 1_000 => $"{Downloads / 1_000.0:0.#} тыс.",
        _ => Downloads.ToString()
    };

    public string ProviderDisplay => Provider == ModProvider.Modrinth ? "Modrinth" : "CurseForge";
}

public sealed class ModFile
{
    public required ModProvider Provider { get; init; }
    public required string FileId { get; init; }
    public required string ProjectId { get; init; }
    public required string FileName { get; init; }
    public required string DownloadUrl { get; init; }
    public string DisplayName { get; init; } = "";
    public long Size { get; init; }
    public string? Sha1 { get; init; }
    public DateTimeOffset? Published { get; init; }
    public List<string> GameVersions { get; init; } = new();
    public List<string> Loaders { get; init; } = new();
    public string ReleaseType { get; init; } = "release";
    public List<ModDependency> Dependencies { get; init; } = new();

    public string SizeDisplay
    {
        get
        {
            string[] u = { "Б", "КБ", "МБ" };
            double v = Size;
            var i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.#} {u[i]}";
        }
    }
}

public sealed class ModDependency
{
    public string? ProjectId { get; init; }
    public string? FileId { get; init; }
    public string Type { get; init; } = "required";

    public bool IsRequired => string.Equals(Type, "required", StringComparison.OrdinalIgnoreCase);
}

public sealed class InstalledMod
{
    [JsonPropertyName("provider")] public ModProvider Provider { get; set; }
    [JsonPropertyName("projectId")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("fileId")] public string FileId { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("installed")] public DateTimeOffset Installed { get; set; } = DateTimeOffset.Now;
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("auto")] public bool InstalledAsDependency { get; set; }

    [JsonIgnore] public bool Enabled => !FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
}