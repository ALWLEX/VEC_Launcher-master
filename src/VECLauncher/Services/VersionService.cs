using System.Net.Http;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class VersionService
{
    public const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
    private const string ManifestMirror = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public static readonly Version MinimumVersion = new(1, 16, 5);

    private readonly HttpClient _http;

    public VersionService(HttpClient http) => _http = http;

    public GamePaths Paths { get; set; } = GamePaths.Shared;

    public async Task<VersionManifest> GetManifestAsync(CancellationToken ct = default)
    {
        Exception? last = null;

        foreach (var url in new[] { ManifestUrl, ManifestMirror })
        {
            try
            {
                var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                CacheManifest(json);
                return JsonSerializer.Deserialize<VersionManifest>(json)
                       ?? throw new InvalidOperationException("Empty manifest.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                Log.Warn($"VersionService: manifest unavailable at {url}: {ex.Message}");
            }
        }

        var cached = ReadCachedManifest();
        if (cached is not null)
        {
            Log.Warn("VersionService: using cached manifest (no network).");
            return cached;
        }

        throw new InvalidOperationException(
            "Failed to load Minecraft version list. Check your internet connection.", last);
    }

    public static List<ManifestVersion> FilterSupported(VersionManifest manifest, bool includeSnapshots = false)
    {
        return manifest.Versions
            .Where(v => includeSnapshots || v.IsRelease)
            .Where(v => IsAtLeastMinimum(v.Id))
            .OrderByDescending(v => v.ReleaseTime)
            .ToList();
    }

    public static bool IsAtLeastMinimum(string id)
    {
        var parsed = ParseMcVersion(id);
        if (parsed is null)
        {
            if (id.Length >= 2 && char.IsDigit(id[0]) && char.IsDigit(id[1]) && id.Contains('w'))
                return int.TryParse(id[..2], out var year) && year >= 21;
            return false;
        }
        return parsed >= MinimumVersion;
    }

    public static Version? ParseMcVersion(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(id, @"\b(1\.\d+(\.\d+)?)\b");
        if (match.Success)
        {
            var p = match.Value.Split('.');
            if (p.Length >= 2 &&
                int.TryParse(p[0], out var maj) &&
                int.TryParse(p[1], out var min))
            {
                var bld = 0;
                if (p.Length >= 3) int.TryParse(p[2], out bld);
                return new Version(maj, min, bld);
            }
        }

        var core = id.Split('-')[0].Trim();
        var parts = core.Split('.');
        if (parts.Length < 2) return null;

        if (!int.TryParse(parts[0], out var major)) return null;
        if (!int.TryParse(parts[1], out var minor)) return null;
        var build = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out build)) return null;

        return new Version(major, minor, build);
    }

    public async Task<VersionDetail> GetVersionDetailAsync(ManifestVersion version, CancellationToken ct = default)
    {
        var result = await TryGetVersionDetailAsync(version, ct).ConfigureAwait(false);
        return result.IsSuccess ? result.Value! : throw new InvalidOperationException(result.Error);
    }

    /// <summary>
    /// Tries to get version detail, returning Result&lt;VersionDetail&gt; instead of throwing.
    /// Use this for error-handling without try/catch.
    /// </summary>
    public async Task<Result<VersionDetail>> TryGetVersionDetailAsync(ManifestVersion version, CancellationToken ct = default)
    {
        var path = Paths.VersionJson(version.Id);

        if (File.Exists(path))
        {
            try
            {
                var cachedJson = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var cached = JsonSerializer.Deserialize<VersionDetail>(cachedJson);
                if (cached is not null && !string.IsNullOrEmpty(cached.MainClass))
                    return cached;
            }
            catch (Exception ex)
            {
                Log.Warn($"VersionService: corrupted {version.Id}.json, re-downloading: {ex.Message}");
            }
        }

        Log.Info($"VersionService: downloading version profile {version.Id}...");
        try
        {
            var json = await _http.GetStringAsync(version.Url, ct).ConfigureAwait(false);

            Directory.CreateDirectory(Paths.VersionDir(version.Id));
            await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);

            var detail = JsonSerializer.Deserialize<VersionDetail>(json);
            if (detail is null)
                return Result<VersionDetail>.Fail($"Failed to parse JSON for version {version.Id}.");

            return detail;
        }
        catch (Exception ex)
        {
            return Result<VersionDetail>.Fail($"Failed to download version {version.Id}: {ex.Message}");
        }
    }

    public async Task<VersionDetail?> LoadLocalVersionAsync(string versionId, CancellationToken ct = default)
    {
        var path = Paths.VersionJson(versionId);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<VersionDetail>(json);
    }

    public async Task<VersionDetail> ResolveAsync(string versionId, CancellationToken ct = default)
    {
        var local = await LoadLocalVersionAsync(versionId, ct).ConfigureAwait(false);

        if (local is null)
        {
            var manifest = await GetManifestAsync(ct).ConfigureAwait(false);
            var mv = manifest.Versions.FirstOrDefault(v => v.Id == versionId)
                     ?? throw new InvalidOperationException($"Version {versionId} not found locally or in manifest.");
            local = await GetVersionDetailAsync(mv, ct).ConfigureAwait(false);
        }

        return await ResolveInheritanceAsync(local, ct).ConfigureAwait(false);
    }

    public async Task<VersionDetail> ResolveInheritanceAsync(
        VersionDetail child, CancellationToken ct = default, int depth = 0)
    {
        if (string.IsNullOrEmpty(child.InheritsFrom)) return child;

        if (depth > 8)
            throw new InvalidOperationException("Inheritance chain too deep - possible circular reference.");

        var parentId = child.InheritsFrom!;
        Log.Info($"VersionService: {child.Id} inherits from {parentId} - merging profiles.");

        var parentLocal = await LoadLocalVersionAsync(parentId, ct).ConfigureAwait(false);

        if (parentLocal is null)
        {
            var manifest = await GetManifestAsync(ct).ConfigureAwait(false);
            var mv = manifest.Versions.FirstOrDefault(v => v.Id == parentId)
                     ?? throw new InvalidOperationException(
                         $"Base version {parentId} not found (required for {child.Id}).");
            parentLocal = await GetVersionDetailAsync(mv, ct).ConfigureAwait(false);
        }

        var parent = await ResolveInheritanceAsync(parentLocal, ct, depth + 1).ConfigureAwait(false);

        return Merge(parent, child);
    }

    private static VersionDetail Merge(VersionDetail parent, VersionDetail child)
    {
        var merged = new VersionDetail
        {
            Id = child.Id,
            InheritsFrom = null,
            MainClass = string.IsNullOrEmpty(child.MainClass) ? parent.MainClass : child.MainClass,
            Type = string.IsNullOrEmpty(child.Type) ? parent.Type : child.Type,

            Assets = string.IsNullOrEmpty(child.Assets) || child.Assets == "legacy" ? parent.Assets : child.Assets,
            AssetIndex = child.AssetIndex ?? parent.AssetIndex,
            Downloads = child.Downloads ?? parent.Downloads,
            JavaVersion = child.JavaVersion ?? parent.JavaVersion,
            Logging = child.Logging ?? parent.Logging,

            MinecraftArguments = child.MinecraftArguments ?? parent.MinecraftArguments,

            Libraries = MergeLibraries(parent.Libraries, child.Libraries),

            Arguments = MergeArguments(parent.Arguments, child.Arguments)
        };

        return merged;
    }

    private static List<Library> MergeLibraries(List<Library> parent, List<Library> child)
    {
        var result = new List<Library>(child);

        var taken = child.Select(LibraryKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var lib in parent)
        {
            if (taken.Add(LibraryKey(lib))) result.Add(lib);
        }

        return result;
    }

    private static string LibraryKey(Library lib)
    {
        var parts = lib.Name.Split(':');
        if (parts.Length < 2) return lib.Name;
        var classifier = parts.Length >= 4 ? ":" + parts[3] : "";
        return parts[0] + ":" + parts[1] + classifier;
    }

    private static JsonElement? MergeArguments(JsonElement? parent, JsonElement? child)
    {
        if (parent is null) return child;
        if (child is null) return parent;

        var p = parent.Value;
        var c = child.Value;
        if (p.ValueKind != JsonValueKind.Object || c.ValueKind != JsonValueKind.Object) return child;

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();

            foreach (var section in new[] { "game", "jvm" })
            {
                var hasP = p.TryGetProperty(section, out var pv) && pv.ValueKind == JsonValueKind.Array;
                var hasC = c.TryGetProperty(section, out var cv) && cv.ValueKind == JsonValueKind.Array;
                if (!hasP && !hasC) continue;

                w.WritePropertyName(section);
                w.WriteStartArray();

                if (hasP) foreach (var e in pv.EnumerateArray()) e.WriteTo(w);
                if (hasC) foreach (var e in cv.EnumerateArray()) e.WriteTo(w);

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static string CachePath => Path.Combine(LauncherPaths.CacheDir, "version_manifest_v2.json");

    private static void CacheManifest(string json)
    {
        try
        {
            Directory.CreateDirectory(LauncherPaths.CacheDir);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private static VersionManifest? ReadCachedManifest()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            return JsonSerializer.Deserialize<VersionManifest>(File.ReadAllText(CachePath));
        }
        catch { return null; }
    }
}