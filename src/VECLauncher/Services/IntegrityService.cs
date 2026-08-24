using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class IntegrityReport
{
    public List<string> Ok { get; } = new();
    public List<string> Problems { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Fixable { get; } = new();

    public bool IsHealthy => Problems.Count == 0;

    public string Summary => Problems.Count == 0 && Warnings.Count == 0
        ? "Instance is healthy - everything in place."
        : $"Problems: {Problems.Count}, Warnings: {Warnings.Count}";
}

public sealed class IntegrityService
{
    private readonly VersionService _versions;

    public IntegrityService(VersionService versions) => _versions = versions;

    public event Action<string>? Status;

    public async Task<IntegrityReport> CheckAsync(GameInstance inst, CancellationToken ct = default)
    {
        var report = new IntegrityReport();
        var paths = GamePaths.ForInstance(inst);

        Status?.Invoke("Checking version files...");

        var jar = paths.VersionJar(inst.McVersion);
        if (File.Exists(jar))
        {
            var size = new FileInfo(jar).Length;
            if (size < 1024 * 1024)
            {
                report.Problems.Add($"client.jar suspiciously small ({size / 1024} KB) - likely corrupt");
                report.Fixable.Add("client");
            }
            else if (!IsValidZip(jar))
            {
                report.Problems.Add("client.jar corrupted (not a valid archive)");
                report.Fixable.Add("client");
            }
            else report.Ok.Add($"client.jar present ({size / 1048576} MB)");
        }
        else
        {
            report.Problems.Add($"client.jar missing for {inst.McVersion}");
            report.Fixable.Add("client");
        }

        var launchId = inst.EffectiveVersionId;
        var json = paths.VersionJson(launchId);

        if (!File.Exists(json))
        {
            report.Problems.Add($"Version profile missing: {launchId}");
            report.Fixable.Add("version");
        }
        else
        {
            try
            {
                var detail = await _versions.ResolveAsync(launchId, ct).ConfigureAwait(false);
                report.Ok.Add($"Version profile readable ({detail.Libraries.Count} libraries)");

                Status?.Invoke("Checking libraries...");

                var missing = 0;
                var checkedCount = 0;

                foreach (var lib in detail.Libraries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!RuleEvaluator.Allows(lib.Rules)) continue;
                    if (RuleEvaluator.IsNativeArtifactName(lib.Name) &&
                        !RuleEvaluator.NativeMatchesCurrentArch(lib.Name)) continue;

                    var rel = lib.Downloads?.Artifact?.Path;
                    if (rel is null)
                    {
                        try { rel = RuleEvaluator.MavenNameToPath(lib.Name); }
                        catch { continue; }
                    }

                    var path = Path.Combine(paths.LibrariesDir,
                        rel.Replace('/', Path.DirectorySeparatorChar));

                    checkedCount++;
                    if (!File.Exists(path)) missing++;
                }

                if (missing > 0)
                {
                    report.Problems.Add($"Missing libraries: {missing} of {checkedCount}");
                    report.Fixable.Add("libraries");
                }
                else report.Ok.Add($"Libraries present ({checkedCount})");

                var nativesDir = DownloadManager.ResolveNativesExtractDir(
                    detail, paths.NativesDir(launchId));

                if (Directory.Exists(nativesDir))
                {
                    var dlls = Directory.GetFiles(nativesDir, "*.dll").Length;
                    if (dlls == 0)
                    {
                        report.Problems.Add("Native libraries not extracted (empty folder)");
                        report.Fixable.Add("natives");
                    }
                    else if (!File.Exists(Path.Combine(nativesDir, "lwjgl.dll")))
                    {
                        report.Problems.Add("lwjgl.dll missing - game won't launch");
                        report.Fixable.Add("natives");
                    }
                    else report.Ok.Add($"Native libraries present ({dlls} files)");
                }
                else
                {
                    report.Problems.Add("Natives folder missing");
                    report.Fixable.Add("natives");
                }

                Status?.Invoke("Checking game assets...");

                if (detail.AssetIndex is not null)
                {
                    var indexPath = Path.Combine(paths.AssetsIndexesDir, detail.AssetIndex.Id + ".json");

                    if (!File.Exists(indexPath))
                    {
                        report.Problems.Add($"Asset index missing: {detail.AssetIndex.Id}");
                        report.Fixable.Add("assets");
                    }
                    else
                    {
                        try
                        {
                            var idx = JsonSerializer.Deserialize<AssetIndexFile>(
                                await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false));

                            if (idx is not null)
                            {
                                var sample = idx.Objects.Values.Take(150).ToList();
                                var lost = sample.Count(o => !File.Exists(
                                    Path.Combine(paths.AssetsObjectsDir, o.TwoLetterPrefix, o.Hash)));

                                if (lost > 0)
                                {
                                    var percent = lost * 100 / Math.Max(1, sample.Count);
                                    report.Problems.Add(
                                        $"Approximately {percent}% of assets missing (sounds, languages)");
                                    report.Fixable.Add("assets");
                                }
                                else report.Ok.Add($"Assets present (checked {sample.Count} of {idx.Objects.Count})");
                            }
                        }
                        catch (Exception ex)
                        {
                            report.Warnings.Add("Asset index unreadable: " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report.Problems.Add("Version profile corrupted: " + ex.Message);
                report.Fixable.Add("version");
            }
        }

        Status?.Invoke("Checking mods...");

        var modsDir = InstanceService.ModsDir(inst);
        if (Directory.Exists(modsDir))
        {
            var jars = Directory.GetFiles(modsDir, "*.jar");
            var broken = jars.Where(f => !IsValidZip(f)).ToList();

            if (broken.Count > 0)
                report.Problems.Add($"Corrupted mods: {string.Join(", ", broken.Select(Path.GetFileName))}");
            else if (jars.Length > 0)
                report.Ok.Add($"Mods readable ({jars.Length} files)");

            if (jars.Length > 0 && inst.Loader == LoaderKind.Vanilla)
                report.Warnings.Add(
                    $"Instance has {jars.Length} mod(s) but no loader installed - they won't load");

            var conflicts = ModInspector.FindConflicts(
                ModInspector.ReadAll(modsDir), inst.Loader);

            foreach (var c in conflicts.Where(x => x.IsError))
                report.Problems.Add(c.Title + " — " + c.Details);

            foreach (var c in conflicts.Where(x => !x.IsError))
                report.Warnings.Add(c.Title);
        }

        var required = JavaService.RequiredJavaFor(inst.McVersion);
        if (!string.IsNullOrWhiteSpace(inst.JavaPath))
        {
            if (!File.Exists(inst.JavaPath))
                report.Problems.Add("Instance-specific java.exe not found");
            else
            {
                var probe = JavaService.Probe(inst.JavaPath, "check");
                if (probe is null) report.Problems.Add("Instance-specific java.exe won't launch");
                else if (probe.MajorVersion < required)
                    report.Warnings.Add(
                        $"Selected Java {probe.MajorVersion}, {inst.McVersion} requires {required}");
                else report.Ok.Add($"Java {probe.MajorVersion} compatible");
            }
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(LauncherPaths.Root)!);
            var freeGb = drive.AvailableFreeSpace / 1073741824.0;

            if (freeGb < 1) report.Problems.Add($"Less than 1 GB free on disk ({freeGb:0.#} GB)");
            else if (freeGb < 3) report.Warnings.Add($"Low disk space: {freeGb:0.#} GB");
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        Status?.Invoke(report.Summary);
        return report;
    }

    private static bool IsValidZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch { return false; }
    }

    public static int Repair(GameInstance inst, IntegrityReport report)
    {
        var removed = 0;
        var paths = GamePaths.ForInstance(inst);

        try
        {
            if (report.Fixable.Contains("client"))
            {
                var jar = paths.VersionJar(inst.McVersion);
                if (File.Exists(jar)) { File.Delete(jar); removed++; }

                var ok = jar + ".ok";
                if (File.Exists(ok)) File.Delete(ok);
            }

            if (report.Fixable.Contains("natives"))
            {
                var dir = paths.NativesDir(inst.EffectiveVersionId);
                if (Directory.Exists(dir)) { Directory.Delete(dir, true); removed++; }
            }

            if (report.Fixable.Contains("version"))
            {
                var dir = paths.VersionDir(inst.EffectiveVersionId);
                if (Directory.Exists(dir)) { Directory.Delete(dir, true); removed++; }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"IntegrityService: repair failed: {ex.Message}");
        }

        return removed;
    }
}