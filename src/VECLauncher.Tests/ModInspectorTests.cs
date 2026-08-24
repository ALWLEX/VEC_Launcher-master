using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

public class ModInspectorTests
{
    // ── FindConflicts() ──

    [Fact]
    public void FindConflicts_NoMods_ReturnsEmpty()
    {
        var result = ModInspector.FindConflicts(new List<LocalModInfo>(), LoaderKind.Fabric);
        Assert.Empty(result);
    }

    [Fact]
    public void FindConflicts_NoDuplicates_NoConflicts()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "a.jar", FileName = "a.jar", ModId = "mod_a", Name = "Mod A", Enabled = true, Loader = LoaderKind.Fabric },
            new() { FilePath = "b.jar", FileName = "b.jar", ModId = "mod_b", Name = "Mod B", Enabled = true, Loader = LoaderKind.Fabric },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Empty(result);
    }

    [Fact]
    public void FindConflicts_DuplicateModId_DetectsConflict()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "a1.jar", FileName = "a1.jar", ModId = "sodium", Name = "Sodium 1", Enabled = true, Loader = LoaderKind.Fabric },
            new() { FilePath = "a2.jar", FileName = "a2.jar", ModId = "sodium", Name = "Sodium 2", Enabled = true, Loader = LoaderKind.Fabric },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Single(result);
        Assert.Equal("duplicate", result[0].Kind);
        Assert.Equal(2, result[0].Files.Count);
    }

    [Fact]
    public void FindConflicts_DuplicateDisabled_NotDetected()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "a.jar", FileName = "a.jar", ModId = "sodium", Name = "Sodium 1", Enabled = true, Loader = LoaderKind.Fabric },
            new() { FilePath = "b.jar.disabled", FileName = "b.jar.disabled", ModId = "sodium", Name = "Sodium 2", Enabled = false, Loader = LoaderKind.Fabric },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Empty(result);
    }

    [Fact]
    public void FindConflicts_WrongLoader_DetectsConflict()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "forge.jar", FileName = "forge.jar", ModId = "forge_mod", Name = "Forge Mod", Enabled = true, Loader = LoaderKind.Forge },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Single(result);
        Assert.Equal("loader", result[0].Kind);
        Assert.True(result[0].IsError);
    }

    [Fact]
    public void FindConflicts_NeoForgeWithForge_IsSoftConflict()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "f.jar", FileName = "f.jar", ModId = "mod", Name = "Forge Mod", Enabled = true, Loader = LoaderKind.Forge },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.NeoForge);
        Assert.Single(result);
        Assert.Equal("loader", result[0].Kind);
        Assert.False(result[0].IsError); // soft conflict for Forge→NeoForge
    }

    [Fact]
    public void FindConflicts_UnknownModId_DetectsUnknown()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "unknown.jar", FileName = "unknown.jar", ModId = "", Name = "unknown", Enabled = true },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Single(result);
        Assert.Equal("unknown", result[0].Kind);
        Assert.False(result[0].IsError);
    }

    [Fact]
    public void FindConflicts_CaseInsensitive_Duplicates()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "a.jar", FileName = "a.jar", ModId = "Sodium", Name = "Sodium A", Enabled = true },
            new() { FilePath = "b.jar", FileName = "b.jar", ModId = "sodium", Name = "Sodium B", Enabled = true },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Single(result);
        Assert.Equal("duplicate", result[0].Kind);
    }

    [Fact]
    public void FindConflicts_VanillaExpected_SkipsLoaderCheck()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "f.jar", FileName = "f.jar", ModId = "mod", Name = "Mod", Enabled = true, Loader = LoaderKind.Forge },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Vanilla);
        // No loader conflict expected since vanilla doesn't check loaders
        Assert.Empty(result);
    }

    [Fact]
    public void FindConflicts_MultipleConflictTypes()
    {
        var mods = new List<LocalModInfo>
        {
            new() { FilePath = "a.jar", FileName = "a.jar", ModId = "dup", Name = "Dup 1", Enabled = true, Loader = LoaderKind.Fabric },
            new() { FilePath = "b.jar", FileName = "b.jar", ModId = "dup", Name = "Dup 2", Enabled = true, Loader = LoaderKind.Fabric },
            new() { FilePath = "c.jar", FileName = "c.jar", ModId = "forge_only", Name = "Forge Only", Enabled = true, Loader = LoaderKind.Forge },
            new() { FilePath = "unknown.jar", FileName = "unknown.jar", ModId = "", Name = "Unknown", Enabled = true },
        };
        var result = ModInspector.FindConflicts(mods, LoaderKind.Fabric);
        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.Kind == "duplicate");
        Assert.Contains(result, r => r.Kind == "loader");
        Assert.Contains(result, r => r.Kind == "unknown");
    }
}
