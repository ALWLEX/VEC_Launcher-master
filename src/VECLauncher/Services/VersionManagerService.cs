using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class InstalledVersion
{
    public required string Id { get; init; }
    public required string Directory { get; init; }
    public long SizeBytes { get; init; }
    public DateTime Installed { get; init; }
    public bool HasJar { get; init; }
    public string? InheritsFrom { get; init; }
    public bool IsIsolated { get; init; }
    public string? OwnerInstance { get; init; }

    public List<string> UsedBy { get; init; } = new();

    public bool InUse => UsedBy.Count > 0;

    public string SizeDisplay
    {
        get
        {
            string[] u = { "B", "KB", "MB", "GB" };
            double v = SizeBytes;
            var i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.#} {u[i]}";
        }
    }

    public string Kind
    {
        get
        {
            var lower = Id.ToLowerInvariant();
            if (lower.Contains("fabric")) return "Fabric";
            if (lower.Contains("neoforge")) return "NeoForge";
            if (lower.Contains("forge")) return "Forge";
            return "Vanilla";
        }
    }
}

public static class VersionManagerService
{
    public static List<InstalledVersion> Scan(List<GameInstance> instances)
    {
        var result = new List<InstalledVersion>();

        result.AddRange(ScanDir(LauncherPaths.VersionsDir, instances, isolated: false, owner: null));

        foreach (var inst in instances.Where(i => i.Isolated))
        {
            var paths = GamePaths.ForInstance(inst);
            result.AddRange(ScanDir(paths.VersionsDir, instances, isolated: true, owner: inst.Name));
        }

        return result.OrderByDescending(v => v.SizeBytes).ToList();
    }

    private static List<InstalledVersion> ScanDir(
        string versionsDir, List<GameInstance> instances, bool isolated, string? owner)
    {
        var list = new List<InstalledVersion>();

        try
        {
            if (!Directory.Exists(versionsDir)) return list;

            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var id = Path.GetFileName(dir);
                var json = Path.Combine(dir, id + ".json");
                if (!File.Exists(json)) continue;

                long size = 0;
                try
                {
                    size = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => f.Length);
                }
                catch (Exception ex) { Log.Warn(ex.Message); }

                string? inherits = null;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
                    if (doc.RootElement.TryGetProperty("inheritsFrom", out var inh))
                        inherits = inh.GetString();
                }
                catch (Exception ex) { Log.Warn(ex.Message); }

                var usedBy = instances
                    .Where(i =>
                    {
                        if (isolated && !string.Equals(i.Name, owner, StringComparison.Ordinal)) return false;
                        if (!isolated && i.Isolated) return false;

                        return string.Equals(i.McVersion, id, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(i.LaunchVersionId, id, StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(i => i.Name)
                    .ToList();

                list.Add(new InstalledVersion
                {
                    Id = id,
                    Directory = dir,
                    SizeBytes = size,
                    Installed = Directory.GetCreationTime(dir),
                    HasJar = File.Exists(Path.Combine(dir, id + ".jar")),
                    InheritsFrom = inherits,
                    IsIsolated = isolated,
                    OwnerInstance = owner,
                    UsedBy = usedBy
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"VersionManagerService: scanning versions in {versionsDir}: {ex.Message}");
        }

        return list;
    }

    public static long Delete(InstalledVersion version)
    {
        var freed = version.SizeBytes;

        if (Directory.Exists(version.Directory))
            Directory.Delete(version.Directory, true);

        try
        {
            var natives = Path.Combine(LauncherPaths.NativesRoot, version.Id);
            if (Directory.Exists(natives))
            {
                try { freed += new DirectoryInfo(natives).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
                catch (Exception ex) { Log.Warn(ex.Message); }
                Directory.Delete(natives, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"VersionManagerService: failed to delete natives for {version.Id}: {ex.Message}");
        }

        Log.Info($"VersionManagerService: version {version.Id} deleted, freed {freed / 1048576} MB.");
        return freed;
    }

    public static long CleanupUnusedLibraries()
    {
        long freed = 0;

        try
        {
            if (!Directory.Exists(LauncherPaths.LibrariesDir)) return 0;
            RemoveEmptyDirs(LauncherPaths.LibrariesDir, ref freed);
        }
        catch (Exception ex)
        {
            Log.Warn($"VersionManagerService: library cleanup: {ex.Message}");
        }

        return freed;
    }

    private static void RemoveEmptyDirs(string root, ref long freed)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            RemoveEmptyDirs(dir, ref freed);

            try
            {
                if (Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    freed += 4096;
                }
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }
    }
}