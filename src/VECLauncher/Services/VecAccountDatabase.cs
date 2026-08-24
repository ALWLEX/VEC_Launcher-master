using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VECLauncher.Models;

namespace VECLauncher.Services;

public sealed class VecUserRecord
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("passwordHash")] public string PasswordHash { get; set; } = "";
    [JsonPropertyName("passwordSalt")] public string PasswordSalt { get; set; } = "";
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("registeredAt")] public DateTimeOffset RegisteredAt { get; set; }
    [JsonPropertyName("lastLoginAt")] public DateTimeOffset LastLoginAt { get; set; }
    [JsonPropertyName("skinModel")] public string SkinModel { get; set; } = "classic";
    [JsonPropertyName("customSkinBase64")] public string? CustomSkinBase64 { get; set; }
}

public static class VecAccountDatabase
{
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VECLauncher", "vec_accounts.json");

    private static readonly object Lock = new();
    private static Dictionary<string, VecUserRecord> _users = new(StringComparer.OrdinalIgnoreCase);

    static VecAccountDatabase()
    {
        Load();
    }

    private static void Load()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(DbPath))
                {
                    var json = File.ReadAllText(DbPath);
                    var list = JsonSerializer.Deserialize<List<VecUserRecord>>(json);
                    if (list != null)
                    {
                        _users.Clear();
                        foreach (var u in list)
                            _users[u.Username] = u;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"VecAccountDatabase: failed to load VEC accounts: {ex.Message}");
            }
        }
    }

    private static void Save()
    {
        lock (Lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(DbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var list = new List<VecUserRecord>(_users.Values);
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(DbPath, json);
            }
            catch (Exception ex)
            {
                Log.Warn($"VecAccountDatabase: failed to save VEC accounts: {ex.Message}");
            }
        }
    }

    public static (bool Success, string Message, MinecraftAccount? Account) Register(string username, string password, string? email = null)
    {
        var cleanName = username.Trim();
        if (cleanName.Length < 3 || cleanName.Length > 16)
            return (false, "Никнейм должен содержать от 3 до 16 символов.", null);

        foreach (var ch in cleanName)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
                return (false, "Никнейм может содержать только латинские буквы, цифры и знак подчёркивания (_).", null);
        }

        if (string.IsNullOrEmpty(password) || password.Length < 4)
            return (false, "Пароль должен содержать минимум 4 символа.", null);

        lock (Lock)
        {
            if (_users.ContainsKey(cleanName))
                return (false, $"Пользователь с ником «{cleanName}» уже зарегистрирован. Введите пароль для входа.", null);

            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);
            var uuid = OfflineAccountService.GenerateOfflineUuid(cleanName);

            var record = new VecUserRecord
            {
                Username = cleanName,
                Uuid = uuid,
                PasswordHash = hash,
                PasswordSalt = salt,
                Email = email,
                RegisteredAt = DateTimeOffset.UtcNow,
                LastLoginAt = DateTimeOffset.UtcNow
            };

            _users[cleanName] = record;
            Save();

            var acc = new MinecraftAccount
            {
                Username = record.Username,
                Uuid = record.Uuid,
                AccessToken = Guid.NewGuid().ToString("N"),
                Type = AccountType.Vec,
                ServerUrl = VecAuthService.DefaultVecServerUrl,
                CapeUrl = $"{VecAuthService.DefaultVecServerUrl}/api/cape/{record.Username}.png",
                ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
                SkinModel = record.SkinModel
            };

            return (true, "Регистрация успешна!", acc);
        }
    }

    public static (bool Success, string Message, MinecraftAccount? Account) Login(string username, string password)
    {
        var cleanName = username.Trim();
        if (string.IsNullOrEmpty(cleanName))
            return (false, "Введите имя пользователя.", null);

        if (string.IsNullOrEmpty(password))
            return (false, "Введите пароль.", null);

        lock (Lock)
        {
            if (!_users.TryGetValue(cleanName, out var record))
            {
                return (false, $"Пользователь «{cleanName}» не найден. Нажмите «Регистрация», чтобы создать аккаунт.", null);
            }

            var checkHash = HashPassword(password, record.PasswordSalt);
            if (!CryptographicEquals(checkHash, record.PasswordHash))
            {
                return (false, "Неверный пароль. Проверьте правильность ввода.", null);
            }

            record.LastLoginAt = DateTimeOffset.UtcNow;
            Save();

            var acc = new MinecraftAccount
            {
                Username = record.Username,
                Uuid = record.Uuid,
                AccessToken = Guid.NewGuid().ToString("N"),
                Type = AccountType.Vec,
                ServerUrl = VecAuthService.DefaultVecServerUrl,
                CapeUrl = $"{VecAuthService.DefaultVecServerUrl}/api/cape/{record.Username}.png",
                ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
                SkinModel = record.SkinModel
            };

            return (true, "Вход выполнен успешно!", acc);
        }
    }

    public static bool Delete(string username)
    {
        lock (Lock)
        {
            if (_users.Remove(username))
            {
                Save();
                Log.Info($"VecAccountDatabase: deleted account '{username}'");
                return true;
            }
            return false;
        }
    }

    public static void UpdateSkinModel(string username, string model)
    {
        lock (Lock)
        {
            if (_users.TryGetValue(username, out var record))
            {
                record.SkinModel = model;
                Save();
            }
        }
    }

    public static IReadOnlyList<VecUserRecord> GetAllUsers()
    {
        lock (Lock)
        {
            return new List<VecUserRecord>(_users.Values);
        }
    }

    private static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashPassword(string password, string salt)
    {
        var combined = Encoding.UTF8.GetBytes(password + salt + "VEC_SECURITY_SALT_2026");
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(combined);
        return Convert.ToBase64String(hash);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}