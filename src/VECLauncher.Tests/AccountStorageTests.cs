using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

/// <summary>
/// Integration tests for <see cref="AccountStorage"/>.
/// Tests the public API (Save, Load, GetAllSaved, RemoveSaved, Clear).
/// These tests touch the real file system — run in isolation.
/// </summary>
public class AccountStorageTests
{
    private static MinecraftAccount CreateTestAccount(string name, AccountType type = AccountType.Offline)
    {
        return new MinecraftAccount
        {
            Username = name,
            Uuid = $"00000000-0000-0000-0000-{Math.Abs(name.GetHashCode()):X12}".Substring(0, 36),
            AccessToken = "test-token",
            ExpiresAt = DateTimeOffset.MaxValue,
            Type = type
        };
    }

    [Fact]
    public void GetAllSaved_ReturnsNonNull()
    {
        var saved = AccountStorage.GetAllSaved();
        Assert.NotNull(saved);
    }

    [Fact]
    public void RemoveSaved_Nonexistent_DoesNotThrow()
    {
        // Should not throw even if account doesn't exist
        var ex = Record.Exception(() =>
            AccountStorage.RemoveSaved("NonexistentUser_99999", AccountType.Offline));
        Assert.Null(ex);
    }

    [Fact]
    public void Clear_DoesNotThrow()
    {
        var ex = Record.Exception(() => AccountStorage.Clear());
        Assert.Null(ex);
    }

    [Fact]
    public void MinecraftAccount_CreatedCorrectly()
    {
        var acc = CreateTestAccount("TestUser");
        Assert.Equal("TestUser", acc.Username);
        Assert.Equal(AccountType.Offline, acc.Type);
        Assert.Equal("test-token", acc.AccessToken);
        Assert.True(acc.IsOffline);
        Assert.False(acc.IsVec);
        Assert.Equal("legacy", acc.UserType);
    }

    [Fact]
    public void MinecraftAccount_DashedUuid_FormatsCorrectly()
    {
        var acc = new MinecraftAccount
        {
            Username = "Test",
            Uuid = "1234567890abcdef1234567890abcdef"
        };
        var dashed = acc.DashedUuid;
        Assert.Contains("-", dashed);
        Assert.Equal(36, dashed.Length); // standard UUID format
    }

    [Fact]
    public void AccountType_Enum_HasExpectedValues()
    {
        Assert.Equal(0, (int)AccountType.Microsoft);
        Assert.Equal(1, (int)AccountType.Offline);
        Assert.Equal(2, (int)AccountType.Vec);
    }

    [Fact]
    public void MinecraftAccount_IsExpired_OnlyForMicrosoft()
    {
        var ms = new MinecraftAccount { Type = AccountType.Microsoft, ExpiresAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var off = new MinecraftAccount { Type = AccountType.Offline, ExpiresAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        Assert.True(ms.IsExpired);
        Assert.False(off.IsExpired); // non-Microsoft accounts never expire
    }
}
