using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class CapeItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "VEC";
    public string? RequiredCode { get; set; }
    public bool IsDefaultUnlocked { get; set; }
    public bool IsUnlocked { get; set; }
    public bool IsEquipped { get; set; }
    public bool IsOfficialMojang { get; set; }
    public byte[]? RawTextureBytes { get; set; }
    public ImageSource? PreviewImage { get; set; }
}

public static class CapeService
{
    private static readonly string StoragePath = Path.Combine(LauncherPaths.Root, "unlocked_capes.json");

    public static List<string> GetUnlockedCapeIds(string username)
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                if (dict != null && dict.TryGetValue(username.ToLowerInvariant(), out var list))
                {
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to load unlocked capes for {username}: {ex.Message}");
        }

        return new List<string>();
    }

    public static void UnlockCape(string username, string capeId)
    {
        try
        {
            var dict = new Dictionary<string, List<string>>();
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
            }

            var key = username.ToLowerInvariant();
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<string>();
                dict[key] = list;
            }

            if (!list.Contains(capeId, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(capeId);
                var dir = Path.GetDirectoryName(StoragePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StoragePath, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
                Log.Info($"CapeService: unlocked cape '{capeId}' for user '{username}'");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to unlock cape '{capeId}' for {username}: {ex.Message}");
        }
    }

    public static List<CapeItem> GetAllCapes(MinecraftAccount? account, byte[]? currentlyEquippedBytes)
    {
        var result = new List<CapeItem>();

        result.Add(new CapeItem
        {
            Id = "none",
            Name = "Без плаща",
            Description = "Снять плащ с персонажа.",
            Category = "Базовые",
            IsDefaultUnlocked = true,
            IsUnlocked = true,
            IsEquipped = currentlyEquippedBytes == null || currentlyEquippedBytes.Length == 0,
            RawTextureBytes = null,
            PreviewImage = CreateEmptyPreview()
        });

        var vecBytes = LoadEmbeddedCape("cape_vec.png") ?? DefaultSkinService.GetDefaultCape();
        if (vecBytes != null)
        {
            var isEq = currentlyEquippedBytes != null && currentlyEquippedBytes.Length > 0 &&
                       (currentlyEquippedBytes.SequenceEqual(vecBytes) || (currentlyEquippedBytes.Length == vecBytes.Length && currentlyEquippedBytes[0] == vecBytes[0]));
            result.Add(new CapeItem
            {
                Id = "vec_default",
                Name = "Плащ VEC (КПВК)",
                Description = "Официальный плащ инженеров и студентов VEC КПВК с эмблемой и поддержкой элитр.",
                Category = "Эксклюзив VEC",
                IsDefaultUnlocked = true,
                IsUnlocked = true,
                IsEquipped = isEq,
                RawTextureBytes = vecBytes,
                PreviewImage = CreateCapePreview(vecBytes)
            });
        }

        var migratorBytes = LoadEmbeddedCape("cape_migrator.png");
        if (migratorBytes != null)
        {
            var isEq = currentlyEquippedBytes != null && currentlyEquippedBytes.Length > 0 &&
                       currentlyEquippedBytes.SequenceEqual(migratorBytes);
            result.Add(new CapeItem
            {
                Id = "mojang_migrator",
                Name = "Плащ Migrator",
                Description = "Классический плащ миграции аккаунта Mojang.",
                Category = "Официальные",
                IsDefaultUnlocked = true,
                IsUnlocked = true,
                IsEquipped = isEq,
                IsOfficialMojang = true,
                RawTextureBytes = migratorBytes,
                PreviewImage = CreateCapePreview(migratorBytes)
            });
        }

        return result;
    }

    private static byte[]? LoadEmbeddedCape(string fileName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = $"VECLauncher.Assets.{fileName}";
            using var s = asm.GetManifestResourceStream(resName);
            if (s != null)
            {
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }

            var local = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(local)) return File.ReadAllBytes(local);
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to load embedded cape '{fileName}': {ex.Message}");
        }

        return null;
    }

    public static ImageSource? CreateCapePreview(byte[]? capeBytes)
    {
        if (capeBytes == null || capeBytes.Length == 0) return CreateEmptyPreview();

        try
        {
            using var ms = new MemoryStream(capeBytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            int cropX = 1;
            int cropY = 1;
            int cropW = 10;
            int cropH = 16;

            if (frame.PixelWidth >= 64 && frame.PixelHeight >= 32)
            {
                var cropped = new CroppedBitmap(frame, new System.Windows.Int32Rect(cropX, cropY, cropW, cropH));
                
                var scaled = new TransformedBitmap(cropped, new ScaleTransform(6, 6));
                RenderOptions.SetBitmapScalingMode(scaled, BitmapScalingMode.NearestNeighbor);
                scaled.Freeze();
                return scaled;
            }

            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            Log.Warn($"CapeService: failed to create cape preview: {ex.Message}");
            return null;
        }
    }

    private static ImageSource CreateEmptyPreview()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 
                             new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1), 
                             new System.Windows.Rect(0, 0, 60, 96));
            var ft = new FormattedText("✕", 
                System.Globalization.CultureInfo.CurrentCulture, 
                System.Windows.FlowDirection.LeftToRight, 
                new Typeface("Segoe UI"), 22, 
                new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 96);
            dc.DrawText(ft, new System.Windows.Point(23, 33));
        }

        var rtb = new RenderTargetBitmap(60, 96, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}