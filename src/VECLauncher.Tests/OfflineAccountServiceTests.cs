using VECLauncher.Services;

namespace VECLauncher.Tests;

public class OfflineAccountServiceTests
{
    // ── TryValidateName() ──

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("ab", false)]
    [InlineData("a", false)]
    [InlineData("Toolongnameexceeding", false)]
    [InlineData("abc", true)]
    [InlineData("Player1", true)]
    [InlineData("Test_Name", true)]
    [InlineData("ABC123def", true)]
    [InlineData("1234567890123456", true)] // exactly 16
    public void TryValidateName_ValidatesNames(string? name, bool expected)
    {
        var result = OfflineAccountService.TryValidateName(name, out _);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ab", "too short")]
    [InlineData("a", "too short")]
    [InlineData("Toolongnameexceeding", "too long")]
    [InlineData("abc def", "invalid chars")]
    [InlineData("abc-def", "invalid chars")]
    [InlineData("привет", "invalid chars (cyrillic)")]
    public void TryValidateName_ReturnsErrorForInvalid(string name, string _)
    {
        var result = OfflineAccountService.TryValidateName(name, out var error);
        Assert.False(result);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryValidateName_EmptyName_ReturnsError()
    {
        var result = OfflineAccountService.TryValidateName("", out var error);
        Assert.False(result);
        Assert.Contains("никнейм", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Create() ──

    [Fact]
    public void Create_ValidName_ReturnsAccountWithCorrectFields()
    {
        var acc = OfflineAccountService.Create("TestPlayer");

        Assert.Equal("TestPlayer", acc.Username);
        Assert.NotNull(acc.Uuid);
        Assert.Equal(32, acc.Uuid.Length); // MD5 hex = 32 chars
        Assert.Equal("0", acc.AccessToken);
        Assert.Equal(DateTimeOffset.MaxValue, acc.ExpiresAt);
    }

    [Fact]
    public void Create_TrimmedName()
    {
        var acc = OfflineAccountService.Create("  TestPlayer  ");
        Assert.Equal("TestPlayer", acc.Username);
    }

    [Fact]
    public void Create_InvalidName_Throws()
    {
        Assert.Throws<ArgumentException>(() => OfflineAccountService.Create("ab"));
    }

    // ── GenerateOfflineUuid() ──

    [Fact]
    public void GenerateOfflineUuid_Deterministic()
    {
        var uuid1 = OfflineAccountService.GenerateOfflineUuid("TestPlayer");
        var uuid2 = OfflineAccountService.GenerateOfflineUuid("TestPlayer");
        Assert.Equal(uuid1, uuid2);
    }

    [Fact]
    public void GenerateOfflineUuid_DifferentNames_DifferentUuids()
    {
        var uuid1 = OfflineAccountService.GenerateOfflineUuid("Player1");
        var uuid2 = OfflineAccountService.GenerateOfflineUuid("Player2");
        Assert.NotEqual(uuid1, uuid2);
    }

    [Fact]
    public void GenerateOfflineUuid_ReturnsValidUuid()
    {
        var uuid = OfflineAccountService.GenerateOfflineUuid("TestPlayer");

        // Should be 32 hex chars
        Assert.Equal(32, uuid.Length);
        Assert.True(uuid.All("0123456789abcdef".Contains));

        // Version nibble should be 3 (UUID v3 variant)
        Assert.Equal('3', uuid[12]);
    }
}
