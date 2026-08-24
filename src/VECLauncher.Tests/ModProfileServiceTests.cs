using System.IO;
using System.Reflection;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

/// <summary>
/// Integration tests for ModProfileService.
/// Uses a temp directory as the launcher root to test profile CRUD operations.
/// </summary>
public class ModProfileServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _originalRoot;

    public ModProfileServiceTests()
    {
        _originalRoot = LauncherPaths.Root;
        _tempRoot = Path.Combine(Path.GetTempPath(), "veclauncher_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);

        // Redirect LauncherPaths.Root to temp directory via reflection
        var prop = typeof(LauncherPaths).GetProperty("Root",
            BindingFlags.Public | BindingFlags.Static);
        prop!.SetValue(null, _tempRoot);
    }

    public void Dispose()
    {
        // Restore original root
        var prop = typeof(LauncherPaths).GetProperty("Root",
            BindingFlags.Public | BindingFlags.Static);
        prop!.SetValue(null, _originalRoot);

        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    private GameInstance CreateTestInstance(string id = "testinst1", string name = "Test Instance", string profile = "По умолчанию")
    {
        return new GameInstance
        {
            Id = id,
            Name = name,
            McVersion = "1.20.4",
            ActiveModProfile = profile
        };
    }

    // ── List ──

    [Fact]
    public void List_ReturnsDefaultProfile_WhenNoProfilesExist()
    {
        var inst = CreateTestInstance();
        var profiles = ModProfileService.List(inst);

        Assert.Single(profiles);
        Assert.Equal("По умолчанию", profiles[0]);
    }

    [Fact]
    public void List_IncludesCustomProfiles()
    {
        var inst = CreateTestInstance();
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(Path.Combine(
            Path.GetDirectoryName(InstanceService.InstanceDir(inst))!,
            "..", "..", "..", "instances", inst.Id, "mods_profiles", "My Profile"));

        // Simpler: create directory directly
        var profileRoot = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles");
        Directory.CreateDirectory(Path.Combine(profileRoot, "Combat Pack"));
        Directory.CreateDirectory(Path.Combine(profileRoot, "Tech Mods"));

        var profiles = ModProfileService.List(inst);

        Assert.Equal(3, profiles.Count); // default + 2 custom
        Assert.Contains("Combat Pack", profiles);
        Assert.Contains("Tech Mods", profiles);
        Assert.Contains("По умолчанию", profiles);
    }

    // ── Create ──

    [Fact]
    public void Create_CreatesProfileDirectory()
    {
        var inst = CreateTestInstance();
        ModProfileService.Create(inst, "New Profile", copyCurrent: false);

        var dir = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "New Profile");
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Create_ThrowsOnDuplicateName()
    {
        var inst = CreateTestInstance();
        ModProfileService.Create(inst, "Duplicate", copyCurrent: false);

        Assert.Throws<InvalidOperationException>(() =>
            ModProfileService.Create(inst, "Duplicate", copyCurrent: false));
    }

    [Fact]
    public void Create_CopiesCurrentMods_WhenCopyCurrentIsTrue()
    {
        var inst = CreateTestInstance();
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "sodium.jar"), "fake");
        File.WriteAllText(Path.Combine(modsDir, "iris.jar"), "fake");

        ModProfileService.Create(inst, "With Mods", copyCurrent: true);

        var profileDir = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "With Mods");
        var files = Directory.GetFiles(profileDir);
        Assert.Equal(2, files.Length);
        Assert.Contains(files, f => Path.GetFileName(f) == "sodium.jar");
        Assert.Contains(files, f => Path.GetFileName(f) == "iris.jar");
    }

    [Fact]
    public void Create_DoesNotCopyMods_WhenCopyCurrentIsFalse()
    {
        var inst = CreateTestInstance();
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "sodium.jar"), "fake");

        ModProfileService.Create(inst, "Empty Profile", copyCurrent: false);

        var profileDir = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "Empty Profile");
        Assert.Empty(Directory.GetFiles(profileDir));
    }

    // ── CountMods ──

    [Fact]
    public void CountMods_ReturnsZero_ForEmptyProfile()
    {
        var inst = CreateTestInstance();
        Assert.Equal(0, ModProfileService.CountMods(inst, "По умолчанию"));
    }

    [Fact]
    public void CountMods_CountsJarFiles()
    {
        var inst = CreateTestInstance();
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "mod1.jar"), "fake");
        File.WriteAllText(Path.Combine(modsDir, "mod2.jar"), "fake");
        File.WriteAllText(Path.Combine(modsDir, "mod3.jar.disabled"), "fake");

        Assert.Equal(3, ModProfileService.CountMods(inst, "По умолчанию"));
    }

    [Fact]
    public void CountMods_IgnoresNonJarFiles()
    {
        var inst = CreateTestInstance();
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "mod1.jar"), "fake");
        File.WriteAllText(Path.Combine(modsDir, "readme.txt"), "fake");
        File.WriteAllText(Path.Combine(modsDir, "config.json"), "fake");

        Assert.Equal(1, ModProfileService.CountMods(inst, "По умолчанию"));
    }

    // ── Switch ──

    [Fact]
    public void Switch_MovesModsToOldProfileAndLoadsNew()
    {
        var inst = CreateTestInstance(profile: "Profile A");

        // Create Profile A with mods
        var profileARoot = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "Profile A");
        Directory.CreateDirectory(profileARoot);
        File.WriteAllText(Path.Combine(profileARoot, "mod_a.jar"), "fake");

        // Create Profile B (empty)
        var profileBRoot = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "Profile B");
        Directory.CreateDirectory(profileBRoot);

        // Create empty active mods dir
        var modsDir = InstanceService.ModsDir(inst);
        Directory.CreateDirectory(modsDir);

        ModProfileService.Switch(inst, "Profile B");

        // Active mods dir should now have Profile B's files (empty)
        Assert.Empty(Directory.GetFiles(modsDir));

        // Profile A should have its files moved back
        var filesA = Directory.GetFiles(profileARoot);
        Assert.Single(filesA);
        Assert.Equal("mod_a.jar", Path.GetFileName(filesA[0]));

        Assert.Equal("Profile B", inst.ActiveModProfile);
    }

    // ── Delete ──

    [Fact]
    public void Delete_RemovesProfileDirectory()
    {
        var inst = CreateTestInstance();
        ModProfileService.Create(inst, "To Delete", copyCurrent: false);
        ModProfileService.Delete(inst, "To Delete");

        var dir = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "To Delete");
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Delete_ThrowsOnDefaultProfile()
    {
        var inst = CreateTestInstance();
        Assert.Throws<InvalidOperationException>(() =>
            ModProfileService.Delete(inst, "По умолчанию"));
    }

    [Fact]
    public void Delete_ThrowsOnActiveProfile()
    {
        var inst = CreateTestInstance(profile: "Active");
        ModProfileService.Create(inst, "Active", copyCurrent: false);

        Assert.Throws<InvalidOperationException>(() =>
            ModProfileService.Delete(inst, "Active"));
    }

    // ── Rename ──

    [Fact]
    public void Rename_ChangesProfileName()
    {
        var inst = CreateTestInstance();
        var profileRoot = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "Old Name");
        Directory.CreateDirectory(profileRoot);
        File.WriteAllText(Path.Combine(profileRoot, "test.jar"), "fake");

        ModProfileService.Rename(inst, "Old Name", "New Name");

        var newDir = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "New Name");
        Assert.True(Directory.Exists(newDir));
        Assert.Single(Directory.GetFiles(newDir));
    }

    [Fact]
    public void Rename_UpdatesActiveProfileName()
    {
        var inst = CreateTestInstance(profile: "Old Name");
        var profileRoot = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles", "Old Name");
        Directory.CreateDirectory(profileRoot);

        ModProfileService.Rename(inst, "Old Name", "New Name");

        Assert.Equal("New Name", inst.ActiveModProfile);
    }

    [Fact]
    public void Rename_ThrowsOnDefaultProfile()
    {
        var inst = CreateTestInstance();
        Assert.Throws<InvalidOperationException>(() =>
            ModProfileService.Rename(inst, "По умолчанию", "Something"));
    }

    [Fact]
    public void Rename_ThrowsOnExistingName()
    {
        var inst = CreateTestInstance();
        ModProfileService.Create(inst, "Profile A", copyCurrent: false);
        ModProfileService.Create(inst, "Profile B", copyCurrent: false);

        Assert.Throws<InvalidOperationException>(() =>
            ModProfileService.Rename(inst, "Profile A", "Profile B"));
    }

    // ── Sanitize (indirect via Create) ──

    [Fact]
    public void Create_SanitizesInvalidCharsInName()
    {
        var inst = CreateTestInstance();
        ModProfileService.Create(inst, "Test<>:\"/\\|?*", copyCurrent: false);

        var profileRoot = Path.Combine(InstanceService.InstanceDir(inst), "mods_profiles");
        var dirs = Directory.GetDirectories(profileRoot);
        Assert.Single(dirs);
        // Invalid chars should be replaced
        Assert.DoesNotContain("<>", Path.GetFileName(dirs[0]));
    }
}
