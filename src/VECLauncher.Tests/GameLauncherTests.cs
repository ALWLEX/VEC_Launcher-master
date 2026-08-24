using System.IO;
using System.Reflection;
using System.Text.Json;
using VECLauncher.Models;
using VECLauncher.Services;

namespace VECLauncher.Tests;

/// <summary>
/// Tests for GameLauncher.BuildArguments and SplitArgs.
/// Verifies JVM argument construction, variable substitution, server address parsing,
/// fullscreen vs windowed mode, and extra JVM args.
/// </summary>
public class GameLauncherTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _originalRoot;
    private readonly GameLauncher _launcher = new();

    public GameLauncherTests()
    {
        _originalRoot = LauncherPaths.Root;
        _tempRoot = Path.Combine(Path.GetTempPath(), "veclauncher_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);

        var prop = typeof(LauncherPaths).GetProperty("Root",
            BindingFlags.Public | BindingFlags.Static);
        prop!.SetValue(null, _tempRoot);
    }

    public void Dispose()
    {
        var prop = typeof(LauncherPaths).GetProperty("Root",
            BindingFlags.Public | BindingFlags.Static);
        prop!.SetValue(null, _originalRoot);

        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    private LaunchOptions CreateOptions(
        string? serverAddress = null,
        bool fullscreen = false,
        string extraJvmArgs = "",
        int maxMemoryMb = 4096,
        int windowWidth = 1280,
        int windowHeight = 720)
    {
        var detail = new VersionDetail
        {
            Id = "1.20.4",
            MainClass = "net.minecraft.client.main.Main",
            Type = "release",
            Assets = "1204"
        };

        return new LaunchOptions
        {
            Account = new MinecraftAccount
            {
                Username = "TestPlayer",
                Uuid = "12345678-1234-1234-1234-123456789012",
                AccessToken = "test-token-abc"
            },
            Install = new DownloadManager.InstallResult
            {
                Detail = detail,
                ClasspathJars = new List<string> { "client.jar", "libraries/lib1.jar" },
                ClientJar = "client.jar",
                NativesDir = Path.Combine(_tempRoot, "natives"),
                NativesExtractDir = Path.Combine(_tempRoot, "natives_extract"),
                AssetsDir = Path.Combine(_tempRoot, "assets"),
                AssetIndexId = "1204"
            },
            Java = new JavaInstallation
            {
                JavaExe = "javaw.exe",
                JavaConsoleExe = "java.exe",
                MajorVersion = 17,
                DisplayVersion = "17.0.2"
            },
            GameDir = _tempRoot,
            MaxMemoryMb = maxMemoryMb,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            Fullscreen = fullscreen,
            ServerAddress = serverAddress,
            ExtraJvmArgs = extraJvmArgs
        };
    }

    // ── SplitArgs ──

    [Fact]
    public void SplitArgs_SplitsOnWhitespace()
    {
        var args = GameLauncher.SplitArgs("-Xmx2G -Xms1G").ToList();
        Assert.Equal(2, args.Count);
        Assert.Equal("-Xmx2G", args[0]);
        Assert.Equal("-Xms1G", args[1]);
    }

    [Fact]
    public void SplitArgs_RespectsQuotes()
    {
        var args = GameLauncher.SplitArgs("-Dkey=\"value with spaces\" -other").ToList();
        Assert.Equal(2, args.Count);
        Assert.Equal("-Dkey=value with spaces", args[0]);
        Assert.Equal("-other", args[1]);
    }

    [Fact]
    public void SplitArgs_ReturnsEmpty_WhenWhitespaceOnly()
    {
        Assert.Empty(GameLauncher.SplitArgs("   "));
        Assert.Empty(GameLauncher.SplitArgs(""));
        Assert.Empty(GameLauncher.SplitArgs(null!));
    }

    [Fact]
    public void SplitArgs_SingleArg()
    {
        var args = GameLauncher.SplitArgs("-XX:+UseG1GC").ToList();
        Assert.Single(args);
        Assert.Equal("-XX:+UseG1GC", args[0]);
    }

    // ── BuildArguments: JVM Memory ──

    [Fact]
    public void BuildArguments_ContainsMemoryArgs()
    {
        var opts = CreateOptions(maxMemoryMb: 8192);
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("-Xms1024M", args);
        Assert.Contains("-Xmx8192M", args);
    }

    [Fact]
    public void BuildArguments_CustomMinMemory()
    {
        var opts = CreateOptions();
        opts = new LaunchOptions
        {
            Account = opts.Account,
            Install = opts.Install,
            Java = opts.Java,
            GameDir = opts.GameDir,
            MinMemoryMb = 2048,
            MaxMemoryMb = 4096
        };
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("-Xms2048M", args);
        Assert.Contains("-Xmx4096M", args);
    }

    // ── BuildArguments: Server Address ──

    [Fact]
    public void BuildArguments_ServerWithPort_UsesQuickPlay()
    {
        var opts = CreateOptions(serverAddress: "95.59.233.227:25565");
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("--quickPlayMultiplayer", args);
        Assert.Contains("95.59.233.227:25565", args);
    }

    [Fact]
    public void BuildArguments_ServerWithoutPort_DefaultsTo25565()
    {
        var opts = CreateOptions(serverAddress: "95.59.233.227");
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("--quickPlayMultiplayer", args);
        Assert.Contains("95.59.233.227:25565", args);
    }

    [Fact]
    public void BuildArguments_NoServer_NoQuickPlay()
    {
        var opts = CreateOptions(serverAddress: null);
        var args = _launcher.BuildArguments(opts);

        Assert.DoesNotContain("--quickPlayMultiplayer", args);
    }

    [Fact]
    public void BuildArguments_EmptyServer_NoQuickPlay()
    {
        var opts = CreateOptions(serverAddress: "  ");
        var args = _launcher.BuildArguments(opts);

        Assert.DoesNotContain("--quickPlayMultiplayer", args);
    }

    // ── BuildArguments: Fullscreen ──

    [Fact]
    public void BuildArguments_Fullscreen_AddsFlag()
    {
        var opts = CreateOptions(fullscreen: true);
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("--fullscreen", args);
        Assert.DoesNotContain("--width", args);
        Assert.DoesNotContain("--height", args);
    }

    [Fact]
    public void BuildArguments_Windowed_AddsWidthHeight()
    {
        var opts = CreateOptions(fullscreen: false, windowWidth: 1920, windowHeight: 1080);
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("--width", args);
        Assert.Contains("1920", args);
        Assert.Contains("--height", args);
        Assert.Contains("1080", args);
        Assert.DoesNotContain("--fullscreen", args);
    }

    // ── BuildArguments: Main Class ──

    [Fact]
    public void BuildArguments_ContainsMainClass()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("net.minecraft.client.main.Main", args);
    }

    // ── BuildArguments: Standard JVM Flags ──

    [Fact]
    public void BuildArguments_ContainsStandardJvmFlags()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("-XX:+UnlockExperimentalVMOptions", args);
        Assert.Contains("-XX:+UseG1GC", args);
        Assert.Contains("-Dfile.encoding=UTF-8", args);
    }

    [Fact]
    public void BuildArguments_ContainsLauncherBrand()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        Assert.Contains($"-Dminecraft.launcher.brand={GameLauncher.LauncherName}", args);
        Assert.Contains($"-Dminecraft.launcher.version={GameLauncher.LauncherVersion}", args);
    }

    // ── BuildArguments: Extra JVM Args ──

    [Fact]
    public void BuildArguments_AppendsExtraJvmArgs()
    {
        var opts = CreateOptions(extraJvmArgs: "-XX:+UseZGC -Xlog:gc*");
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("-XX:+UseZGC", args);
        Assert.Contains("-Xlog:gc*", args);
    }

    [Fact]
    public void BuildArguments_EmptyExtraJvmArgs_NoEffect()
    {
        var opts = CreateOptions(extraJvmArgs: "");
        var args = _launcher.BuildArguments(opts);

        // Should not have empty strings in args
        Assert.DoesNotContain("", args);
    }

    // ── BuildArguments: Variable Substitution ──

    [Fact]
    public void BuildArguments_AuthVariables_Substituted()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        // auth_player_name should be substituted in game args
        Assert.Contains("--username", args);
        Assert.Contains("TestPlayer", args);
    }

    [Fact]
    public void BuildArguments_AuthUuid_NoDashes()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        // UUID should have dashes removed in auth_uuid
        Assert.Contains("--uuid", args);
        Assert.Contains("12345678123412341234123456789012", args);
    }

    // ── BuildArguments: Classpath ──

    [Fact]
    public void BuildArguments_ContainsClasspath()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        var cpIdx = args.IndexOf("-cp");
        Assert.True(cpIdx >= 0, "Should contain -cp argument");
        Assert.True(cpIdx + 1 < args.Count, "Classpath value should follow -cp");
        Assert.Contains("client.jar", args[cpIdx + 1]);
    }

    // ── BuildArguments: Windows-specific ──

    [Fact]
    public void BuildArguments_Windows_HeapDumpPath()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        // On Windows, should contain heap dump path
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(args, a => a.Contains("HeapDumpPath"));
        }
    }

    // ── BuildArguments: JSON arguments (1.13+ style) ──

    [Fact]
    public void BuildArguments_WithJsonArguments()
    {
        var detail = new VersionDetail
        {
            Id = "1.20.4",
            MainClass = "net.minecraft.client.main.Main",
            Type = "release",
            Arguments = JsonDocument.Parse(@"{
                ""jvm"": [
                    ""-Djava.library.path=${natives_directory}"",
                    {""rules"": [{""features"": {""is_demo_user"": true}}], ""value"": ""--demo""},
                    [""-cp"", ""${classpath}""]
                ],
                ""game"": [
                    ""--username"", ""${auth_player_name}"",
                    ""--version"", ""${version_name}"",
                    ""--gameDir"", ""${game_directory}""
                ]
            }").RootElement
        };

        var opts = CreateOptions();
        opts = new LaunchOptions
        {
            Account = opts.Account,
            Install = new DownloadManager.InstallResult
            {
                Detail = detail,
                ClasspathJars = opts.Install.ClasspathJars,
                ClientJar = opts.Install.ClientJar,
                NativesDir = opts.Install.NativesDir,
                NativesExtractDir = opts.Install.NativesExtractDir,
                AssetsDir = opts.Install.AssetsDir,
                AssetIndexId = opts.Install.AssetIndexId
            },
            Java = opts.Java,
            GameDir = opts.GameDir
        };

        var args = _launcher.BuildArguments(opts);

        Assert.Contains("net.minecraft.client.main.Main", args);
        Assert.Contains("--username", args);
        Assert.Contains("TestPlayer", args);
    }

    // ── BuildArguments: Demo user feature ──

    [Fact]
    public void BuildArguments_DemoFeature_NotIncludedByDefault()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        // Normal user should not get --demo
        Assert.DoesNotContain("--demo", args);
    }

    // ── BuildArguments: Version args ──

    [Fact]
    public void BuildArguments_ContainsVersionArgs()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("--version", args);
        Assert.Contains("1.20.4", args);
    }

    [Fact]
    public void BuildArguments_ContainsGameDir()
    {
        var opts = CreateOptions();
        var args = _launcher.BuildArguments(opts);

        Assert.Contains("--gameDir", args);
        Assert.Contains(_tempRoot, args);
    }
}
