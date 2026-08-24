using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.ViewModels;

/// <summary>
/// Handles mod search (CurseForge/Modrinth), content browsing,
/// modpack import, and mod installation into instances.
/// </summary>
public partial class ModsViewModel : ObservableObject
{
    private readonly IAccountState _state;
    private readonly ModService _mods;
    private readonly EventAggregator _events;

    /// <summary>Raised when instance changes — refresh mod list for new instance.</summary>
    public event Action? InstanceChanged;

    public ModsViewModel(IAccountState state, ModService mods, EventAggregator events)
    {
        _state = state;
        _mods = mods;
        _events = events;

        _events.Subscribe<InstanceSelectedEvent>(_ =>
        {
            InstanceChanged?.Invoke();
        });
    }

    // ── Mod Search ──
    private List<ModSearchResult> _modResults = new();
    public List<ModSearchResult> ModResults => _modResults;

    private CancellationTokenSource? _modCts;
    public const int ModPageSize = 20;
    public int ModOffset;
    public int ModTotal;

    public ModContentType SelectedContentType { get; set; } = ModContentType.Mod;
    public ModProvider? SelectedProvider { get; set; }

    [ObservableProperty]
    private string _modStatus = "";

    [ObservableProperty]
    private bool _modPagerVisible;

    [ObservableProperty]
    private bool _prevPageEnabled;

    [ObservableProperty]
    private bool _nextPageEnabled;

    public async Task<ModService.SearchPage?> SearchModsAsync(string query, string mcVersion, LoaderKind loader)
    {
        _modCts?.Cancel();
        _modCts = new CancellationTokenSource();
        var ct = _modCts.Token;

        try
        {
            var page = await _mods.SearchAsync(
                query, mcVersion, loader,
                SelectedContentType, SelectedProvider,
                ModPageSize, ModOffset, ct);

            if (ct.IsCancellationRequested) return null;

            _modResults = page.Items;
            ModTotal = page.TotalCount;

            return page;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            ModStatus = "Ошибка поиска: " + ex.Message;
            Log.Error("Поиск модов", ex);
            return null;
        }
    }

    public void UpdatePager(ModService.SearchPage page)
    {
        ModPagerVisible = page.TotalCount > ModPageSize;
        PrevPageEnabled = page.HasPrevious;
        NextPageEnabled = page.HasNext;
    }

    public static List<int> GetPageNumbers(int current, int total)
    {
        var result = new List<int>();
        if (total <= 7)
        {
            for (int i = 1; i <= total; i++) result.Add(i);
        }
        else
        {
            result.Add(1);
            if (current > 3) result.Add(-1);
            for (int i = Math.Max(2, current - 1); i <= Math.Min(total - 1, current + 1); i++)
                result.Add(i);
            if (current < total - 2) result.Add(-1);
            result.Add(total);
        }
        return result;
    }

    // ── Install Mod ──
    public async Task<ModService.InstallOutcome> InstallModAsync(
        ModSearchResult project, ModFile chosen, GameInstance inst)
    {
        var targetDir = SelectedContentType switch
        {
            ModContentType.ResourcePack => InstanceService.ResourcePacksDir(inst),
            ModContentType.ShaderPack => InstanceService.ShaderPacksDir(inst),
            _ => InstanceService.ModsDir(inst)
        };

        return await _mods.InstallAsync(
            chosen, targetDir, inst.McVersion, inst.Loader);
    }

    // ── Content Management ──
    public enum ContentKind { Mods, ResourcePacks, Shaders, Worlds }
    public ContentKind CurrentContentKind { get; set; } = ContentKind.Mods;

    public string CurrentContentDir(GameInstance inst)
    {
        return CurrentContentKind switch
        {
            ContentKind.ResourcePacks => InstanceService.ResourcePacksDir(inst),
            ContentKind.Shaders => InstanceService.ShaderPacksDir(inst),
            ContentKind.Worlds => InstanceService.SavesDir(inst),
            _ => InstanceService.ModsDir(inst)
        };
    }

    public void ToggleContentFile(string path)
    {
        var target = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? path[..^".disabled".Length]
            : path + ".disabled";

        if (File.Exists(target)) File.Delete(target);
        File.Move(path, target);
    }

    public void DeleteContent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
        else
            File.Delete(path);
    }

    public void RevealContent(string path)
    {
        if (Directory.Exists(path))
            InstanceService.OpenFolder(path);
        else
            InstanceService.RevealFile(path);
    }

    // ── Mod Info Extraction ──
    private static readonly Dictionary<string, string> _modNameCache = new();
    private static readonly Dictionary<string, string?> _modIdCache = new();
    private static readonly Dictionary<string, BitmapImage?> _modIconCache = new();
    private static readonly HttpClient _iconHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly Dictionary<string, string> _knownModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jei"] = "Just Enough Items",
        ["create"] = "Create",
        ["mekanism"] = "Mekanism",
        ["botania"] = "Botania",
        ["ae2"] = "Applied Energistics 2",
        ["journeymap"] = "JourneyMap",
        ["xaeros_minimap"] = "Xaero's Minimap",
        ["farmers_delight"] = "Farmer's Delight",
        ["quark"] = "Quark",
        ["ironchest"] = "Iron Chests",
        ["ironfurnaces"] = "Iron Furnaces",
        ["patchouli"] = "Patchouli",
        ["cloth_config"] = "Cloth Config",
        ["curios"] = "Curios API",
        ["architectury"] = "Architectury",
    };

    private static readonly System.Text.RegularExpressions.Regex _rxVersionSuffix = new(
        @"[-_](mc)?(1\.|forge|fabric|neoforge|quilt|sl|babric|legacy)[^""]*",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _rxVersionNumber = new(
        @"[-_]v?\d+\.\d+[^""]*",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string CleanSlugFromFilename(string filename)
    {
        var slug = Path.GetFileNameWithoutExtension(filename).ToLower();
        slug = _rxVersionSuffix.Replace(slug, "");
        slug = _rxVersionNumber.Replace(slug, "");
        return slug.Trim('-', '_');
    }

    public static string FormatModName(string slug)
    {
        if (_knownModNames.TryGetValue(slug, out var known)) return known;
        return string.Join(" ", slug.Split('_', '-').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    public static string CleanTomlValue(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return "";
        var val = line[(eq + 1)..].Trim();
        bool inQuote = false;
        int hashIdx = -1;
        for (int i = 0; i < val.Length; i++)
        {
            if (val[i] == '"') inQuote = !inQuote;
            if (!inQuote && val[i] == '#') { hashIdx = i; break; }
        }
        if (hashIdx >= 0) val = val[..hashIdx].TrimEnd();
        return val.Trim('"', '\'', ' ', '\r', '\n');
    }

    public (string modName, string? modId, BitmapImage? icon) ExtractModInfo(string jarPath)
    {
        if (_modNameCache.TryGetValue(jarPath, out var cachedName))
        {
            _modIconCache.TryGetValue(jarPath, out var cachedIcon);
            _modIdCache.TryGetValue(jarPath, out var cachedId);
            return (cachedName, cachedId, cachedIcon);
        }

        string name = Path.GetFileNameWithoutExtension(jarPath);
        string? modId = null;
        BitmapImage? icon = null;

        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry is not null)
            {
                using var stream = fabricEntry.Open();
                var doc = JsonNode.Parse(stream);
                name = doc?["name"]?.GetValue<string>() ?? name;
                modId = doc?["id"]?.GetValue<string>();

                var iconPath = doc?["icon"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iconPath))
                {
                    var iconEntry = archive.GetEntry(iconPath);
                    if (iconEntry is not null)
                        icon = LoadIconFromEntry(iconEntry);
                }
            }
            else
            {
                var modsToml = archive.GetEntry("META-INF/mods.toml")
                              ?? archive.GetEntry("META-INF/neoforge.mods.toml");
                if (modsToml is not null)
                {
                    using var reader = new StreamReader(modsToml.Open());
                    var tomlText = reader.ReadToEnd();
                    var lines = tomlText.Split('\n');
                    bool inMods = false;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        var sectionCheck = trimmed.Split('#')[0].Trim();
                        if (sectionCheck == "[[mods]]" || sectionCheck.StartsWith("[[mods.")) inMods = true;
                        else if (sectionCheck.StartsWith("[")) inMods = false;

                        if (inMods && trimmed.StartsWith("displayName"))
                            name = CleanTomlValue(trimmed);
                        if (inMods && trimmed.StartsWith("modId"))
                            modId = CleanTomlValue(trimmed);
                        if (inMods && trimmed.StartsWith("logoFile"))
                        {
                            var logoPath = CleanTomlValue(trimmed);
                            var logoEntry = archive.GetEntry(logoPath);
                            if (logoEntry is not null)
                                icon = LoadIconFromEntry(logoEntry);
                        }
                    }
                }
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        _modNameCache[jarPath] = name;
        _modIconCache[jarPath] = icon;
        if (modId != null) _modIdCache[jarPath] = modId;
        return (name, modId, icon);
    }

    private static BitmapImage? LoadIconFromEntry(ZipArchiveEntry entry)
    {
        try
        {
            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static readonly Dictionary<string, (string? name, BitmapImage? icon)> _modrinthProjectCache = new();

    public async Task<(string? title, BitmapImage? icon)> FetchModrinthProjectAsync(string query, string projectType = "mod")
    {
        var cacheKey = $"{projectType}:{query}";
        if (_modrinthProjectCache.TryGetValue(cacheKey, out var cached)) return cached;

        string? title = null;
        BitmapImage? icon = null;

        try
        {
            var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "VEC Launcher/1.0");
            using var resp = await _iconHttp.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                var doc = JsonNode.Parse(json);
                title = doc?["title"]?.GetValue<string>();
                var iconUrl = doc?["icon_url"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(iconUrl))
                    icon = await DownloadIconAsync(iconUrl, cacheKey);
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        _modrinthProjectCache[cacheKey] = (title, icon);
        return (title, icon);
    }

    private async Task<BitmapImage?> DownloadIconAsync(string url, string cacheKey)
    {
        try
        {
            var data = await _iconHttp.GetByteArrayAsync(url);
            using var ms = new MemoryStream(data);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ── Import ──
    public (int ok, List<string> skipped, List<string> failed) ImportFiles(IEnumerable<string> paths, GameInstance inst)
    {
        InstanceService.EnsureFolders(inst);
        var ok = 0;
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var src in paths)
        {
            try
            {
                if (Directory.Exists(src))
                {
                    var worldDst = Path.Combine(InstanceService.SavesDir(inst), Path.GetFileName(src));
                    if (Directory.Exists(worldDst)) { skipped.Add(Path.GetFileName(src) + " (уже есть)"); continue; }
                    CopyDirectory(src, worldDst);
                    ok++;
                    continue;
                }

                if (!File.Exists(src)) continue;
                var ext = Path.GetExtension(src).ToLowerInvariant();
                var name = Path.GetFileName(src);

                string dstDir = ext switch
                {
                    ".jar" => InstanceService.ModsDir(inst),
                    ".zip" => LooksLikeShaderPack(src)
                        ? InstanceService.ShaderPacksDir(inst)
                        : InstanceService.ResourcePacksDir(inst),
                    _ => ""
                };

                if (string.IsNullOrEmpty(dstDir))
                {
                    skipped.Add(name + " (неизвестный тип)");
                    continue;
                }

                var dst = Path.Combine(dstDir, name);
                if (File.Exists(dst)) { skipped.Add(name + " (уже есть)"); continue; }
                File.Copy(src, dst);
                ok++;
            }
            catch (Exception ex)
            {
                failed.Add(Path.GetFileName(src) + ": " + ex.Message);
            }
        }

        return (ok, skipped, failed);
    }

    private static bool LooksLikeShaderPack(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(entry =>
                entry.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.Contains("/shaders/", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    public static string? ExtractModrinthSlug(string input)
    {
        input = input.Trim();
        if (input.Contains("modrinth.com", StringComparison.OrdinalIgnoreCase))
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                input, @"modrinth\.com/(?:mod|plugin|datapack|resourcepack|shader|modpack)/([A-Za-z0-9._-]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
        return System.Text.RegularExpressions.Regex.IsMatch(input, @"^[A-Za-z0-9._-]{2,64}$")
            ? input : null;
    }
}
