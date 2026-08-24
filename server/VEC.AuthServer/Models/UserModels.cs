using System.Text.Json.Serialization;

namespace VEC.AuthServer.Models;

public sealed class UserEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string SkinModel { get; set; } = "classic";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastLoginAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SessionEntity
{
    public string AccessToken { get; set; } = Guid.NewGuid().ToString("N");
    public string ClientToken { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
}

public sealed record RegisterRequest(string Username, string Password, string? Email);
public sealed record LoginRequest(string Username, string Password, string? ClientToken);

public sealed record AuthResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("clientToken")] string ClientToken,
    [property: JsonPropertyName("selectedProfile")] ProfileDto SelectedProfile,
    [property: JsonPropertyName("user")] UserDto User,
    [property: JsonPropertyName("skinUrl")] string? SkinUrl
);

public sealed record ProfileDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name
);

public sealed record UserDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username
);

public sealed record JoinServerRequest(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("selectedProfile")] string SelectedProfile,
    [property: JsonPropertyName("serverId")] string ServerId
);

public sealed record HasJoinedResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("properties")] List<PropertyDto> Properties
);

public sealed record PropertyDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("signature")] string? Signature = null
);
