using System.Text.Json.Serialization;

namespace VECLauncher.Models;

public enum AccountType
{
    Microsoft = 0,
    Offline = 1,
    Vec = 2
}

public sealed class MinecraftAccount
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
    [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("expiresAt")] public DateTimeOffset ExpiresAt { get; set; }
    [JsonPropertyName("msRefreshToken")] public string? MicrosoftRefreshToken { get; set; }
    [JsonPropertyName("xuid")] public string? Xuid { get; set; }
    [JsonPropertyName("type")] public AccountType Type { get; set; } = AccountType.Microsoft;
    [JsonPropertyName("serverUrl")] public string? ServerUrl { get; set; }
    [JsonPropertyName("skinUrl")] public string? SkinUrl { get; set; }
    [JsonPropertyName("capeUrl")] public string? CapeUrl { get; set; }
    [JsonPropertyName("skinModel")] public string SkinModel { get; set; } = "classic";

    [JsonIgnore] public bool IsOffline => Type == AccountType.Offline;
    [JsonIgnore] public bool IsVec => Type == AccountType.Vec;
    [JsonIgnore] public bool IsExpired => Type == AccountType.Microsoft && DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-5);
    [JsonIgnore] public string UserType => (IsOffline || IsVec) ? "legacy" : "msa";
    [JsonIgnore]
    public string DashedUuid
    {
        get
        {
            var u = Uuid.Replace("-", "");
            if (u.Length != 32) return Uuid;
            return $"{u[..8]}-{u.Substring(8, 4)}-{u.Substring(12, 4)}-{u.Substring(16, 4)}-{u.Substring(20)}";
        }
    }
}

public sealed class MicrosoftTokenResponse
{
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
}

public sealed class DeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; } = 5;
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class XboxAuthResponse
{
    [JsonPropertyName("IssueInstant")] public DateTimeOffset IssueInstant { get; set; }
    [JsonPropertyName("NotAfter")] public DateTimeOffset NotAfter { get; set; }
    [JsonPropertyName("Token")] public string Token { get; set; } = "";
    [JsonPropertyName("DisplayClaims")] public XboxDisplayClaims? DisplayClaims { get; set; }

    [JsonIgnore] public string? UserHash => DisplayClaims?.Xui?.FirstOrDefault()?.Uhs;
    [JsonIgnore] public string? Xuid => DisplayClaims?.Xui?.FirstOrDefault()?.Xid;
}

public sealed class XboxDisplayClaims
{
    [JsonPropertyName("xui")] public List<XboxXui>? Xui { get; set; }
}

public sealed class XboxXui
{
    [JsonPropertyName("uhs")] public string? Uhs { get; set; }
    [JsonPropertyName("xid")] public string? Xid { get; set; }
}

public sealed class MinecraftLoginResponse
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

public sealed class MinecraftProfileResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("skins")] public List<ProfileSkin>? Skins { get; set; }
    [JsonPropertyName("capes")] public List<ProfileCape>? Capes { get; set; }
}

public sealed class ProfileSkin
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("variant")] public string? Variant { get; set; }
}

public sealed class ProfileCape
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("alias")] public string? Alias { get; set; }
}

public sealed class MinecraftEntitlements
{
    [JsonPropertyName("items")] public List<EntitlementItem>? Items { get; set; }
}

public sealed class EntitlementItem
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("signature")] public string? Signature { get; set; }
}
