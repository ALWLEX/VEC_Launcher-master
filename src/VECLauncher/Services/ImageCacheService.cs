using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace VECLauncher.Services;

public static class ImageCacheService
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Memory = new();
    private static readonly SemaphoreSlim Limiter = new(6);

    private static string DiskDir => Path.Combine(LauncherPaths.CacheDir, "images");

    public static BitmapImage? TryGetCached(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Memory.TryGetValue(url, out var img) ? img : null;
    }

    public static async Task<BitmapImage?> GetAsync(
        string? url, HttpClient http, int decodeWidth = 128, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Memory.TryGetValue(url!, out var cached)) return cached;

        try
        {
            var path = DiskPathFor(url!);
            byte[]? bytes = null;

            if (File.Exists(path))
            {
                try { bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false); }
                catch { bytes = null; }
            }

            if (bytes is null || bytes.Length == 0)
            {
                await Limiter.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("User-Agent", "VEC Launcher/1.0");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    using var resp = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return null;

                    bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                }
                finally { Limiter.Release(); }

                if (bytes.Length == 0) return null;

                try
                {
                    Directory.CreateDirectory(DiskDir);
                    await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }

            return Decode(url!, bytes, decodeWidth);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Log.Warn($"Failed to load image {url}: {ex.Message}");
            return null;
        }
    }

    private static BitmapImage? Decode(string url, byte[] bytes, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);

            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            Memory[url] = bmp;
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn($"Corrupt image {url}: {ex.Message}");
            return null;
        }
    }

    private static string DiskPathFor(string url)
    {
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".img";
        return Path.Combine(DiskDir, hash + ext);
    }

    public static void ClearMemory() => Memory.Clear();

    public static long DiskCacheSize()
    {
        try
        {
            if (!Directory.Exists(DiskDir)) return 0;
            return new DirectoryInfo(DiskDir).EnumerateFiles().Sum(f => f.Length);
        }
        catch { return 0; }
    }
}