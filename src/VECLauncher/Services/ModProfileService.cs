using System.IO.Compression;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public static class ModProfileService
{
    private const string DefaultProfile = "По умолчанию";

    private static string ProfilesRoot(GameInstance inst) =>
        Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles");

    private static string ProfileDir(GameInstance inst, string name) =>
        Path.Combine(ProfilesRoot(inst), Sanitize(name));

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    public static List<string> List(GameInstance inst)
    {
        var result = new List<string> { DefaultProfile };

        try
        {
            var root = ProfilesRoot(inst);
            if (!Directory.Exists(root)) return result;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (!string.Equals(name, DefaultProfile, StringComparison.OrdinalIgnoreCase))
                    result.Add(name);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"ModProfileService: failed to read mod profiles: {ex.Message}");
        }

        return result;
    }

    public static int CountMods(GameInstance inst, string profile)
    {
        try
        {
            var dir = string.Equals(profile, inst.ActiveModProfile, StringComparison.OrdinalIgnoreCase)
                ? InstanceService.ModsDir(inst)
                : ProfileDir(inst, profile);

            if (!Directory.Exists(dir)) return 0;

            return Directory.GetFiles(dir, "*.jar").Length +
                   Directory.GetFiles(dir, "*.jar.disabled").Length;
        }
        catch { return 0; }
    }

    public static void Create(GameInstance inst, string name, bool copyCurrent)
    {
        var dir = ProfileDir(inst, name);

        if (Directory.Exists(dir))
            throw new InvalidOperationException($"Профиль «{name}» уже существует.");

        Directory.CreateDirectory(dir);

        if (!copyCurrent) return;

        var mods = InstanceService.ModsDir(inst);
        if (!Directory.Exists(mods)) return;

        foreach (var f in Directory.GetFiles(mods))
        {
            try { File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true); }
            catch (Exception ex) { Log.Warn($"ModProfileService: copy {Path.GetFileName(f)}: {ex.Message}"); }
        }
    }

    public static void Switch(GameInstance inst, string targetProfile)
    {
        if (string.Equals(inst.ActiveModProfile, targetProfile, StringComparison.OrdinalIgnoreCase))
            return;

        var mods = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(mods);

        var oldDir = ProfileDir(inst, inst.ActiveModProfile);
        Directory.CreateDirectory(oldDir);

        foreach (var f in Directory.GetFiles(mods))
        {
            var dst = Path.Combine(oldDir, Path.GetFileName(f));
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(f, dst);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Не удалось убрать «{Path.GetFileName(f)}» в профиль. " +
                    "Возможно, игра запущена.", ex);
            }
        }

        var newDir = ProfileDir(inst, targetProfile);
        Directory.CreateDirectory(newDir);

        foreach (var f in Directory.GetFiles(newDir))
        {
            var dst = Path.Combine(mods, Path.GetFileName(f));
            try
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(f, dst);
            }
            catch (Exception ex) { Log.Warn($"ModProfileService: restore {Path.GetFileName(f)}: {ex.Message}"); }
        }

        inst.ActiveModProfile = targetProfile;
        Log.Info($"ModProfileService: instance '{inst.Name}' switched to profile '{targetProfile}'");
    }

    public static void Delete(GameInstance inst, string name)
    {
        if (string.Equals(name, DefaultProfile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Профиль «По умолчанию» удалить нельзя.");

        if (string.Equals(name, inst.ActiveModProfile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Нельзя удалить активный профиль. Сначала переключитесь на другой.");

        var dir = ProfileDir(inst, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public static void Rename(GameInstance inst, string oldName, string newName)
    {
        if (string.Equals(oldName, DefaultProfile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Профиль «По умолчанию» переименовать нельзя.");

        var from = ProfileDir(inst, oldName);
        var to = ProfileDir(inst, newName);

        if (Directory.Exists(to))
            throw new InvalidOperationException($"Профиль «{newName}» уже существует.");

        if (Directory.Exists(from)) Directory.Move(from, to);

        if (string.Equals(inst.ActiveModProfile, oldName, StringComparison.OrdinalIgnoreCase))
            inst.ActiveModProfile = newName;
    }
}

public sealed class ModConflict
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Details { get; init; }
    public List<string> Files { get; init; } = new();
    public bool IsError { get; init; } = true;
}

public sealed class LocalModInfo
{
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public string ModId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
    public List<string> GameVersions { get; set; } = new List<string>();
    public bool Enabled { get; set; } = true;
}

public static class ModInspector
{
    public static LocalModInfo Read(string jarPath)
    {
        var info = new LocalModInfo
        {
            FilePath = jarPath,
            FileName = Path.GetFileName(jarPath),
            Enabled = !jarPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
        };

        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            var fabric = zip.GetEntry("fabric.mod.json") ?? zip.GetEntry("quilt.mod.json");
            if (fabric is not null)
            {
                using var s = fabric.Open();
                using var doc = JsonDocument.Parse(s, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

                var root = doc.RootElement;

                if (root.TryGetProperty("quilt_loader", out var ql)) root = ql;

                info.ModId = Str(root, "id") ?? "";
                info.Name = Str(root, "name") ?? Path.GetFileNameWithoutExtension(jarPath);
                info.Version = Str(root, "version") ?? "";
                info.Loader = LoaderKind.Fabric;
                return info;
            }

            var toml = zip.GetEntry("META-INF/mods.toml")
                       ?? zip.GetEntry("META-INF/neoforge.mods.toml");

            if (toml is not null)
            {
                using var reader = new StreamReader(toml.Open());
                var text = reader.ReadToEnd();

                info.ModId = TomlValue(text, "modId");
                info.Name = TomlValue(text, "displayName") is { Length: > 0 } dn
                    ? dn : Path.GetFileNameWithoutExtension(jarPath);
                info.Version = TomlValue(text, "version");
                info.Loader = toml.FullName.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                    ? LoaderKind.NeoForge : LoaderKind.Forge;
                return info;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"ModInspector: reading mod {Path.GetFileName(jarPath)}: {ex.Message}");
        }

        info.Name = Path.GetFileNameWithoutExtension(jarPath);
        return info;
    }

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string TomlValue(string toml, string key)
    {
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#')) continue;
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var value = line[(eq + 1)..].Trim().Trim('"', '\'', ' ');

            if (value.StartsWith("${")) return "";

            return value;
        }

        return "";
    }

    public static List<LocalModInfo> ReadAll(string modsDir)
    {
        var list = new List<LocalModInfo>();

        try
        {
            if (!Directory.Exists(modsDir)) return list;

            foreach (var f in Directory.GetFiles(modsDir))
            {
                if (!f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                    !f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) continue;

                list.Add(Read(f));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"ModInspector: scanning mods: {ex.Message}");
        }

        return list;
    }

    public static List<ModConflict> FindConflicts(List<LocalModInfo> mods, LoaderKind expectedLoader)
    {
        var conflicts = new List<ModConflict>();
        var active = mods.Where(m => m.Enabled).ToList();

        foreach (var group in active
                     .Where(m => m.ModId.Length > 0)
                     .GroupBy(m => m.ModId, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            conflicts.Add(new ModConflict
            {
                Kind = "duplicate",
                Title = $"Дубликат мода: {group.First().Name}",
                Details = $"Идентификатор «{group.Key}» встречается {group.Count()} раза. " +
                          "Игра не запустится — оставьте одну версию.",
                Files = group.Select(m => m.FileName).ToList()
            });
        }

        if (expectedLoader != LoaderKind.Vanilla)
        {
            var wrong = active
                .Where(m => m.Loader != LoaderKind.Vanilla && m.Loader != expectedLoader)
                .ToList();

            foreach (var m in wrong)
            {
                var soft = expectedLoader == LoaderKind.NeoForge && m.Loader == LoaderKind.Forge;

                conflicts.Add(new ModConflict
                {
                    Kind = "loader",
                    Title = $"{m.Name}: мод для {m.Loader.Display()}",
                    Details = soft
                        ? $"Сборка использует {expectedLoader.Display()}. Forge-моды иногда работают, но не всегда."
                        : $"Сборка использует {expectedLoader.Display()} — этот мод не загрузится.",
                    Files = new List<string> { m.FileName },
                    IsError = !soft
                });
            }
        }

        var unknown = active.Where(m => m.ModId.Length == 0).ToList();
        if (unknown.Count > 0)
        {
            conflicts.Add(new ModConflict
            {
                Kind = "unknown",
                Title = $"Не удалось определить {unknown.Count} файл(ов)",
                Details = "Внутри нет fabric.mod.json или mods.toml. " +
                          "Возможно, это библиотека или файл повреждён.",
                Files = unknown.Select(m => m.FileName).ToList(),
                IsError = false
            });
        }

        return conflicts;
    }
}