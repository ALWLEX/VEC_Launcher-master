using System;
using System.IO;
using System.Reflection;

namespace VECLauncher.Services;

public static class DefaultSkinService
{
    private static byte[]? _cachedSteve;
    private static byte[]? _cachedAlex;
    private static byte[]? _cachedCape;

    public static byte[] GetDefaultSkin(string username, bool isSlim = false)
    {
        if (isSlim)
        {
            if (_cachedAlex != null) return _cachedAlex;
            var skin = LoadEmbeddedSkin("alex_slim.png") ?? LoadEmbeddedSkin("s2.png");
            if (skin != null) { _cachedAlex = skin; return skin; }
        }
        else
        {
            if (_cachedSteve != null) return _cachedSteve;
            var skin = LoadEmbeddedSkin("steve_classic.png") ?? LoadEmbeddedSkin("s1.png");
            if (skin != null) { _cachedSteve = skin; return skin; }
        }

        return Array.Empty<byte>();
    }

    public static byte[]? GetDefaultCape()
    {
        if (_cachedCape != null) return _cachedCape;
        var cape = LoadEmbeddedSkin("cape_vec.png") ?? LoadEmbeddedSkin("cape_migrator.png");
        if (cape != null) { _cachedCape = cape; return cape; }
        return null;
    }

    private static byte[]? LoadEmbeddedSkin(string skinFileName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resourceName = $"VECLauncher.Assets.{skinFileName}";
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }

            var resourceNameSkins = $"VECLauncher.Assets.Skins.{skinFileName}";
            using var streamSkins = asm.GetManifestResourceStream(resourceNameSkins);
            if (streamSkins != null)
            {
                using var ms = new MemoryStream();
                streamSkins.CopyTo(ms);
                return ms.ToArray();
            }

            var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", skinFileName);
            if (File.Exists(localPath))
            {
                return File.ReadAllBytes(localPath);
            }
        }
        catch (Exception ex) { Log.Warn(ex.Message); }

        return null;
    }
}