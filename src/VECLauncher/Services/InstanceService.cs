using System.Diagnostics;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public static class InstanceService
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string InstancesRoot => Path.Combine(LauncherPaths.Root, "instances");

    public static string InstanceDir(GameInstance inst) => Path.Combine(InstancesRoot, inst.Id);
    public static string ModsDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "mods");
    public static string ResourcePacksDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "resourcepacks");
    public static string ShaderPacksDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "shaderpacks");
    public static string ScreenshotsDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "screenshots");
    public static string SavesDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "saves");
    public static string LogsDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "logs");
    public static string CrashReportsDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "crash-reports");
    public static string ConfigDir(GameInstance inst) => Path.Combine(InstanceDir(inst), "config");

    private static string IndexFile => Path.Combine(InstancesRoot, "instances.json");

    private static string BackupFile => IndexFile + ".bak";

    public static bool Loaded { get; private set; }

    public static List<GameInstance> LoadAll()
    {
        foreach (var file in new[] { IndexFile, BackupFile })
        {
            try
            {
                if (!File.Exists(file)) continue;

                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json)) continue;

                var list = JsonSerializer.Deserialize<List<GameInstance>>(json);
                if (list is null) continue;

                if (file == BackupFile)
                    Log.Warn("InstanceService: primary instance list corrupted - restored from backup");

                Loaded = true;
                return list;
            }
            catch (Exception ex)
            {
                Log.Warn($"InstanceService: failed to read {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (!File.Exists(IndexFile) && !File.Exists(BackupFile))
        {
            Loaded = true;
            return new List<GameInstance>();
        }

        Loaded = false;
        Log.Error("InstanceService: failed to read instance list. Saving disabled to prevent data loss.");
        return new List<GameInstance>();
    }

    public static void SaveAll(IEnumerable<GameInstance> instances)
    {
        if (!Loaded)
        {
            Log.Warn("InstanceService: save skipped - instance list was not loaded correctly");
            return;
        }

        try
        {
            Directory.CreateDirectory(InstancesRoot);

            var list = instances.ToList();
            var json = JsonSerializer.Serialize(list, Opts);

            if (list.Count == 0 && File.Exists(IndexFile) && new FileInfo(IndexFile).Length > 8)
            {
                Log.Warn("InstanceService: attempted to save empty list over existing data - cancelled");
                return;
            }

            var tmp = IndexFile + ".tmp";
            File.WriteAllText(tmp, json);

            if (File.Exists(IndexFile))
            {
                try { File.Copy(IndexFile, BackupFile, true); } catch (Exception ex) { Log.Warn(ex.Message); }
                File.Replace(tmp, IndexFile, null);
            }
            else
            {
                File.Move(tmp, IndexFile);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"InstanceService: failed to save instance list: {ex.Message}");
        }
    }

    public static List<GameInstance> ScanOrphans(List<GameInstance> known)
    {
        var found = new List<GameInstance>();

        try
        {
            if (!Directory.Exists(InstancesRoot)) return found;

            var knownIds = known.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in Directory.GetDirectories(InstancesRoot))
            {
                var id = Path.GetFileName(dir);
                if (knownIds.Contains(id)) continue;

                var looksLikeInstance =
                    Directory.Exists(Path.Combine(dir, "mods")) ||
                    Directory.Exists(Path.Combine(dir, "saves")) ||
                    Directory.Exists(Path.Combine(dir, ".minecraft")) ||
                    File.Exists(Path.Combine(dir, "options.txt"));

                if (!looksLikeInstance) continue;

                var mcVersion = "";
                var isolated = Directory.Exists(Path.Combine(dir, ".minecraft"));
                var versionsDir = Path.Combine(dir, ".minecraft", "versions");

                if (Directory.Exists(versionsDir))
                {
                    var vers = Directory.GetDirectories(versionsDir).Select(Path.GetFileName).ToList();
                    mcVersion = vers.FirstOrDefault(v => v is not null &&
                                    VersionService.ParseMcVersion(v) is not null) ?? "";
                }

                if (string.IsNullOrEmpty(mcVersion))
                    mcVersion = GameOptionsService.GetLanguage(dir) is not null ? "1.20.1" : "1.20.1";

                found.Add(new GameInstance
                {
                    Id = id,
                    Name = $"Minecraft {mcVersion}",
                    McVersion = mcVersion,
                    Loader = LoaderKind.Vanilla,
                    LaunchVersionId = mcVersion,
                    Isolated = isolated,
                    IconColor = "#38BDF8"
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"InstanceService: failed to scan instance folders: {ex.Message}");
        }

        return found;
    }

    public static void EnsureFolders(GameInstance inst)
    {
        foreach (var d in new[]
                 {
                     InstanceDir(inst), ModsDir(inst), ResourcePacksDir(inst), ShaderPacksDir(inst),
                     ScreenshotsDir(inst), SavesDir(inst), LogsDir(inst), ConfigDir(inst)
                 })
        {
            Directory.CreateDirectory(d);
        }
    }

    public static void Delete(GameInstance inst, bool deleteFiles)
    {
        if (!deleteFiles) return;

        try
        {
            if (Directory.Exists(InstanceDir(inst)))
                Directory.Delete(InstanceDir(inst), true);
        }
        catch (Exception ex)
        {
            Log.Warn($"InstanceService: failed to delete instance folder: {ex.Message}");
            throw new IOException(
                "Failed to delete instance folder. Files may be in use by another program.", ex);
        }
    }

    public sealed class FolderStats
    {
        public int Mods { get; init; }
        public int ResourcePacks { get; init; }
        public int ShaderPacks { get; init; }
        public int Screenshots { get; init; }
        public int Worlds { get; init; }
        public long TotalBytes { get; init; }

        public string SizeDisplay
        {
            get
            {
                string[] u = { "B", "KB", "MB", "GB" };
                double v = TotalBytes;
                var i = 0;
                while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
                return $"{v:0.#} {u[i]}";
            }
        }
    }

    public static FolderStats GetStats(GameInstance inst)
    {
        int Count(string dir, params string[] patterns)
        {
            if (!Directory.Exists(dir)) return 0;
            try
            {
                return patterns.Length == 0
                    ? Directory.GetFiles(dir).Length
                    : patterns.Sum(p => Directory.GetFiles(dir, p).Length);
            }
            catch { return 0; }
        }

        long size = 0;
        try
        {
            var d = InstanceDir(inst);
            if (Directory.Exists(d))
                size = new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        var worlds = 0;
        try
        {
            if (Directory.Exists(SavesDir(inst)))
                worlds = Directory.GetDirectories(SavesDir(inst)).Length;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return new FolderStats
        {
            Mods = Count(ModsDir(inst), "*.jar", "*.disabled"),
            ResourcePacks = Count(ResourcePacksDir(inst), "*.zip"),
            ShaderPacks = Count(ShaderPacksDir(inst), "*.zip"),
            Screenshots = Count(ScreenshotsDir(inst), "*.png"),
            Worlds = worlds,
            TotalBytes = size
        };
    }

    public static List<FileInfo> GetScreenshots(GameInstance inst, int limit = 200)
    {
        var dir = ScreenshotsDir(inst);
        if (!Directory.Exists(dir)) return new List<FileInfo>();

        try
        {
            return new DirectoryInfo(dir)
                .GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(limit)
                .ToList();
        }
        catch { return new List<FileInfo>(); }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"InstanceService: failed to open folder {path}: {ex.Message}");
            throw new IOException("Failed to open folder: " + ex.Message, ex);
        }
    }

    public static void RevealFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) { OpenFolder(Path.GetDirectoryName(filePath)!); return; }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"InstanceService: failed to reveal file: {ex.Message}");
        }
    }
}