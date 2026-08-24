using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class SkinService
{
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private const string SkinsUrl = "https://api.minecraftservices.com/minecraft/profile/skins";
    private const string ActiveSkinUrl = "https://api.minecraftservices.com/minecraft/profile/skins/active";
    private const string CapeActiveUrl = "https://api.minecraftservices.com/minecraft/profile/capes/active";

    private readonly HttpClient _http;

    public SkinService(HttpClient http) => _http = http;

    public static string BodyRenderUrl(string uuid, int scale = 10) =>
        $"https://crafatar.com/renders/body/{Clean(uuid)}?scale={scale}&overlay&default=MHF_Steve";

    public static string HeadRenderUrl(string uuid, int scale = 10) =>
        $"https://crafatar.com/renders/head/{Clean(uuid)}?scale={scale}&overlay&default=MHF_Steve";

    public static string AvatarUrl(string uuid, int size = 64) =>
        $"https://crafatar.com/avatars/{Clean(uuid)}?size={size}&overlay&default=MHF_Steve";

    public static string RawSkinUrl(string uuid) =>
        $"https://crafatar.com/skins/{Clean(uuid)}?default=MHF_Steve";

    public static string FallbackBodyRenderUrl(string username) =>
        $"https://minotar.net/armor/body/{Uri.EscapeDataString(username)}/220.png";

    public static string AvatarByNameUrl(string username, int size = 64) =>
        $"https://minotar.net/helm/{Uri.EscapeDataString(username)}/{size}.png";

    private static string Clean(string uuid) => uuid.Replace("-", "").Trim();

    public sealed class MojangTexturesResult
    {
        public string? SkinUrl { get; set; }
        public string? SkinModel { get; set; }
        public string? CapeUrl { get; set; }
    }

    public async Task<MojangTexturesResult?> FetchMojangTexturesAsync(string uuid, CancellationToken ct = default)
    {
        try
        {
            var cleanUuid = Clean(uuid);
            if (string.IsNullOrEmpty(cleanUuid)) return null;

            var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{cleanUuid}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("properties", out var props))
            {
                foreach (var prop in props.EnumerateArray())
                {
                    if (prop.TryGetProperty("name", out var n) && n.GetString() == "textures" &&
                        prop.TryGetProperty("value", out var v))
                    {
                        var base64 = v.GetString();
                        if (string.IsNullOrEmpty(base64)) continue;

                        var jsonBytes = Convert.FromBase64String(base64);
                        using var texDoc = JsonDocument.Parse(jsonBytes);
                        var root = texDoc.RootElement;

                        var res = new MojangTexturesResult();
                        if (root.TryGetProperty("textures", out var textures))
                        {
                            if (textures.TryGetProperty("SKIN", out var skinObj))
                            {
                                if (skinObj.TryGetProperty("url", out var su)) res.SkinUrl = su.GetString();
                                if (skinObj.TryGetProperty("metadata", out var meta) &&
                                    meta.TryGetProperty("model", out var m))
                                {
                                    res.SkinModel = m.GetString();
                                }
                            }
                            if (textures.TryGetProperty("CAPE", out var capeObj))
                            {
                                if (capeObj.TryGetProperty("url", out var cu)) res.CapeUrl = cu.GetString();
                            }
                        }
                        return res;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to query Mojang session server: {ex.Message}");
        }
        return null;
    }

    public async Task<byte[]?> TryDownloadAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"SkinService: failed to download image {url}: {ex.Message}");
            return null;
        }
    }

    public async Task<byte[]?> GetBodyRenderAsync(MinecraftAccount acc, CancellationToken ct = default)
    {
        if (acc.IsOffline)
        {
            return await TryDownloadAsync(FallbackBodyRenderUrl(acc.Username), ct).ConfigureAwait(false)
                   ?? await TryDownloadAsync(BodyRenderUrl(acc.Uuid), ct).ConfigureAwait(false);
        }

        return await TryDownloadAsync(BodyRenderUrl(acc.Uuid), ct).ConfigureAwait(false)
               ?? await TryDownloadAsync(FallbackBodyRenderUrl(acc.Username), ct).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetAvatarAsync(MinecraftAccount acc, int size = 72, CancellationToken ct = default)
    {
        if (acc.IsOffline)
        {
            return await TryDownloadAsync(AvatarByNameUrl(acc.Username, size), ct).ConfigureAwait(false)
                   ?? await TryDownloadAsync(AvatarUrl(acc.Uuid, size), ct).ConfigureAwait(false);
        }

        return await TryDownloadAsync(AvatarUrl(acc.Uuid, size), ct).ConfigureAwait(false);
    }

    public async Task<MinecraftProfileResponse?> GetProfileAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MinecraftProfileResponse>(body);
    }

    public enum SkinModel { Classic, Slim }

    public async Task UploadSkinAsync(string accessToken, string filePath, SkinModel model, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Skin file not found.", filePath);

        var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        ValidateSkinPng(bytes);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(model == SkinModel.Slim ? Constants.SkinModel.Slim : Constants.SkinModel.Classic), "variant");

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var req = new HttpRequestMessage(HttpMethod.Post, SkinsUrl) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeSkinError(resp.StatusCode, body));

        Log.Info($"SkinService: skin uploaded successfully ({model}).");
    }

    public async Task<bool> UploadSkinToVecServerAsync(string serverUrl, string username, byte[] skinBytes, SkinModel model, string? uuid = null, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");
            if (!string.IsNullOrEmpty(uuid))
                content.Add(new StringContent(uuid), "uuid");
            content.Add(new StringContent(model == SkinModel.Slim ? Constants.SkinModel.Slim : Constants.SkinModel.Classic), "model");

            var fileContent = new ByteArrayContent(skinBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", $"{username}.png");

            Log.Info($"SkinService: uploading skin '{username}' to VEC server ({cleanUrl}), model={model}, size={skinBytes.Length} bytes...");
            using var resp = await _http.PostAsync($"{cleanUrl}/api/skin/upload", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: skin '{username}' synced to VEC server. Response: {body}");
                return true;
            }
            else
            {
                Log.Warn($"SkinService: VEC server returned {(int)resp.StatusCode} for skin '{username}': {body}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to upload skin to VEC server ({serverUrl}): {ex.Message}");
            return false;
        }
    }

    public async Task UploadCapeToVecServerAsync(string serverUrl, string username, byte[] capeBytes, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");

            var fileContent = new ByteArrayContent(capeBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", $"{username}.png");

            using var resp = await _http.PostAsync($"{cleanUrl}/api/cape/upload", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: cape '{username}' synced to VEC server ({cleanUrl}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to upload cape to VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task ResetCapeOnVecServerAsync(string serverUrl, string username, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");

            using var resp = await _http.PostAsync($"{cleanUrl}/api/cape/reset", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: cape '{username}' reset on VEC server ({cleanUrl}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to reset cape on VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task ResetSkinOnVecServerAsync(string serverUrl, string username, byte[]? defaultSkinBytes, string model, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");
            content.Add(new StringContent(model), "model");

            if (defaultSkinBytes != null && defaultSkinBytes.Length > 0)
            {
                var fileContent = new ByteArrayContent(defaultSkinBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                content.Add(fileContent, "file", $"{username}.png");
            }

            using var resp = await _http.PostAsync($"{cleanUrl}/api/skin/reset", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: skin '{username}' reset on VEC server ({cleanUrl}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to reset skin on VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task UpdateSkinModelOnVecServerAsync(string serverUrl, string username, string model, CancellationToken ct = default)
    {
        try
        {
            var cleanUrl = serverUrl.TrimEnd('/');
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(username), "username");
            content.Add(new StringContent(model), "model");

            using var resp = await _http.PostAsync($"{cleanUrl}/api/skin/model", content, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                Log.Info($"SkinService: skin model '{username}' updated on VEC server: {model}.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"SkinService: failed to update skin model on VEC server ({serverUrl}): {ex.Message}");
        }
    }

    public async Task ChangeSkinByUrlAsync(string accessToken, string skinUrl, SkinModel model, CancellationToken ct = default)
    {
        var payload = new
        {
            variant = model == SkinModel.Slim ? Constants.SkinModel.Slim : Constants.SkinModel.Classic,
            url = skinUrl
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, ActiveSkinUrl)
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeSkinError(resp.StatusCode, body));

        Log.Info("SkinService: skin applied by URL successfully.");
    }

    public async Task ResetSkinAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, ActiveSkinUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeSkinError(resp.StatusCode, body));

        Log.Info("SkinService: skin reset to default.");
    }

    public async Task SetCapeAsync(string accessToken, string? capeId, CancellationToken ct = default)
    {
        HttpRequestMessage req;

        if (string.IsNullOrEmpty(capeId))
        {
            req = new HttpRequestMessage(HttpMethod.Delete, CapeActiveUrl);
        }
        else
        {
            req = new HttpRequestMessage(HttpMethod.Put, CapeActiveUrl)
            {
                Content = JsonContent.Create(new { capeId })
            };
        }

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using (req)
        using (var resp = await _http.SendAsync(req, ct).ConfigureAwait(false))
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(DescribeSkinError(resp.StatusCode, body));
        }
    }

    public static void ValidateSkinPng(byte[] data)
    {
        if (data.Length < 24)
            throw new InvalidDataException("File too small - not a PNG.");

        ReadOnlySpan<byte> sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!data.AsSpan(0, 8).SequenceEqual(sig))
            throw new InvalidDataException("Skin must be in PNG format.");

        var width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
        var height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];

        var valid = (width == 64 && (height == 64 || height == 32)) ||
                    (width == 128 && height == 128) ||
                    (width == 256 && height == 256);

        if (!valid)
            throw new InvalidDataException(
                $"Invalid skin size: {width}x{height}. Required: 64x64 (or 64x32).");

        if (data.Length > 512 * 1024)
            throw new InvalidDataException("Skin file too large (max 512 KB).");
    }

    private static string DescribeSkinError(System.Net.HttpStatusCode code, string body)
    {
        var detail = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorMessage", out var m))
                detail = m.GetString() ?? body;
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return code switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                "Session expired. Please sign in to your Microsoft account again.",
            System.Net.HttpStatusCode.BadRequest =>
                "Mojang rejected the skin file: " + detail,
            System.Net.HttpStatusCode.TooManyRequests =>
                "Too many requests to Mojang. Please wait a minute and try again.",
            _ => $"Skin update error ({(int)code}): {detail}"
        };
    }

    public static System.Windows.Media.Imaging.BitmapSource? CreateCrispHeadAvatar(byte[] skinBytes, int targetSize = 64)
    {
        try
        {
            using var ms = new MemoryStream(skinBytes);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);

            int w = conv.PixelWidth;
            int h = conv.PixelHeight;
            int stride = w * 4;
            var pixels = new byte[h * stride];
            conv.CopyPixels(pixels, stride, 0);

            var scale = Math.Max(1, w / 64);
            int headX = 8 * scale;
            int headY = 8 * scale;
            int headSize = 8 * scale;
            int hatX = 40 * scale;
            int hatY = 8 * scale;

            int outStride = targetSize * 4;
            var outPixels = new byte[targetSize * outStride];

            for (int dy = 0; dy < targetSize; dy++)
            {
                int srcY = headY + (dy * headSize / targetSize);
                int hatSrcY = hatY + (dy * headSize / targetSize);

                for (int dx = 0; dx < targetSize; dx++)
                {
                    int srcX = headX + (dx * headSize / targetSize);
                    int hatSrcX = hatX + (dx * headSize / targetSize);

                    int baseIdx = srcY * stride + srcX * 4;
                    byte b = pixels[baseIdx];
                    byte g = pixels[baseIdx + 1];
                    byte r = pixels[baseIdx + 2];
                    byte a = pixels[baseIdx + 3];

                    if (h >= 64 * scale)
                    {
                        int hatIdx = hatSrcY * stride + hatSrcX * 4;
                        byte hatA = pixels[hatIdx + 3];
                        if (hatA > 20)
                        {
                            double alpha = hatA / 255.0;
                            b = (byte)(pixels[hatIdx] * alpha + b * (1.0 - alpha));
                            g = (byte)(pixels[hatIdx + 1] * alpha + g * (1.0 - alpha));
                            r = (byte)(pixels[hatIdx + 2] * alpha + r * (1.0 - alpha));
                            a = 255;
                        }
                    }

                    int outIdx = dy * outStride + dx * 4;
                    outPixels[outIdx] = b;
                    outPixels[outIdx + 1] = g;
                    outPixels[outIdx + 2] = r;
                    outPixels[outIdx + 3] = a;
                }
            }

            var result = System.Windows.Media.Imaging.BitmapSource.Create(
                targetSize, targetSize, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null,
                outPixels, outStride);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> RedeemPromoCodeAsync(string serverUrl, string username, string code, CancellationToken ct = default)
    {
        var cleanUrl = serverUrl.TrimEnd('/');
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(username), "username");
        content.Add(new StringContent(code), "code");

        using var resp = await _http.PostAsync($"{cleanUrl}/api/promo/redeem", content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Error: {body}");

        var json = System.Text.Json.Nodes.JsonNode.Parse(body);
        return json?["message"]?.ToString() ?? "Done!";
    }
}