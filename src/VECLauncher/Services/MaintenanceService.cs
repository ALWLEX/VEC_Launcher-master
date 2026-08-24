using System.Diagnostics;
using System.Text;

namespace VECLauncher.Services;

public static class MaintenanceService
{
    public enum CleanTarget
    {
        Cache,
        ImageCache,
        Logs,
        Versions,
        Libraries,
        Assets,
        JavaRuntime,
        Instances,
        Settings,
        Account
    }

    public sealed class TargetInfo
    {
        public required CleanTarget Target { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Path { get; init; }
        public long Size { get; set; }
        public bool IsFile { get; init; }
        public bool Dangerous { get; init; }

        public string SizeDisplay
        {
            get
            {
                string[] u = { "B", "KB", "MB", "GB" };
                double v = Size;
                var i = 0;
                while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
                return $"{v:0.#} {u[i]}";
            }
        }
    }

    public static List<TargetInfo> Enumerate()
    {
        var list = new List<TargetInfo>
        {
            new()
            {
                Target = CleanTarget.Cache, Title = "Temporary Files",
                Description = "Loader installers, manifest cache. Safe to delete.",
                Path = LauncherPaths.CacheDir
            },
            new()
            {
                Target = CleanTarget.ImageCache, Title = "Image Cache",
                Description = "Mod and server icons. Will be re-downloaded.",
                Path = System.IO.Path.Combine(LauncherPaths.CacheDir, "images")
            },
            new()
            {
                Target = CleanTarget.Logs, Title = "Launcher Logs",
                Description = "Event history. Doesn't affect functionality.",
                Path = LauncherPaths.LauncherLogFile, IsFile = true
            },
            new()
            {
                Target = CleanTarget.JavaRuntime, Title = "Downloaded Java",
                Description = "Portable JREs. Will be re-downloaded on launch.",
                Path = LauncherPaths.RuntimeDir
            },
            new()
            {
                Target = CleanTarget.Assets, Title = "Game Assets",
                Description = "Sounds, languages, textures. The largest folder.",
                Path = LauncherPaths.AssetsDir
            },
            new()
            {
                Target = CleanTarget.Libraries, Title = "Libraries",
                Description = "Shared JAR files for all versions.",
                Path = LauncherPaths.LibrariesDir
            },
            new()
            {
                Target = CleanTarget.Versions, Title = "Game Versions",
                Description = "Minecraft clients and loader profiles.",
                Path = LauncherPaths.VersionsDir
            },
            new()
            {
                Target = CleanTarget.Instances, Title = "Instances (Full)",
                Description = "MODS, WORLDS, SCREENSHOTS AND SETTINGS. Cannot be restored.",
                Path = InstanceService.InstancesRoot, Dangerous = true
            },
            new()
            {
                Target = CleanTarget.Settings, Title = "Launcher Settings",
                Description = "Theme, memory, paths. Will reset to defaults.",
                Path = LauncherPaths.SettingsFile, IsFile = true
            },
            new()
            {
                Target = CleanTarget.Account, Title = "Account",
                Description = "Saved profile. You'll need to log in again.",
                Path = LauncherPaths.AccountFile, IsFile = true
            }
        };

        foreach (var t in list) t.Size = Measure(t.Path, t.IsFile);
        return list;
    }

    private static long Measure(string path, bool isFile)
    {
        try
        {
            if (isFile) return File.Exists(path) ? new FileInfo(path).Length : 0;

            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch { return 0; }
    }

    public static long Clean(IEnumerable<TargetInfo> targets)
    {
        long freed = 0;

        foreach (var t in targets)
        {
            try
            {
                var size = Measure(t.Path, t.IsFile);

                if (t.IsFile)
                {
                    if (File.Exists(t.Path)) File.Delete(t.Path);

                    foreach (var extra in new[] { t.Path + ".bak", t.Path + ".tmp" })
                        if (File.Exists(extra)) File.Delete(extra);
                }
                else if (Directory.Exists(t.Path))
                {
                    Directory.Delete(t.Path, true);
                }

                freed += size;
                Log.Info($"MaintenanceService: cleaned {t.Title} ({size / 1048576} MB)");
            }
            catch (Exception ex)
            {
                Log.Warn($"MaintenanceService: failed to clean {t.Title}: {ex.Message}");
            }
        }

        LauncherPaths.EnsureAll();
        return freed;
    }

    public static string PrepareUninstall(bool removeExe)
    {
        var root = LauncherPaths.Root;
        var exe = Environment.ProcessPath ?? "";

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("chcp 65001 >nul");
        script.AppendLine("echo Uninstalling VEC Launcher...");

        script.AppendLine("timeout /t 2 /nobreak >nul");

        script.AppendLine($"rmdir /s /q \"{root}\" 2>nul");

        if (removeExe && exe.Length > 0 && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            script.AppendLine("timeout /t 1 /nobreak >nul");
            script.AppendLine($"del /f /q \"{exe}\" 2>nul");
        }

        script.AppendLine("echo Done. Window will close automatically.");
        script.AppendLine("timeout /t 3 /nobreak >nul");
        script.AppendLine("del \"%~f0\" 2>nul");

        var path = Path.Combine(Path.GetTempPath(), "mayslauncher_uninstall.bat");
        File.WriteAllText(path, script.ToString(), Encoding.UTF8);

        return path;
    }

    public static void RunUninstall(string scriptPath)
    {
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Minimized
        });
    }

    public static long TotalSize() => Measure(LauncherPaths.Root, false);
}