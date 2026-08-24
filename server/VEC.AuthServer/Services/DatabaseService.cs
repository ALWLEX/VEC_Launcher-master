using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using VEC.AuthServer.Models;

namespace VEC.AuthServer.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(IConfiguration config)
    {
        var dbPath = config["Database:Path"] ?? "data/vec_auth.db";
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connectionString = $"Data Source={dbPath};";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL UNIQUE,
                email TEXT,
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                skin_model TEXT DEFAULT 'classic',
                created_at TEXT NOT NULL,
                last_login_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
                access_token TEXT PRIMARY KEY,
                client_token TEXT NOT NULL,
                user_id TEXT NOT NULL,
                server_id TEXT,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                FOREIGN KEY (user_id) REFERENCES users(id)
            );
        ";
        cmd.ExecuteNonQuery();
    }

    public UserEntity? GetUserByUsername(string username)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, email, password_hash, password_salt, skin_model, created_at, last_login_at FROM users WHERE LOWER(username) = LOWER(@u);";
        cmd.Parameters.AddWithValue("@u", username);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new UserEntity
        {
            Id = reader.GetString(0),
            Username = reader.GetString(1),
            Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
            PasswordHash = reader.GetString(3),
            PasswordSalt = reader.GetString(4),
            SkinModel = reader.IsDBNull(5) ? "classic" : reader.GetString(5),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
            LastLoginAt = DateTimeOffset.Parse(reader.GetString(7))
        };
    }

    public UserEntity? GetUserById(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, email, password_hash, password_salt, skin_model, created_at, last_login_at FROM users WHERE id = @id OR id = @cleanId;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@cleanId", id.Replace("-", ""));

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new UserEntity
            {
                Id = reader.GetString(0),
                Username = reader.GetString(1),
                Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PasswordHash = reader.GetString(3),
                PasswordSalt = reader.GetString(4),
                SkinModel = reader.IsDBNull(5) ? "classic" : reader.GetString(5),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                LastLoginAt = DateTimeOffset.Parse(reader.GetString(7))
            };
        }

        var cleanId = id.Replace("-", "");
        var searchDirs = new[]
        {
            Path.Combine("data", "skins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VECLauncher", "skins")
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                var uname = Path.GetFileNameWithoutExtension(file);
                var offId = GenerateOfflineUuid(uname);
                if (string.Equals(offId, cleanId, StringComparison.OrdinalIgnoreCase))
                {
                    return GetOrCreateUser(uname, offId);
                }
            }
        }

        return null;
    }

    public UserEntity GetOrCreateUser(string username, string? uuid = null, string model = "classic")
    {
        var cleanName = username.Trim();
        var existing = GetUserByUsername(cleanName);
        if (existing != null) return existing;

        var id = !string.IsNullOrEmpty(uuid) ? uuid.Replace("-", "") : GenerateOfflineUuid(cleanName);
        var salt = GenerateSalt();
        var hash = HashPassword(Guid.NewGuid().ToString("N"), salt);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, username, email, password_hash, password_salt, skin_model, created_at, last_login_at)
            VALUES (@id, @u, '', @h, @s, @m, @ca, @la);
        ";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@u", cleanName);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@m", model);
        cmd.Parameters.AddWithValue("@ca", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@la", DateTimeOffset.UtcNow.ToString("o"));

        try
        {
            cmd.ExecuteNonQuery();
        }
        catch
        {
            var fallback = GetUserByUsername(cleanName);
            if (fallback != null) return fallback;
        }

        return new UserEntity
        {
            Id = id,
            Username = cleanName,
            SkinModel = model,
            CreatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = DateTimeOffset.UtcNow
        };
    }

    public UserEntity CreateUser(string username, string password, string? email = null)
    {
        var cleanName = username.Trim();
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);
        var uuid = GenerateOfflineUuid(cleanName);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, username, email, password_hash, password_salt, skin_model, created_at, last_login_at)
            VALUES (@id, @u, @e, @h, @s, 'classic', @ca, @la);
        ";
        cmd.Parameters.AddWithValue("@id", uuid);
        cmd.Parameters.AddWithValue("@u", cleanName);
        cmd.Parameters.AddWithValue("@e", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@ca", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@la", DateTimeOffset.UtcNow.ToString("o"));

        cmd.ExecuteNonQuery();

        return new UserEntity
        {
            Id = uuid,
            Username = cleanName,
            Email = email ?? "",
            PasswordHash = hash,
            PasswordSalt = salt
        };
    }

    public void UpdateLastLogin(string userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET last_login_at = @la WHERE id = @id;";
        cmd.Parameters.AddWithValue("@la", DateTimeOffset.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.ExecuteNonQuery();
    }

    public void UpdateSkinModel(string username, string model)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET skin_model = @m WHERE LOWER(username) = LOWER(@u);";
        cmd.Parameters.AddWithValue("@m", model);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.ExecuteNonQuery();
    }

    public SessionEntity CreateSession(string userId, string? clientToken = null)
    {
        var session = new SessionEntity
        {
            AccessToken = Guid.NewGuid().ToString("N"),
            ClientToken = clientToken ?? Guid.NewGuid().ToString("N"),
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sessions (access_token, client_token, user_id, server_id, created_at, expires_at)
            VALUES (@at, @ct, @uid, '', @ca, @ea);
        ";
        cmd.Parameters.AddWithValue("@at", session.AccessToken);
        cmd.Parameters.AddWithValue("@ct", session.ClientToken);
        cmd.Parameters.AddWithValue("@uid", session.UserId);
        cmd.Parameters.AddWithValue("@ca", session.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@ea", session.ExpiresAt.ToString("o"));

        cmd.ExecuteNonQuery();
        return session;
    }

    public SessionEntity? GetSessionByToken(string accessToken)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT access_token, client_token, user_id, server_id, created_at, expires_at FROM sessions WHERE access_token = @at;";
        cmd.Parameters.AddWithValue("@at", accessToken);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new SessionEntity
        {
            AccessToken = reader.GetString(0),
            ClientToken = reader.GetString(1),
            UserId = reader.GetString(2),
            ServerId = reader.IsDBNull(3) ? "" : reader.GetString(3),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
            ExpiresAt = DateTimeOffset.Parse(reader.GetString(5))
        };
    }

    public void SetServerId(string accessToken, string serverId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET server_id = @sid WHERE access_token = @at;";
        cmd.Parameters.AddWithValue("@sid", serverId);
        cmd.Parameters.AddWithValue("@at", accessToken);
        cmd.ExecuteNonQuery();
    }

    public UserEntity? GetUserByServerId(string username, string serverId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT u.id, u.username, u.email, u.password_hash, u.password_salt, u.skin_model, u.created_at, u.last_login_at
            FROM users u
            JOIN sessions s ON u.id = s.user_id
            WHERE LOWER(u.username) = LOWER(@u) AND s.server_id = @sid;
        ";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@sid", serverId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new UserEntity
        {
            Id = reader.GetString(0),
            Username = reader.GetString(1),
            Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
            PasswordHash = reader.GetString(3),
            PasswordSalt = reader.GetString(4),
            SkinModel = reader.IsDBNull(5) ? "classic" : reader.GetString(5),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
            LastLoginAt = DateTimeOffset.Parse(reader.GetString(7))
        };
    }

    public static string GenerateSalt()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        var combined = Encoding.UTF8.GetBytes(password + salt + "VEC_SECURITY_SALT_2026");
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(combined);
        return Convert.ToBase64String(hash);
    }

    public static bool VerifyPassword(string password, string salt, string hash)
    {
        var checkHash = HashPassword(password, salt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(checkHash),
            Encoding.UTF8.GetBytes(hash));
    }

    public static string GenerateOfflineUuid(string username)
    {
        var bytes = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(bytes);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
