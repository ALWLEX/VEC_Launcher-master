using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VECLauncher.Models;

namespace VECLauncher.Services;

public static class OfflineSkinService
{
    private const string CslProject = "customskinloader";
    private const string CslLocalSkins = "CustomSkinLoader/LocalSkin/skins";
    private const string CslLocalCapes = "CustomSkinLoader/LocalSkin/capes";

    public static string AccountSkinPath(string username) =>
        Path.Combine(LauncherPaths.Root, "skins", username + ".png");

    public static string AccountCapePath(string username) =>
        Path.Combine(LauncherPaths.Root, "capes", username + ".png");

    public static string? FindAccountSkin(string username)
    {
        var exact = AccountSkinPath(username);
        if (File.Exists(exact)) return exact;

        var dir = Path.Combine(LauncherPaths.Root, "skins");
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.png")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), username, StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindAccountCape(string username)
    {
        var exact = AccountCapePath(username);
        if (File.Exists(exact)) return exact;

        var dir = Path.Combine(LauncherPaths.Root, "capes");
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.png")
            .FirstOrDefault(f => string.Equals(
                Path.GetFileNameWithoutExtension(f), username, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsCslSupported(GameInstance inst) => inst.Loader != LoaderKind.Vanilla;

    public static bool IsCslInstalled(GameInstance inst)
    {
        var modsDir = InstanceService.ModsDir(inst);
        if (!Directory.Exists(modsDir)) return false;
        return Directory.GetFiles(modsDir, "*.jar")
            .Any(f => Path.GetFileName(f).Contains("customskinloader", StringComparison.OrdinalIgnoreCase));
    }

    public static void SyncToInstance(GameInstance inst, string username, string? skinFile, string? capeFile = null, bool isSlim = false)
    {
        var instDir = InstanceService.InstanceDir(inst);
        var skinsDir = Path.Combine(instDir, CslLocalSkins);
        var capesDir = Path.Combine(instDir, CslLocalCapes);

        Directory.CreateDirectory(skinsDir);
        Directory.CreateDirectory(capesDir);

        if (!string.IsNullOrEmpty(skinFile) && File.Exists(skinFile))
        {
            File.Copy(skinFile, Path.Combine(skinsDir, username + ".png"), overwrite: true);
            File.Copy(skinFile, Path.Combine(skinsDir, username.ToLowerInvariant() + ".png"), overwrite: true);

            var modelJson = JsonSerializer.Serialize(new { model = isSlim ? Constants.SkinModel.Slim : "default" });
            File.WriteAllText(Path.Combine(skinsDir, username + ".json"), modelJson);
            File.WriteAllText(Path.Combine(skinsDir, username.ToLowerInvariant() + ".json"), modelJson);
        }

        if (!string.IsNullOrEmpty(capeFile) && File.Exists(capeFile))
        {
            File.Copy(capeFile, Path.Combine(capesDir, username + ".png"), overwrite: true);
            File.Copy(capeFile, Path.Combine(capesDir, username.ToLowerInvariant() + ".png"), overwrite: true);
        }

        try
        {
            var cacheDir = Path.Combine(instDir, "CustomSkinLoader", "caches");
            if (Directory.Exists(cacheDir))
            {
                foreach (var f in Directory.GetFiles(cacheDir))
                {
                    try { File.Delete(f); } catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        EnsureCslConfig(instDir);
    }

    public static void RemoveFromInstance(GameInstance inst, string username)
    {
        var instDir = InstanceService.InstanceDir(inst);
        var skinsDir = Path.Combine(instDir, CslLocalSkins);
        var capesDir = Path.Combine(instDir, CslLocalCapes);

        foreach (var name in new[] { username, username.ToLowerInvariant() })
        {
            if (Directory.Exists(skinsDir))
            {
                var sFile = Path.Combine(skinsDir, name + ".png");
                if (File.Exists(sFile)) File.Delete(sFile);
                var jFile = Path.Combine(skinsDir, name + ".json");
                if (File.Exists(jFile)) File.Delete(jFile);
            }
            if (Directory.Exists(capesDir))
            {
                var cFile = Path.Combine(capesDir, name + ".png");
                if (File.Exists(cFile)) File.Delete(cFile);
            }
        }
    }

    public static void RemoveCapeFromInstance(GameInstance inst, string username)
    {
        var instDir = InstanceService.InstanceDir(inst);
        var capesDir = Path.Combine(instDir, CslLocalCapes);
        if (!Directory.Exists(capesDir)) return;

        foreach (var name in new[] { username, username.ToLowerInvariant() })
        {
            var cFile = Path.Combine(capesDir, name + ".png");
            if (File.Exists(cFile)) File.Delete(cFile);
        }

        ClearCslCache(instDir);
    }

    public static void ClearCslCache(string instanceDir)
    {
        try
        {
            var cacheDir = Path.Combine(instanceDir, "CustomSkinLoader", "caches");
            if (Directory.Exists(cacheDir))
            {
                foreach (var f in Directory.GetFiles(cacheDir))
                {
                    try { File.Delete(f); } catch (Exception ex) { Log.Warn(ex.Message); }
                }
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    private static void EnsureCslConfig(string instanceDir)
    {
        try
        {
            var configDir = Path.Combine(instanceDir, "CustomSkinLoader");
            Directory.CreateDirectory(configDir);
            var configFile = Path.Combine(configDir, "CustomSkinLoader.json");

            var cslConfig = new
            {
                version = "14.21",
                loadlist = new object[]
                {
                    new { name = "LocalSkin", type = "Legacy", checkPNG = true,
                          skin = "LocalSkin/skins/{USERNAME}.png",
                          model = "auto",
                          cape = "LocalSkin/capes/{USERNAME}.png",
                          elytra = "LocalSkin/elytras/{USERNAME}.png" },
                    new { name = "VecCslLocal", type = "CustomSkinAPI", root = "http://localhost:8080/api/csl/" },
                    new { name = "VecCslRemote", type = "CustomSkinAPI", root = "http://95.59.233.227:8080/api/csl/" },
                    new { name = "Mojang", type = "MojangAPI",
                          apiRoot = "https://api.mojang.com/",
                          sessionRoot = "https://sessionserver.mojang.com/" }
                }
            };

            File.WriteAllText(configFile, JsonSerializer.Serialize(cslConfig, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Warn(ex.Message); }
    }

    public static async Task<bool> EnsureCslModAsync(GameInstance inst, CancellationToken ct = default)
    {
        if (!IsCslSupported(inst)) return false;
        if (IsCslInstalled(inst)) return true;
        if (string.IsNullOrEmpty(inst.McVersion)) return false;

        var loader = inst.Loader switch
        {
            LoaderKind.Fabric => "fabric",
            LoaderKind.Forge => "forge",
            LoaderKind.NeoForge => "neoforge",
            _ => null
        };
        if (loader == null) return false;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add("User-Agent", "VEC Launcher/1.0");

            var url = $"https://api.modrinth.com/v2/project/{CslProject}/version" +
                      $"?game_versions={Uri.EscapeDataString("[\"" + inst.McVersion + "\"]")}" +
                      $"&loaders={Uri.EscapeDataString("[\"" + loader + "\"]")}";

            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;

            string? fileUrl = null;
            foreach (var v in doc.RootElement.EnumerateArray())
            {
                if (!v.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) continue;
                foreach (var f in files.EnumerateArray())
                {
                    if (f.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    {
                        fileUrl = u.GetString();
                        break;
                    }
                }
                if (fileUrl != null) break;
            }

            if (string.IsNullOrEmpty(fileUrl)) return false;

            Directory.CreateDirectory(InstanceService.ModsDir(inst));
            var target = Path.Combine(InstanceService.ModsDir(inst), "CustomSkinLoader.jar");
            var data = await http.GetByteArrayAsync(fileUrl, ct);
            await File.WriteAllBytesAsync(target, data, ct);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"OfflineSkinService: failed to install CustomSkinLoader: {ex.Message}");
            return false;
        }
    }
}