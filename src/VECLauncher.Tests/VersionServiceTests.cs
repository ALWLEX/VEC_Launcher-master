using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

public class VersionServiceTests
{
    // ── ParseMcVersion() ──

    [Theory]
    [InlineData("1.20.4", 1, 20, 4)]
    [InlineData("1.19.3", 1, 19, 3)]
    [InlineData("1.18.2", 1, 18, 2)]
    [InlineData("1.16.5", 1, 16, 5)]
    [InlineData("1.12.2", 1, 12, 2)]
    [InlineData("1.7.10", 1, 7, 10)]
    public void ParseMcVersion_StandardVersions(string id, int maj, int min, int bld)
    {
        var result = VersionService.ParseMcVersion(id);
        Assert.NotNull(result);
        Assert.Equal(new Version(maj, min, bld), result);
    }

    [Theory]
    [InlineData("1.20", 1, 20, 0)]
    [InlineData("1.16", 1, 16, 0)]
    public void ParseMcVersion_TwoPartVersions(string id, int maj, int min, int bld)
    {
        var result = VersionService.ParseMcVersion(id);
        Assert.NotNull(result);
        Assert.Equal(new Version(maj, min, bld), result);
    }

    [Theory]
    [InlineData("1.20.4-pre1", 1, 20, 4)]
    [InlineData("1.19.3-rc1", 1, 19, 3)]
    [InlineData("1.18.2-fat", 1, 18, 2)]
    public void ParseMcVersion_VersionWithSuffix(string id, int maj, int min, int bld)
    {
        var result = VersionService.ParseMcVersion(id);
        Assert.NotNull(result);
        Assert.Equal(new Version(maj, min, bld), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseMcVersion_NullOrEmpty_ReturnsNull(string? id)
    {
        Assert.Null(VersionService.ParseMcVersion(id!));
    }

    [Theory]
    [InlineData("24w14a")]  // snapshot, no "1." prefix
    [InlineData("snapshot")]
    public void ParseMcVersion_SnapshotNoVersion_ReturnsNull(string id)
    {
        // Snapshots like "24w14a" don't match "1.x" pattern
        var result = VersionService.ParseMcVersion(id);
        Assert.Null(result);
    }

    // ── IsAtLeastMinimum() ──

    [Theory]
    [InlineData("1.20.4", true)]
    [InlineData("1.16.5", true)]
    [InlineData("1.17.0", true)]
    [InlineData("1.16.4", false)]
    [InlineData("1.15.2", false)]
    [InlineData("1.7.10", false)]
    [InlineData("1.8.9", false)]
    public void IsAtLeastMinimum_CorrectForVersions(string id, bool expected)
    {
        Assert.Equal(expected, VersionService.IsAtLeastMinimum(id));
    }

    [Theory]
    [InlineData("21w14a", true)]   // 2021+ snapshot
    [InlineData("23w16a", true)]   // 2023+ snapshot
    [InlineData("20w14a", false)]  // 2020 snapshot
    public void IsAtLeastMinimum_SnapshotByYear(string id, bool expected)
    {
        Assert.Equal(expected, VersionService.IsAtLeastMinimum(id));
    }

    // ── FilterSupported() ──

    [Fact]
    public void FilterSupported_OnlyReleasesByDefault()
    {
        var manifest = new VersionManifest
        {
            Versions = new List<ManifestVersion>
            {
                new() { Id = "1.20.4", Type = "release", ReleaseTime = DateTimeOffset.Now },
                new() { Id = "1.20.5-pre1", Type = "snapshot", ReleaseTime = DateTimeOffset.Now },
                new() { Id = "1.16.5", Type = "release", ReleaseTime = DateTimeOffset.Now.AddDays(-1) },
            }
        };

        var result = VersionService.FilterSupported(manifest, includeSnapshots: false);
        Assert.All(result, v => Assert.True(v.IsRelease));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterSupported_WithSnapshots()
    {
        var manifest = new VersionManifest
        {
            Versions = new List<ManifestVersion>
            {
                new() { Id = "1.20.4", Type = "release", ReleaseTime = DateTimeOffset.Now },
                new() { Id = "24w14a", Type = "snapshot", ReleaseTime = DateTimeOffset.Now },
            }
        };

        // 24w14a has year >= 21 so passes IsAtLeastMinimum, both included
        var result = VersionService.FilterSupported(manifest, includeSnapshots: true);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterSupported_ExcludesOldVersions()
    {
        var manifest = new VersionManifest
        {
            Versions = new List<ManifestVersion>
            {
                new() { Id = "1.20.4", Type = "release", ReleaseTime = DateTimeOffset.Now },
                new() { Id = "1.8.9", Type = "release", ReleaseTime = DateTimeOffset.Now },
                new() { Id = "1.12.2", Type = "release", ReleaseTime = DateTimeOffset.Now },
            }
        };

        var result = VersionService.FilterSupported(manifest);
        Assert.Single(result);
        Assert.Equal("1.20.4", result[0].Id);
    }

    [Fact]
    public void FilterSupported_OrderedByReleaseTimeDesc()
    {
        var manifest = new VersionManifest
        {
            Versions = new List<ManifestVersion>
            {
                new() { Id = "1.16.5", Type = "release", ReleaseTime = new DateTimeOffset(2020, 12, 1, 0, 0, 0, TimeSpan.Zero) },
                new() { Id = "1.20.4", Type = "release", ReleaseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
                new() { Id = "1.18.2", Type = "release", ReleaseTime = new DateTimeOffset(2022, 6, 1, 0, 0, 0, TimeSpan.Zero) },
            }
        };

        var result = VersionService.FilterSupported(manifest);
        Assert.Equal("1.20.4", result[0].Id);
        Assert.Equal("1.18.2", result[1].Id);
        Assert.Equal("1.16.5", result[2].Id);
    }
}
