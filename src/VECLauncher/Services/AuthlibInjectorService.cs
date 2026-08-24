using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VECLauncher.Services;

public static class AuthlibInjectorService
{
    private const string PrimaryDownloadUrl = "https://authlib-injector.yushi.moe/artifact/latest/authlib-injector.jar";
    private const string BackupDownloadUrl = "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.5/authlib-injector-1.2.5.jar";

    public static string JarPath => Path.Combine(LauncherPaths.Root, "authlib-injector.jar");

    public static bool IsInstalled => File.Exists(JarPath) && new FileInfo(JarPath).Length > 10000;

    public static async Task<string?> EnsureInstalledAsync(CancellationToken ct = default)
    {
        if (IsInstalled) return JarPath;

        try
        {
            LauncherPaths.EnsureAll();
            var tmpPath = JarPath + ".tmp";

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.Add("User-Agent", "VECLauncher/1.0 (AuthlibInjector)");

            byte[]? data = null;

            try
            {
                data = await http.GetByteArrayAsync(PrimaryDownloadUrl, ct);
            }
            catch
            {
                try
                {
                    data = await http.GetByteArrayAsync(BackupDownloadUrl, ct);
                }
                catch (Exception ex)
                {
                    Log.Warn("Failed to download authlib-injector: " + ex.Message);
                    return null;
                }
            }

            if (data != null && data.Length > 10000)
            {
                await File.WriteAllBytesAsync(tmpPath, data, ct);
                File.Move(tmpPath, JarPath, overwrite: true);
                Log.Info($"authlib-injector successfully saved to {JarPath} ({data.Length} bytes)");
                return JarPath;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Error installing authlib-injector: " + ex.Message);
        }

        return IsInstalled ? JarPath : null;
    }

    public static async Task<bool> IsServerAvailableAsync(string serverUrl, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            var yggUrl = cleanUrl.EndsWith("/api/yggdrasil", StringComparison.OrdinalIgnoreCase)
                ? cleanUrl
                : $"{cleanUrl}/api/yggdrasil";

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2.5) };
            using var resp = await http.GetAsync(yggUrl, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildJvmArg(string jarPath, string serverUrl)
    {
        var cleanUrl = serverUrl.TrimEnd('/');
        var yggUrl = cleanUrl.EndsWith("/api/yggdrasil", StringComparison.OrdinalIgnoreCase) 
            ? cleanUrl 
            : $"{cleanUrl}/api/yggdrasil";

        return $"-javaagent:{jarPath}={yggUrl}";
    }
}