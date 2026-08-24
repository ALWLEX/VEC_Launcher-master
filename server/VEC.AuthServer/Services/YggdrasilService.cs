using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VEC.AuthServer.Models;

namespace VEC.AuthServer.Services;

public sealed class YggdrasilService
{
    private readonly RSA _rsa;
    private readonly string _serverPublicDomain;
    private readonly string _keyFilePath = Path.Combine("data", "yggdrasil_key.pem");

    public YggdrasilService(IConfiguration config)
    {
        _serverPublicDomain = config["Server:PublicUrl"] ?? "http://localhost:8080";
        _rsa = LoadOrGenerateRsaKey();
    }

    private RSA LoadOrGenerateRsaKey()
    {
        var rsa = RSA.Create(2048);

        try
        {
            var dir = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(_keyFilePath))
            {
                var pem = File.ReadAllText(_keyFilePath);
                rsa.ImportFromPem(pem);
                Console.WriteLine($"Loaded RSA key from {_keyFilePath}");
            }
            else
            {
                var pem = rsa.ExportPkcs8PrivateKeyPem();
                File.WriteAllText(_keyFilePath, pem);
                Console.WriteLine($"Generated new RSA key at {_keyFilePath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RSA key warning: {ex.Message}");
        }

        return rsa;
    }

    public string GetPublicKeyPem()
    {
        return _rsa.ExportSubjectPublicKeyInfoPem();
    }

    public PropertyDto BuildTexturesProperty(UserEntity user, string? baseUrl = null)
    {
        var domain = (baseUrl ?? _serverPublicDomain).TrimEnd('/');
        var skinPath = Path.Combine("data", "skins", $"{user.Username}.png");
        var capePath = Path.Combine("data", "capes", $"{user.Username}.png");

        if (!File.Exists(skinPath))
        {
            var appDataSkin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "skins", $"{user.Username}.png");
            if (File.Exists(appDataSkin))
            {
                try
                {
                    Directory.CreateDirectory("data/skins");
                    File.Copy(appDataSkin, skinPath, overwrite: true);
                }
                catch { }
            }
        }

        if (!File.Exists(capePath))
        {
            var appDataCape = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "capes", $"{user.Username}.png");
            if (File.Exists(appDataCape))
            {
                try
                {
                    Directory.CreateDirectory("data/capes");
                    File.Copy(appDataCape, capePath, overwrite: true);
                }
                catch { }
            }
        }

        var textures = new Dictionary<string, object>();

        if (File.Exists(skinPath))
        {
            var skinVersion = File.GetLastWriteTimeUtc(skinPath).Ticks.ToString("x");
            var skinMeta = new Dictionary<string, object>
            {
                ["url"] = $"{domain}/api/skin/{user.Username}.png?v={skinVersion}"
            };
            if (user.SkinModel.Equals("slim", StringComparison.OrdinalIgnoreCase))
            {
                skinMeta["metadata"] = new { model = "slim" };
            }
            textures["SKIN"] = skinMeta;
        }

        if (File.Exists(capePath))
        {
            var capeVersion = File.GetLastWriteTimeUtc(capePath).Ticks.ToString("x");
            textures["CAPE"] = new
            {
                url = $"{domain}/api/cape/{user.Username}.png?v={capeVersion}"
            };
        }

        var payloadObj = new
        {
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            profileId = user.Id,
            profileName = user.Username,
            signatureRequired = true,
            textures = textures
        };

        var payloadJson = JsonSerializer.Serialize(payloadObj);
        var payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));

        var signatureBytes = _rsa.SignData(Encoding.UTF8.GetBytes(payloadBase64), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        return new PropertyDto("textures", payloadBase64, signatureBase64);
    }
}
